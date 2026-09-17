using System.IO;
using FluentFTP;
using SocRcManager.Models;

namespace SocRcManager.Files;

/// <summary>
/// Ficheros por FTP o FTPS con FluentFTP (MIT). FTPS explicito (AUTH TLS en el puerto 21) o
/// implicito (TLS desde el primer byte, puerto 990); el certificado del servidor se acepta aunque
/// no sea de confianza (es lo normal en servidores propios), como hace FileZilla la primera vez.
/// </summary>
public sealed class FtpFileSystem : IRemoteFileSystem
{
    private readonly AsyncFtpClient _ftp;

    private FtpFileSystem(AsyncFtpClient ftp, string initial)
    {
        _ftp = ftp;
        InitialDirectory = initial;
    }

    public string InitialDirectory { get; }

    public static async Task<FtpFileSystem> ConnectAsync(Connection connection, string password, CancellationToken cancellationToken)
    {
        var ftp = new AsyncFtpClient(connection.Host, connection.UserName.Length > 0 ? connection.UserName : "anonymous",
            connection.UserName.Length > 0 ? password : "anonymous@", connection.Port);
        ftp.Config.EncryptionMode = connection.FtpsMode switch
        {
            1 => FtpEncryptionMode.Explicit,
            2 => FtpEncryptionMode.Implicit,
            _ => FtpEncryptionMode.None,
        };
        ftp.Config.ValidateAnyCertificate = true;
        // Las fechas del listado (MLSD) vienen en UTC: a la hora de este PC.
        ftp.Config.TimeConversion = FtpDate.LocalTime;
        ftp.Config.ServerTimeZone = TimeZoneInfo.Utc;
        var timeout = Math.Max(5, connection.FilesTimeoutSeconds) * 1000;
        ftp.Config.ConnectTimeout = timeout;
        ftp.Config.ReadTimeout = timeout;
        ftp.Config.DataConnectionConnectTimeout = timeout;
        ftp.Config.DataConnectionReadTimeout = timeout;
        ftp.Config.DataConnectionType = connection.FtpPassive ? FtpDataConnectionType.AutoPassive : FtpDataConnectionType.AutoActive;
        ftp.Config.SocketKeepAlive = true;
        ftp.Encoding = connection.FtpUtf8 ? System.Text.Encoding.UTF8 : System.Text.Encoding.Latin1;
        await ftp.Connect(cancellationToken);
        var initial = await ftp.GetWorkingDirectory(cancellationToken);
        var result = new FtpFileSystem(ftp, string.IsNullOrEmpty(initial) ? "/" : initial);
        if (connection.FilesKeepAliveSeconds > 0)
            result.StartKeepAlive(TimeSpan.FromSeconds(connection.FilesKeepAliveSeconds));
        return result;
    }

    // «Sigo aqui» (NOOP) cuando la conexion lleva un rato sin usarse; no se manda si hay una
    // operacion en curso, porque el cliente no admite dos ordenes a la vez.
    private System.Threading.Timer? _keepAlive;
    private int _busy;
    private DateTime _lastUse = DateTime.UtcNow;

