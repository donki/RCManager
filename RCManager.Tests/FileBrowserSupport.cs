using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SocRcManager.Files;

namespace SocRcManager.Tests;

/// <summary>
/// Un servidor de ficheros en memoria mas completo que <see cref="FakeRemote"/>: guarda contenidos,
/// sube y baja de verdad (a disco local), informa del progreso, puede fallar a peticion y esperar a
/// que la prueba le deje seguir. Varias conexiones (<see cref="Another"/>) comparten el mismo servidor.
/// </summary>
public sealed class MemServer : IRemoteFileSystem
{
    private sealed class State
    {
        public readonly Dictionary<string, (byte[] Data, DateTime? Modified, int? Mode, string? Owner, string? Group)> Files = new(StringComparer.Ordinal);
        public readonly Dictionary<string, (int? Mode, string? Owner, string? Group)> Dirs = new(StringComparer.Ordinal);
        public readonly List<string> Log = [];
        public readonly Dictionary<string, Exception> Throw = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> FailTimes = new(StringComparer.Ordinal);
        public readonly List<MemServer> Connections = [];
        public TaskCompletionSource? Hold;
        public int Running;
        public int MaxRunning;
    }

    private readonly State _s;

    public MemServer(string initial = "/home/u")
    {
        _s = new State();
        InitialDirectory = initial;
        _s.Dirs["/"] = (null, null, null);
        _s.Connections.Add(this);
        MkdirAll(initial);
    }

    private MemServer(State s, string initial)
    {
        _s = s;
        InitialDirectory = initial;
        s.Connections.Add(this);
    }

    /// <summary>Otra conexion al mismo servidor.</summary>
    public MemServer Another() => new(_s, InitialDirectory);

    public string InitialDirectory { get; set; }
    public bool SupportsPermissions { get; set; } = true;
    public bool Disposed { get; private set; }
    public List<string> Log => _s.Log;
    public IReadOnlyList<MemServer> Connections => _s.Connections;

    /// <summary>Cuantas subidas/bajadas llegaron a ir a la vez.</summary>
    public int MaxRunning => _s.MaxRunning;

    /// <summary>La operacion («list», «get», «put», «mkdir», «rm», «rmdir», «mv», «stat», «touch», «chmod», «chown») lanza esto.</summary>
    public void Fail(string op, Exception ex) => _s.Throw[op] = ex;

    /// <summary>Las proximas <paramref name="times"/> subidas/bajadas de esa ruta fallan (a medias, tras contar progreso).</summary>
    public void FailTimes(string remotePath, int times) => _s.FailTimes[remotePath] = times;

    /// <summary>Las subidas y bajadas se quedan esperando hasta <see cref="Release"/>.</summary>
    public void HoldTransfers() => _s.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _s.Hold?.TrySetResult();

    public MemServer File(string path, string content, DateTime? modified = null, int? mode = null, string? owner = null, string? group = null)
    {
        MkdirAll(RemotePath.Parent(path));
        _s.Files[path] = (System.Text.Encoding.UTF8.GetBytes(content), modified, mode, owner, group);
        return this;
    }

    public MemServer Folder(string path, int? mode = null, string? owner = null, string? group = null)
    {
        MkdirAll(RemotePath.Parent(path));
        _s.Dirs[path] = (mode, owner, group);
        return this;
    }

    public bool HasFile(string path) => _s.Files.ContainsKey(path);
    public bool HasDir(string path) => _s.Dirs.ContainsKey(path);
    public string Text(string path) => System.Text.Encoding.UTF8.GetString(_s.Files[path].Data);
    public DateTime? ModifiedOf(string path) => _s.Files[path].Modified;

    private void MkdirAll(string path)
    {
        while (path.Length > 0 && !_s.Dirs.ContainsKey(path))
        {
            _s.Dirs[path] = (null, null, null);
            path = path == "/" ? string.Empty : RemotePath.Parent(path);
        }
    }

    private void Check(string op, string path)
    {
        lock (_s.Log)
            _s.Log.Add($"{op} {path}");
        if (_s.Throw.TryGetValue(op, out var ex))
            throw ex;
    }

    private static bool IsChild(string dir, string path) =>
        path != dir && RemotePath.Parent(path) == (dir.Length > 1 ? dir.TrimEnd('/') : "/");

    private FileEntry EntryOf(string path)
    {
        if (_s.Files.TryGetValue(path, out var f))
            return new FileEntry(RemotePath.Name(path), path, false, f.Data.Length, f.Modified, f.Mode, f.Owner, f.Group);
        var d = _s.Dirs[path];
        return new FileEntry(RemotePath.Name(path), path, true, 0, null, d.Mode, d.Owner, d.Group);
    }

