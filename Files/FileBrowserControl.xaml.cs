using System.IO;
using System.Windows;
using System.Windows.Controls;
using SocRcManager.Localization;

namespace SocRcManager.Files;

/// <summary>
/// El explorador de dos paneles de una conexion de ficheros: este PC a la izquierda, el servidor a
/// la derecha, y las transferencias entre los dos (con carpetas enteras) una detras de otra, con
/// progreso y boton de parar.
/// </summary>
public partial class FileBrowserControl : UserControl
{
    private IFileSide? _local;
    private IFileSide? _remote;
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
    }

    public event Action<string>? Failed;

    public void Attach(IFileSide local, IFileSide remote, string remoteTitle)
    {
        _local = local;
        _remote = remote;
        LocalPane.Attach(local, Loc.Get("FilesThisPc"), Loc.Get("FilesUpload"));
        RemotePane.Attach(remote, remoteTitle, Loc.Get("FilesDownload"));
    }

    public void FocusRemote() => RemotePane.Focus();

    public Task RemotePaneNavigateAsync(string path) => RemotePane.NavigateAsync(path);

    private void Enqueue(FilePane from, FilePane to, IReadOnlyList<FileEntry> entries)
    {
        if (entries.Count == 0 || to.CurrentPath.Length == 0)
            return;
        _queue.Enqueue((from, to, entries));
        if (!_busy)
            _ = RunQueueAsync();
    }

    private async Task RunQueueAsync()
    {
        _busy = true;
        _transfer = new CancellationTokenSource();
        var token = _transfer.Token;
        Progress.Visibility = CancelButton.Visibility = Visibility.Visible;
        var failed = false;
        try
        {
            while (_queue.Count > 0 && !token.IsCancellationRequested)
            {
                var (from, to, entries) = _queue.Dequeue();
                // Primero se mide todo (para la barra), luego se transfiere.
                var plan = new List<(FileEntry Entry, string Target)>();
                long total = 0;
                foreach (var entry in entries)
                    total += await PlanAsync(from.Side!, to.Side!, entry, to.CurrentPath, plan, token);

                long done = 0;
                var progress = new Progress<long>(delta =>
                {
                    done += delta;
                    Progress.Value = total > 0 ? Math.Min(100, done * 100.0 / total) : 0;
                });
                foreach (var (entry, target) in plan)
                {
                    token.ThrowIfCancellationRequested();
                    TransferText.Text = Loc.Format(from.Side!.IsLocal ? "FilesUploading" : "FilesDownloading", entry.Name, FileRow.Format(done), FileRow.Format(total));
                    if (entry.IsDirectory)
                    {
                        try { await to.Side!.CreateDirectoryAsync(target, token); } catch (Exception) { /* ya existe */ }
                        continue;
                    }
                    if (from.Side.IsLocal)
                        await ((RemoteSide)to.Side!).Fs.UploadAsync(entry.FullPath, target, progress, token);
                    else
                        await ((RemoteSide)from.Side).Fs.DownloadAsync(entry.FullPath, target, progress, token);
                }
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

    public void Shutdown()
    {
        _transfer?.Cancel();
        (_remote as RemoteSide)?.Fs.Dispose();
    }
}
