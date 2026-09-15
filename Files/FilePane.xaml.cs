using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SocRcManager.Localization;

namespace SocRcManager.Files;

/// <summary>Una fila del panel.</summary>
public sealed class FileRow(FileEntry entry)
{
    public FileEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string Glyph => Entry.IsDirectory ? "" : "";
    public string SizeText => Entry.IsDirectory ? string.Empty : Format(Entry.Size);
    public string DateText => Entry.Modified?.ToString("g", CultureInfo.CurrentCulture) ?? string.Empty;
    public string ModeText => Entry.ModeText;
    public string OwnerText => Entry.Owner is null ? string.Empty : Entry.Group is null ? Entry.Owner : $"{Entry.Owner}:{Entry.Group}";

    public static string Format(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB",
    };
}

/// <summary>
/// Un lado del explorador: la ruta, la lista y los botones. Navegar (doble clic, Intro, ↑,
/// Retroceso), crear carpeta (F7), renombrar (F2), borrar (Supr) y pedir la transferencia al otro
/// lado (F5, el boton o arrastrar). Quien transfiere es <see cref="FileBrowserControl"/>.
/// </summary>
public partial class FilePane : UserControl
{
    private IFileSide? _side;
    private CancellationTokenSource? _listing;

    public FilePane()
    {
        InitializeComponent();
        UpButton.ToolTip = Loc.Get("FilesUp");
        RefreshButton.ToolTip = Loc.Get("FilesRefresh");
        NewFolderButton.ToolTip = Loc.Get("FilesNewFolder");
        RenameButton.ToolTip = Loc.Get("FilesRename");
        DeleteButton.ToolTip = Loc.Get("FilesDelete");
        NameColumn.Header = Loc.Get("FilesName");
        SizeColumn.Header = Loc.Get("FilesSize");
        DateColumn.Header = Loc.Get("FilesModified");
        ModeColumn.Header = Loc.Get("PermsPermissions");
        OwnerColumn.Header = Loc.Get("PermsOwner");
        PermissionsButton.ToolTip = Loc.Get("PermsTooltip");
        SizeChanged += (_, _) => FitNameColumn();
    }

    public IFileSide? Side => _side;

    public string CurrentPath { get; private set; } = string.Empty;

    /// <summary>Lo seleccionado, o nada.</summary>
    public IReadOnlyList<FileEntry> SelectedEntries => List.SelectedItems.OfType<FileRow>().Select(r => r.Entry).ToList();

    /// <summary>El usuario quiere llevar estos elementos al otro lado.</summary>
    public event Action<IReadOnlyList<FileEntry>>? TransferRequested;

    /// <summary>Han soltado aqui elementos del otro panel: bajarlos o subirlos a este directorio.</summary>
    public event Action<FilePane, IReadOnlyList<FileEntry>>? DroppedFrom;

    public event Action<string>? Failed;

    public void Attach(IFileSide side, string title, string transferTooltip)
    {
        _side = side;
        SideTitle.Text = title;
        TransferButton.ToolTip = transferTooltip;
        TransferButton.Content = side.IsLocal ? "" : "";
        _ = NavigateAsync(side.InitialDirectory);
    }

    private void FitNameColumn() =>
        NameColumn.Width = Math.Max(120, ActualWidth - SizeColumn.Width - DateColumn.Width - ModeColumn.Width - OwnerColumn.Width - 40);

    /// <summary>En un servidor Unix se enseñan permisos y propietario, y el boton para cambiarlos.</summary>
    private void ShowUnixColumns(bool unix)
    {
        var show = unix && ModeColumn.Width == 0;
        var hide = !unix && ModeColumn.Width > 0;
        if (show) { ModeColumn.Width = 90; OwnerColumn.Width = 110; }
        if (hide) { ModeColumn.Width = 0; OwnerColumn.Width = 0; }
        PermissionsButton.Visibility = unix ? Visibility.Visible : Visibility.Collapsed;
        if (show || hide)
            FitNameColumn();
    }

