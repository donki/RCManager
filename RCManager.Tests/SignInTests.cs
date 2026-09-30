using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Web;
using SocRcManager.Localization;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>
/// La entrada con cuenta (PKCE y vuelta a 127.0.0.1) sin navegador: en vez de abrirlo, la prueba
/// hace de navegador y llama a la direccion de vuelta; el proveedor es un HttpMessageHandler falso.
/// </summary>
public sealed class SignInTests : IDisposable
{
    private readonly Action<string> _original = OAuthClient.OpenBrowser;
    private readonly TempDir _dir = new();
    private readonly string _settingsPath = AppSettings.FilePath;
    private readonly string _language = Loc.Language;
    private string? _authorizeUrl;
    private string? _page;

    public SignInTests()
    {
        AppSettings.FilePath = Path.Combine(_dir.Path, "settings.json");
        Lang.Set("en");
    }

    public void Dispose()
    {
        OAuthClient.OpenBrowser = _original;
        AppSettings.FilePath = _settingsPath;
        Lang.Set(_language);
        _dir.Dispose();
    }

    /// <summary>El «navegador»: vuelve a la direccion local con lo que diga <paramref name="query"/> (recibe el state).</summary>
    private void Browser(Func<string, string> query) => OAuthClient.OpenBrowser = url =>
    {
        _authorizeUrl = url;
        var q = HttpUtility.ParseQueryString(new Uri(url).Query);
        var redirect = q["redirect_uri"]!;
        _ = Task.Run(async () =>
        {
            using var http = new HttpClient();
            _page = await http.GetStringAsync(redirect + "?" + query(q["state"]!));
        });
    };

    private static OAuthProvider Provider(string secret = "sec") =>
        new("Google", "cid", secret, "https://auth.example/authorize", "https://auth.example/token", "openid email", "&prompt=consent");

    private static string Jwt(string email) =>
        "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { email }))).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".x";

    [Fact]
    public async Task Entra_con_PKCE_y_cambia_el_codigo_por_tokens()
    {
        Browser(state => $"code=CODIGO&state={state}");
        var h = new FakeHandler((_, _) => FakeHandler.Json("{\"access_token\":\"at\",\"refresh_token\":\"rt\",\"expires_in\":3600,\"scope\":\"openid email\"}"));
        var tokens = await new OAuthClient(new HttpClient(h), Provider()).SignInAsync();

        Assert.Equal(("at", "rt"), (tokens.AccessToken, tokens.RefreshToken));
        var q = HttpUtility.ParseQueryString(new Uri(_authorizeUrl!).Query);
        Assert.StartsWith("https://auth.example/authorize?", _authorizeUrl);
        Assert.Equal("cid", q["client_id"]);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("openid email", q["scope"]);
        Assert.Equal("consent", q["prompt"]);
        Assert.Matches(@"^http://127\.0\.0\.1:\d+/auth/$", q["redirect_uri"]);

        var body = HttpUtility.ParseQueryString(Assert.Single(h.Requests).Body!);
        Assert.Equal("authorization_code", body["grant_type"]);
        Assert.Equal("CODIGO", body["code"]);
        Assert.Equal("sec", body["client_secret"]);
        Assert.Equal(q["redirect_uri"], body["redirect_uri"]);
        // El verificador es el que corresponde al desafio (SHA-256 en base64url).
        var challenge = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(body["code_verifier"]!)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(q["code_challenge"], challenge);

        for (var i = 0; i < 50 && _page is null; i++) await Task.Delay(20);
        Assert.Contains("Ya puedes volver", _page);
    }

    [Fact]
    public async Task Cliente_publico_sin_secreto()
    {
        Browser(state => $"code=C&state={state}");
        var h = new FakeHandler((_, _) => FakeHandler.Json("{\"access_token\":\"at\"}"));
        await new OAuthClient(new HttpClient(h), Provider(secret: "")).SignInAsync();
        Assert.DoesNotContain("client_secret", h.Requests[0].Body);
    }

