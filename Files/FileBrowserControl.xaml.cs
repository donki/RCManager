using System.IO;
using System.Windows;
using System.Windows.Controls;
using SocRcManager.Localization;
using SocRcManager.Models;

namespace SocRcManager.Files;

/// <summary>
/// El explorador de dos paneles de una conexion de ficheros: este PC a la izquierda, el servidor a
/// la derecha, y las transferencias entre los dos (con carpetas enteras), con las opciones de la
/// conexion: varias a la vez (cada una con su propia conexion al servidor), que hacer si el
/// fichero ya existe, reintentos y conservar las fechas.
/// </summary>
public partial class FileBrowserControl : UserControl
{
    private IFileSide? _local;
    private RemoteSide? _remote;
    private Connection? _options;
    private Func<CancellationToken, Task<IRemoteFileSystem>>? _factory;
    private readonly List<IRemoteFileSystem> _pool = [];
    private CancellationTokenSource? _transfer;
    private readonly Queue<(FilePane From, FilePane To, IReadOnlyList<FileEntry> Entries)> _queue = new();
    private bool _busy;

    public FileBrowserControl()
    {
        InitializeComponent();
        CancelButton.ToolTip = Loc.Get("FilesCancel");
        LocalPane.TransferRequested += entries => Enqueue(LocalPane, RemotePane, entries);
        RemotePane.TransferRequested += entries => Enqueue(RemotePane, LocalPane, entries);
        LocalPane.DroppedFrom += (from, entries) => Enqueue(from, LocalPane, entries);
        RemotePane.DroppedFrom += (from, entries) => Enqueue(from, RemotePane, entries);
        LocalPane.Failed += m => Failed?.Invoke(m);
        RemotePane.Failed += m => Failed?.Invoke(m);
        RemotePane.EditRequested += entry => _ = EditRemoteAsync(entry);
        RemotePane.OpenExternalRequested += entry => _ = OpenRemoteExternalAsync(entry);
    }

    public event Action<string>? Failed;

    /// <summary>
    /// <paramref name="factory"/> abre otra conexion al servidor, para las transferencias en paralelo.
    /// </summary>
    public void Attach(IFileSide local, RemoteSide remote, string remoteTitle, Connection options, Func<CancellationToken, Task<IRemoteFileSystem>> factory)
    {
        _local = local;
        _remote = remote;
        _options = options;
        _factory = factory;
        LocalPane.ShowHidden = RemotePane.ShowHidden = options.FilesShowHidden;
        LocalPane.Attach(local, Loc.Get("FilesThisPc"), Loc.Get("FilesUpload"));
        RemotePane.Attach(remote, remoteTitle, Loc.Get("FilesDownload"));
    }

    public void FocusRemote() => RemotePane.Focus();

    public Task RemotePaneNavigateAsync(string path) => RemotePane.NavigateAsync(path);

    /// <summary>Tamaño de letra de los dos paneles (zoom de la pestaña).</summary>
    public double PaneFontSize
    {
        get => LocalPane.FontSize;
        set { LocalPane.FontSize = value; RemotePane.FontSize = value; }
    }

    // =====================================================================
    //  Transferencias
    // =====================================================================

    private void Enqueue(FilePane from, FilePane to, IReadOnlyList<FileEntry> entries)
    {
        if (entries.Count == 0 || to.CurrentPath.Length == 0)
            return;
        _queue.Enqueue((from, to, entries));
        if (!_busy)
            _ = RunQueueAsync();
    }

    private enum Conflict { Ask, Overwrite, Skip }

