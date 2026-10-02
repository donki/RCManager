using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Web;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>
/// La nube de mentira de las pruebas de Ajustes: el proveedor OAuth (cambio de codigo por tokens) y
/// el fichero de Google Drive, todo en un HttpMessageHandler. Nada sale a la red.
/// </summary>
public sealed class DCloud
{
    /// <summary>Lo que hay en la «nube» (cifrado), o null si no hay fichero.</summary>
    public string? Content;
    public DateTimeOffset Modified = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    public int Uploads;

    /// <summary>Si no es OK, Drive contesta con ese error.</summary>
    public HttpStatusCode DriveStatus = HttpStatusCode.OK;

    /// <summary>El cambio de codigo por tokens se corta (como un tiempo de espera).</summary>
    public bool TokenTimesOut;

    public string Email = "ana@example.org";

    public FakeHandler Handler { get; }

    public DCloud() => Handler = new FakeHandler(Respond);

    /// <summary>Contesta tarde, como la red de verdad (lo de despues sigue en otro hilo).</summary>
    public bool Slow;

    public HttpClient Http() => new(new Delay(this) { InnerHandler = Handler });

    private sealed class Delay(DCloud cloud) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (cloud.Slow)
                await Task.Delay(30, cancellationToken).ConfigureAwait(false);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private HttpResponseMessage Respond(HttpRequestMessage r, string? body)
    {
        var url = r.RequestUri!.ToString();
        if (url.Contains("/token"))
        {
            if (TokenTimesOut)
                throw new TaskCanceledException("Se acabo el tiempo");
            return FakeHandler.Json(JsonSerializer.Serialize(new
            {
                access_token = "at",
                refresh_token = "rt",
                expires_in = 3600,
                scope = "openid email https://www.googleapis.com/auth/drive.appdata Files.ReadWrite.AppFolder",
                id_token = Jwt(Email),
            }));
        }
        if (DriveStatus != HttpStatusCode.OK)
            return FakeHandler.Json("{\"error\":\"caida\"}", DriveStatus);
        if (r.Method == HttpMethod.Get && url.Contains("alt=media"))
            return FakeHandler.Text(Content!);
        if (r.Method == HttpMethod.Get)
            return FakeHandler.Json(Content is null ? "{\"files\":[]}" : $"{{\"files\":[{{\"id\":\"F\",\"modifiedTime\":\"{Modified:O}\"}}]}}");
        // Subida: POST multipart o PATCH con el contenido.
        Uploads++;
        Content = r.Method == HttpMethod.Patch ? body : body!.Split("\r\n").First(l => l.StartsWith("enc1:"));
        return FakeHandler.Json("{}");
    }

    public static string Jwt(string email) =>
        "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { email }))).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".x";

    /// <summary>Tokens validos para una hora (ya «dentro»).</summary>
    public static OAuthTokens Tokens() => new() { AccessToken = "at", RefreshToken = "rt", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
}

/// <summary>
/// El «navegador» de la entrada con cuenta: en vez de abrirlo, vuelve a la direccion local de la
/// aplicacion con lo que diga la funcion (que recibe el state). Se deja como estaba con Dispose.
/// </summary>
public sealed class DBrowser : IDisposable
{
    private readonly Action<string> _original = OAuthClient.OpenBrowser;

    public List<string> Opened { get; } = [];

    public DBrowser(Func<string, string>? query)
    {
        OAuthClient.OpenBrowser = url =>
        {
            Opened.Add(url);
            if (query is null)
                return;   // nadie vuelve
            var q = HttpUtility.ParseQueryString(new Uri(url).Query);
            var redirect = q["redirect_uri"]!;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var http = new HttpClient();
                    await http.GetStringAsync(redirect + "?" + query(q["state"]!));
                }
                catch (Exception)
                {
                }
            });
        };
    }

    public void Dispose() => OAuthClient.OpenBrowser = _original;
}