    public Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken cancellationToken)
    {
        Check("list", path);
        if (!_s.Dirs.ContainsKey(path))
            throw new System.IO.DirectoryNotFoundException($"No such directory:\n{path}");
        var list = _s.Dirs.Keys.Where(d => IsChild(path, d)).Concat(_s.Files.Keys.Where(f => IsChild(path, f))).Select(EntryOf).ToList();
        return Task.FromResult<IReadOnlyList<FileEntry>>(list);
    }

    private async Task Transfer(string remotePath, byte[] data, IProgress<long> progress, CancellationToken token)
    {
        var running = Interlocked.Increment(ref _s.Running);
        lock (_s.Log)
            _s.MaxRunning = Math.Max(_s.MaxRunning, running);
        try
        {
            // La mitad, y si toca fallar, falla a medias.
            var half = data.Length / 2;
            progress.Report(half);
            if (_s.FailTimes.TryGetValue(remotePath, out var n) && n > 0)
            {
                _s.FailTimes[remotePath] = n - 1;
                throw new System.IO.IOException("Connection reset");
            }
            if (_s.Hold is { } hold)
                await hold.Task.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            progress.Report(data.Length - half);
        }
        finally
        {
            Interlocked.Decrement(ref _s.Running);
        }
    }

    public async Task DownloadAsync(string remotePath, string localPath, IProgress<long> progress, CancellationToken cancellationToken)
    {
        Check("get", remotePath);
        if (!_s.Files.TryGetValue(remotePath, out var f))
            throw new System.IO.FileNotFoundException("No such file", remotePath);
        await Transfer(remotePath, f.Data, progress, cancellationToken);
        await System.IO.File.WriteAllBytesAsync(localPath, f.Data, CancellationToken.None);
    }

    public async Task UploadAsync(string localPath, string remotePath, IProgress<long> progress, CancellationToken cancellationToken)
    {
        Check("put", remotePath);
        var data = await System.IO.File.ReadAllBytesAsync(localPath, CancellationToken.None);
        await Transfer(remotePath, data, progress, cancellationToken);
        lock (_s.Log)
            _s.Files[remotePath] = (data, null, null, null, null);
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        Check("mkdir", path);
        if (_s.Dirs.ContainsKey(path))
            throw new System.IO.IOException("File exists");
        _s.Dirs[path] = (null, null, null);
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        Check("rm", path);
        _s.Files.Remove(path);
        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        Check("rmdir", path);
        if (_s.Files.Keys.Any(f => IsChild(path, f)) || _s.Dirs.Keys.Any(d => IsChild(path, d)))
            throw new System.IO.IOException("Directory not empty");
        _s.Dirs.Remove(path);
        return Task.CompletedTask;
    }

    public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken)
    {
        Check("mv", $"{path} {newPath}");
        if (_s.Files.Remove(path, out var f))
            _s.Files[newPath] = f;
        else if (_s.Dirs.Remove(path, out var d))
            _s.Dirs[newPath] = d;
        return Task.CompletedTask;
    }

    public Task<FileEntry?> StatAsync(string path, CancellationToken cancellationToken)
    {
        Check("stat", path);
        return Task.FromResult(_s.Files.ContainsKey(path) || _s.Dirs.ContainsKey(path) ? EntryOf(path) : null);
    }

    public Task SetModifiedAsync(string path, DateTime modified, CancellationToken cancellationToken)
    {
        Check("touch", path);
        lock (_s.Log)
            _s.Files[path] = _s.Files[path] with { Modified = modified };
        return Task.CompletedTask;
    }

    public Task ChangeModeAsync(string path, int mode, CancellationToken cancellationToken)
    {
        Check("chmod", $"{UnixMode.ToOctal(mode)} {path}");
        return Task.CompletedTask;
    }

    public Task ChangeOwnerAsync(string path, string owner, string group, CancellationToken cancellationToken)
    {
        Check("chown", $"{owner}:{group} {path}");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Disposed = true;
        if (_s.Throw.TryGetValue("dispose", out var ex))
            throw ex;
    }
}

/// <summary>Teclas, raton y arrastrar sobre los controles de las pruebas (en el hilo de interfaz).</summary>
public static class PaneInput
{
    /// <summary>Pulsa una tecla en el elemento (KeyDown, como si tuviera el foco).</summary>
    public static bool Key(UIElement target, System.Windows.Input.Key key)
    {
        var source = PresentationSource.FromVisual(target) ?? throw new InvalidOperationException("El control no esta en una ventana enseñada");
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent };
        target.RaiseEvent(e);
        return e.Handled;
    }

    /// <summary>
    /// Boton izquierdo pulsado sobre ese elemento: PreviewMouseDown baja hasta el y cada elemento del
    /// camino lo convierte en PreviewMouseLeftButtonDown (que es directo).
    /// </summary>
    public static void MouseDown(DependencyObject over)
    {
        var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
        ((UIElement)FindUiElement(over)).RaiseEvent(e);
    }

    public static void DoubleClick(Control target)
    {
        var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent };
        target.RaiseEvent(e);
    }

    /// <summary>Mover el raton (sin boton: no arrastra).</summary>
    public static void MouseMove(UIElement target)
    {
        var e = new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent };
        target.RaiseEvent(e);
    }

    /// <summary>Lanza DragOver (o Drop) con esos datos sobre el elemento; devuelve los efectos que dejo y si se manejo.</summary>
    public static (DragDropEffects Effects, bool Handled) Drag(UIElement target, IDataObject data, bool drop)
    {
        var ctor = typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .First(c => c.GetParameters().Length == 5);
        var e = (DragEventArgs)ctor.Invoke([data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, target, new Point(1, 1)]);
        e.RoutedEvent = drop ? DragDrop.DropEvent : DragDrop.DragOverEvent;
        e.Effects = DragDropEffects.Copy | DragDropEffects.Move;
        target.RaiseEvent(e);
        return (e.Effects, e.Handled);
    }

    private static DependencyObject FindUiElement(DependencyObject o)
    {
        while (o is not UIElement)
            o = System.Windows.Media.VisualTreeHelper.GetParent(o) ?? LogicalTreeHelper.GetParent(o);
        return o;
    }
}
