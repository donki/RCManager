using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SocRcManager.Localization;
using SocRcManager.Models;
using SocRcManager.Services;

namespace SocRcManager.Tests;

public sealed class CloudErrorTests : IDisposable
{
    private readonly string _original = Loc.Language;

    public CloudErrorTests() => Lang.Set("en");

    public void Dispose() => Lang.Set(_original);

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void Sin_permiso_es_la_casilla_sin_marcar(HttpStatusCode status)
    {
        Assert.Equal(Loc.Get("GoogleScopeMissing"), CloudError.Describe("Google Drive", status, "{}", "GoogleScopeMissing"));
    }

    [Fact]
    public void Mensaje_del_JSON_de_error()
    {
        Assert.Equal("OneDrive: 500 Algo fallo aqui", CloudError.Describe("OneDrive", HttpStatusCode.InternalServerError, "{\"error\":{\"code\":\"x\",\"message\":\"Algo fallo\\naqui\"}}", "k"));
        Assert.Equal("Google Drive: 400 invalid_grant", CloudError.Describe("Google Drive", HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}", "k"));
    }

    [Fact]
    public void Sin_mensaje_util_va_el_cuerpo_recortado()
    {
        Assert.Equal("S: 502 <html>", CloudError.Describe("S", HttpStatusCode.BadGateway, "<html>", "k"));
        Assert.Equal("S: 500 {\"error\":{\"code\":1}}", CloudError.Describe("S", HttpStatusCode.InternalServerError, "{\"error\":{\"code\":1}}", "k"));
        Assert.Equal("S: 500 {\"otro\":1}", CloudError.Describe("S", HttpStatusCode.InternalServerError, "{\"otro\":1}", "k"));
        var big = new string('a', 300);
        Assert.Equal("S: 500 " + new string('a', 160) + "…", CloudError.Describe("S", HttpStatusCode.InternalServerError, big, "k"));
    }
}

public class GoogleDriveTests
{
    private static GoogleDrive Drive(FakeHandler h) => new(new HttpClient(h), _ => Task.FromResult("tok"));

    [Fact]
    public async Task Sin_fichero_la_descarga_es_null()
    {
        var h = new FakeHandler((_, _) => FakeHandler.Json("{\"files\":[]}"));
        Assert.Null(await Drive(h).DownloadAsync());
        var (req, _) = Assert.Single(h.Requests);
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("tok", req.Headers.Authorization.Parameter);
        Assert.Contains("spaces=appDataFolder", req.RequestUri!.ToString());
        Assert.Equal(StorageMode.GoogleDrive, Drive(h).Mode);
    }

    [Fact]
    public async Task Con_fichero_baja_el_contenido_y_su_fecha()
    {
        var h = new FakeHandler((r, _) => r.RequestUri!.Query.Contains("alt=media")
            ? FakeHandler.Text("enc1:abc")
            : FakeHandler.Json("{\"files\":[{\"id\":\"F1\",\"modifiedTime\":\"2026-09-01T10:00:00Z\"}]}"));
        var result = await Drive(h).DownloadAsync();
        Assert.NotNull(result);
        Assert.Equal("enc1:abc", result.Value.Item1);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero), result.Value.Item2);
        Assert.EndsWith("/files/F1?alt=media", h.Requests[1].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Subir_crea_el_fichero_en_la_carpeta_de_la_aplicacion()
    {
        var h = new FakeHandler((r, _) => r.Method == HttpMethod.Get ? FakeHandler.Json("{\"files\":[]}") : FakeHandler.Json("{}"));
        await Drive(h).UploadAsync("enc1:xyz");
        var (post, body) = h.Requests[1];
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.Contains("uploadType=multipart", post.RequestUri!.ToString());
        Assert.Contains("appDataFolder", body);
        Assert.Contains("connections.enc", body);
        Assert.Contains("enc1:xyz", body);
    }

    [Fact]
    public async Task Subir_sobre_el_que_hay_lo_sustituye()
    {
        var h = new FakeHandler((r, _) => r.Method == HttpMethod.Get
            ? FakeHandler.Json("{\"files\":[{\"id\":\"F9\",\"modifiedTime\":\"2026-09-01T10:00:00Z\"}]}")
            : FakeHandler.Json("{}"));
        await Drive(h).UploadAsync("nuevo");
        var (patch, body) = h.Requests[1];
        Assert.Equal(HttpMethod.Patch, patch.Method);
        Assert.Contains("/files/F9?uploadType=media", patch.RequestUri!.ToString());
        Assert.Equal("nuevo", body);
    }

    [Fact]
    public async Task Un_error_HTTP_lanza_con_el_mensaje()
    {
        var h = new FakeHandler((_, _) => FakeHandler.Json("{\"error\":{\"message\":\"cuota\"}}", HttpStatusCode.TooManyRequests));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Drive(h).DownloadAsync());
        Assert.Equal("Google Drive: 429 cuota", ex.Message);
    }
}

