using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SocRcManager.Services;

/// <summary>Donde se guardan las conexiones.</summary>
public enum StorageMode
{
    Local,
    GoogleDrive,
    OneDrive,
}

/// <summary>Un fichero en la nube: bajarlo, subirlo y saber de cuando es.</summary>
public interface ICloudDrive
{
    StorageMode Mode { get; }

    /// <summary>El fichero y su fecha, o null si aun no existe.</summary>
    Task<(string Content, DateTimeOffset ModifiedAt)?> DownloadAsync(CancellationToken cancellationToken = default);

    Task UploadAsync(string content, CancellationToken cancellationToken = default);
}

/// <summary>
/// Google Drive: el fichero va a la <b>carpeta de datos de la aplicacion</b> (<c>appDataFolder</c>),
/// que el usuario no ve entre sus ficheros y que solo esta aplicacion puede leer. Es el ambito
/// <c>drive.appdata</c>, que no da acceso a nada mas del Drive del usuario.
/// </summary>
public sealed class GoogleDrive : ICloudDrive
{
    public const string Scopes = "openid email https://www.googleapis.com/auth/drive.appdata";
    private const string FileName = "connections.enc";

    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<string>> _token;

    public GoogleDrive(HttpClient http, Func<CancellationToken, Task<string>> token)
    {
        _http = http;
        _token = token;
    }

    public StorageMode Mode => StorageMode.GoogleDrive;

    public async Task<(string, DateTimeOffset)?> DownloadAsync(CancellationToken cancellationToken = default)
    {
        var file = await FindAsync(cancellationToken).ConfigureAwait(false);
        if (file is null)
            return null;

        using var request = await RequestAsync(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{file.Value.Id}?alt=media", cancellationToken).ConfigureAwait(false);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), file.Value.ModifiedAt);
    }

    public async Task UploadAsync(string content, CancellationToken cancellationToken = default)
    {
        var file = await FindAsync(cancellationToken).ConfigureAwait(false);
        HttpRequestMessage request;
        if (file is null)
        {
            request = await RequestAsync(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart", cancellationToken).ConfigureAwait(false);
            var multipart = new MultipartContent("related");
            multipart.Add(new StringContent(JsonSerializer.Serialize(new { name = FileName, parents = new[] { "appDataFolder" } }), Encoding.UTF8, "application/json"));
            multipart.Add(new StringContent(content, Encoding.UTF8, "application/octet-stream"));
            request.Content = multipart;
        }
        else
        {
            request = await RequestAsync(HttpMethod.Patch, $"https://www.googleapis.com/upload/drive/v3/files/{file.Value.Id}?uploadType=media", cancellationToken).ConfigureAwait(false);
            request.Content = new StringContent(content, Encoding.UTF8, "application/octet-stream");
        }

        using (request)
        using (var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false))
            await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(string Id, DateTimeOffset ModifiedAt)?> FindAsync(CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString($"name = '{FileName}' and 'appDataFolder' in parents and trashed = false");
        using var request = await RequestAsync(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files?spaces=appDataFolder&q={query}&fields=files(id,modifiedTime)", cancellationToken).ConfigureAwait(false);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        foreach (var f in doc.RootElement.GetProperty("files").EnumerateArray())
            return (f.GetProperty("id").GetString()!, f.GetProperty("modifiedTime").GetDateTimeOffset());
        return null;
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string url, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _token(cancellationToken).ConfigureAwait(false));
        return request;
    }

    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(CloudError.Describe("Google Drive", response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), "GoogleScopeMissing"));
    }
}

/// <summary>
/// OneDrive: el fichero va a la <b>carpeta de la aplicacion</b> (<c>/Aplicaciones/sOC Remote
/// Connection Manager</c>, <c>special/approot</c> en Graph). Es el ambito
/// <c>Files.ReadWrite.AppFolder</c>, que no da acceso al resto del OneDrive del usuario.
/// </summary>
public sealed class OneDrive : ICloudDrive
{
    public const string Scopes = "openid email offline_access Files.ReadWrite.AppFolder";
    private const string Item = "https://graph.microsoft.com/v1.0/me/drive/special/approot:/connections.enc";

    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<string>> _token;

    public OneDrive(HttpClient http, Func<CancellationToken, Task<string>> token)
    {
        _http = http;
        _token = token;
    }

    public StorageMode Mode => StorageMode.OneDrive;

    public async Task<(string, DateTimeOffset)?> DownloadAsync(CancellationToken cancellationToken = default)
    {
        using var meta = await RequestAsync(HttpMethod.Get, Item + "?select=id,lastModifiedDateTime", cancellationToken).ConfigureAwait(false);
        using var metaResponse = await _http.SendAsync(meta, cancellationToken).ConfigureAwait(false);
        if (metaResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureAsync(metaResponse, cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await metaResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var modified = doc.RootElement.GetProperty("lastModifiedDateTime").GetDateTimeOffset();

        using var request = await RequestAsync(HttpMethod.Get, Item + ":/content", cancellationToken).ConfigureAwait(false);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), modified);
    }

    public async Task UploadAsync(string content, CancellationToken cancellationToken = default)
    {
        using var request = await RequestAsync(HttpMethod.Put, Item + ":/content", cancellationToken).ConfigureAwait(false);
        request.Content = new StringContent(content, Encoding.UTF8, "application/octet-stream");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string url, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _token(cancellationToken).ConfigureAwait(false));
        return request;
    }

    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(CloudError.Describe("OneDrive", response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), "MicrosoftScopeMissing"));
    }
}

/// <summary>
/// Un error HTTP de la nube en una linea: el mensaje del JSON de error, no el JSON entero (que
/// llenaba la barra de estado con veinte lineas y dejaba la ventana sin sitio para nada mas).
/// </summary>
internal static class CloudError
{
    public static string Describe(string service, System.Net.HttpStatusCode status, string body, string scopeKey)
    {
        // 401/403 con el token: casi siempre es que el permiso de la carpeta no se concedio
        // (Google lo enseña como una casilla que se puede dejar sin marcar).
        if (status is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            return Localization.Loc.Get(scopeKey);

        var message = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m))
                    message = m.GetString() ?? string.Empty;
                else if (error.ValueKind == JsonValueKind.String)
                    message = error.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
        }

        if (message.Length == 0)
            message = body.Length > 160 ? body[..160] + "…" : body;
        return $"{service}: {(int)status} {message.ReplaceLineEndings(" ")}";
    }
}
