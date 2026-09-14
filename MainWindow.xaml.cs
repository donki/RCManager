using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Connections.Localization;
using Connections.Models;
using Connections.Services;
using Connections.Sessions;

namespace Connections;

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
    private readonly List<(TabItem Tab, ISession Session, Connection Connection)> _open = [];

    public MainWindow()
    {
        InitializeComponent();

        ApplyTexts();
        Loc.LanguageChanged += ApplyTexts;
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        _store.Load();
        BuildTree();
        Closing += (_, _) =>
        {
            foreach (var (_, session, _) in _open.ToList())
                session.Disconnect();
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
        ConnectButton.ToolTip = Loc.Get("ConnectTooltip");
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
                Header = Header(c.Kind == ConnectionKind.Ssh ? "" : "", c.Name.Length > 0 ? c.Name : Loc.Get("Unnamed"), c.Caption),
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
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 14,
            Foreground = (System.Windows.Media.Brush)FindResource("Primary"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary") });
        if (detail is not null)
            panel.Children.Add(new TextBlock { Text = detail, Style = (Style)FindResource("HintText"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return panel;
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
            password = PromptWindow.AskPassword(this, Loc.Get("PasswordTitle"), Loc.Format("PasswordPrompt", connection.UserName, connection.Host)) ?? string.Empty;
            if (password.Length == 0 && connection.Kind == ConnectionKind.Ssh)
                return;
        }

        ISession session = connection.Kind == ConnectionKind.Ssh ? new SshSession(connection) : new RdpSession(connection);

        var closeButton = new Button
        {
            Style = (Style)FindResource("GhostIconButton"),
            Content = "",
            Width = 24, Height = 24, FontSize = 11,
            Margin = new Thickness(8, 0, -4, 0),
            ToolTip = Loc.Get("DisconnectTooltip"),
        };
        var title = new TextBlock { Text = connection.Name, VerticalAlignment = VerticalAlignment.Center };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Children = { title, closeButton } };
        var tab = new TabItem { Header = header, Content = session.View };

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
}
