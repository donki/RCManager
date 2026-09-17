using System.Windows;
using System.Windows.Controls;
using SocRcManager.Localization;
using SocRcManager.Services;

namespace SocRcManager.Files;

/// <summary>El fichero ya existe en el destino: sobrescribir, saltar o parar; y si vale para todos los que vengan.</summary>
public sealed class ConflictWindow : Window
{
    public sealed record Answer(bool Overwrite, bool ApplyToAll, bool Cancel);

    private Answer _answer = new(false, false, true);

    private ConflictWindow(Window owner, string name)
    {
        Owner = owner;
        Title = Loc.Get("ConflictTitle");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)FindResource("PageBackground");
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(18, 14, 18, 16) };
        var stack = new StackPanel();
        card.Child = stack;
        stack.Children.Add(new TextBlock { Text = Loc.Format("ConflictMessage", name), TextWrapping = TextWrapping.Wrap, Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary") });
        var all = new CheckBox { Style = (Style)FindResource("Check"), Content = Loc.Get("ConflictApplyAll"), Margin = new Thickness(0, 12, 0, 0) };
        stack.Children.Add(all);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var skip = new Button { Style = (Style)FindResource("OutlineButton"), Content = Loc.Get("ConflictSkip"), Margin = new Thickness(0, 0, 8, 0) };
        skip.Click += (_, _) => { _answer = new Answer(false, all.IsChecked == true, false); DialogResult = true; };
        var overwrite = new Button { Style = (Style)FindResource("OutlineButton"), Content = Loc.Get("ConflictOverwrite"), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        overwrite.Click += (_, _) => { _answer = new Answer(true, all.IsChecked == true, false); DialogResult = true; };
        var cancel = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("FilesCancel"), IsCancel = true };
        buttons.Children.Add(skip);
        buttons.Children.Add(overwrite);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(card);
        root.Children.Add(buttons);
        Content = root;
    }

    public static Answer Ask(Window owner, string name)
    {
        var w = new ConflictWindow(owner, name);
        w.ShowDialog();
        return w._answer;
    }
}
