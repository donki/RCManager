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
    private readonly AppSettings _settings = AppSettings.Current = AppSettings.Load();
    private TrayIcon? _tray;
    private readonly CloudSync _sync;
    private readonly List<OpenSession> _open = [];

    /// <summary>Como se crea la sincronizacion con la nube (las pruebas le dan un cliente HTTP de mentira).</summary>
    internal static Func<AppSettings, Store, CloudSync> CreateSync { get; set; } = (settings, store) => new CloudSync(settings, store);

    /// <summary>
    /// Una sesion abierta: su pestaña en la principal o, si se ha sacado, su ventana propia
    /// (<see cref="SessionWindow"/>). La cabecera y el control de la sesion se mueven de una a otra.
    /// </summary>
    private sealed class OpenSession
    {
        public required TabItem Tab { get; init; }
        public required ISession Session { get; init; }
        public required Connection Connection { get; init; }

        /// <summary>Contenido de la pestaña: un Border cuyo hijo es el control de la sesion (se suelta al instante).</summary>
        public required Border Holder { get; init; }
        public required StackPanel Header { get; init; }
        public required TextBlock TitleText { get; init; }
        public required Button DetachButton { get; init; }

        /// <summary>La ventana propia, si se ha sacado de la principal.</summary>
        public SessionWindow? Window { get; set; }
        public bool IsDetached => Window is not null;
    }

    private OpenSession? Find(object? tab) => tab is TabItem t ? _open.FirstOrDefault(x => ReferenceEquals(x.Tab, t)) : null;

    /// <summary>Si hay alguna sesion abierta (en pestaña o en ventana suelta).</summary>
    public bool HasOpenSessions => _open.Count > 0;

    public int OpenSessionCount => _open.Count;

    public MainWindow()
    {
        InitializeComponent();

        ApplyTexts();
        Loc.LanguageChanged += ApplyTexts;
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        _store.Load();
        if (_settings.TreeWidth >= 200)
            TreeColumn.Width = new GridLength(_settings.TreeWidth);
        _restoreTree = true;
        BuildTree();
        _restoreTree = false;
        Loaded += (_, _) => (Tree.SelectedItem as TreeViewItem)?.BringIntoView();

        // Con la nube elegida, al arrancar se baja lo que haya (si es mas nuevo) y cada guardado
        // se sube detras. Todo cifrado con la frase del usuario (CloudSync).
        _sync = CreateSync(_settings, _store);
        _sync.Status += OnSyncStatus;
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

        // Icono en el area de notificacion: al minimizar, la ventana se esconde ahi (Ajustes lo apaga).
        // Se crea cuando la ventana ya tiene handle; «Salir» del menu cierra de verdad.
        SourceInitialized += (_, _) =>
        {
            _tray = new TrayIcon(this, Loc.Get, Close) { MinimizeToTray = _settings.TrayOnMinimize };
        };

        Closing += (_, e) =>
        {
            // Con pestañas sacadas a su ventana: preguntar antes (o no, segun Ajustes).
            var detached = _open.Count(x => x.IsDetached);
            if (SessionPlacement.OnMainClosing(detached, _settings.AskBeforeClosingDetached, _forceClose) == SessionPlacement.CloseAction.Ask)
            {
                if (!IsVisible)
                {
                    // Escondida en el area de notificacion («Salir» del icono): mientras se cierra no
                    // se puede enseñar (WPF lanza). Se deja para despues: se saca y se vuelve a cerrar.
                    e.Cancel = true;
                    Dispatcher.BeginInvoke(() =>
                    {
                        BringToFront();
                        Close();
                    });
                    return;
                }
                if (!PromptWindow.Confirm(this, Loc.Get("CloseAllTitle"), Loc.Format("CloseDetachedConfirm", detached), Loc.Get("CloseAllTitle"), "\uE711"))
                {
                    e.Cancel = true;
                    return;
                }
            }
            foreach (var open in _open.ToList())
            {
                open.Session.Disconnect();
                if (open.Window is { } w)
                    CloseDetachedWindow(open, w);
            }
            _settings.Save();
            _sync.Dispose();
            SaveTreeState();
            _tray?.Dispose();
        };
    }

    private void ApplyTexts()
    {
        Title = Sandbox.IsOn ? Loc.Get("AppTitle") + " [SOC_SANDBOX]" : Loc.Get("AppTitle");
        NewConnectionButton.ToolTip = Loc.Get("NewConnectionTooltip");
        NewFolderButton.ToolTip = Loc.Get("NewFolderTooltip");
        EditButton.ToolTip = Loc.Get("EditTooltip");
        DuplicateButton.ToolTip = Loc.Get("DuplicateTooltip");
        DeleteButton.ToolTip = Loc.Get("DeleteTooltip");
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

    /// <summary>
    /// Lo que cuenta la sincronizacion. En el hilo de la interfaz se pone al momento: dejarlo para
    /// luego hacia que un fallo inmediato (sin sesion iniciada, por ejemplo) quedase tapado por el
    /// «Sincronizando…» de antes, que llegaba detras.
    /// </summary>
    private void OnSyncStatus(string text)
    {
        if (Dispatcher.CheckAccess())
            SetStatus(text);
        else
            Dispatcher.BeginInvoke(() => SetStatus(text));
    }

    // Una sola linea pase lo que pase: un mensaje con saltos de linea hacia crecer la barra de
    // estado hasta comerse la ventana.
    private void SetStatus(string text) => StatusText.Text = text.ReplaceLineEndings(" ").Trim();

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

    private bool _restoreTree;

    /// <summary>Carpetas abiertas, seleccion y ancho del panel, para encontrarlo igual al volver a abrir.</summary>
    private void SaveTreeState()
    {
        _settings.ExpandedFolders = Tree.Items.OfType<TreeViewItem>().SelectMany(Flatten)
            .Where(i => i.IsExpanded && i.Tag is Node { IsFolder: true })
            .Select(i => ((Node)i.Tag).FolderPath!).ToList();
        _settings.SelectedConnectionId = Selected?.Connection?.Id;
        _settings.TreeWidth = _fullScreen ? _treeWidthBeforeFullScreen.Value : TreeColumn.Width.Value;
        _settings.Save();
    }

    private void BuildTree()
    {
        var filter = SearchBox.Text.Trim();
        var expanded = Tree.Items.OfType<TreeViewItem>().SelectMany(Flatten).Where(i => i.IsExpanded && i.Tag is Node { IsFolder: true })
            .Select(i => ((Node)i.Tag).FolderPath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = (Tree.SelectedItem as TreeViewItem)?.Tag as Node;
        var allExpanded = expanded.Count == 0;

        // Primer arbol de la sesion: como se dejo al cerrar la ultima vez.
        if (_restoreTree && _settings.ExpandedFolders is { } saved)
        {
            expanded = saved.ToHashSet(StringComparer.OrdinalIgnoreCase);
            allExpanded = false;
            if (_settings.SelectedConnectionId is { } id && _store.Connections.FirstOrDefault(c => c.Id == id) is { } c)
                selected = new Node { Connection = c };
        }

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
                IsExpanded = filter.Length > 0 || allExpanded || expanded.Contains(path),
            };
            item.ContextMenu = FolderMenu(item, path);
            folders[path] = item;

            if (slash >= 0)
                FolderItem(path[..slash]).Items.Add(item);
            else
                Tree.Items.Add(item);
            return item;
        }

        // Con filtro solo salen las carpetas de las conexiones que encajan.
        if (filter.Length == 0)
            foreach (var folder in _store.AllFolders())
                FolderItem(folder);

        var shown = 0;
        foreach (var c in _store.Connections.OrderBy(c => c.Folder, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (filter.Length > 0 && !c.Matches(filter))
                continue;

            var item = new TreeViewItem
            {
                Style = (Style)FindResource("TreeItem"),
                // El servidor al lado del nombre, salvo que sea lo mismo (importado de RDM suele serlo).
                Header = Header(c.Kind switch { ConnectionKind.Ssh => "", ConnectionKind.Sftp or ConnectionKind.Ftp => "", _ => "" }, c.Name.Length > 0 ? c.Name : Loc.Get("Unnamed"),
                    string.Equals(c.Caption, c.Name, StringComparison.OrdinalIgnoreCase) ? null : c.Caption),
                Tag = new Node { Connection = c },
            };
            item.ContextMenu = ConnectionMenu(item);
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

    /// <summary>
    /// Boton derecho sobre una conexion: Copiar (lo mismo que Duplicar) y Editar. El boton derecho no
    /// selecciona la fila en un TreeView: se selecciona al abrir el menu, y las dos opciones actuan
    /// sobre la seleccion, como los botones de arriba.
    /// </summary>
    private ContextMenu ConnectionMenu(TreeViewItem item)
    {
        var menu = new ContextMenu();
        var copy = MenuEntry("CopyMenu", "", "CopyMenuItem", OnDuplicateClick);
        var edit = MenuEntry("EditMenu", "", "EditMenuItem", OnEditClick);
        menu.Items.Add(copy);
        menu.Items.Add(edit);
        menu.Opened += (_, _) =>
        {
            item.IsSelected = true;
            // Por si se cambio de idioma con el arbol ya montado.
            copy.Header = Loc.Get("CopyMenu");
            edit.Header = Loc.Get("EditMenu");
        };
        return menu;
    }

    /// <summary>Boton derecho sobre una carpeta: exportar esa rama (con todo lo que tiene dentro).</summary>
    private ContextMenu FolderMenu(TreeViewItem item, string path)
    {
        var menu = new ContextMenu();
        var export = MenuEntry("ExportFolderMenu", "", "ExportFolderMenuItem", (_, _) => ExportConnections(path));
        menu.Items.Add(export);
        menu.Opened += (_, _) =>
        {
            item.IsSelected = true;
            export.Header = Loc.Get("ExportFolderMenu");
        };
        return menu;
    }

    /// <summary>
    /// Exporta a un fichero <c>.rcm</c> todas las conexiones (<paramref name="branch"/> vacio) o
    /// las de una carpeta y sus subcarpetas, para importarlas en otro equipo
    /// (<see cref="ConnectionExport"/>). Con frase, cifrado y con las contraseñas; sin ella, sin
    /// contraseñas.
    /// </summary>
    public void ExportConnections(string branch)
    {
        var content = ConnectionExport.Select(_store.Connections, _store.EmptyFolders, branch);
        if (content.Connections.Count == 0 && branch.Length == 0)
        {
            SetStatus(Loc.Get("ExportNothing"));
            return;
        }

        var passphrase = PromptWindow.AskPassword(this, Loc.Get("ExportTitle"), Loc.Format("ExportPassphrase", content.Connections.Count));
        if (passphrase is null)
            return;

        var name = branch.Length == 0 ? "conexiones" : branch[(branch.LastIndexOf('/') + 1)..];
        if (Dialogs.PickSaveFile(this, Loc.Get("ExportFilter"), name + ConnectionExport.Extension) is not { } file)
            return;

        try
        {
            Dialogs.WriteFile(file, ConnectionExport.Write(content, passphrase));
            SetStatus(Loc.Format(passphrase.Length > 0 ? "ExportedEncrypted" : "ExportedPlain", content.Connections.Count, Path.GetFileName(file)));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.Format("ExportFailed", ex.Message));
        }
    }

    private static MenuItem MenuEntry(string key, string glyph, string automationId, RoutedEventHandler click)
    {
        var entry = new MenuItem
        {
            Header = Loc.Get(key),
            Icon = new TextBlock { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") },
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(entry, automationId);
        entry.Click += click;
        return entry;
    }

    private static IEnumerable<TreeViewItem> Flatten(TreeViewItem item)
    {
        yield return item;
        foreach (var child in item.Items.OfType<TreeViewItem>())
            foreach (var sub in Flatten(child))
                yield return sub;
    }

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

    private void OnTreeMouseMove(object sender, MouseEventArgs e) => TreeMouseMove(e.GetPosition(Tree), e.LeftButton == MouseButtonState.Pressed);

    /// <summary>Con el boton pulsado y pasado el umbral, empieza a arrastrar el elemento donde se pulso.</summary>
    internal void TreeMouseMove(Point position, bool leftPressed)
    {
        if (!leftPressed || _dragItem?.Tag is not Node node)
            return;
        var delta = position - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var item = _dragItem;
        _dragItem = null;
        Desktop.Current.DoDragDrop(item, new DataObject(typeof(Node), node), DragDropEffects.Move | DragDropEffects.Copy);
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

    // Doble clic sobre una conexion: conectar. Ctrl + doble clic: editar la conexion o renombrar la
    // carpeta (sobre una carpeta, el doble clic a secas la despliega, que es lo suyo).
    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TreeDoubleClick(e.OriginalSource as DependencyObject, Keyboard.Modifiers))
            e.Handled = true;
    }

    /// <summary>El doble clic sobre <paramref name="source"/>; devuelve si lo ha atendido.</summary>
    internal bool TreeDoubleClick(DependencyObject? source, ModifierKeys modifiers)
    {
        if (ItemAt(source) is null || Selected is not { } node)
            return false;
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            OnEditClick(this, new RoutedEventArgs());
            return true;
        }
        if (node.Connection is not { } c)
            return false;
        Open(c);
        return true;
    }

    /// <summary>Abre la conexion sin esperar (como un manejador de eventos asincrono).</summary>
    private async void Open(Connection connection) => await OpenAsync(connection);

    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Selected?.Connection is { } c)
        {
            e.Handled = true;
            Open(c);
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
    /// Importa un .rdm exportado por otro gestor de conexiones. Las conexiones que ya existan (mismo nombre,
    /// servidor y carpeta) no se repiten; las carpetas se crean aunque esten vacias.
    /// </summary>
    /// <summary>Importar .rdm o .rdp (lo pide Ajustes: el boton salio de la barra del arbol el 2026-09-16).</summary>
    public void ImportRdm() => OnImportClick(this, new RoutedEventArgs());

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (Dialogs.PickFiles(this, Loc.Get("ImportFilter"), true) is not { } files)
            return;

        try
        {
            // Varios .rdp de golpe (uno por conexion) o un .rdm entero: todo a la misma lista.
            var connections = new List<Connection>();
            var folders = new List<string>();
            var skipped = 0;
            var withPasswords = false;
            foreach (var file in files)
            {
                if (file.EndsWith(".rdp", StringComparison.OrdinalIgnoreCase))
                {
                    var rdp = RdpFileImport.Read(file);
                    if (rdp.Host.Length > 0) connections.Add(rdp); else skipped++;
                }
                else if (file.EndsWith(ConnectionExport.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    // Exportado de otro sOC Remote Connections Manager: si va cifrado, se pide su frase.
                    var text = File.ReadAllText(file);
                    string? passphrase = null;
                    if (ConnectionExport.IsEncrypted(text))
                    {
                        passphrase = PromptWindow.AskPassword(this, Loc.Get("ImportTitle"), Loc.Format("ImportPassphrase", Path.GetFileName(file)));
                        if (passphrase is null)
                            return;
                        withPasswords = true;
                    }
                    var r = ConnectionExport.Read(text, passphrase);
                    connections.AddRange(r.Connections);
                    folders.AddRange(r.Folders);
                }
                else
                {
                    var r = RdmImport.Read(file);
                    connections.AddRange(r.Connections);
                    folders.AddRange(r.Folders);
                    skipped += r.Skipped;
                }
            }
            var result = new RdmImport.Result(connections, folders, skipped);
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

            _store.EmptyFolders.AddRange(result.Folders.Where(f => !_store.EmptyFolders.Contains(f, StringComparer.OrdinalIgnoreCase)));
            _store.Save();
            BuildTree();
            SetStatus(Loc.Format(withPasswords ? "ImportedWithPasswords" : "Imported", added, result.Connections.Count - added, result.Skipped));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            SetStatus(Loc.Get("ImportWrongPassphrase"));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.Format("ImportFailed", ex.Message));
        }
    }

    private bool Edit(Connection connection)
    {
        var dialog = new ConnectionWindow(connection, _store.AllFolders(), _store.Connections) { Owner = this };
        return Dialogs.ShowModal(dialog) == true;
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
            Dialogs.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Store.Location}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    // =====================================================================
    //  Sesiones
    // =====================================================================

    /// <summary>Abre la conexion con ese nombre (parametro --open de la linea de comandos).</summary>
    public async void OpenByName(string name)
    {
        var connection = _store.Connections.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (connection is null)
        {
            SetStatus(Loc.Format("OpenNotFound", name));
            return;
        }
        SelectConnection(connection);
        await OpenAsync(connection);
    }

    /// <summary>Abre esa conexion de ficheros y un fichero suyo en el editor integrado (--edit-file).</summary>
    public async void OpenFileInEditor(string name, string path)
    {
        var connection = _store.Connections.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (connection is null || !connection.IsFiles)
            return;
        SelectConnection(connection);
        await OpenAsync(connection);
        if (_open.FirstOrDefault(x => ReferenceEquals(x.Connection, connection))?.Session is FileSession files)
            await files.EditFileAsync(path);
    }

    /// <summary>Abre el editor de esa conexion en la pestaña dada (parametros --edit / --edit-tab).</summary>
    public void EditByName(string name, int tab)
    {
        var connection = _store.Connections.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (connection is null)
            return;
        SelectConnection(connection);
        var copy = connection.Clone();
        var dialog = new ConnectionWindow(copy, _store.AllFolders(), _store.Connections) { Owner = this, InitialTab = tab };
        if (Dialogs.ShowModal(dialog) == true)
        {
            _store.Connections[_store.Connections.IndexOf(connection)] = copy;
            _store.Save();
            BuildTree();
        }
    }

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (Selected?.Connection is { } c)
            await OpenAsync(c);
    }

    private async Task OpenAsync(Connection connection)
    {
        // Modo aislado de las pruebas: nunca se conecta a un servidor de verdad (general 8.4). Se
        // abre la pestaña con el control de la sesion sin conectar (para probar las pestañas, sacarlas
        // a una ventana y devolverlas), sin pedir contraseña.
        var password = Sandbox.IsOn ? string.Empty : Secrets.Unprotect(connection.PasswordProtected);
        if (!Sandbox.IsOn && password.Length == 0 && connection.PrivateKeyPath.Length == 0)
        {
            var asked = PromptWindow.AskPassword(this, Loc.Get("PasswordTitle"), Loc.Format("PasswordPrompt", connection.UserName, connection.Host), Loc.Get("SavePassword"));
            password = asked?.Password ?? string.Empty;
            if (password.Length == 0 && connection.IsSsh)
                return;

            // Guardarla: cifrada con DPAPI para este usuario de Windows, como desde el editor.
            if (asked is { Save: true } && password.Length > 0)
            {
                connection.PasswordProtected = Secrets.Protect(password);
                _store.Save();
            }
        }

        var session = SessionFactory.Create(connection);

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
        var header = new StackPanel { Orientation = Orientation.Horizontal, Children = { title } };
        // Zoom: letra del terminal y de los paneles de ficheros, escala del escritorio en RDP.
        if (session.CanZoom)
        {
            var zoomOut = TabButton("\uE71F", Loc.Get("ZoomOutTooltip"), new Thickness(8, 0, -6, 0));
            var zoomIn = TabButton("\uE8A3", Loc.Get("ZoomInTooltip"), new Thickness(0, 0, -6, 0));
            zoomOut.Click += (_, _) => { SetStatus(Loc.Format("ZoomStatus", connection.Name, session.Zoom(-1))); _store.Save(); };
            zoomIn.Click += (_, _) => { SetStatus(Loc.Format("ZoomStatus", connection.Name, session.Zoom(+1))); _store.Save(); };
            header.Children.Add(zoomOut);
            header.Children.Add(zoomIn);
            fullButton.Margin = new Thickness(0, 0, -6, 0);
        }
        // Sacar la pestaña a una ventana propia (o devolverla, desde esa ventana).
        var detachButton = TabButton("\uE8A7", Loc.Get("DetachTooltip"), new Thickness(0, 0, -6, 0));
        System.Windows.Automation.AutomationProperties.SetAutomationId(detachButton, "DetachButton");
        System.Windows.Automation.AutomationProperties.SetAutomationId(closeButton, "DisconnectButton");
        System.Windows.Automation.AutomationProperties.SetAutomationId(fullButton, "TabFullScreenButton");
        header.Children.Add(fullButton);
        header.Children.Add(detachButton);
        header.Children.Add(closeButton);
        var holder = new Border { Child = session.View };
        var tab = new TabItem { Header = header, Content = holder };
        System.Windows.Automation.AutomationProperties.SetAutomationId(tab, "SessionTab");
        System.Windows.Automation.AutomationProperties.SetName(tab, connection.Name);

        var entry = new OpenSession
        {
            Tab = tab,
            Session = session,
            Connection = connection,
            Holder = holder,
            Header = header,
            TitleText = title,
            DetachButton = detachButton,
        };
        fullButton.Click += (_, _) => EnterFullScreen(entry);
        detachButton.Click += (_, _) =>
        {
            if (entry.IsDetached)
                Attach(entry);
            else
                Detach(entry, null);
        };
        tab.ContextMenu = TabMenu(entry);

        _open.Add(entry);
        Tabs.Items.Add(tab);
        UpdateSessionsButton();
        Tabs.SelectedItem = tab;
        EmptyTabs.Visibility = Visibility.Collapsed;

        closeButton.Click += (_, _) => CloseSession(entry);
        session.TitleChanged += t => Dispatcher.BeginInvoke(() =>
        {
            title.Text = t.Length > 0 ? $"{connection.Name} · {t}" : connection.Name;
            if (entry.Window is { } w)
                w.Title = title.Text;
        });
        if (session is RdpSession rdp)
            rdp.MinimizeRequested += () => Dispatcher.BeginInvoke(() =>
            {
                if (entry.Window is { } w)
                    w.WindowState = WindowState.Minimized;
                else
                    WindowState = WindowState.Minimized;
            });
        session.Ended += reason => Dispatcher.BeginInvoke(() =>
        {
            SetStatus(reason is null ? Loc.Format("SessionClosed", connection.Name) : Loc.Format("SessionEnded", connection.Name, reason));
            CloseSession(entry);
        });

        if (Sandbox.IsOn)
        {
            SetStatus(Loc.Format("SandboxNotConnected", connection.Name));
            return;
        }

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
            // En la barra de estado se pasa por alto: un aviso con la razon en cristiano
            // (nombre que no resuelve, puerto cerrado, sin respuesta, credenciales, TLS…).
            var reason = ConnectionErrors.Describe(ex, connection);
            SetStatus(Loc.Format("ConnectFailed", connection.Name, reason));
            CloseSession(entry);
            PromptWindow.Alert(this, Loc.Get("ConnectFailedTitle"), Loc.Format("ConnectFailedText", connection.Name, connection.Host, connection.Port, reason));
        }
    }

    private Button TabButton(string glyph, string tooltip, Thickness margin) => new()
    {
        Style = (Style)FindResource("GhostIconButton"),
        Content = glyph,
        Width = 24, Height = 24, FontSize = 11,
        Margin = margin,
        ToolTip = tooltip,
    };

    // =====================================================================
    //  Pantalla completa
    // =====================================================================

    /// <summary>
    /// Lleva la ventana a la pantalla pedida (1..n; 0 = dejarla donde esta) antes de ponerla a
    /// pantalla completa: tanto la ventana maximizada como el control RDP se van a pantalla
    /// completa en el monitor donde esten.
    /// </summary>
    private void MoveToScreen(int screen)
    {
        var screens = Desktop.Current.Monitors();
        if (screen <= 0 || screen > screens.Count)
            return;
        var bounds = screens[screen - 1].Bounds;
        var source = PresentationSource.FromVisual(this);
        var m = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var topLeft = m.Transform(new Point(bounds.Left, bounds.Top));
        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;
        Left = topLeft.X + 40;
        Top = topLeft.Y + 40;
    }

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
            FullScreenTitle.Text = Find(Tabs.SelectedItem)?.Connection.Name ?? string.Empty;
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

        if (Find(Tabs.SelectedItem)?.Session is { } session)
            Dispatcher.BeginInvoke(session.Focus, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void HideTabHeaders(bool hide)
    {
        foreach (TabItem item in Tabs.Items)
        {
            var selected = ReferenceEquals(item, Tabs.SelectedItem);
            item.Visibility = hide && !selected ? Visibility.Collapsed : Visibility.Visible;
            // La cabecera de la pestaña visible se encoge a nada: el contenido ocupa todo. Las demas,
            // a su altura: si no, la que estuvo activa al cambiar de sesion en pantalla completa se
            // quedaba sin cabecera al salir.
            item.Height = hide && selected ? 0 : double.NaN;
        }
    }

    private void OnToggleFullScreenClick(object sender, RoutedEventArgs e) => SetFullScreen(!_fullScreen);

    private void OnFullScreenCloseClick(object sender, RoutedEventArgs e)
    {
        if (Find(Tabs.SelectedItem) is { } open)
            CloseSession(open);
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

    /// <summary>Menu con las sesiones abiertas: se elige una y pasa a ser la pestaña activa (o su ventana suelta, al frente).</summary>
    private void OnSessionsClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var open in _open)
        {
            var item = new MenuItem
            {
                Header = open.TitleText.Text.Length > 0 ? open.TitleText.Text : open.Connection.Name,
                IsChecked = open.IsDetached ? open.Window!.IsActive : ReferenceEquals(Tabs.SelectedItem, open.Tab),
                Icon = new TextBlock { Text = open.Connection.Kind switch { ConnectionKind.Ssh => "", ConnectionKind.Sftp or ConnectionKind.Ftp => "", _ => "" }, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") },
            };
            var target = open;
            item.Click += (_, _) => SelectSession(target);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0)
            return;
        menu.PlacementTarget = sender as UIElement;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        Desktop.Current.OpenMenu(menu);
    }

    private void SelectSession(OpenSession open)
    {
        if (open.Window is { } w)
        {
            // En modo aislado sin activarla, como la principal (no se le quita el foco a nadie).
            w.BringToFront(activate: !Sandbox.IsOn);
            Dispatcher.BeginInvoke(open.Session.Focus, System.Windows.Threading.DispatcherPriority.Input);
            return;
        }
        Tabs.SelectedItem = open.Tab;
        if (_fullScreen)
        {
            HideTabHeaders(true);
            FullScreenTitle.Text = open.Connection.Name;
        }
        Dispatcher.BeginInvoke(open.Session.Focus, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Ctrl+Tab: entre las pestañas de la ventana principal (las sueltas tienen su ventana).</summary>
    private void CycleTab(int direction)
    {
        var tabs = _open.Where(x => !x.IsDetached).ToList();
        if (tabs.Count < 2)
            return;
        var index = tabs.FindIndex(x => ReferenceEquals(x.Tab, Tabs.SelectedItem));
        var next = ((index < 0 ? 0 : index) + direction + tabs.Count) % tabs.Count;
        SelectSession(tabs[next]);
    }

    private void UpdateSessionsButton() => SessionsButton.Visibility = _open.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (Shortcut(e.Key, Keyboard.Modifiers))
            e.Handled = true;
    }

    /// <summary>Los atajos de la ventana; devuelve si la tecla era uno.</summary>
    internal bool Shortcut(Key key, ModifierKeys modifiers)
    {
        // Ctrl+Tab / Ctrl+Mayus+Tab: siguiente / anterior pestaña (con el escritorio remoto enfocado
        // las teclas se van al remoto; para eso esta el boton de sesiones).
        if (key == Key.Tab && (modifiers & ModifierKeys.Control) != 0)
        {
            CycleTab((modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
            return true;
        }
        if (key == Key.Escape && _fullScreen && (modifiers & ModifierKeys.Control) != 0)
        {
            SetFullScreen(false);
            return true;
        }
        if (key != Key.F11)
            return false;
        SetFullScreen(!_fullScreen);
        return true;
    }

    /// <summary>Desconecta y quita la sesion, este en una pestaña o en su ventana suelta.</summary>
    private void CloseSession(OpenSession open)
    {
        if (!_open.Remove(open))
            return;

        open.Session.Disconnect();
        if (open.Window is { } w)
        {
            CloseDetachedWindow(open, w);
            _settings.Save();
        }
        else
        {
            Tabs.Items.Remove(open.Tab);
        }
        UpdateSessionsButton();
        EmptyTabs.Visibility = Tabs.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Tabs.Items.Count == 0 && _fullScreen)
            SetFullScreen(false);
    }

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Find(Tabs.SelectedItem)?.Session is { } session)
            Dispatcher.BeginInvoke(session.Focus, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>El boton de pantalla completa de la pestaña (o de la ventana suelta).</summary>
    private void EnterFullScreen(OpenSession open)
    {
        var screen = open.Connection.FullScreenScreen;
        if (open.Window is { } w)
        {
            SessionWindow.MoveToScreen(w, screen);
            if (open.Session.HasNativeFullScreen)
                open.Session.EnterFullScreen(screen);
            else
                w.SetFullScreen(true);
            return;
        }
        Tabs.SelectedItem = open.Tab;
        MoveToScreen(screen);
        if (open.Session.HasNativeFullScreen)
            open.Session.EnterFullScreen(screen);
        else
            SetFullScreen(true);
    }

    // =====================================================================
    //  Pestañas sueltas: sacar una pestaña a su propia ventana y devolverla
    // =====================================================================

    /// <summary>Boton derecho sobre la pestaña: sacarla (o devolverla) y desconectar.</summary>
    private ContextMenu TabMenu(OpenSession open)
    {
        var menu = new ContextMenu();
        var move = new MenuItem();
        var disconnect = new MenuItem { Header = Loc.Get("DisconnectMenu") };
        move.Click += (_, _) => { if (open.IsDetached) Attach(open); else Detach(open, null); };
        disconnect.Click += (_, _) => CloseSession(open);
        menu.Items.Add(move);
        menu.Items.Add(disconnect);
        menu.Opened += (_, _) =>
        {
            move.Header = Loc.Get(open.IsDetached ? "AttachMenu" : "DetachMenu");
            disconnect.Header = Loc.Get("DisconnectMenu");
        };
        return menu;
    }

    private void UpdateDetachButton(OpenSession open)
    {
        open.DetachButton.Content = open.IsDetached ? "" : "";
        open.DetachButton.ToolTip = Loc.Get(open.IsDetached ? "AttachTooltip" : "DetachTooltip");
        System.Windows.Automation.AutomationProperties.SetAutomationId(open.DetachButton, open.IsDetached ? "AttachButton" : "DetachButton");
    }

    /// <summary>
    /// Saca la pestaña a una ventana propia. La sesion no se reconecta: el control se mueve tal cual
    /// (ver <see cref="SessionWindow"/>). <paramref name="cursor"/> es donde se solto al arrastrar
    /// (unidades de WPF); sin el, la ventana vuelve a donde estuvo la ultima vez.
    /// </summary>
    private void Detach(OpenSession open, Point? cursor)
    {
        if (open.IsDetached || !_open.Contains(open))
            return;
        if (_fullScreen)
            SetFullScreen(false);
        // La pantalla completa propia de la sesion (RDP) no se lleva a la otra ventana (si no lo esta, no hace nada).
        open.Session.LeaveFullScreen();

        // Tamaño de la ventana: el de la pestaña ahora (mas el marco), o el que tuvo la ultima vez.
        var width = Math.Max(SessionPlacement.MinWidth, TabsArea.ActualWidth + 16);
        var height = Math.Max(SessionPlacement.MinHeight, TabsArea.ActualHeight + 40);
        _settings.DetachedWindows.TryGetValue(SessionPlacement.Key(open.Connection.Id), out var saved);
        var bounds = SessionPlacement.ForDetach(cursor is { } c ? (c.X, c.Y) : null, saved, width, height,
            new WindowBounds(Left, Top, Width, Height), _open.Count(x => x.IsDetached), ScreenAreas());

        // Primero se suelta el control (al instante: Border.Child), luego la cabecera y la pestaña.
        var view = (FrameworkElement)open.Holder.Child;
        open.Holder.Child = null;
        open.Tab.Header = null;
        Tabs.Items.Remove(open.Tab);

        var window = new SessionWindow(open.TitleText.Text, open.Header, view);
        window.Place(bounds);
        window.AttachRequested += () => Attach(open);
        window.BarDragStarted += () => DragSession(open, window);
        window.FullScreenToggleRequested += () =>
        {
            if (!open.Session.HasNativeFullScreen)
                window.SetFullScreen(!window.IsFullScreen);
        };
        open.Window = window;
        UpdateDetachButton(open);
        Dialogs.ShowWindow(window);

        EmptyTabs.Visibility = Tabs.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SetStatus(Loc.Format("DetachedStatus", open.Connection.Name));
        Dispatcher.BeginInvoke(open.Session.Focus, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Devuelve a la principal, como pestaña, una sesion que estaba en su ventana. Sin reconectar.</summary>
    private void Attach(OpenSession open)
    {
        if (open.Window is not { } window || !_open.Contains(open))
            return;
        open.Session.LeaveFullScreen();

        CloseDetachedWindow(open, window, keepParts: true, out var view, out var header);
        _settings.Save();
        open.Holder.Child = view;
        open.Tab.Header = header;
        Tabs.Items.Add(open.Tab);
        UpdateDetachButton(open);
        Tabs.SelectedItem = open.Tab;
        EmptyTabs.Visibility = Visibility.Collapsed;
        SetStatus(Loc.Format("AttachedStatus", open.Connection.Name));
        BringToFront();
        Dispatcher.BeginInvoke(open.Session.Focus, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CloseDetachedWindow(OpenSession open, SessionWindow window) =>
        CloseDetachedWindow(open, window, keepParts: false, out _, out _);

    /// <summary>
    /// Cierra la ventana suelta recordando donde estaba. El control de la sesion se suelta antes de
    /// cerrarla: Windows destruye las ventanas hijas de la que se cierra, y el escritorio remoto lo es.
    /// </summary>
    private void CloseDetachedWindow(OpenSession open, SessionWindow window, bool keepParts, out FrameworkElement? view, out FrameworkElement? header)
    {
        _settings.DetachedWindows[SessionPlacement.Key(open.Connection.Id)] = window.Bounds;
        if (window.IsFullScreen)
            window.SetFullScreen(false);
        view = window.ReleaseView();
        header = window.ReleaseHeader();
        window.ForceClose = true;
        window.Close();
        open.Window = null;
        if (!keepParts)
        {
            view = null;
            header = null;
        }
    }

    // ------------------------------------------------------------------ Arrastrar pestañas

    private const string SessionDragFormat = "SocRcManager.Session";
    private Point? _tabDragStart;
    private TabItem? _tabDragItem;
    private bool _dropInMain;

    private void OnTabsPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _tabDragStart = null;
        _tabDragItem = null;
        // Solo en la cabecera de una pestaña y no en sus botones (el contenido no es hijo del TabItem).
        for (var o = e.OriginalSource as DependencyObject; o is not null; o = System.Windows.Media.VisualTreeHelper.GetParent(o))
        {
            if (o is System.Windows.Controls.Primitives.ButtonBase)
                return;
            if (o is TabItem t)
            {
                _tabDragStart = e.GetPosition(this);
                _tabDragItem = t;
                return;
            }
        }
    }

    private void OnTabsPreviewMouseMove(object sender, MouseEventArgs e) => TabsMouseMove(e.GetPosition(this), e.LeftButton == MouseButtonState.Pressed);

    /// <summary>Con el boton pulsado sobre una cabecera y pasado el umbral, empieza a arrastrar la pestaña.</summary>
    internal void TabsMouseMove(Point position, bool leftPressed)
    {
        if (_tabDragStart is not { } start || !leftPressed || Find(_tabDragItem) is not { } open)
            return;
        // Capturado desde el primer movimiento: aunque el raton salga deprisa de la cabecera, el
        // arrastre empieza. Se suelta antes de arrastrar (y al soltar el boton).
        if (!Tabs.IsMouseCaptured)
            Desktop.Current.CaptureMouse(Tabs);
        var delta = position - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _tabDragStart = null;
        _tabDragItem = null;
        Tabs.ReleaseMouseCapture();
        DragSession(open, this);
    }

    private void OnTabsPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _tabDragStart = null;
        _tabDragItem = null;
        Tabs.ReleaseMouseCapture();   // si no lo tiene, no hace nada
    }

    /// <summary>
    /// Arrastrar una sesion: desde la principal, soltada fuera de ella, sale a una ventana en ese
    /// sitio; desde su ventana suelta, soltada sobre la principal, vuelve como pestaña (y soltada en
    /// otro sitio, la ventana se lleva alli). Esc cancela.
    /// </summary>
    private void DragSession(OpenSession open, Window source)
    {
        var cancelled = false;
        void Query(object? s, QueryContinueDragEventArgs q)
        {
            if (q.EscapePressed)
                cancelled = true;
        }
        _dropInMain = false;
        source.QueryContinueDrag += Query;
        try
        {
            Desktop.Current.DoDragDrop(source, new DataObject(SessionDragFormat, open.Connection.Id.ToString()), DragDropEffects.Move);
        }
        finally
        {
            source.QueryContinueDrag -= Query;
            // Que ninguna captura se quede colgada: se comeria los clics de las otras ventanas.
            Mouse.Capture(null);
        }
        var p = Desktop.Current.CursorPosition();
        var root = Desktop.Current.RootWindowAt(p.X, p.Y);
        var mainHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var overDetached = _open.Any(x => x.Window is { } w && new System.Windows.Interop.WindowInteropHelper(w).Handle == root);
        var action = SessionPlacement.AfterDrag(open.IsDetached, cancelled, _dropInMain || root == mainHwnd, overDetached);
        var cursor = ToDip(p.X, p.Y);
        // Fuera del manejador del raton de la ventana de origen (que puede cerrarse).
        Dispatcher.BeginInvoke(() =>
        {
            switch (action)
            {
                case SessionPlacement.DropAction.Detach:
                    Detach(open, cursor);
                    break;
                case SessionPlacement.DropAction.Attach:
                    Attach(open);
                    break;
                case SessionPlacement.DropAction.MoveWindow when open.Window is { } w:
                    var b = SessionPlacement.AtCursor(cursor.X, cursor.Y, w.Width, w.Height, ScreenAreas());
                    w.Left = b.Left;
                    w.Top = b.Top;
                    break;
            }
        });
    }

    private bool IsSessionDrag(DragEventArgs e) => e.Data.GetDataPresent(SessionDragFormat);

    // Soltar una conexion del arbol en el area de pestañas la abre; soltar ahi una ventana suelta la devuelve.
    private void OnTabsDragOver(object sender, DragEventArgs e)
    {
        e.Effects = IsSessionDrag(e) || e.Data.GetData(typeof(Node)) is Node { Connection: not null } ? DragDropEffects.Move | DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnTabsDrop(object sender, DragEventArgs e)
    {
        if (IsSessionDrag(e))
        {
            _dropInMain = true;
            e.Handled = true;
            return;
        }
        if (e.Data.GetData(typeof(Node)) is not Node { Connection: { } c })
            return;
        e.Handled = true;
        await OpenAsync(c);
    }

    /// <summary>Areas de trabajo de los monitores en unidades de WPF, la principal primero.</summary>
    private List<ScreenArea> ScreenAreas()
    {
        var list = new List<ScreenArea>();
        foreach (var s in Desktop.Current.Monitors().OrderByDescending(s => s.Primary))
        {
            var tl = ToDip(s.WorkingArea.Left, s.WorkingArea.Top);
            var br = ToDip(s.WorkingArea.Right, s.WorkingArea.Bottom);
            list.Add(new ScreenArea(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y));
        }
        return list;
    }

    private Point ToDip(int x, int y)
    {
        var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        return m.Transform(new Point(x, y));
    }

    // =====================================================================
    //  Instancia unica y bandeja
    // =====================================================================

    private bool _forceClose;

    /// <summary>Cerrar sin preguntar por las ventanas sueltas (otra version toma el relevo).</summary>
    public void CloseForced()
    {
        _forceClose = true;
        Close();
    }

    /// <summary>Arrancar escondida en el area de notificacion (--tray), sin enseñarse antes.</summary>
    public void StartInTray()
    {
        new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        _tray?.HideToTray();
    }

    /// <summary>
    /// Al frente: de la bandeja si estaba escondida, restaurada si estaba minimizada, y con el foco.
    /// En modo aislado no se activa (no se le quita el foco a quien este trabajando).
    /// </summary>
    public void BringToFront()
    {
        var activate = !Sandbox.IsOn;
        if (!IsVisible)
        {
            if (_tray is not null)
                _tray.Restore(activate);
            else
                Show();
        }
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        if (activate)
            Desktop.Current.ForceForeground(this);
    }

    // =====================================================================

    private void OnLanguageClick(object sender, RoutedEventArgs e)
    {
        Loc.Toggle();
        BuildTree();
    }

    private void OnAboutClick(object sender, RoutedEventArgs e) => Dialogs.ShowModal(new AboutWindow { Owner = this });

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings, _sync) { Owner = this };
        Dialogs.ShowModal(dialog);
        // El interruptor de la bandeja se puede haber cambiado ahi.
        if (_tray is not null)
            _tray.MinimizeToTray = _settings.TrayOnMinimize;
        if (dialog.LocalReplaced)
            BuildTree();
    }
}
