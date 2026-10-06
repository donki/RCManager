using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SocRcManager.Localization;
using SocRcManager.Models;
using SocRcManager.Services;

namespace SocRcManager;

/// <summary>
/// Elegir una conexion guardada, con buscador: la usa el editor para «Copiar la configuracion de
/// otra conexion». Se monta en codigo, como <see cref="PromptWindow"/>, con su mismo aspecto.
/// </summary>
public sealed class ConnectionPickerWindow : Window
{
    private readonly IReadOnlyList<Connection> _all;
    private readonly Button _ok;

    internal TextBox Search { get; }
    internal ListBox List { get; }

    private ConnectionPickerWindow(Window owner, IReadOnlyList<Connection> connections)
    {
        _all = connections;
        Owner = owner;
        Title = Loc.Get("CopyFromTitle");
        Width = 460;
        Height = 520;
        MinHeight = 360;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("PageBackground");
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        Search = new TextBox { Style = (Style)FindResource("Field"), Margin = new Thickness(0, 10, 0, 8) };
        List = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("TextPrimary"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(List, ScrollBarVisibility.Disabled);
        System.Windows.Automation.AutomationProperties.SetAutomationId(Search, "CopyFromSearch");
        System.Windows.Automation.AutomationProperties.SetAutomationId(List, "CopyFromList");

        var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(18, 14, 18, 16) };
        var layout = new DockPanel();
        var message = new TextBlock
        {
            Text = Loc.Get("CopyFromMessage"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextPrimary"),
        };
        DockPanel.SetDock(message, Dock.Top);
        DockPanel.SetDock(Search, Dock.Top);
        layout.Children.Add(message);
        layout.Children.Add(Search);
        layout.Children.Add(List);
        card.Child = layout;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("Cancel"), IsCancel = true };
        _ok = new Button { Style = (Style)FindResource("IconButton"), Content = "", ToolTip = Loc.Get("CopyFromOk"), IsDefault = true, IsEnabled = false };
        System.Windows.Automation.AutomationProperties.SetAutomationId(cancel, "CancelButton");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_ok, "OkButton");
        _ok.Click += (_, _) => Accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(_ok);

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(card);
        Content = root;

        Search.TextChanged += (_, _) => Fill();
        // Flecha abajo desde el buscador baja a la lista, como en el arbol de la ventana principal.
        Search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && List.Items.Count > 0)
            {
                List.SelectedIndex = Math.Max(0, List.SelectedIndex);
                (List.ItemContainerGenerator.ContainerFromIndex(List.SelectedIndex) as ListBoxItem)?.Focus();
                e.Handled = true;
            }
        };
        List.SelectionChanged += (_, _) => _ok.IsEnabled = List.SelectedItem is not null;
        List.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(List, d) is ListBoxItem)
                Accept();
        };

        Fill();
        Loaded += (_, _) => Search.Focus();
    }

    /// <summary>La conexion elegida, o null si se cancela.</summary>
    public static Connection? Pick(Window owner, IReadOnlyList<Connection> connections)
    {
        var w = new ConnectionPickerWindow(owner, connections);
        return Dialogs.ShowModal(w) == true ? (w.List.SelectedItem as ListBoxItem)?.Tag as Connection : null;
    }

    /// <summary>Las que casan con el buscador (<see cref="Connection.Matches"/>), por carpeta y nombre.</summary>
    internal static IReadOnlyList<Connection> Filter(IEnumerable<Connection> connections, string? filter)
    {
        var text = filter?.Trim() ?? string.Empty;
        return connections
            .Where(c => text.Length == 0 || c.Matches(text))
            .OrderBy(c => c.Folder, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void Fill()
    {
        List.Items.Clear();
        foreach (var c in Filter(_all, Search.Text))
        {
            var detail = string.Join(" · ", new[] { c.Folder, c.Kind.ToString().ToUpperInvariant(), c.Caption }.Where(t => t.Length > 0));
            var text = new StackPanel { Margin = new Thickness(4, 3, 4, 3) };
            text.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = detail, Style = (Style)FindResource("HintText"), TextTrimming = TextTrimming.CharacterEllipsis });
            List.Items.Add(new ListBoxItem { Content = text, Tag = c });
        }

        if (List.Items.Count == 1)
            List.SelectedIndex = 0;
    }

    private void Accept()
    {
        if (List.SelectedItem is not null)
            DialogResult = true;
    }
}
