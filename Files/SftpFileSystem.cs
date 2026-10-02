using System.IO;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using SocRcManager.Models;

namespace SocRcManager.Files;

/// <summary>
/// Ficheros por SSH con SSH.NET: se navega por SFTP y, si la conexion lo pide, las transferencias
/// van por SCP (el protocolo antiguo, que algunos servidores tienen mas rapido o es lo unico que
/// dejan usar). SCP no sabe listar directorios: por eso el listado va siempre por SFTP.
/// </summary>
public sealed class SftpFileSystem : IRemoteFileSystem
{
    private readonly ISftpClient _sftp;
    private readonly IScpChannel? _scp;
    private readonly Connection _connection;
    private readonly string _password;
    private readonly SshClients _clients;

    private SftpFileSystem(Connection connection, string password, SshClients clients, ISftpClient sftp, IScpChannel? scp)
    {
        _connection = connection;
        _password = password;
        _clients = clients;
        _sftp = sftp;
        _scp = scp;
        InitialDirectory = sftp.WorkingDirectory;
    }

    public bool SupportsPermissions => true;

    public Task ChangeModeAsync(string path, int mode, CancellationToken cancellationToken) =>
        Task.Run(() => _sftp.ChangePermissions(path, (short)mode), cancellationToken);

    /// <summary>
    /// SFTP solo cambia el propietario por numero (uid/gid). Con nombres se lanza un
    /// <c>chown</c> por SSH con las mismas credenciales; si el servidor no deja ejecutar comandos
    /// y se dieron numeros, se hace por SFTP.
    /// </summary>
    public Task ChangeOwnerAsync(string path, string owner, string group, CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (int.TryParse(owner, out var uid) && (group.Length == 0 || int.TryParse(group, out _)))
        {
            var attrs = _sftp.GetAttributes(path);
            attrs.UserId = uid;
            if (group.Length > 0)
                attrs.GroupId = int.Parse(group);
            _sftp.SetAttributes(path, attrs);
            return;
        }

        var spec = group.Length > 0 ? $"{owner}:{group}" : owner;
        var (status, error) = _clients.Run(SshAuth.Build(_connection, _password), $"chown {Quote(spec)} {Quote(path)}");
        if (status != 0)
            throw new IOException(error.Trim().Length > 0 ? error.Trim() : $"chown: {status}");
    }, cancellationToken);

    /// <summary>Entre comillas simples para el shell (una comilla dentro se cierra, se escapa y se reabre).</summary>
    public static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    public string InitialDirectory { get; }

    public static Task<SftpFileSystem> ConnectAsync(Connection connection, string password, CancellationToken cancellationToken) =>
        ConnectAsync(connection, password, SshClients.Real, cancellationToken);

    /// <summary>Lo mismo con los clientes que se le den (los de verdad o, en las pruebas, dobles).</summary>
    internal static async Task<SftpFileSystem> ConnectAsync(Connection connection, string password, SshClients clients, CancellationToken cancellationToken)
    {
        var sftp = clients.Sftp(SshAuth.Build(connection, password));
        sftp.OperationTimeout = TimeSpan.FromSeconds(Math.Max(5, connection.FilesTimeoutSeconds));
        if (connection.FilesKeepAliveSeconds > 0)
            sftp.KeepAliveInterval = TimeSpan.FromSeconds(connection.FilesKeepAliveSeconds);
        await Task.Run(sftp.Connect, cancellationToken);

        IScpChannel? scp = null;
        if (connection.UseScp)
        {
            scp = clients.Scp(SshAuth.Build(connection, password));
            await Task.Run(scp.Connect, cancellationToken);
        }
        return new SftpFileSystem(connection, password, clients, sftp, scp);
    }

    public Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var entries = new List<FileEntry>();
        foreach (ISftpFile f in _sftp.ListDirectory(path))
        {
            if (f.Name is "." or "..")
                continue;
            // Un enlace simbolico a un directorio se trata como directorio (si se puede saber).
            var isDir = f.IsDirectory;
            if (f.IsSymbolicLink)
            {
                try { isDir = _sftp.GetAttributes(f.FullName).IsDirectory; } catch (Exception) { }
            }
            entries.Add(ToEntry(f, isDir));
        }
        return (IReadOnlyList<FileEntry>)entries;
    }, cancellationToken);

    /// <summary>Una entrada del listado SFTP, con sus bits rwx y el uid/gid como propietario y grupo.</summary>
    public static FileEntry ToEntry(ISftpFile f, bool isDirectory)
    {
        var mode = (f.OwnerCanRead ? 0x100 : 0) | (f.OwnerCanWrite ? 0x80 : 0) | (f.OwnerCanExecute ? 0x40 : 0)
                 | (f.GroupCanRead ? 0x20 : 0) | (f.GroupCanWrite ? 0x10 : 0) | (f.GroupCanExecute ? 0x08 : 0)
                 | (f.OthersCanRead ? 0x04 : 0) | (f.OthersCanWrite ? 0x02 : 0) | (f.OthersCanExecute ? 0x01 : 0);
        return new FileEntry(f.Name, f.FullName, isDirectory, f.Length, f.LastWriteTime, mode, f.UserId.ToString(), f.GroupId.ToString());
    }

    public Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var file = File.Create(localPath);
        if (_scp is not null)
        {
            long last = 0;
            void OnProgress(object? _, Renci.SshNet.Common.ScpDownloadEventArgs e) { progress.Report(e.Downloaded - last); last = e.Downloaded; }
            _scp.Downloading += OnProgress;
            try { _scp.Download(remotePath, file); }
            finally { _scp.Downloading -= OnProgress; }
            return;
        }
        long previous = 0;
        _sftp.DownloadFile(remotePath, file, done => { progress.Report((long)done - previous); previous = (long)done; });
    }, cancellationToken);

    public Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var file = File.OpenRead(localPath);
        if (_scp is not null)
        {
            long last = 0;
            void OnProgress(object? _, Renci.SshNet.Common.ScpUploadEventArgs e) { progress.Report(e.Uploaded - last); last = e.Uploaded; }
            _scp.Uploading += OnProgress;
            try { _scp.Upload(file, remotePath); }
            finally { _scp.Uploading -= OnProgress; }
            return;
        }
        long previous = 0;
        _sftp.UploadFile(file, remotePath, true, done => { progress.Report((long)done - previous); previous = (long)done; });
    }, cancellationToken);

    public Task<FileEntry?> StatAsync(string path, CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            var a = _sftp.GetAttributes(path);
            return (FileEntry?)new FileEntry(RemotePath.Name(path), path, a.IsDirectory, a.Size, a.LastWriteTime);
        }
        catch (Renci.SshNet.Common.SftpPathNotFoundException) { return null; }
    }, cancellationToken);

    public Task SetModifiedAsync(string path, DateTime modified, CancellationToken cancellationToken) =>
        Task.Run(() => _sftp.SetLastWriteTime(path, modified), cancellationToken);

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken) => Task.Run(() => _sftp.CreateDirectory(path), cancellationToken);

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken) => Task.Run(() => _sftp.DeleteFile(path), cancellationToken);

    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken) => Task.Run(() => _sftp.DeleteDirectory(path), cancellationToken);

    public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken) => Task.Run(() => _sftp.RenameFile(path, newPath), cancellationToken);

    public void Dispose()
    {
        // Se liberan aunque la desconexion falle (la conexion ya rota no debe dejar el cliente vivo).
        try { _scp?.Disconnect(); } catch (Exception) { }
        try { _scp?.Dispose(); } catch (Exception) { }
        try { _sftp.Disconnect(); } catch (Exception) { }
        try { _sftp.Dispose(); } catch (Exception) { }
    }
}