    private async Task RunQueueAsync()
    {
        _busy = true;
        _transfer = new CancellationTokenSource();
        var token = _transfer.Token;
        Progress.Visibility = CancelButton.Visibility = Visibility.Visible;
        var failed = false;
        var options = _options ?? new Connection();
        var parallel = Math.Clamp(options.TransferParallel, 1, 8);
        var retries = Math.Clamp(options.TransferRetries, 0, 5);
        try
        {
            while (_queue.Count > 0 && !token.IsCancellationRequested)
            {
                var (from, to, entries) = _queue.Dequeue();
                var upload = from.Side!.IsLocal;

                // 1. Medir: que ficheros, a donde, cuantos bytes.
                var plan = new List<(FileEntry Entry, string Target)>();
                long total = 0;
                foreach (var entry in entries)
                    total += await PlanAsync(from.Side!, to.Side!, entry, to.CurrentPath, plan, token);

                // 2. Conflictos: lo que ya existe en el destino, segun la opcion de la conexion.
                var policy = (Conflict)Math.Clamp(options.TransferOnConflict, 0, 2);
                var files = new List<(FileEntry Entry, string Target)>();
                Conflict? forAll = null;
                foreach (var (entry, target) in plan.Where(p => !p.Entry.IsDirectory))
                {
                    token.ThrowIfCancellationRequested();
                    var exists = upload ? await _remote!.Fs.StatAsync(target, token) is not null : File.Exists(target);
                    if (!exists)
                    {
                        files.Add((entry, target));
                        continue;
                    }
                    var decision = forAll ?? policy;
                    if (decision == Conflict.Ask)
                    {
                        var answer = ConflictWindow.Ask(Window.GetWindow(this)!, entry.Name);
                        if (answer.Cancel)
                            throw new OperationCanceledException();
                        decision = answer.Overwrite ? Conflict.Overwrite : Conflict.Skip;
                        if (answer.ApplyToAll)
                            forAll = decision;
                    }
                    if (decision == Conflict.Overwrite)
                        files.Add((entry, target));
                    else
                        total -= entry.Size;
                }

                // 3. Carpetas primero, en orden (una dentro de otra), en la conexion principal.
                foreach (var (entry, target) in plan.Where(p => p.Entry.IsDirectory))
                {
                    token.ThrowIfCancellationRequested();
                    try { await to.Side!.CreateDirectoryAsync(target, token); } catch (Exception) { /* ya existe */ }
                }

                // 4. Ficheros, N a la vez, cada uno con su conexion (los clientes SFTP/FTP no admiten
                //    dos operaciones a la vez en la misma conexion).
                long done = 0;
                var active = 0;
                void Report(string name) => Dispatcher.BeginInvoke(() =>
                {
                    Progress.Value = total > 0 ? Math.Min(100, Interlocked.Read(ref done) * 100.0 / total) : 0;
                    TransferText.Text = Loc.Format(upload ? "FilesUploading" : "FilesDownloading", name, FileRow.Format(Interlocked.Read(ref done)), FileRow.Format(total))
                        + (active > 1 ? $"  ·  ×{active}" : string.Empty);
                });

                using var gate = new SemaphoreSlim(parallel);
                var tasks = files.Select(async item =>
                {
                    await gate.WaitAsync(token);
                    var fs = await RentAsync(token);
                    Interlocked.Increment(ref active);
                    try
                    {
                        Report(item.Entry.Name);
                        long mine = 0;
                        var progress = new Progress<long>(delta =>
                        {
                            Interlocked.Add(ref done, delta);
                            Interlocked.Add(ref mine, delta);
                            Report(item.Entry.Name);
                        });
                        for (var attempt = 0; ; attempt++)
                        {
                            try
                            {
                                if (upload)
                                    await fs.UploadAsync(item.Entry.FullPath, item.Target, progress, token);
                                else
                                    await fs.DownloadAsync(item.Entry.FullPath, item.Target, progress, token);
                                break;
                            }
                            catch (Exception) when (attempt < retries && !token.IsCancellationRequested)
                            {
                                // Lo que se contó de este intento se descuenta y se vuelve a empezar.
                                Interlocked.Add(ref done, -Interlocked.Exchange(ref mine, 0));
                                await Task.Delay(1000, token);
                            }
                        }
                        if (options.TransferPreserveTimes && item.Entry.Modified is { } when)
                        {
                            try
                            {
                                if (upload) await fs.SetModifiedAsync(item.Target, when, token);
                                else File.SetLastWriteTime(item.Target, when);
                            }
                            catch (Exception) { /* no todos los servidores lo admiten */ }
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref active);
                        Return(fs);
                        gate.Release();
                    }
                }).ToList();
                await Task.WhenAll(tasks);
                await to.RefreshAsync();
            }
            TransferText.Text = token.IsCancellationRequested ? Loc.Get("FilesCancelled") : Loc.Get("FilesDone");
        }
        catch (OperationCanceledException)
        {
            TransferText.Text = Loc.Get("FilesCancelled");
        }
        catch (Exception ex)
        {
            failed = true;
            TransferText.Text = ex.Message.ReplaceLineEndings(" ");
            Failed?.Invoke(ex.Message);
        }
        finally
        {
            _queue.Clear();
            _busy = false;
            Progress.Value = 0;
            Progress.Visibility = CancelButton.Visibility = Visibility.Collapsed;
            if (failed)
            {
                await LocalPane.RefreshAsync();
                await RemotePane.RefreshAsync();
            }
        }
    }