    [Fact]
    public async Task El_usuario_dice_que_no()
    {
        Browser(state => $"error=access_denied&error_description=Cancelado+por+el+usuario&state={state}");
        var client = new OAuthClient(new HttpClient(new FakeHandler((_, _) => FakeHandler.Json("{}"))), Provider());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SignInAsync());
        Assert.Equal("Cancelado por el usuario", ex.Message);
    }

    [Fact]
    public async Task Error_sin_descripcion_y_vuelta_sin_codigo()
    {
        Browser(state => $"error=server_error&state={state}");
        var client = new OAuthClient(new HttpClient(new FakeHandler((_, _) => FakeHandler.Json("{}"))), Provider());
        Assert.Equal("server_error", (await Assert.ThrowsAsync<InvalidOperationException>(() => client.SignInAsync())).Message);

        Browser(state => $"state={state}");
        Assert.Equal("El proveedor no devolvio ningun codigo.", (await Assert.ThrowsAsync<InvalidOperationException>(() => client.SignInAsync())).Message);
    }

    [Fact]
    public async Task Una_vuelta_de_otra_entrada_se_rechaza()
    {
        Browser(_ => "code=C&state=otro");
        var client = new OAuthClient(new HttpClient(new FakeHandler((_, _) => FakeHandler.Json("{}"))), Provider());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SignInAsync());
        Assert.Contains("no corresponde", ex.Message);
    }

    [Fact]
    public async Task Cancelar_mientras_se_espera_al_navegador()
    {
        OAuthClient.OpenBrowser = _ => { };   // nadie vuelve
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var client = new OAuthClient(new HttpClient(new FakeHandler((_, _) => FakeHandler.Json("{}"))), Provider());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SignInAsync(cts.Token));
    }

    [Fact]
    public async Task CloudSync_entra_guarda_la_cuenta_y_el_correo()
    {
        Browser(state => $"code=C&state={state}");
        var settings = new AppSettings();
        var h = new FakeHandler((_, _) => FakeHandler.Json(JsonSerializer.Serialize(new
        {
            access_token = "at", refresh_token = "rt", expires_in = 3600,
            scope = "openid email https://www.googleapis.com/auth/drive.appdata", id_token = Jwt("ana@example.org"),
        })));
        using var sync = new CloudSync(settings, new Store(), new HttpClient(h));
        Assert.Equal("ana@example.org", await sync.SignInAsync(StorageMode.GoogleDrive));
        Assert.Equal(StorageMode.GoogleDrive, settings.Storage);
        Assert.Equal("rt", settings.Tokens!.RefreshToken);
        Assert.Equal("ana@example.org", AppSettings.Load().AccountEmail);
    }

    [Theory]
    [InlineData(StorageMode.GoogleDrive, "openid email", "GoogleScopeMissing")]
    [InlineData(StorageMode.OneDrive, "openid email offline_access", "MicrosoftScopeMissing")]
    public async Task CloudSync_sin_el_permiso_de_la_carpeta_se_dice(StorageMode mode, string scope, string key)
    {
        Browser(state => $"code=C&state={state}");
        var settings = new AppSettings();
        var h = new FakeHandler((_, _) => FakeHandler.Json(JsonSerializer.Serialize(new { access_token = "at", scope })));
        using var sync = new CloudSync(settings, new Store(), new HttpClient(h));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sync.SignInAsync(mode));
        Assert.Equal(Loc.Get(key), ex.Message);
        Assert.Equal(StorageMode.Local, settings.Storage);   // no se queda a medias
    }

    [Fact]
    public async Task CloudSync_OneDrive_sin_ambito_en_la_respuesta_vale()
    {
        Browser(state => $"code=C&state={state}");
        var settings = new AppSettings();
        var h = new FakeHandler((_, _) => FakeHandler.Json("{\"access_token\":\"at\"}"));
        using var sync = new CloudSync(settings, new Store(), new HttpClient(h));
        Assert.Equal("", await sync.SignInAsync(StorageMode.OneDrive));
        Assert.Equal(StorageMode.OneDrive, settings.Storage);
    }
}
