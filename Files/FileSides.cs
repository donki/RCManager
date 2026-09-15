using System.IO;

namespace SocRcManager.Files;

/// <summary>
/// Un lado del explorador (este PC o el remoto), con lo justo para navegar y tocar ficheros. Las
/// transferencias entre lados las hace el explorador, que conoce los dos.
/// </summary>
public interface IFileSide
{
    bool IsLocal { get; }
    string InitialDirectory { get; }
    Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken cancellationToken);
    string Parent(string path);
    string Combine(string directory, string name);
    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken);
    Task DeleteFileAsync(string path, CancellationToken cancellationToken);
    Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken);
    Task RenameAsync(string path, string newPath, CancellationToken cancellationToken);
}

/// <summary>Este PC. La ruta vacia es «Este equipo»: la lista de unidades.</summary>
public sealed class LocalSide : IFileSide
{
    public LocalSide(string initial)
    {
        InitialDirectory = initial.Length > 0 && Directory.Exists(initial) ? initial : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public bool IsLocal => true;

    public string InitialDirectory { get; }

    public Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var entries = new List<FileEntry>();
        if (path.Length == 0)
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                entries.Add(new FileEntry(drive.VolumeLabel.Length > 0 ? $"{drive.Name.TrimEnd('\\')} {drive.VolumeLabel}" : drive.Name.TrimEnd('\\'), drive.RootDirectory.FullName, true, drive.TotalSize - drive.TotalFreeSpace, null));
            return (IReadOnlyList<FileEntry>)entries;
        }

        var dir = new DirectoryInfo(path);
        foreach (var d in dir.EnumerateDirectories())
            entries.Add(new FileEntry(d.Name, d.FullName, true, 0, d.LastWriteTime));
        foreach (var f in dir.EnumerateFiles())
            entries.Add(new FileEntry(f.Name, f.FullName, false, f.Length, f.LastWriteTime));
        return entries;
    }, cancellationToken);

    public string Parent(string path)
    {
        if (path.Length == 0)
            return string.Empty;
        var parent = Directory.GetParent(path);
        return parent?.FullName ?? string.Empty;   // desde la raiz de una unidad se va a «Este equipo»
    }

    public string Combine(string directory, string name) => Path.Combine(directory, name);

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken) => Task.Run(() => Directory.CreateDirectory(path), cancellationToken);

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken) => Task.Run(() => File.Delete(path), cancellationToken);

    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken) => Task.Run(() => Directory.Delete(path, true), cancellationToken);

    public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (Directory.Exists(path))
            Directory.Move(path, newPath);
        else
            File.Move(path, newPath, true);
    }, cancellationToken);
}

/// <summary>El servidor, detras de <see cref="IRemoteFileSystem"/>.</summary>
public sealed class RemoteSide(IRemoteFileSystem fs) : IFileSide
{
    public IRemoteFileSystem Fs { get; } = fs;

    public bool IsLocal => false;

    public string InitialDirectory => Fs.InitialDirectory;

    public Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken cancellationToken) => Fs.ListAsync(path, cancellationToken);

    public string Parent(string path) => RemotePath.Parent(path);

    public string Combine(string directory, string name) => RemotePath.Combine(directory, name);

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken) => Fs.CreateDirectoryAsync(path, cancellationToken);

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken) => Fs.DeleteFileAsync(path, cancellationToken);

    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken) => Fs.DeleteDirectoryAsync(path, cancellationToken);

    public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken) => Fs.RenameAsync(path, newPath, cancellationToken);
}
