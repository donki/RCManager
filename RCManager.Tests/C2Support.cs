using System.Reflection;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using SocRcManager.Files;

namespace SocRcManager.Tests;

/// <summary>
/// Un servidor SFTP en memoria detras de <see cref="ISftpClient"/> (con DispatchProxy: solo lo que
/// usa la aplicacion; cualquier otra llamada falla la prueba). Ficheros, directorios, permisos,
/// uid/gid, fechas y enlaces simbolicos, y un diario de lo que se pide.
/// </summary>
public sealed class MemorySftp
{
    public string WorkingDirectory { get; set; } = "/home/pepe";
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Dirs { get; } = new(StringComparer.Ordinal) { "/" };
    /// <summary>Modo rwx, uid y gid de cada ruta (si no esta, 644/755 y 0/0).</summary>
    public Dictionary<string, (int Mode, int Uid, int Gid)> Attrs { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, DateTime> Modified { get; } = new(StringComparer.Ordinal);
    /// <summary>Enlaces simbolicos: ruta → a donde apuntan (null = roto).</summary>
    public Dictionary<string, string?> Links { get; } = new(StringComparer.Ordinal);
    /// <summary>Rutas en las que el servidor dice «permiso denegado».</summary>
    public HashSet<string> Denied { get; } = new(StringComparer.Ordinal);
    public List<string> Log { get; } = [];
    public TimeSpan? OperationTimeout { get; private set; }
    public TimeSpan? KeepAliveInterval { get; private set; }
    public bool Connected { get; private set; }
    public bool Disposed { get; private set; }
    public Exception? ConnectError { get; set; }
    public Exception? DisconnectError { get; set; }
    /// <summary>Bytes por aviso de progreso en las transferencias.</summary>
    public int Chunk { get; set; } = 4;

    public ISftpClient Client()
    {
        var proxy = DispatchProxy.Create<ISftpClient, SftpProxy>();
        ((SftpProxy)(object)proxy).Model = this;
        return proxy;
    }

    public MemorySftp Dir(string path, int mode = 0b111_101_101, int uid = 0, int gid = 0)
    {
        Dirs.Add(path);
        Attrs[path] = (mode, uid, gid);
        return this;
    }

    public MemorySftp File(string path, string content, int mode = 0b110_100_100, int uid = 1000, int gid = 1000)
    {
        Files[path] = System.Text.Encoding.UTF8.GetBytes(content);
        Attrs[path] = (mode, uid, gid);
        return this;
    }

    public string Text(string path) => System.Text.Encoding.UTF8.GetString(Files[path]);

    private static string ParentOf(string path) => RemotePath.Parent(path);

    private void Check(string path)
    {
        if (Denied.Contains(path))
            throw new SftpPermissionDeniedException("Permission denied");
    }

    private bool IsDir(string path) => Dirs.Contains(path);

    private (int Mode, int Uid, int Gid) AttrsOf(string path) =>
        Attrs.TryGetValue(path, out var a) ? a : (IsDir(path) ? 0b111_101_101 : 0b110_100_100, 0, 0);

    /// <summary>Unos atributos SFTP como los de un servidor (el constructor es interno en SSH.NET).</summary>
    public static SftpFileAttributes Attributes(bool directory, long size, int mode, int uid, int gid, DateTime modifiedUtc)
    {
        var ctor = typeof(SftpFileAttributes).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(c => c.GetParameters().Length == 7);
        var type = directory ? 0x4000u : 0x8000u;
        return (SftpFileAttributes)ctor.Invoke([modifiedUtc, modifiedUtc, size, uid, gid, type | (uint)mode, new Dictionary<string, string>()]);
    }

    private SftpFileAttributes Stat(string path)
    {
        Check(path);
        if (Links.TryGetValue(path, out var target))
        {
            if (target is null)
                throw new SftpPathNotFoundException("No such file");
            path = target;
        }
        if (!IsDir(path) && !Files.ContainsKey(path))
            throw new SftpPathNotFoundException("No such file");
        var (mode, uid, gid) = AttrsOf(path);
        var when = Modified.TryGetValue(path, out var m) ? m : new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Attributes(IsDir(path), Files.TryGetValue(path, out var b) ? b.Length : 0, mode, uid, gid, when);
    }

    private ISftpFile Entry(string name, string fullName, bool isDir, bool isLink, long size, int mode, int uid, int gid, DateTime when)
    {
        var f = DispatchProxy.Create<ISftpFile, SftpTests.FileProxy>();
        var v = ((SftpTests.FileProxy)(object)f).Values;
        v["Name"] = name; v["FullName"] = fullName; v["IsDirectory"] = isDir; v["IsSymbolicLink"] = isLink;
        v["Length"] = size; v["LastWriteTime"] = when; v["UserId"] = uid; v["GroupId"] = gid;
        string[] bits = ["OwnerCanRead", "OwnerCanWrite", "OwnerCanExecute", "GroupCanRead", "GroupCanWrite", "GroupCanExecute", "OthersCanRead", "OthersCanWrite", "OthersCanExecute"];
        for (var i = 0; i < 9; i++)
            v[bits[i]] = (mode & UnixMode.Bit(i)) != 0;
        return f;
    }

    private IEnumerable<ISftpFile> List(string path)
    {
        Check(path);
        if (!IsDir(path))
            throw new SftpPathNotFoundException("No such directory");
        var list = new List<ISftpFile>
        {
            Entry(".", RemotePath.Combine(path, "."), true, false, 0, 0b111_101_101, 0, 0, DateTime.Now),
            Entry("..", RemotePath.Combine(path, ".."), true, false, 0, 0b111_101_101, 0, 0, DateTime.Now),
        };
        foreach (var d in Dirs.Where(d => d != "/" && d != path && ParentOf(d) == path).Order(StringComparer.Ordinal))
        {
            var (mode, uid, gid) = AttrsOf(d);
            list.Add(Entry(RemotePath.Name(d), d, true, false, 0, mode, uid, gid, DateTime.Now));
        }
        foreach (var (f, bytes) in Files.Where(f => ParentOf(f.Key) == path).OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var (mode, uid, gid) = AttrsOf(f);
            list.Add(Entry(RemotePath.Name(f), f, false, false, bytes.Length, mode, uid, gid, new DateTime(2026, 3, 4, 5, 6, 7)));
        }
        foreach (var l in Links.Keys.Where(l => ParentOf(l) == path).Order(StringComparer.Ordinal))
            list.Add(Entry(RemotePath.Name(l), l, false, true, 0, 0b111_111_111, 0, 0, DateTime.Now));
        return list;
    }

    internal object? Call(MethodInfo method, object?[] a)
    {
        string P(int i) => (string)a[i]!;
        switch (method.Name)
        {
            case "get_WorkingDirectory": return WorkingDirectory;
            case "set_OperationTimeout": OperationTimeout = (TimeSpan)a[0]!; return null;
            case "set_KeepAliveInterval": KeepAliveInterval = (TimeSpan)a[0]!; return null;
            case "Connect":
                Log.Add("connect");
                if (ConnectError is not null) throw ConnectError;
                Connected = true;
                return null;
            case "Disconnect":
                Log.Add("disconnect");
                if (DisconnectError is not null) throw DisconnectError;
                Connected = false;
                return null;
            case "Dispose": Log.Add("dispose"); Disposed = true; return null;
            case "ListDirectory": Log.Add("ls " + P(0)); return List(P(0));
            case "GetAttributes": Log.Add("stat " + P(0)); return Stat(P(0));
            case "SetAttributes":
            {
                Log.Add("setstat " + P(0));
                Check(P(0));
                var attrs = (SftpFileAttributes)a[1]!;
                var (mode, _, _) = AttrsOf(P(0));
                Attrs[P(0)] = (mode, attrs.UserId, attrs.GroupId);
                return null;
            }
            case "ChangePermissions":
            {
                Log.Add($"chmod {UnixMode.ToOctal((short)a[1]!)} {P(0)}");
                Check(P(0));
                var (_, uid, gid) = AttrsOf(P(0));
                Attrs[P(0)] = ((short)a[1]!, uid, gid);
                return null;
            }
            case "SetLastWriteTime": Log.Add("touch " + P(0)); Check(P(0)); Modified[P(0)] = (DateTime)a[1]!; return null;
            case "CreateDirectory":
                Log.Add("mkdir " + P(0));
                Check(P(0));
                if (IsDir(P(0)) || Files.ContainsKey(P(0))) throw new SshException("Failure");
                Dirs.Add(P(0));
                return null;
            case "DeleteFile":
                Log.Add("rm " + P(0));
                Check(P(0));
                if (!Files.Remove(P(0))) throw new SftpPathNotFoundException("No such file");
                return null;
            case "DeleteDirectory":
                Log.Add("rmdir " + P(0));
                Check(P(0));
                if (Files.Keys.Concat(Dirs).Any(x => x != P(0) && ParentOf(x) == P(0))) throw new SshException("Directory not empty");
                if (!Dirs.Remove(P(0))) throw new SftpPathNotFoundException("No such directory");
                return null;
            case "RenameFile":
                Log.Add($"mv {P(0)} {P(1)}");
                Check(P(0));
                if (Files.Remove(P(0), out var moved)) { Files[P(1)] = moved; return null; }
                if (Dirs.Remove(P(0))) { Dirs.Add(P(1)); return null; }
                throw new SftpPathNotFoundException("No such file");
            case "DownloadFile":
            {
                Log.Add("get " + P(0));
                Check(P(0));
                if (!Files.TryGetValue(P(0), out var bytes)) throw new SftpPathNotFoundException("No such file");
                var stream = (Stream)a[1]!;
                var progress = (Action<ulong>?)a[2];
                for (var done = 0; done < bytes.Length;)
                {
                    var n = Math.Min(Chunk, bytes.Length - done);
                    stream.Write(bytes, done, n);
                    done += n;
                    progress?.Invoke((ulong)done);
                }
                return null;
            }
            case "UploadFile":
            {
                var stream = (Stream)a[0]!;
                var path = P(1);
                Log.Add("put " + path);
                Check(path);
                if (!IsDir(ParentOf(path))) throw new SftpPathNotFoundException("No such directory");
                var progress = (Action<ulong>?)a[^1];
                var ms = new MemoryStream();
                var buffer = new byte[Chunk];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                    progress?.Invoke((ulong)ms.Length);
                }
                Files[path] = ms.ToArray();
                return null;
            }
            default:
                throw new NotSupportedException("El doble de SFTP no sabe hacer " + method.Name);
        }
    }

