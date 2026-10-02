using System.IO;
using System.Windows;
using System.Windows.Controls;
using SocRcManager.Localization;
using SocRcManager.Models;
using SocRcManager.Services;

namespace SocRcManager.Files;

/// <summary>
/// El explorador de dos paneles de una conexion de ficheros: este PC a la izquierda, el servidor a
/// la derecha, y las transferencias entre los dos (con carpetas enteras), con las opciones de la
/// conexion: varias a la vez (cada una con su propia conexion al servidor), que hacer si el
/// fichero ya existe, reintentos y conservar las fechas.
/// </summary>
public partial class FileBrowserControl : UserControl
{
    private RemoteSide? _remote;
    private Connection? _options;
    private FileTransfer? _engine;
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
        _remote = remote;
        _options = options;
        _engine = new FileTransfer(remote, factory);
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

    /// <summary>Lo que transfiere (sin interfaz); null hasta <see cref="Attach"/>.</summary>
    internal FileTransfer? Engine => _engine;

    private async Task RunQueueAsync()
    {
        _busy = true;
        _transfer = new CancellationTokenSource();
        var token = _transfer.Token;
        Progress.Visibility = CancelButton.Visibility = Visibility.Visible;
        var failed = false;
        var options = _options ?? new Connection();
        try
        {
            while (_queue.Count > 0 && !token.IsCancellationRequested)
            {
                var (from, to, entries) = _queue.Dequeue();
                await _engine!.RunAsync(from.Side!, to.Side!, to.CurrentPath, entries, options,
                    name => ConflictWindow.Ask(Window.GetWindow(this)!, name), ShowProgress, token);
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

    // Llega mas tarde (BeginInvoke): si la transferencia ya acabo, no tapa «terminada» ni el error.
    private void ShowProgress(TransferProgress p) => Dispatcher.BeginInvoke(() =>
    {
        if (!_busy)
            return;
        Progress.Value = p.Percent;
        TransferText.Text = p.Text;
    });

    private void OnCancelClick(object sender, RoutedEventArgs e) => _transfer?.Cancel();

    // =====================================================================
    //  Abrir ficheros remotos: editor integrado, o programa por defecto con una copia temporal
    // =====================================================================

    /// <summary>Donde se bajan las copias que se abren con el programa por defecto.</summary>
    internal static string TempRoot { get; set; } = Path.Combine(Path.GetTempPath(), "sOCRCManager", "abiertos");

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
            Dialogs.ShowWindow(editor);
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
            TransferText.Text = Loc.Format("FilesDownloading", entry.Name, "0 B", FileRules.SizeText(entry.Size));
            await _remote.Fs.DownloadAsync(entry.FullPath, local, new Progress<long>(), CancellationToken.None);
            TransferText.Text = Loc.Get("FilesDone");
            Dialogs.Start(new System.Diagnostics.ProcessStartInfo(local) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            TransferText.Text = ex.Message.ReplaceLineEndings(" ");
        }
    }

    public void Shutdown()
    {
        _transfer?.Cancel();
        _engine?.Shutdown();
    }
}
