using System.Net.Http;
using System.Security.Cryptography;

namespace SocRcManager.Services;

/// <summary>
/// Mantiene el almacen local igual que el fichero de la nube elegida (Google Drive u OneDrive).
/// </summary>
/// <remarks>
/// <para><b>Lo local es lo que se usa</b>; la nube es la copia que sigue al usuario de un PC a
/// otro. Al arrancar se baja el fichero: si es mas reciente que lo local, sustituye lo local; si
/// no, se sube lo local. Cada guardado se sube detras (con un pequeño retraso para agrupar
/// cambios seguidos). El ultimo que escribe gana, por fecha de guardado; no hay fusion fina, y
/// el <c>.bak</c> local queda como red de seguridad.</para>
///
/// <para>Lo que sube va <b>cifrado con la frase del usuario</b> (<see cref="Vault"/>): ni Google ni
/// Microsoft ni nadie con acceso a esa cuenta lee las conexiones. Sin frase no se sube nada.</para>
///
/// <para>Los identificadores de cliente OAuth vienen de <c>oauth.local.props</c>; sin ellos, ese
/// proveedor no se ofrece.</para>
/// </remarks>
public sealed class CloudSync : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly AppSettings _settings;
    private readonly Store _store;
    private CancellationTokenSource? _pendingUpload;

    public CloudSync(AppSettings settings, Store store)
    {
        _settings = settings;
        _store = store;
        _store.Saved += OnSaved;
    }

    /// <summary>Lo que ha pasado, para la barra de estado.</summary>
    public event Action<string>? Status;

    /// <summary>Se ha sustituido lo local por lo de la nube: hay que repintar el arbol.</summary>
    public event Action? Replaced;

    public bool IsCloud => _settings.Storage != StorageMode.Local;

    public static bool IsAvailable(StorageMode mode) => mode switch
    {
        StorageMode.GoogleDrive => OAuthSecrets.GoogleClientId.Length > 0,
        StorageMode.OneDrive => OAuthSecrets.MicrosoftClientId.Length > 0,
        _ => true,
    };

    public OAuthClient ClientFor(StorageMode mode) => new(_http, mode switch
    {
        StorageMode.GoogleDrive => new OAuthProvider("Google", OAuthSecrets.GoogleClientId, OAuthSecrets.GoogleClientSecret,
            "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", GoogleDrive.Scopes,
            "&access_type=offline&prompt=consent"),
        StorageMode.OneDrive => new OAuthProvider("Microsoft", OAuthSecrets.MicrosoftClientId, string.Empty,
            "https://login.microsoftonline.com/common/oauth2/v2.0/authorize", "https://login.microsoftonline.com/common/oauth2/v2.0/token", OneDrive.Scopes,
            "&prompt=select_account"),
        _ => throw new InvalidOperationException("Sin proveedor."),
    });

    /// <summary>Entra con la cuenta del proveedor y deja los tokens guardados. Devuelve el correo.</summary>
    public async Task<string> SignInAsync(StorageMode mode, CancellationToken cancellationToken = default)
    {
        var tokens = await ClientFor(mode).SignInAsync(cancellationToken).ConfigureAwait(false);
        _settings.Storage = mode;
        _settings.Tokens = tokens;
        _settings.AccountEmail = OAuthClient.EmailOf(tokens);
        _settings.Save();
        return _settings.AccountEmail;
    }

    public void SignOut()
    {
        _settings.Storage = StorageMode.Local;
        _settings.Tokens = null;
        _settings.AccountEmail = string.Empty;
        _settings.LastSyncAt = null;
        _settings.Save();
    }

    private ICloudDrive Drive()
    {
        var client = ClientFor(_settings.Storage);
        Func<CancellationToken, Task<string>> token = async ct =>
        {
            var tokens = _settings.Tokens ?? throw new InvalidOperationException(Localization.Loc.Get("CloudNotSignedIn"));
            var fresh = await client.RefreshIfNeededAsync(tokens, ct).ConfigureAwait(false);
            if (!ReferenceEquals(fresh, tokens))
            {
                _settings.Tokens = fresh;
                _settings.Save();
            }
            return fresh.AccessToken;
        };
        return _settings.Storage == StorageMode.GoogleDrive ? new GoogleDrive(_http, token) : new OneDrive(_http, token);
    }

    /// <summary>
    /// Sincroniza ahora: baja, compara fechas y se queda con lo mas reciente (subiendo lo local si
    /// es lo que gana). Devuelve true si lo local cambio.
    /// </summary>
    public async Task<bool> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!IsCloud)
            return false;

        var passphrase = _settings.Passphrase;
        if (passphrase.Length == 0)
        {
            Status?.Invoke(Localization.Loc.Get("CloudNoPassphrase"));
            return false;
        }

        var drive = Drive();
        Status?.Invoke(Localization.Loc.Get("CloudSyncing"));
        var remote = await drive.DownloadAsync(cancellationToken).ConfigureAwait(false);

        if (remote is null)
        {
            await UploadAsync(drive, passphrase, cancellationToken).ConfigureAwait(false);
            return false;
        }

        string plain;
        try
        {
            plain = Vault.Decrypt(remote.Value.Content, passphrase);
        }
        catch (CryptographicException)
        {
            Status?.Invoke(Localization.Loc.Get("CloudWrongPassphrase"));
            return false;
        }

        var remoteModified = Store.ModifiedAtOf(plain);
        if (remoteModified > _store.ModifiedAt)
        {
            _store.ImportPortable(plain);
            _settings.LastSyncAt = DateTimeOffset.UtcNow;
            _settings.Save();
            Status?.Invoke(Localization.Loc.Format("CloudDownloaded", _store.Connections.Count));
            Replaced?.Invoke();
            return true;
        }

        if (remoteModified < _store.ModifiedAt)
        {
            await UploadAsync(drive, passphrase, cancellationToken).ConfigureAwait(false);
            return false;
        }

        _settings.LastSyncAt = DateTimeOffset.UtcNow;
        _settings.Save();
        Status?.Invoke(Localization.Loc.Get("CloudUpToDate"));
        return false;
    }

    private async Task UploadAsync(ICloudDrive drive, string passphrase, CancellationToken cancellationToken)
    {
        await drive.UploadAsync(Vault.Encrypt(_store.ExportPortable(), passphrase), cancellationToken).ConfigureAwait(false);
        _settings.LastSyncAt = DateTimeOffset.UtcNow;
        _settings.Save();
        Status?.Invoke(Localization.Loc.Format("CloudUploaded", drive.Mode == StorageMode.GoogleDrive ? "Google Drive" : "OneDrive"));
    }

    /// <summary>Tras cada guardado local: subir, agrupando los cambios de los siguientes dos segundos.</summary>
    private void OnSaved()
    {
        if (!IsCloud || _settings.Passphrase.Length == 0)
            return;

        _pendingUpload?.Cancel();
        var cts = new CancellationTokenSource();
        _pendingUpload = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000, cts.Token).ConfigureAwait(false);
                await UploadAsync(Drive(), _settings.Passphrase, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Status?.Invoke(Localization.Loc.Format("CloudFailed", ex.Message));
            }
        });
    }

    public void Dispose()
    {
        _store.Saved -= OnSaved;
        _pendingUpload?.Cancel();
        _http.Dispose();
    }
}