    public class SftpProxy : DispatchProxy
    {
        internal MemorySftp Model { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Model.Call(targetMethod!, args ?? []);
    }
}

/// <summary>SCP en memoria sobre los mismos ficheros que <see cref="MemorySftp"/>, con sus eventos de progreso.</summary>
public sealed class MemoryScp(MemorySftp server) : IScpChannel
{
    public event EventHandler<ScpDownloadEventArgs>? Downloading;
    public event EventHandler<ScpUploadEventArgs>? Uploading;
    public List<string> Log { get; } = [];
    public bool Connected { get; private set; }
    public bool Disposed { get; private set; }
    public Exception? DisconnectError { get; set; }
    public bool HasListeners => Downloading is not null || Uploading is not null;

    public void Connect() { Log.Add("connect"); Connected = true; }

    public void Disconnect()
    {
        Log.Add("disconnect");
        if (DisconnectError is not null) throw DisconnectError;
        Connected = false;
    }

    public void Dispose() { Log.Add("dispose"); Disposed = true; }

    public void Download(string filename, Stream destination)
    {
        Log.Add("scp get " + filename);
        if (!server.Files.TryGetValue(filename, out var bytes))
            throw new ScpException("scp: " + filename + ": No such file or directory");
        for (var done = 0; done < bytes.Length;)
        {
            var n = Math.Min(server.Chunk, bytes.Length - done);
            destination.Write(bytes, done, n);
            done += n;
            Downloading?.Invoke(this, new ScpDownloadEventArgs(RemotePath.Name(filename), bytes.Length, done));
        }
    }