    private async void OnPermissionsClick(object sender, RoutedEventArgs e)
    {
        if (_side is not RemoteSide remote || SelectedEntries.Count == 0)
            return;
        var entries = SelectedEntries;
        var dialog = new PermissionsWindow(Window.GetWindow(this)!, entries);
        if (dialog.ShowDialog() != true || (!dialog.ChangeMode && !dialog.ChangeOwner))
            return;
        try
        {
            foreach (var entry in entries)
                await ApplyPermissionsAsync(remote, entry, dialog, CancellationToken.None);
            PaneStatus.Text = Loc.Get("PermsApplied");
        }
        catch (Exception ex)
        {
            PaneStatus.Text = ex.Message.ReplaceLineEndings(" ");
        }
        await RefreshAsync();
    }

    private static async Task ApplyPermissionsAsync(RemoteSide remote, FileEntry entry, PermissionsWindow dialog, CancellationToken cancellationToken)
    {
        if (dialog.ChangeMode && dialog.Mode is { } mode)
            await remote.Fs.ChangeModeAsync(entry.FullPath, mode, cancellationToken);
        if (dialog.ChangeOwner)
            await remote.Fs.ChangeOwnerAsync(entry.FullPath, dialog.OwnerName, dialog.GroupName, cancellationToken);
        if (dialog.Recursive && entry.IsDirectory)
            foreach (var child in await remote.ListAsync(entry.FullPath, cancellationToken))
                await ApplyPermissionsAsync(remote, child, dialog, cancellationToken);
    }

    public async Task NavigateAsync(string path)
    {
        if (_side is null)
            return;
        _listing?.Cancel();
        var cts = _listing = new CancellationTokenSource();
        try
        {
            var entries = await _side.ListAsync(path, cts.Token);
            if (cts.IsCancellationRequested)
                return;
            CurrentPath = path;
            PathBox.Text = path.Length == 0 ? Loc.Get("FilesThisPc") : path;
            List.ItemsSource = entries
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(e => new FileRow(e))
                .ToList();
            PaneStatus.Text = Loc.Format("FilesCount", entries.Count(e => e.IsDirectory), entries.Count(e => !e.IsDirectory));
            ShowUnixColumns(_side is RemoteSide { Fs.SupportsPermissions: true } && entries.Any(e => e.Mode is not null));
            if (List.Items.Count > 0)
                List.ScrollIntoView(List.Items[0]);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            PaneStatus.Text = ex.Message.ReplaceLineEndings(" ");
            Failed?.Invoke(ex.Message);
        }
    }

    public Task RefreshAsync() => NavigateAsync(CurrentPath);

    private async Task GoUpAsync()
    {
        if (_side is null)
            return;
        var parent = _side.Parent(CurrentPath);
        if (parent != CurrentPath)
            await NavigateAsync(parent);
    }

    private async Task OpenSelectedAsync()
    {
        if (List.SelectedItem is FileRow { Entry.IsDirectory: true } row)
            await NavigateAsync(row.Entry.FullPath);
        else if (List.SelectedItem is FileRow)
            TransferRequested?.Invoke(SelectedEntries);
    }

    private void OnUpClick(object sender, RoutedEventArgs e) => _ = GoUpAsync();

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = RefreshAsync();

    private void OnTransferClick(object sender, RoutedEventArgs e)
    {
        if (SelectedEntries.Count > 0)
            TransferRequested?.Invoke(SelectedEntries);
    }