public class OneDriveTests
{
    private static OneDrive Drive(FakeHandler h) => new(new HttpClient(h), _ => Task.FromResult("tok"));

    [Fact]
    public async Task No_encontrado_es_null()
    {
        var h = new FakeHandler((_, _) => FakeHandler.Text("", HttpStatusCode.NotFound));
        Assert.Null(await Drive(h).DownloadAsync());
        Assert.Equal(StorageMode.OneDrive, Drive(h).Mode);
        Assert.Contains("special/approot:/connections.enc", h.Requests[0].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Baja_fecha_y_contenido()
    {
        var h = new FakeHandler((r, _) => r.RequestUri!.ToString().EndsWith(":/content")
            ? FakeHandler.Text("enc1:od")
            : FakeHandler.Json("{\"id\":\"1\",\"lastModifiedDateTime\":\"2026-08-31T08:30:00Z\"}"));
        var result = await Drive(h).DownloadAsync();
        Assert.Equal("enc1:od", result!.Value.Item1);
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 8, 30, 0, TimeSpan.Zero), result.Value.Item2);
    }

    [Fact]
    public async Task Sube_con_PUT()
    {
        var h = new FakeHandler((_, _) => FakeHandler.Json("{}"));
        await Drive(h).UploadAsync("contenido");
        var (put, body) = Assert.Single(h.Requests);
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal("contenido", body);
    }

    [Fact]
    public async Task Error_de_permisos_lanza_con_el_aviso_del_ambito()
    {
        Lang.Set("en");
        var h = new FakeHandler((_, _) => FakeHandler.Json("{}", HttpStatusCode.Forbidden));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Drive(h).UploadAsync("x"));
        Assert.Equal(Loc.Get("MicrosoftScopeMissing"), ex.Message);
    }

    [Fact]
    public async Task Error_al_bajar_el_contenido()
    {
        var h = new FakeHandler((r, _) => r.RequestUri!.ToString().EndsWith(":/content")
            ? FakeHandler.Text("mal", HttpStatusCode.InternalServerError)
            : FakeHandler.Json("{\"lastModifiedDateTime\":\"2026-08-31T08:30:00Z\"}"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Drive(h).DownloadAsync());
        Assert.Equal("OneDrive: 500 mal", ex.Message);
    }
}