    // Conexiones para transferir: la principal (la del panel) y las que se abran ademas, hasta
    // el numero de transferencias a la vez. Se reutilizan.
    private readonly Queue<IRemoteFileSystem> _idle = new();
    private readonly SemaphoreSlim _poolGate = new(1);

    private async Task<IRemoteFileSystem> RentAsync(CancellationToken token)
    {
        await _poolGate.WaitAsync(token);
        try
        {
            if (_idle.Count > 0)
                return _idle.Dequeue();
            if (_pool.Count == 0)
            {
                _pool.Add(_remote!.Fs);
                return _remote.Fs;
            }
            var fresh = await _factory!(token);
            _pool.Add(fresh);
            return fresh;
        }
        finally { _poolGate.Release(); }
    }

    private void Return(IRemoteFileSystem fs)
    {
        _poolGate.Wait();
        try { _idle.Enqueue(fs); }
        finally { _poolGate.Release(); }
    }

    /// <summary>Recorre lo que hay que transferir (directorios incluidos) y devuelve los bytes totales.</summary>
    private static async Task<long> PlanAsync(IFileSide from, IFileSide to, FileEntry entry, string targetDirectory, List<(FileEntry, string)> plan, CancellationToken token)
    {
        var target = to.Combine(targetDirectory, entry.Name);
        plan.Add((entry, target));
        if (!entry.IsDirectory)
            return entry.Size;
        long total = 0;
        foreach (var child in await from.ListAsync(entry.FullPath, token))
            total += await PlanAsync(from, to, child, target, plan, token);
        return total;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => _transfer?.Cancel();

    // =====================================================================
    //  Abrir ficheros remotos: editor integrado, o programa por defecto con una copia temporal
    // =====================================================================

    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "sOCRCManager", "abiertos");

    public async Task EditRemotePathAsync(string path)
    {
        if (_remote is null)
            return;
        var entry = await _remote.Fs.StatAsync(path, CancellationToken.None);
        if (entry is not null)
            await EditRemoteAsync(entry);
    }

    private async Task EditRemoteAsync(FileEntry entry)
    {
        if (_remote is null)
            return;
        try
        {
            var editor = await TextEditorWindow.OpenRemoteAsync(Window.GetWindow(this)!, _remote.Fs, entry);
            editor.Saved += () => _ = RemotePane.RefreshAsync();
            editor.Show();
        }
        catch (Exception ex)
        {
            TransferText.Text = ex.Message.ReplaceLineEndings(" ");
        }
    }

    /// <summary>Baja el fichero a una carpeta temporal y lo abre con el programa que Windows tenga para el.</summary>
    private async Task OpenRemoteExternalAsync(FileEntry entry)
    {
        if (_remote is null)
            return;
        try
        {
            Directory.CreateDirectory(TempRoot);
            var local = Path.Combine(TempRoot, entry.Name);
            TransferText.Text = Loc.Format("FilesDownloading", entry.Name, "0 B", FileRow.Format(entry.Size));
            await _remote.Fs.DownloadAsync(entry.FullPath, local, new Progress<long>(), CancellationToken.None);
            TransferText.Text = Loc.Get("FilesDone");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(local) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            TransferText.Text = ex.Message.ReplaceLineEndings(" ");
        }
    }

    public void Shutdown()
    {
        _transfer?.Cancel();
        foreach (var fs in _pool)
            try { fs.Dispose(); } catch (Exception) { }
        if (_remote is not null && !_pool.Contains(_remote.Fs))
            _remote.Fs.Dispose();
    }
}
