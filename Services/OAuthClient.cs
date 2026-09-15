using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SocRcManager.Services;

/// <summary>Con que proveedor se entra: identificadores de cliente y direcciones.</summary>
public sealed record OAuthProvider(
    string Name,
    string ClientId,
    string ClientSecret,
    string AuthorizeUrl,
    string TokenUrl,
    string Scopes,
    string ExtraAuthorizeParameters = "");

/// <summary>Lo que devuelve el proveedor y se guarda (protegido con DPAPI) para no volver a entrar.</summary>
public sealed class OAuthTokens
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string IdToken { get; set; } = string.Empty;

    /// <summary>Permisos que el usuario concedio de verdad (Google los enseña como casillas y puede dejar alguna sin marcar).</summary>
    public string Scope { get; set; } = string.Empty;

    public bool Has(string scope) => Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Entrada con una cuenta del usuario (Google o Microsoft) desde el escritorio: navegador del
/// sistema, PKCE y vuelta a un servidor local de un solo uso.
/// </summary>
/// <remarks>
/// <para>Es el mismo flujo que usa Task Manager para Windows: ni Google ni Microsoft admiten un
/// WebView incrustado, y con PKCE el codigo llega como parametro de consulta al servidor local.
/// El cliente de Google es de tipo «escritorio» (admite cualquier puerto de <c>127.0.0.1</c>); el
/// de Microsoft es un cliente publico con <c>http://127.0.0.1/auth/</c> registrado (Entra ignora el
/// puerto en loopback).</para>
///
/// <para>No hay dependencia de terceros: son tres peticiones HTTP.</para>
/// </remarks>
public sealed class OAuthClient
{
    private readonly HttpClient _http;
    private readonly OAuthProvider _provider;

    public OAuthClient(HttpClient http, OAuthProvider provider)
    {
        _http = http;
        _provider = provider;
    }

    public bool IsConfigured => _provider.ClientId.Length > 0;

    public async Task<OAuthTokens> SignInAsync(CancellationToken cancellationToken = default)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var port = FindPort();
        var redirect = $"http://127.0.0.1:{port}/auth/";
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        var authorize =
            $"{_provider.AuthorizeUrl}?client_id={Uri.EscapeDataString(_provider.ClientId)}" +
            $"&response_type=code&redirect_uri={Uri.EscapeDataString(redirect)}" +
            $"&scope={Uri.EscapeDataString(_provider.Scopes)}" +
            $"&code_challenge={challenge}&code_challenge_method=S256&state={state}" +
            _provider.ExtraAuthorizeParameters;

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();
        Process.Start(new ProcessStartInfo(authorize) { UseShellExecute = true });

        using var registration = cancellationToken.Register(listener.Abort);
        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().ConfigureAwait(false);
        }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException();
        }

        var query = System.Web.HttpUtility.ParseQueryString(context.Request.Url?.Query ?? string.Empty);
        await RespondAsync(context, query["error"] is null).ConfigureAwait(false);
        listener.Stop();

        if (query["state"] != state)
            throw new InvalidOperationException("La respuesta no corresponde a esta entrada.");
        var code = query["code"];
        if (string.IsNullOrEmpty(code))
            throw new InvalidOperationException(query["error_description"] ?? query["error"] ?? "El proveedor no devolvio ningun codigo.");

        var form = new Dictionary<string, string>
        {
            ["client_id"] = _provider.ClientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirect,
            ["code_verifier"] = verifier,
        };
        if (_provider.ClientSecret.Length > 0)
            form["client_secret"] = _provider.ClientSecret;

        return await PostTokenAsync(form, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Un token de acceso valido, renovandolo si hace falta.</summary>
    public async Task<OAuthTokens> RefreshIfNeededAsync(OAuthTokens tokens, CancellationToken cancellationToken = default)
    {
        if (tokens.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2) && tokens.AccessToken.Length > 0)
            return tokens;
        if (tokens.RefreshToken.Length == 0)
            throw new InvalidOperationException("La sesion ha caducado: hay que volver a entrar.");

        var form = new Dictionary<string, string>
        {
            ["client_id"] = _provider.ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = tokens.RefreshToken,
        };
        if (_provider.ClientSecret.Length > 0)
            form["client_secret"] = _provider.ClientSecret;
        if (_provider.Name == "Microsoft")
            form["scope"] = _provider.Scopes;

        return await PostTokenAsync(form, tokens.RefreshToken, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>El correo de la cuenta, sacado del id_token (sin verificar: es solo para enseñarlo).</summary>
    public static string EmailOf(OAuthTokens tokens)
    {
        try
        {
            var parts = tokens.IdToken.Split('.');
            if (parts.Length < 2) return string.Empty;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            var root = doc.RootElement;
            foreach (var claim in new[] { "email", "preferred_username", "upn" })
                if (root.TryGetProperty(claim, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString() ?? string.Empty;
        }
        catch (Exception)
        {
        }
        return string.Empty;
    }

    private async Task<OAuthTokens> PostTokenAsync(Dictionary<string, string> form, string? previousRefresh, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(_provider.TokenUrl, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{_provider.Name}: {(int)response.StatusCode} {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new OAuthTokens
        {
            AccessToken = root.GetProperty("access_token").GetString() ?? string.Empty,
            RefreshToken = root.TryGetProperty("refresh_token", out var r) ? r.GetString() ?? previousRefresh ?? string.Empty : previousRefresh ?? string.Empty,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600),
            IdToken = root.TryGetProperty("id_token", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            Scope = root.TryGetProperty("scope", out var sc) ? sc.GetString() ?? string.Empty : string.Empty,
        };
    }

    private static int FindPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task RespondAsync(HttpListenerContext context, bool ok)
    {
        var title = ok ? "Ya puedes volver a la aplicación" : "No se ha podido entrar";
        var html = $$$"""
            <!doctype html><html lang="es"><head><meta charset="utf-8"><title>sOC Remote Connections Manager</title>
            <style>body{font-family:Segoe UI,system-ui,sans-serif;background:#F8F9FA;color:#191C1D;display:flex;align-items:center;justify-content:center;height:100vh;margin:0}
            .card{background:#fff;border-radius:16px;padding:32px 40px;text-align:center;box-shadow:0 2px 18px rgba(0,0,0,.12)}h1{color:#3525CD;font-size:20px;margin:0 0 8px}p{margin:0;color:#464555;font-size:14px}
            @media(prefers-color-scheme:dark){body{background:#141318;color:#E6E1E9}.card{background:#201F27;box-shadow:none}h1{color:#635BF2}p{color:#C7C4D8}}</style></head>
            <body><div class="card"><h1>{{{title}}}</h1><p>Puedes cerrar esta pestaña.</p></div></body></html>
            """;
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
    }
}
