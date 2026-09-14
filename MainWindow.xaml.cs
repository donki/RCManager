using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SocRcManager.Localization;
using SocRcManager.Models;
using SocRcManager.Services;
using SocRcManager.Sessions;

namespace SocRcManager;

/// <summary>
/// La ventana: a la izquierda el arbol de carpetas y conexiones, a la derecha una pestaña por
/// sesion abierta.
/// </summary>
/// <remarks>
/// Code-behind delgado: lo que es datos vive en <see cref="Store"/>, lo que es sesion en
/// <see cref="ISession"/>; aqui solo se enlazan botones, arbol y pestañas.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly Store _store = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly CloudSync _sync;
    private readonly List<(TabItem Tab, ISession Session, Connection Connection)> _open = [];

    public MainWindow()
    {
        InitializeComponent();

        ApplyTexts();
        Loc.LanguageChanged += ApplyTexts;
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        _store.Load();
        BuildTree();

        // Con la nube elegida, al arrancar se baja lo que haya (si es mas nuevo) y cada guardado
        // se sube detras. Todo cifrado con la frase del usuario (CloudSync).
        _sync = new CloudSync(_settings, _store);
        _sync.Status += s => Dispatcher.BeginInvoke(() => SetStatus(s));
        _sync.Replaced += () => Dispatcher.BeginInvoke(BuildTree);
        Loaded += async (_, _) =>
        {
            if (!_sync.IsCloud)
                return;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                await _sync.SyncAsync(cts.Token);
            }
            catch (Exception ex)
            {
                SetStatus(Loc.Format("CloudFailed", ex.Message));
            }
        };

        Closing += (_, _) =>
        {
            foreach (var (_, session, _) in _open.ToList())
                session.Disconnect();
            _sync.Dispose();
        };
    }

    private void ApplyTexts()
    {
        Title = Loc.Get("AppTitle");
        NewConnectionButton.ToolTip = Loc.Get("NewConnectionTooltip");
        NewFolderButton.ToolTip = Loc.Get("NewFolderTooltip");
        EditButton.ToolTip = Loc.Get("EditTooltip");
        DuplicateButton.ToolTip = Loc.Get("DuplicateTooltip");
        DeleteButton.ToolTip = Loc.Get("DeleteTooltip");
        ImportButton.ToolTip = Loc.Get("ImportTooltip");
        ConnectButton.ToolTip = Loc.Get("ConnectTooltip");
        SettingsButton.ToolTip = Loc.Get("SettingsTooltip");
        OpenFileButton.ToolTip = Loc.Get("OpenFileTooltip");
        LanguageButton.ToolTip = Loc.Get("LanguageTooltip");
        AboutButton.ToolTip = Loc.Get("AboutTooltip");
        SearchHint.Text = Loc.Get("SearchHint");
        EmptyTree.Text = Loc.Get("EmptyTree");
        EmptyTabs.Text = Loc.Get("EmptyTabs");
        if (StatusText.Text.Length == 0)
            StatusText.Text = Loc.Get("Ready");
    }

    private void SetStatus(string text) => StatusText.Text = text;

    // =====================================================================
    //  Arbol
    // =====================================================================

    /// <summary>Nodo del arbol: una carpeta (ruta) o una conexion.</summary>
    private sealed class Node
    {
        public string? FolderPath { get; init; }
        public Connection? Connection { get; init; }
        public bool IsFolder => FolderPath is not null;
    }

    private void BuildTree()
    {
        var filter = SearchBox.Text.Trim();
        var expanded = Tree.Items.OfType<TreeViewItem>().SelectMany(Flatten).Where(i => i.IsExpanded && i.Tag is Node { IsFolder: true })
            .Select(i => ((Node)i.Tag).FolderPath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = (Tree.SelectedItem as TreeViewItem)?.Tag as Node;

        Tree.Items.Clear();
        var folders = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase);

        TreeViewItem FolderItem(string path)
        {
            if (folders.TryGetValue(path, out var existing))
                return existing;

            var slash = path.LastIndexOf('/');
            var name = slash >= 0 ? path[(slash + 1)..] : path;
            var item = new TreeViewItem
            {
                // El estilo del arbol no baja solo a los nodos anidados creados a mano.
                Style = (Style)FindResource("TreeItem"),
                Header = Header("", name),
                Tag = new Node { FolderPath = path },
                IsExpanded = filter.Length > 0 || expanded.Count == 0 || expanded.Contains(path),
            };
            folders[path] = item;

            if (slash >= 0)
                FolderItem(path[..slash]).Items.Add(item);
            else
                Tree.Items.Add(item);
            return item;
        }

        foreach (var folder in _store.AllFolders())
            FolderItem(folder);

        var shown = 0;
        foreach (var c in _store.Connections.OrderBy(c => c.Folder, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (filter.Length > 0 && !Matches(c, filter))
                continue;

            var item = new TreeViewItem
            {
                Style = (Style)FindResource("TreeItem"),
                // El servidor al lado del nombre, salvo que sea lo mismo (importado de RDM suele serlo).
                Header = Header(c.Kind == ConnectionKind.Ssh ? "" : "", c.Name.Length > 0 ? c.Name : Loc.Get("Unnamed"),
                    string.Equals(c.Caption, c.Name, StringComparison.OrdinalIgnoreCase) ? null : c.Caption),
                Tag = new Node { Connection = c },
            };
            if (c.Folder.Length > 0)
                FolderItem(c.Folder).Items.Add(item);
            else
                Tree.Items.Add(item);
            shown++;

            if (selected?.Connection?.Id == c.Id)
                item.IsSelected = true;
        }

        EmptyTree.Visibility = _store.Connections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    private static IEnumerable<TreeViewItem> Flatten(TreeViewItem item)
    {
        yield return item;
        foreach (var child in item.Items.OfType<TreeViewItem>())
            foreach (var sub in Flatten(child))
                yield return sub;
    }

    private static bool Matches(Connection c, string filter) =>
        c.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
        c.Host.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
        c.UserName.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
        c.Folder.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
        c.Notes.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

    private FrameworkElement Header(string glyph, string text, string? detail = null)
    {
        // Sin Foreground fijo: lo heredan de la fila, que pasa a blanco al seleccionarse. El icono
        // va en indigo salvo en la fila seleccionada, y el detalle atenuado.
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        icon.Style = RowStyle((System.Windows.Media.Brush)FindResource("Primary"));
        panel.Children.Add(icon);
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        if (detail is not null)
        {
            var hint = new TextBlock { Text = detail, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            hint.Style = RowStyle((System.Windows.Media.Brush)FindResource("TextSecondary"));
            panel.Children.Add(hint);
        }
        return panel;
    }

    /// <summary>Estilo de un texto de la fila: su color propio, o el de la fila si esta seleccionada.</summary>
    private static Style RowStyle(System.Windows.Media.Brush normal)
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.ForegroundProperty, normal));
        var selected = new DataTrigger
        {
            Binding = new System.Windows.Data.Binding("IsSelected") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(TreeViewItem), 1) },
            Value = true,
        };
        selected.Setters.Add(new Setter(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(TreeViewItem), 1) }));
        style.Triggers.Add(selected);
        return style;
    }

    // =====================================================================
    //  Arrastrar y soltar en el arbol
    // =====================================================================

    private Point _dragStart;
    private TreeViewItem? _dragItem;
    private TreeViewItem? _dropTarget;

    private static TreeViewItem? ItemAt(DependencyObject? source)
    {
        while (source is not null && source is not TreeViewItem)
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        return source as TreeViewItem;
    }

    private void OnTreeMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(Tree);
        _dragItem = ItemAt(e.OriginalSource as DependencyObject);
    }

    private void OnTreeMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem?.Tag is not Node node)
            return;
        var delta = e.GetPosition(Tree) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var item = _dragItem;
        _dragItem = null;
        DragDrop.DoDragDrop(item, new DataObject(typeof(Node), node), DragDropEffects.Move);
        PaintDropTarget(null);
    }

    /// <summary>La carpeta de destino de soltar sobre un elemento (la suya si es una conexion); null si es la raiz.</summary>
    private static string? TargetFolder(TreeViewItem? item) => item?.Tag is Node n ? (n.FolderPath ?? n.Connection?.Folder ?? string.Empty) : string.Empty;

    private bool CanDrop(Node dragged, string target)
    {
        if (dragged.Connection is { } c)
            return !string.Equals(c.Folder, target, StringComparison.OrdinalIgnoreCase);
        if (dragged.FolderPath is { } path)
        {
            // Ni dentro de si misma, ni a donde ya esta.
            var slash = path.LastIndexOf('/');
            var parent = slash >= 0 ? path[..slash] : string.Empty;
            return !Store.IsInside(target, path) && !string.Equals(parent, target, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        if (e.Data.GetData(typeof(Node)) is not Node dragged)
            return;

        var item = ItemAt(e.OriginalSource as DependencyObject);
        // Sobre una conexion se suelta en su carpeta: se resalta la carpeta, no la conexion.
        if (item?.Tag is Node { Connection: not null })
            item = ItemAt(System.Windows.Media.VisualTreeHelper.GetParent(item));
        var target = TargetFolder(item) ?? string.Empty;
        if (CanDrop(dragged, target))
        {
            e.Effects = DragDropEffects.Move;
            PaintDropTarget(item);
        }
        else
        {
            PaintDropTarget(null);
        }
    }

    private void OnTreeDragLeave(object sender, DragEventArgs e)
    {
        if (!Tree.IsMouseOver)
            PaintDropTarget(null);
    }

    private void OnTreeDrop(object sender, DragEventArgs e)
    {
        PaintDropTarget(null);
        if (e.Data.GetData(typeof(Node)) is not Node dragged)
            return;
        e.Handled = true;

        var item = ItemAt(e.OriginalSource as DependencyObject);
        if (item?.Tag is Node { Connection: not null })
            item = ItemAt(System.Windows.Media.VisualTreeHelper.GetParent(item));
        var target = TargetFolder(item) ?? string.Empty;
        if (!CanDrop(dragged, target))
            return;

        if (dragged.Connection is { } c)
        {
            // Si la carpeta de origen se queda sin nada, que no desaparezca del arbol.
            var from = c.Folder;
            c.Folder = target;
            if (from.Length > 0 && !_store.Connections.Any(x => Store.IsInside(x.Folder, from)) && !_store.EmptyFolders.Contains(from, StringComparer.OrdinalIgnoreCase))
                _store.EmptyFolders.Add(from);
            _store.Save();
            BuildTree();
            SelectConnection(c);
        }
        else if (dragged.FolderPath is { } path)
        {
            var name = path[(path.LastIndexOf('/') + 1)..];
            var newPath = target.Length > 0 ? $"{target}/{name}" : name;
            _store.RenameFolder(path, newPath);
            if (!_store.EmptyFolders.Contains(newPath, StringComparer.OrdinalIgnoreCase))
                _store.EmptyFolders.Add(newPath);
            _store.Save();
            BuildTree();
            foreach (var i in Tree.Items.OfType<TreeViewItem>().SelectMany(Flatten))
                if (i.Tag is Node { FolderPath: { } p } && string.Equals(p, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    i.IsSelected = true;
                    i.BringIntoView();
                    break;
                }
        }
    }

    /// <summary>Tiñe la carpeta sobre la que se va a soltar (o la quita, con null).</summary>
    private void PaintDropTarget(TreeViewItem? item)
    {
        if (ReferenceEquals(_dropTarget, item))
            return;
        if (_dropTarget?.Template.FindName("Bd", _dropTarget) is Border old)
            old.ClearValue(Border.BackgroundProperty);
        _dropTarget = item;
        if (item?.Template.FindName("Bd", item) is Border bd)
            bd.Background = (System.Windows.Media.Brush)FindResource("PrimaryLight");
    }

    private Node? Selected => (Tree.SelectedItem as TreeViewItem)?.Tag as Node;

    private void UpdateButtons()
    {
        var node = Selected;
        EditButton.IsEnabled = node is not null;
        DuplicateButton.IsEnabled = node?.Connection is not null;
        DeleteButton.IsEnabled = node is not null;
        ConnectButton.IsEnabled = node?.Connection is not null;
        EditButton.ToolTip = node?.IsFolder == true ? Loc.Get("RenameFolderTooltip") : Loc.Get("EditTooltip");
    }

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => UpdateButtons();

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        BuildTree();
    }

    private async void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected?.Connection is { } c)
            await OpenAsync(c);
    }

    private async void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Selected?.Connection is { } c)
        {
            e.Handled = true;
            await OpenAsync(c);
        }
        else if (e.Key == Key.Delete && Selected is not null)
        {
            e.Handled = true;
            OnDeleteClick(sender, e);
        }
    }

    // =====================================================================
    //  Alta, edicion, borrado
    // =====================================================================

    private void OnNewConnectionClick(object sender, RoutedEventArgs e)
    {
        var folder = Selected?.FolderPath ?? Selected?.Connection?.Folder ?? string.Empty;
        var connection = new Connection { Folder = folder };
        if (Edit(connection))
        {
            _store.Connections.Add(connection);
            _store.Save();
            BuildTree();
            SelectConnection(connection);
        }
    }

    private void OnNewFolderClick(object sender, RoutedEventArgs e)
    {
        var parent = Selected?.FolderPath ?? Selected?.Connection?.Folder ?? string.Empty;
        var name = PromptWindow.Ask(this, Loc.Get("NewFolder"), Loc.Get("FolderName"), string.Empty);
        if (string.IsNullOrWhiteSpace(name))
            return;

        var path = parent.Length > 0 ? $"{parent}/{name.Trim().Replace('/', '-')}" : name.Trim().Replace('/', '-');
        _store.EmptyFolders.Add(path);
        _store.Save();
        BuildTree();
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        var node = Selected;
        if (node?.Connection is { } c)
        {
            var copy = c.Clone();
            if (Edit(copy))
            {
                var index = _store.Connections.IndexOf(c);
                _store.Connections[index] = copy;
                _store.Save();
                BuildTree();
                SelectConnection(copy);
            }
        }
        else if (node?.FolderPath is { } path)
        {
            var slash = path.LastIndexOf('/');
            var current = slash >= 0 ? path[(slash + 1)..] : path;
            var name = PromptWindow.Ask(this, Loc.Get("RenameFolderTooltip"), Loc.Get("FolderName"), current);
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == current)
                return;

            var newPath = (slash >= 0 ? path[..(slash + 1)] : string.Empty) + name.Trim().Replace('/', '-');
            _store.RenameFolder(path, newPath);
            _store.Save();
            BuildTree();
        }
    }

    private void OnDuplicateClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Connection is not { } c)
            return;

        var copy = c.Clone();
        copy.Id = Guid.NewGuid();
        copy.Name = c.Name + " (2)";
        copy.LastConnectedAt = null;
        _store.Connections.Add(copy);
        _store.Save();
        BuildTree();
        SelectConnection(copy);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var node = Selected;
        if (node?.Connection is { } c)
        {
            if (!PromptWindow.Confirm(this, Loc.Get("Delete"), Loc.Format("DeleteConnectionConfirm", c.Name)))
                return;
            _store.Connections.Remove(c);
        }
        else if (node?.FolderPath is { } path)
        {
            var count = _store.Connections.Count(x => Store.IsInside(x.Folder, path));
            if (!PromptWindow.Confirm(this, Loc.Get("Delete"), Loc.Format("DeleteFolderConfirm", path, count)))
                return;
            _store.DeleteFolder(path);
        }
        else
        {
            return;
        }

        _store.Save();
        BuildTree();
    }

    /// <summary>
    /// Importa un .rdm de Remote Desktop Manager. Las conexiones que ya existan (mismo nombre,
    /// servidor y carpeta) no se repiten; las carpetas se crean aunque esten vacias.
    /// </summary>
    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Remote Desktop Manager (*.rdm)|*.rdm|*.*|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var result = RdmImport.Read(dialog.FileName);
            var added = 0;
            foreach (var c in result.Connections)
            {
                var exists = _store.Connections.Any(x =>
                    string.Equals(x.Name, c.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Host, c.Host, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Folder, c.Folder, StringComparison.OrdinalIgnoreCase));
                if (exists)
                    continue;
                _store.Connections.Add(c);
                added++;
            }

            _store.EmptyFolders.AddRange(result.Folders);
            _store.Save();
            BuildTree();
            SetStatus(Loc.Format("Imported", added, result.Connections.Count - added, result.Skipped));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.Format("ImportFailed", ex.Message));
        }
    }

    private bool Edit(Connection connection)
    {
        var dialog = new ConnectionWindow(connection, _store.AllFolders()) { Owner = this };
        return dialog.ShowDialog() == true;
    }

    private void SelectConnection(Connection connection)
    {
        foreach (var item in Tree.Items.OfType<TreeViewItem>().SelectMany(Flatten))
        {
            if (item.Tag is Node { Connection: { } c } && c.Id == connection.Id)
            {
                item.IsSelected = true;
                item.BringIntoView();
                return;
            }
        }
    }

    private void OnOpenFileClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Store.Location}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    // =====================================================================
    //  Sesiones
    // =====================================================================

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Connection is { } c)
            await OpenAsync(c);
    }

    private async Task OpenAsync(Connection connection)
    {
        var password = Secrets.Unprotect(connection.PasswordProtected);
        if (password.Length == 0 && connection.PrivateKeyPath.Length == 0)
        {
            var asked = PromptWindow.AskPassword(this, Loc.Get("PasswordTitle"), Loc.Format("PasswordPrompt", connection.UserName, connection.Host), Loc.Get("SavePassword"));
            password = asked?.Password ?? string.Empty;
            if (password.Length == 0 && connection.Kind == ConnectionKind.Ssh)
                return;

            // Guardarla: cifrada con DPAPI para este usuario de Windows, como desde el editor.
            if (asked is { Save: true } && password.Length > 0)
            {
                connection.PasswordProtected = Secrets.Protect(password);
                _store.Save();
            }
        }

        ISession session = connection.Kind == ConnectionKind.Ssh ? new SshSession(connection) : new RdpSession(connection);

        var fullButton = new Button
        {
            Style = (Style)FindResource("GhostIconButton"),
            Content = "",
            Width = 24, Height = 24, FontSize = 11,
            Margin = new Thickness(8, 0, -6, 0),
            ToolTip = Loc.Get("FullScreenTooltip"),
        };
        var closeButton = new Button
        {
            Style = (Style)FindResource("GhostIconButton"),
            Content = "",
            Width = 24, Height = 24, FontSize = 11,
            Margin = new Thickness(0, 0, -4, 0),
            ToolTip = Loc.Get("DisconnectTooltip"),
        };
        var title = new TextBlock { Text = connection.Name, VerticalAlignment = VerticalAlignment.Center };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Children = { title, fullButton, closeButton } };
        var tab = new TabItem { Header = header, Content = session.View };
        fullButton.Click += (_, _) =>
        {
            Tabs.SelectedItem = tab;
            if (session.HasNativeFullScreen)
                session.EnterFullScreen();
            else
                SetFullScreen(true);
        };

        var entry = (tab, session, connection);
        _open.Add(entry);
        Tabs.Items.Add(tab);
        Tabs.SelectedItem = tab;
        EmptyTabs.Visibility = Visibility.Collapsed;

        closeButton.Click += (_, _) => CloseTab(tab);
        session.TitleChanged += t => Dispatcher.BeginInvoke(() => title.Text = t.Length > 0 ? $"{connection.Name} · {t}" : connection.Name);
        session.Ended += reason => Dispatcher.BeginInvoke(() =>
        {
            SetStatus(reason is null ? Loc.Format("SessionClosed", connection.Name) : Loc.Format("SessionEnded", connection.Name, reason));
            CloseTab(tab);
        });

        SetStatus(Loc.Format("Connecting", connection.Name));
        try
        {
            // Que la pestaña tenga tamaño antes de conectar: el RDP pide el escritorio con el
            // tamaño que vea, y el terminal SSH abre el shell con sus columnas y filas.
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            await session.ConnectAsync(password);
            connection.LastConnectedAt = DateTime.Now;
            _store.Save();
            SetStatus(Loc.Format("Connected", connection.Name));
            session.Focus();
        }
        catch (Exception ex)
        {
            SetStatus(Loc.Format("ConnectFailed", connection.Name, ex.Message));
            CloseTab(tab);
        }
    }

    // =====================================================================
    //  Pantalla completa
    // =====================================================================

    private bool _fullScreen;
    private WindowState _stateBeforeFullScreen;
    private GridLength _treeWidthBeforeFullScreen;

    /// <summary>
    /// La pestaña activa a toda la pantalla: sin arbol, sin cabecera de pestañas, sin barra de
    /// estado y sin marco de ventana. El escritorio remoto se redimensiona con SmartSizing al
    /// tamaño nuevo. Esc o el boton flotante vuelven.
    /// </summary>
    private void SetFullScreen(bool on)
    {
        if (_fullScreen == on)
            return;
        _fullScreen = on;

        if (on)
        {
            _stateBeforeFullScreen = WindowState;
            _treeWidthBeforeFullScreen = TreeColumn.Width;
            TreeColumn.Width = new GridLength(0);
            TreeColumn.MinWidth = 0;
            TreePane.Visibility = Visibility.Collapsed;
            Splitter.Visibility = Visibility.Collapsed;
            SplitterColumn.Width = new GridLength(0);
            StatusBar.Visibility = Visibility.Collapsed;
            HideTabHeaders(true);
            FullScreenTitle.Text = Tabs.SelectedItem is TabItem t && _open.FirstOrDefault(x => ReferenceEquals(x.Tab, t)).Connection is { } c ? c.Name : string.Empty;
            ShowFullScreenBar();

            // Primero Normal y luego Maximized: si ya estaba maximizada, cambiar el estilo no
            // vuelve a calcular el tamaño y quedaria la barra de tareas a la vista.
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _stateBeforeFullScreen == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            TreeColumn.MinWidth = 200;
            TreeColumn.Width = _treeWidthBeforeFullScreen;
            TreePane.Visibility = Visibility.Visible;
            Splitter.Visibility = Visibility.Visible;
            SplitterColumn.Width = GridLength.Auto;
            StatusBar.Visibility = Visibility.Visible;
            HideTabHeaders(false);
            FullScreenBar.Visibility = Visibility.Collapsed;
            _barTimer?.Stop();
        }

        if (Tabs.SelectedItem is TabItem current && _open.FirstOrDefault(x => ReferenceEquals(x.Tab, current)).Session is { } session)
            Dispatcher.BeginInvoke(session.Focus, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void HideTabHeaders(bool hide)
    {
        foreach (TabItem item in Tabs.Items)
            item.Visibility = hide && !ReferenceEquals(item, Tabs.SelectedItem) ? Visibility.Collapsed : Visibility.Visible;
        if (Tabs.SelectedItem is TabItem selected)
            selected.Visibility = Visibility.Visible;
        // La cabecera de la pestaña visible se encoge a nada: el contenido ocupa todo.
        if (Tabs.SelectedItem is TabItem s)
            s.Height = hide ? 0 : double.NaN;
    }

    private void OnToggleFullScreenClick(object sender, RoutedEventArgs e) => SetFullScreen(!_fullScreen);

    private void OnFullScreenCloseClick(object sender, RoutedEventArgs e)
    {
        if (Tabs.SelectedItem is TabItem tab)
            CloseTab(tab);
    }

    // La barra se enseña al entrar y al llevar el raton al borde de arriba; se esconde sola a los
    // dos segundos de que el raton la deje.
    private System.Windows.Threading.DispatcherTimer? _barTimer;
    private bool _mouseOnBar;

    private void ShowFullScreenBar()
    {
        FullScreenBar.Visibility = Visibility.Visible;
        _barTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _barTimer.Tick -= HideFullScreenBar;
        _barTimer.Tick += HideFullScreenBar;
        _barTimer.Stop();
        _barTimer.Start();
    }

    private void HideFullScreenBar(object? sender, EventArgs e)
    {
        _barTimer?.Stop();
        if (!_mouseOnBar)
            FullScreenBar.Visibility = Visibility.Collapsed;
    }

    private void OnFullScreenBarEnter(object sender, MouseEventArgs e) { _mouseOnBar = true; _barTimer?.Stop(); }

    private void OnFullScreenBarLeave(object sender, MouseEventArgs e) { _mouseOnBar = false; ShowFullScreenBar(); }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_fullScreen && e.GetPosition(this).Y <= 3 && FullScreenBar.Visibility != Visibility.Visible)
            ShowFullScreenBar();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape && _fullScreen && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            SetFullScreen(false);
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            SetFullScreen(!_fullScreen);
            e.Handled = true;
        }
    }

    private void CloseTab(TabItem tab)
    {
        var index = _open.FindIndex(x => ReferenceEquals(x.Tab, tab));
        if (index < 0)
            return;

        var (_, session, _) = _open[index];
        _open.RemoveAt(index);
        session.Disconnect();
        Tabs.Items.Remove(tab);
        EmptyTabs.Visibility = Tabs.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Tabs.Items.Count == 0 && _fullScreen)
            SetFullScreen(false);
    }

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tabs.SelectedItem is TabItem tab && _open.FirstOrDefault(x => ReferenceEquals(x.Tab, tab)).Session is { } session)
            Dispatcher.BeginInvoke(session.Focus, System.Windows.Threading.DispatcherPriority.Input);
    }

    // =====================================================================

    private void OnLanguageClick(object sender, RoutedEventArgs e)
    {
        Loc.Toggle();
        BuildTree();
    }

    private void OnAboutClick(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings, _sync) { Owner = this };
        dialog.ShowDialog();
        if (dialog.LocalReplaced)
            BuildTree();
    }
}