public class OAuthTests
{
    private static string Jwt(object payload)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"none\"}")}.{B64(JsonSerializer.Serialize(payload))}.firma";
    }

    [Theory]
    [InlineData("email")]
    [InlineData("preferred_username")]
    [InlineData("upn")]
    public void EmailOf_lee_el_correo_del_id_token(string claim)
    {
        var token = Jwt(new Dictionary<string, object> { [claim] = "ana@example.org", ["sub"] = "1" });
        Assert.Equal("ana@example.org", OAuthClient.EmailOf(new OAuthTokens { IdToken = token }));
    }

    [Fact]
    public void EmailOf_prefiere_email_y_tolera_basura()
    {
        Assert.Equal("a@x", OAuthClient.EmailOf(new OAuthTokens { IdToken = Jwt(new { upn = "b@x", email = "a@x" }) }));
        Assert.Equal("", OAuthClient.EmailOf(new OAuthTokens { IdToken = Jwt(new { email = 5 }) }));
        Assert.Equal("", OAuthClient.EmailOf(new OAuthTokens()));
        Assert.Equal("", OAuthClient.EmailOf(new OAuthTokens { IdToken = "a.%%%.c" }));
        Assert.Equal("", OAuthClient.EmailOf(new OAuthTokens { IdToken = "sinpuntos" }));
    }

    [Fact]
    public void Has_mira_los_permisos_concedidos()
    {
        var t = new OAuthTokens { Scope = "openid  email https://www.googleapis.com/auth/drive.appdata" };
        Assert.True(t.Has("https://www.googleapis.com/auth/drive.appdata"));
        Assert.True(t.Has("EMAIL"));
        Assert.False(t.Has("drive"));
        Assert.False(new OAuthTokens().Has("email"));
    }

    private static OAuthProvider Provider(string name = "Google", string secret = "sec") =>
        new(name, "cid", secret, "https://auth.example/authorize", "https://auth.example/token", "openid email", "&x=1");

    [Fact]
    public async Task Token_vigente_no_se_renueva()
    {
        var h = new FakeHandler((_, _) => throw new InvalidOperationException("no deberia llamar"));
        var client = new OAuthClient(new HttpClient(h), Provider());
        var t = new OAuthTokens { AccessToken = "at", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        Assert.Same(t, await client.RefreshIfNeededAsync(t));
        Assert.True(client.IsConfigured);
        Assert.False(new OAuthClient(new HttpClient(h), Provider() with { ClientId = "" }).IsConfigured);
    }

    [Fact]
    public async Task Caducado_sin_refresh_token_pide_volver_a_entrar()
    {
        var client = new OAuthClient(new HttpClient(new FakeHandler((_, _) => FakeHandler.Json("{}"))), Provider());
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RefreshIfNeededAsync(new OAuthTokens { AccessToken = "at", ExpiresAt = DateTimeOffset.UtcNow }));
    }

    [Fact]
    public async Task Renueva_con_el_secreto_y_conserva_el_refresh_token_si_no_viene_otro()
    {
        var h = new FakeHandler((_, _) => FakeHandler.Json("{\"access_token\":\"nuevo\",\"expires_in\":120,\"scope\":\"a b\"}"));
        var client = new OAuthClient(new HttpClient(h), Provider());
        var fresh = await client.RefreshIfNeededAsync(new OAuthTokens { AccessToken = "", RefreshToken = "rt", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
        Assert.Equal("nuevo", fresh.AccessToken);
        Assert.Equal("rt", fresh.RefreshToken);
        Assert.Equal("a b", fresh.Scope);
        Assert.Equal("", fresh.IdToken);
        Assert.InRange(fresh.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(100), DateTimeOffset.UtcNow.AddSeconds(125));
        var (req, body) = Assert.Single(h.Requests);
        Assert.Equal("https://auth.example/token", req.RequestUri!.ToString());
        Assert.Contains("grant_type=refresh_token", body);
        Assert.Contains("refresh_token=rt", body);
        Assert.Contains("client_secret=sec", body);
        Assert.DoesNotContain("scope=", body);   // Google no lo necesita
    }

    [Fact]
    public async Task Microsoft_manda_el_ambito_y_no_hay_secreto()
    {
        var h = new FakeHandler((_, _) => FakeHandler.Json("{\"access_token\":\"n\",\"refresh_token\":\"rt2\",\"id_token\":\"i\"}"));
        var client = new OAuthClient(new HttpClient(h), Provider("Microsoft", ""));
        var fresh = await client.RefreshIfNeededAsync(new OAuthTokens { RefreshToken = "rt" });
        Assert.Equal("rt2", fresh.RefreshToken);
        Assert.Equal("i", fresh.IdToken);
        Assert.InRange(fresh.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(59), DateTimeOffset.UtcNow.AddMinutes(61));   // sin expires_in: una hora
        var body = h.Requests[0].Body!;
        Assert.Contains("scope=openid+email", body);
        Assert.DoesNotContain("client_secret", body);
    }

    [Fact]
    public async Task Error_del_proveedor_lanza_con_codigo_y_cuerpo()
    {
        var h = new FakeHandler((_, _) => FakeHandler.Json("{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest));
        var client = new OAuthClient(new HttpClient(h), Provider());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.RefreshIfNeededAsync(new OAuthTokens { RefreshToken = "rt" }));
        Assert.Equal("Google: 400 {\"error\":\"invalid_grant\"}", ex.Message);
    }

    [Fact]
    public async Task Refresh_token_null_en_la_respuesta_conserva_el_anterior()
    {
        var h = new FakeHandler((_, _) => FakeHandler.Json("{\"access_token\":\"n\",\"refresh_token\":null}"));
        var fresh = await new OAuthClient(new HttpClient(h), Provider()).RefreshIfNeededAsync(new OAuthTokens { RefreshToken = "viejo" });
        Assert.Equal("viejo", fresh.RefreshToken);
    }
}

/// <summary>La sincronizacion entera contra una nube de mentira, con el almacen y los ajustes en temporales.</summary>
public sealed class CloudSyncTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _storeFolder = Store.Folder;
    private readonly string _settingsPath = AppSettings.FilePath;
    private readonly string _language = Loc.Language;

    public CloudSyncTests()
    {
        Store.Folder = Path.Combine(_dir.Path, "store");
        AppSettings.FilePath = Path.Combine(_dir.Path, "settings.json");
        Lang.Set("en");
    }

    public void Dispose()
    {
        Store.Folder = _storeFolder;
        AppSettings.FilePath = _settingsPath;
        Lang.Set(_language);
        _dir.Dispose();
    }

    private sealed class FakeCloud
    {
        public string? Content;
        public DateTimeOffset Modified = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        public int Uploads;
        public HttpStatusCode Status = HttpStatusCode.OK;

        public FakeHandler Handler() => new((r, body) =>
        {
            if (Status != HttpStatusCode.OK)
                return FakeHandler.Json("{\"error\":\"caida\"}", Status);
            var url = r.RequestUri!.ToString();
            if (r.Method == HttpMethod.Get && url.Contains("alt=media"))
                return FakeHandler.Text(Content!);
            if (r.Method == HttpMethod.Get)
                return FakeHandler.Json(Content is null ? "{\"files\":[]}" : $"{{\"files\":[{{\"id\":\"F\",\"modifiedTime\":\"{Modified:O}\"}}]}}");
            // Subida: POST multipart o PATCH con el contenido.
            Uploads++;
            Content = r.Method == HttpMethod.Patch ? body : body!.Split("\r\n").First(l => l.StartsWith("enc1:"));
            return FakeHandler.Json("{}");
        });
    }

    private static AppSettings Settings(StorageMode mode = StorageMode.GoogleDrive, string passphrase = "frase larga") => new()
    {
        Storage = mode,
        Passphrase = passphrase,
        Tokens = new OAuthTokens { AccessToken = "at", RefreshToken = "rt", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) },
    };

    private static Store StoreWith(params string[] names)
    {
        var s = new Store();
        foreach (var n in names)
            s.Connections.Add(new Connection { Name = n, Host = n + ".lan" });
        s.Save();
        return s;
    }

    [Fact]
    public void Disponibilidad_segun_los_identificadores()
    {
        Assert.True(CloudSync.IsAvailable(StorageMode.Local));
        Assert.True(CloudSync.IsAvailable(StorageMode.GoogleDrive));
        Assert.True(CloudSync.IsAvailable(StorageMode.OneDrive));
    }

    [Fact]
    public void ClientFor_por_proveedor()
    {
        using var sync = new CloudSync(new AppSettings(), new Store(), new HttpClient(new FakeHandler((_, _) => FakeHandler.Json("{}"))));
        Assert.True(sync.ClientFor(StorageMode.GoogleDrive).IsConfigured);
        Assert.True(sync.ClientFor(StorageMode.OneDrive).IsConfigured);
        Assert.Throws<InvalidOperationException>(() => sync.ClientFor(StorageMode.Local));
        Assert.False(sync.IsCloud);
    }

    [Fact]
    public async Task En_local_no_hace_nada()
    {
        var cloud = new FakeCloud();
        var h = cloud.Handler();
        using var sync = new CloudSync(new AppSettings(), StoreWith("a"), new HttpClient(h));
        Assert.False(await sync.SyncAsync());
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Sin_frase_no_sube_nada()
    {
        var cloud = new FakeCloud();
        var h = cloud.Handler();
        using var sync = new CloudSync(Settings(passphrase: ""), StoreWith("a"), new HttpClient(h));
        var status = new List<string>();
        sync.Status += status.Add;
        Assert.False(await sync.SyncAsync());
        Assert.Equal([Loc.Get("CloudNoPassphrase")], status);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Nube_vacia_sube_lo_local_cifrado()
    {
        var cloud = new FakeCloud();
        var settings = Settings();
        using var sync = new CloudSync(settings, StoreWith("a", "b"), new HttpClient(cloud.Handler()));
        var status = new List<string>();
        sync.Status += status.Add;
        Assert.False(await sync.SyncAsync());
        Assert.Equal(1, cloud.Uploads);
        Assert.True(Vault.IsEncrypted(cloud.Content!));
        Assert.Contains("a.lan", Vault.Decrypt(cloud.Content!, "frase larga"));
        Assert.NotNull(settings.LastSyncAt);
        Assert.Equal(Loc.Format("CloudUploaded", "Google Drive"), status[^1]);
    }

    [Fact]
    public async Task Nube_mas_reciente_sustituye_lo_local()
    {
        var other = new Store();
        other.Connections.Add(new Connection { Name = "remota", Host = "r.lan", PasswordProtected = Secrets.Protect("pw") });
        var json = other.ExportPortable().Replace("\"ModifiedAt\": \"0001-01-01T00:00:00+00:00\"", "\"ModifiedAt\": \"2099-01-01T00:00:00+00:00\"");
        var cloud = new FakeCloud { Content = Vault.Encrypt(json, "frase larga") };

        var local = StoreWith("local");
        using var sync = new CloudSync(Settings(), local, new HttpClient(cloud.Handler()));
        var replaced = 0;
        sync.Replaced += () => replaced++;
        Assert.True(await sync.SyncAsync());
        Assert.Equal(1, replaced);
        Assert.Equal("remota", Assert.Single(local.Connections).Name);
        Assert.Equal("pw", Secrets.Unprotect(local.Connections[0].PasswordProtected));
        Assert.Equal(0, cloud.Uploads);
    }

    [Fact]
    public async Task Local_mas_reciente_se_sube_por_encima()
    {
        var old = new Store();
        old.Connections.Add(new Connection { Name = "vieja", Host = "v" });
        var cloud = new FakeCloud { Content = Vault.Encrypt(old.ExportPortable(), "frase larga") };   // fecha minima
        using var sync = new CloudSync(Settings(), StoreWith("nueva"), new HttpClient(cloud.Handler()));
        Assert.False(await sync.SyncAsync());
        Assert.Equal(1, cloud.Uploads);
        Assert.Contains("nueva", Vault.Decrypt(cloud.Content!, "frase larga"));
    }

    [Fact]
    public async Task Misma_fecha_esta_al_dia()
    {
        var local = StoreWith("x");
        var cloud = new FakeCloud { Content = Vault.Encrypt(local.ExportPortable(), "frase larga") };
        using var sync = new CloudSync(Settings(), local, new HttpClient(cloud.Handler()));
        var status = new List<string>();
        sync.Status += status.Add;
        Assert.False(await sync.SyncAsync());
        Assert.Equal(0, cloud.Uploads);
        Assert.Equal(Loc.Get("CloudUpToDate"), status[^1]);
    }

    [Fact]
    public async Task Frase_equivocada_se_dice_y_no_se_toca_nada()
    {
        var cloud = new FakeCloud { Content = Vault.Encrypt("{}", "otra frase") };
        var local = StoreWith("x");
        using var sync = new CloudSync(Settings(), local, new HttpClient(cloud.Handler()));
        var status = new List<string>();
        sync.Status += status.Add;
        Assert.False(await sync.SyncAsync());
        Assert.Equal(Loc.Get("CloudWrongPassphrase"), status[^1]);
        Assert.Equal("x", Assert.Single(local.Connections).Name);
        Assert.Equal(0, cloud.Uploads);
    }

    [Fact]
    public async Task OneDrive_sube_con_su_nombre()
    {
        var uploads = 0;
        var h = new FakeHandler((r, _) =>
        {
            if (r.Method == HttpMethod.Get) return FakeHandler.Text("", HttpStatusCode.NotFound);
            uploads++;
            return FakeHandler.Json("{}");
        });
        using var sync = new CloudSync(Settings(StorageMode.OneDrive), StoreWith("a"), new HttpClient(h));
        var status = new List<string>();
        sync.Status += status.Add;
        await sync.SyncAsync();
        Assert.Equal(1, uploads);
        Assert.Equal(Loc.Format("CloudUploaded", "OneDrive"), status[^1]);
    }

    [Fact]
    public async Task Sin_sesion_lanza()
    {
        var settings = Settings();
        settings.Tokens = null;
        using var sync = new CloudSync(settings, StoreWith("a"), new HttpClient(new FakeCloud().Handler()));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sync.SyncAsync());
        Assert.Equal(Loc.Get("CloudNotSignedIn"), ex.Message);
    }

    [Fact]
    public async Task Token_caducado_se_renueva_y_se_guarda()
    {
        var cloud = new FakeCloud();
        var inner = cloud.Handler();
        var h = new FakeHandler((r, body) => r.RequestUri!.Host == "oauth2.googleapis.com"
            ? FakeHandler.Json("{\"access_token\":\"renovado\",\"expires_in\":3600}")
            : SendThrough(inner, r));
        var settings = Settings();
        settings.Tokens = new OAuthTokens { AccessToken = "viejo", RefreshToken = "rt", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5) };
        using var sync = new CloudSync(settings, StoreWith("a"), new HttpClient(h));
        await sync.SyncAsync();
        Assert.Equal("renovado", settings.Tokens!.AccessToken);
        Assert.Equal("rt", settings.Tokens.RefreshToken);
        Assert.True(File.Exists(AppSettings.FilePath));
        Assert.Equal(1, cloud.Uploads);
    }

    private static HttpResponseMessage SendThrough(HttpMessageHandler inner, HttpRequestMessage r) =>
        new HttpMessageInvoker(inner).SendAsync(r, CancellationToken.None).GetAwaiter().GetResult();

    [Fact]
    public async Task Al_guardar_sube_a_los_dos_segundos_agrupando()
    {
        var cloud = new FakeCloud();
        var store = StoreWith("a");
        using var sync = new CloudSync(Settings(), store, new HttpClient(cloud.Handler()));
        store.Save();
        store.Save();   // el segundo cancela el primero: una sola subida
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (cloud.Uploads == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        await Task.Delay(500);
        Assert.Equal(1, cloud.Uploads);
    }

    [Fact]
    public async Task Al_guardar_un_fallo_se_cuenta_en_el_estado()
    {
        var cloud = new FakeCloud { Status = HttpStatusCode.InternalServerError };
        var store = StoreWith("a");
        using var sync = new CloudSync(Settings(), store, new HttpClient(cloud.Handler()));
        var status = new List<string>();
        sync.Status += s => { lock (status) status.Add(s); };
        store.Save();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            lock (status) if (status.Count > 0) break;
            await Task.Delay(100);
        }
        lock (status)
            Assert.StartsWith(Loc.Format("CloudFailed", "").TrimEnd(), Assert.Single(status));
    }

    [Fact]
    public void Al_guardar_en_local_o_sin_frase_no_sube()
    {
        var cloud = new FakeCloud();
        var h = cloud.Handler();
        var store = StoreWith("a");
        using (new CloudSync(new AppSettings(), store, new HttpClient(h)))
            store.Save();
        using (new CloudSync(Settings(passphrase: ""), store, new HttpClient(h)))
            store.Save();
        Assert.Empty(h.Requests);
    }

    [Fact]
    public void SignOut_vuelve_a_local_y_borra_la_sesion()
    {
        var settings = Settings();
        settings.AccountEmail = "a@b";
        settings.LastSyncAt = DateTimeOffset.UtcNow;
        using var sync = new CloudSync(settings, new Store(), new HttpClient(new FakeCloud().Handler()));
        Assert.True(sync.IsCloud);
        sync.SignOut();
        Assert.Equal(StorageMode.Local, settings.Storage);
        Assert.Null(settings.Tokens);
        Assert.Equal("", settings.AccountEmail);
        Assert.Null(settings.LastSyncAt);
        Assert.False(sync.IsCloud);
        Assert.Equal(StorageMode.Local, AppSettings.Load().Storage);
    }
}