    private void StartKeepAlive(TimeSpan every)
    {
        _keepAlive = new System.Threading.Timer(async _ =>
        {
            if (DateTime.UtcNow - _lastUse < every || Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                return;
            try { await _ftp.Execute("NOOP"); } catch (Exception) { }
            finally { _lastUse = DateTime.UtcNow; Interlocked.Exchange(ref _busy, 0); }
        }, null, every, every);
    }

    /// <summary>Envuelve cada operacion: marca la conexion como ocupada para que el keep-alive no se cruce.</summary>
    private async Task<T> UseAsync<T>(Func<Task<T>> operation)
    {
        while (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            await Task.Delay(50);
        try { return await operation(); }
        finally { _lastUse = DateTime.UtcNow; Interlocked.Exchange(ref _busy, 0); }
    }

    public Task<FileEntry?> StatAsync(string path, CancellationToken cancellationToken) => UseAsync(async () =>
    {
        var info = await _ftp.GetObjectInfo(path, true, cancellationToken);
        return info is null ? null : new FileEntry(info.Name, info.FullName, info.Type is FtpObjectType.Directory or FtpObjectType.Link, info.Size, info.Modified == DateTime.MinValue ? null : info.Modified);
    });

    /// <summary>MFMT; los servidores que no lo tienen lo rechazan y se ignora.</summary>
    public Task SetModifiedAsync(string path, DateTime modified, CancellationToken cancellationToken) =>
        UseAsync(async () => { await _ftp.SetModifiedTime(path, modified.ToUniversalTime(), cancellationToken); return true; });

    /// <summary>Se sabe que es Unix cuando el listado trae permisos rwx (un IIS no los trae).</summary>
    public bool SupportsPermissions { get; private set; }

    public Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken cancellationToken) => UseAsync(() => ListCoreAsync(path, cancellationToken));

    private async Task<IReadOnlyList<FileEntry>> ListCoreAsync(string path, CancellationToken cancellationToken)
    {
        var items = await _ftp.GetListing(path, cancellationToken);
        var list = items
            .Where(i => i.Name is not ("." or ".."))
            .Select(i => new FileEntry(i.Name, i.FullName, i.Type is FtpObjectType.Directory or FtpObjectType.Link, i.Size,
                i.Modified == DateTime.MinValue ? null : i.Modified,
                i.Chmod > 0 || !string.IsNullOrEmpty(i.RawPermissions) ? Convert.ToInt32(i.Chmod.ToString(), 8) : null,   // 644 -> bits
                string.IsNullOrEmpty(i.RawOwner) ? null : i.RawOwner,
                string.IsNullOrEmpty(i.RawGroup) ? null : i.RawGroup))
            .ToList();
        if (list.Any(e => e.Mode is not null))
            SupportsPermissions = true;
        return list;
    }

    /// <summary>SITE CHMOD, que entienden casi todos los servidores Unix.</summary>
    public Task ChangeModeAsync(string path, int mode, CancellationToken cancellationToken) =>
        _ftp.Chmod(path, Convert.ToInt32(Convert.ToString(mode, 8)), cancellationToken);

    /// <summary>SITE CHOWN: solo lo admiten algunos servidores (ProFTPD con mod_site_misc, pure-ftpd…); si no, se dice.</summary>
    public async Task ChangeOwnerAsync(string path, string owner, string group, CancellationToken cancellationToken)
    {
        var spec = group.Length > 0 ? $"{owner}:{group}" : owner;
        var reply = await _ftp.Execute($"SITE CHOWN {spec} {path}", cancellationToken);
        if (!reply.Success)
            throw new IOException(reply.Message.Length > 0 ? reply.Message : "SITE CHOWN");
    }

    public Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken) =>
        UseAsync(async () => { await DownloadCoreAsync(remotePath, localPath, progress, cancellationToken); return true; });

    private async Task DownloadCoreAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken)
    {
        long previous = 0;
        var p = new Progress<FtpProgress>(x => { progress.Report(x.TransferredBytes - previous); previous = x.TransferredBytes; });
        var status = await _ftp.DownloadFile(localPath, remotePath, FtpLocalExists.Overwrite, FtpVerify.None, p, cancellationToken);
        if (status == FtpStatus.Failed)
            throw new IOException($"FTP: {RemotePath.Name(remotePath)}");
    }

    public Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken) =>
        UseAsync(async () => { await UploadCoreAsync(localPath, remotePath, progress, cancellationToken); return true; });

    private async Task UploadCoreAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken)
    {
        long previous = 0;
        var p = new Progress<FtpProgress>(x => { progress.Report(x.TransferredBytes - previous); previous = x.TransferredBytes; });
        var status = await _ftp.UploadFile(localPath, remotePath, FtpRemoteExists.Overwrite, false, FtpVerify.None, p, cancellationToken);
        if (status == FtpStatus.Failed)
            throw new IOException($"FTP: {RemotePath.Name(remotePath)}");
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken) => UseAsync(() => _ftp.CreateDirectory(path, cancellationToken));

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken) => UseAsync(async () => { await _ftp.DeleteFile(path, cancellationToken); return true; });

    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken) => UseAsync(async () => { await _ftp.DeleteDirectory(path, cancellationToken); return true; });

    public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken) => UseAsync(async () => { await _ftp.Rename(path, newPath, cancellationToken); return true; });

    public void Dispose()
    {
        _keepAlive?.Dispose();
        try { _ftp.Disconnect().GetAwaiter().GetResult(); } catch (Exception) { }
        _ftp.Dispose();
    }
}