    public void Upload(Stream source, string path)
    {
        Log.Add("scp put " + path);
        var ms = new MemoryStream();
        source.CopyTo(ms);
        var bytes = ms.ToArray();
        for (var done = 0; done < bytes.Length;)
        {
            done = Math.Min(done + server.Chunk, bytes.Length);
            Uploading?.Invoke(this, new ScpUploadEventArgs(RemotePath.Name(path), bytes.Length, done));
        }
        server.Files[path] = bytes;
    }
}

/// <summary>Suma lo que llega por el progreso (de forma sincrona, sin pasar por un contexto) y apunta cada aviso.</summary>
public sealed class ProgressLog : IProgress<long>
{
    public List<long> Reports { get; } = [];
    public long Total => Reports.Sum();

    public void Report(long value)
    {
        lock (Reports) Reports.Add(value);
    }
}

/// <summary>
/// Un servidor de ficheros en memoria que de verdad baja y sube contenidos (para el editor): lo que
/// se sube queda en <see cref="Contents"/> y en <see cref="Uploads"/>.
/// </summary>
public sealed class ContentRemote : IRemoteFileSystem
{
    public Dictionary<string, byte[]> Contents { get; } = new(StringComparer.Ordinal);
    public List<(string Path, byte[] Bytes)> Uploads { get; } = [];
    public Exception? DownloadError { get; set; }
    public Exception? UploadError { get; set; }
    /// <summary>Si esta, la subida espera a que se complete (para ver el estado «Guardando…»).</summary>
    public TaskCompletionSource? UploadGate { get; set; }

    public string InitialDirectory => "/";
    public bool SupportsPermissions => true;

    public static FileEntry Entry(string path) => new(RemotePath.Name(path), path, false, 0, null);

    public Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken)
    {
        if (DownloadError is not null)
            throw DownloadError;
        File.WriteAllBytes(localPath, Contents[remotePath]);
        progress.Report(Contents[remotePath].Length);
        return Task.CompletedTask;
    }

    public async Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(localPath, cancellationToken);
        if (UploadGate is not null)
            await UploadGate.Task;
        if (UploadError is not null)
            throw UploadError;
        Contents[remotePath] = bytes;
        Uploads.Add((remotePath, bytes));
    }

    public Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task DeleteFileAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<FileEntry?> StatAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task SetModifiedAsync(string path, DateTime modified, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task ChangeModeAsync(string path, int mode, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task ChangeOwnerAsync(string path, string owner, string group, CancellationToken cancellationToken) => throw new NotSupportedException();
    public void Dispose() { }
}

/// <summary>Teclas de verdad (eventos de teclado enrutados) sobre un elemento de una ventana enseñada.</summary>
public static class C2Keys
{
    public static System.Windows.Input.KeyEventArgs Press(System.Windows.UIElement target, System.Windows.Input.Key key, System.Windows.RoutedEvent routedEvent)
    {
        var source = System.Windows.PresentationSource.FromVisual(target) ?? throw new InvalidOperationException("La ventana no esta enseñada");
        var e = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = routedEvent };
        target.RaiseEvent(e);
        return e;
    }
}