/// <summary>SCP: lo que se usa de <see cref="ScpClient"/>, para poder sustituirlo en las pruebas.</summary>
public interface IScpChannel : IDisposable
{
    event EventHandler<Renci.SshNet.Common.ScpDownloadEventArgs>? Downloading;
    event EventHandler<Renci.SshNet.Common.ScpUploadEventArgs>? Uploading;
    void Connect();
    void Disconnect();
    void Download(string filename, Stream destination);
    void Upload(Stream source, string path);
}

/// <summary>El <see cref="ScpClient"/> de verdad ya tiene todo lo de <see cref="IScpChannel"/>.</summary>
internal sealed class ScpChannel(ConnectionInfo info) : ScpClient(info), IScpChannel;

/// <summary>
/// Como se crean los clientes SSH: SFTP, SCP y una orden suelta por SSH (chown). Los de verdad
/// estan en <see cref="Real"/>; las pruebas dan los suyos y nunca tocan un servidor.
/// </summary>
internal sealed record SshClients(Func<ConnectionInfo, ISftpClient> Sftp, Func<ConnectionInfo, IScpChannel> Scp, Func<ConnectionInfo, string, (int Status, string Error)> Run)
{
    public static readonly SshClients Real = new(info => new SftpClient(info), info => new ScpChannel(info), RunCommand);

    /// <summary>Una orden por SSH con su propia conexion: codigo de salida y lo que dijo por la salida de error.</summary>
    internal static (int Status, string Error) RunCommand(ConnectionInfo info, string command)
    {
        using var ssh = new SshClient(info);
        ssh.Connect();
        using var cmd = ssh.RunCommand(command);
        return (cmd.ExitStatus ?? -1, cmd.Error);
    }
}

/// <summary>La autenticacion SSH que comparten el terminal y los ficheros: clave privada, contraseña o teclado interactivo.</summary>
public static class SshAuth
{
    public static ConnectionInfo Build(Connection connection, string password)
    {
        var user = connection.UserName;
        var methods = new List<AuthenticationMethod>();
        if (connection.PrivateKeyPath.Length > 0 && File.Exists(connection.PrivateKeyPath))
        {
            var key = password.Length > 0 ? new PrivateKeyFile(connection.PrivateKeyPath, password) : new PrivateKeyFile(connection.PrivateKeyPath);
            methods.Add(new PrivateKeyAuthenticationMethod(user, key));
        }
        if (password.Length > 0)
        {
            methods.Add(new PasswordAuthenticationMethod(user, password));
            var kbd = new KeyboardInteractiveAuthenticationMethod(user);
            kbd.AuthenticationPrompt += (_, e) =>
            {
                foreach (var prompt in e.Prompts)
                    prompt.Response = password;
            };
            methods.Add(kbd);
        }
        if (methods.Count == 0)
            throw new InvalidOperationException(Localization.Loc.Get("SshNoCredentials"));

        return new ConnectionInfo(connection.Host, connection.Port, user, methods.ToArray()) { Timeout = TimeSpan.FromSeconds(Math.Max(5, connection.FilesTimeoutSeconds)) };
    }
}
