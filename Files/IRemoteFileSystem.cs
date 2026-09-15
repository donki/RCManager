namespace SocRcManager.Files;

/// <summary>Una entrada de un directorio, local o remoto.</summary>
public sealed record FileEntry(string Name, string FullPath, bool IsDirectory, long Size, DateTime? Modified);

/// <summary>
/// Lo que el explorador de ficheros necesita del otro lado, sea SFTP/SCP o FTP/FTPS. Las rutas
/// remotas van siempre con «/».
/// </summary>
public interface IRemoteFileSystem : IDisposable
{
    /// <summary>Directorio de trabajo con el que se entra (el home en SFTP, la raiz en FTP).</summary>
    string InitialDirectory { get; }

    Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken cancellationToken);

    /// <summary>Baja un fichero al disco. El progreso llega en bytes transferidos.</summary>
    Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken);

    Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken);

    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken);

    Task DeleteFileAsync(string path, CancellationToken cancellationToken);

    /// <summary>Borra un directorio vacio (el explorador vacia antes lo de dentro).</summary>
    Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken);

    Task RenameAsync(string path, string newPath, CancellationToken cancellationToken);
}

public static class RemotePath
{
    public static string Combine(string directory, string name) =>
        directory.EndsWith('/') ? directory + name : directory + "/" + name;

    public static string Parent(string path)
    {
        var trimmed = path.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash <= 0 ? "/" : trimmed[..slash];
    }

    public static string Name(string path)
    {
        var trimmed = path.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }
}