    private async void OnNewFolderClick(object sender, RoutedEventArgs e)
    {
        if (_side is null || CurrentPath.Length == 0)
            return;
        var name = PromptWindow.Ask(Window.GetWindow(this)!, Loc.Get("FilesNewFolder"), Loc.Get("FolderName"), string.Empty);
        if (string.IsNullOrWhiteSpace(name))
            return;
        try
        {
            await _side.CreateDirectoryAsync(_side.Combine(CurrentPath, name.Trim()), CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            PaneStatus.Text = ex.Message.ReplaceLineEndings(" ");
        }
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (_side is null || List.SelectedItem is not FileRow row || CurrentPath.Length == 0)
            return;
        var name = PromptWindow.Ask(Window.GetWindow(this)!, Loc.Get("FilesRename"), Loc.Get("FilesName"), row.Entry.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == row.Entry.Name)
            return;
        try
        {
            await _side.RenameAsync(row.Entry.FullPath, _side.Combine(CurrentPath, name.Trim()), CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            PaneStatus.Text = ex.Message.ReplaceLineEndings(" ");
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_side is null || SelectedEntries.Count == 0 || CurrentPath.Length == 0)
            return;
        var entries = SelectedEntries;
        var what = entries.Count == 1 ? entries[0].Name : Loc.Format("FilesItems", entries.Count);
        if (!PromptWindow.Confirm(Window.GetWindow(this)!, Loc.Get("Delete"), Loc.Format("FilesDeleteConfirm", what)))
            return;
        try
        {
            foreach (var entry in entries)
                await DeleteRecursiveAsync(_side, entry, CancellationToken.None);
        }
        catch (Exception ex)
        {
            PaneStatus.Text = ex.Message.ReplaceLineEndings(" ");
        }
        await RefreshAsync();
    }

    /// <summary>Borra un fichero, o un directorio con todo lo de dentro (los remotos no lo hacen solos).</summary>
    public static async Task DeleteRecursiveAsync(IFileSide side, FileEntry entry, CancellationToken cancellationToken)
    {
        if (!entry.IsDirectory)
        {
            await side.DeleteFileAsync(entry.FullPath, cancellationToken);
            return;
        }
        if (side.IsLocal)
        {
            await side.DeleteDirectoryAsync(entry.FullPath, cancellationToken);
            return;
        }
        foreach (var child in await side.ListAsync(entry.FullPath, cancellationToken))
            await DeleteRecursiveAsync(side, child, cancellationToken);
        await side.DeleteDirectoryAsync(entry.FullPath, cancellationToken);
    }

    private void OnPathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = NavigateAsync(PathBox.Text.Trim());
        }
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e) => _ = OpenSelectedAsync();

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: e.Handled = true; _ = OpenSelectedAsync(); break;
            case Key.Back: e.Handled = true; _ = GoUpAsync(); break;
            case Key.F5: e.Handled = true; OnTransferClick(sender, e); break;
            case Key.F7: e.Handled = true; OnNewFolderClick(sender, e); break;
            case Key.F2: e.Handled = true; OnRenameClick(sender, e); break;
            case Key.Delete: e.Handled = true; OnDeleteClick(sender, e); break;
        }
    }

    // ------------------------------------------------------------------ arrastrar entre paneles

    private Point _dragStart;
    private bool _dragArmed;

    private void OnListMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(List);
        _dragArmed = ItemUnder(e.OriginalSource as DependencyObject) is not null;
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed || SelectedEntries.Count == 0)
            return;
        var delta = e.GetPosition(List) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _dragArmed = false;
        DragDrop.DoDragDrop(List, new DataObject(typeof(FileDrag), new FileDrag(this, SelectedEntries)), DragDropEffects.Copy);
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetData(typeof(FileDrag)) is FileDrag drag && !ReferenceEquals(drag.Source, this) && CurrentPath.Length > 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(FileDrag)) is FileDrag drag && !ReferenceEquals(drag.Source, this) && CurrentPath.Length > 0)
        {
            e.Handled = true;
            DroppedFrom?.Invoke(drag.Source, drag.Entries);
        }
    }

    private static ListViewItem? ItemUnder(DependencyObject? source)
    {
        while (source is not null && source is not ListViewItem)
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        return source as ListViewItem;
    }

    private sealed record FileDrag(FilePane Source, IReadOnlyList<FileEntry> Entries);
}
