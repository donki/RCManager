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
        ftp.Config.ConnectTimeout = 20000;
        ftp.Config.DataConnectionType = FtpDataConnectionType.AutoPassive;
        await ftp.Connect(cancellationToken);
        var initial = await ftp.GetWorkingDirectory(cancellationToken);
        return new FtpFileSystem(ftp, string.IsNullOrEmpty(initial) ? "/" : initial);
    }

    public async Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken cancellationToken)
    {
        var items = await _ftp.GetListing(path, cancellationToken);
        return items
            .Where(i => i.Name is not ("." or ".."))
            .Select(i => new FileEntry(i.Name, i.FullName, i.Type is FtpObjectType.Directory or FtpObjectType.Link, i.Size,
                i.Modified == DateTime.MinValue ? null : i.Modified))
            .ToList();
    }

    public async Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken)
    {
        long previous = 0;
        var p = new Progress<FtpProgress>(x => { progress.Report(x.TransferredBytes - previous); previous = x.TransferredBytes; });
        var status = await _ftp.DownloadFile(localPath, remotePath, FtpLocalExists.Overwrite, FtpVerify.None, p, cancellationToken);
        if (status == FtpStatus.Failed)
            throw new IOException($"FTP: {RemotePath.Name(remotePath)}");
    }

    public async Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken)
    {
        long previous = 0;
        var p = new Progress<FtpProgress>(x => { progress.Report(x.TransferredBytes - previous); previous = x.TransferredBytes; });
        var status = await _ftp.UploadFile(localPath, remotePath, FtpRemoteExists.Overwrite, false, FtpVerify.None, p, cancellationToken);
        if (status == FtpStatus.Failed)
            throw new IOException($"FTP: {RemotePath.Name(remotePath)}");
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken) => _ftp.CreateDirectory(path, cancellationToken);

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken) => _ftp.DeleteFile(path, cancellationToken);

    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken) => _ftp.DeleteDirectory(path, cancellationToken);

    public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken) => _ftp.Rename(path, newPath, cancellationToken);

    public void Dispose()
    {
        try { _ftp.Disconnect().GetAwaiter().GetResult(); } catch (Exception) { }
        _ftp.Dispose();
    }
}
