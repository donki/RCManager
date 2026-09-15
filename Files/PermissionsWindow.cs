using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SocRcManager.Localization;
using SocRcManager.Services;

namespace SocRcManager.Files;

/// <summary>
/// Permisos (rwx por propietario, grupo y otros, con el octal a la vista) y propietario/grupo de lo
/// seleccionado en el servidor. Solo tiene sentido en servidores Unix; el panel remoto no ofrece el
/// boton si el servidor no lo es.
/// </summary>
public sealed class PermissionsWindow : Window
{
    private readonly CheckBox[] _bits = new CheckBox[9];
    private readonly TextBox _octal;
    private readonly TextBox _owner;
    private readonly TextBox _group;
    private readonly CheckBox _recursive;
    private bool _syncing;

    public int? Mode { get; private set; }
    public string OwnerName => _owner.Text.Trim();
    public string GroupName => _group.Text.Trim();
    public bool Recursive => _recursive.IsChecked == true;
    public bool ChangeMode { get; private set; }
    public bool ChangeOwner { get; private set; }

    public PermissionsWindow(Window owner, IReadOnlyList<FileEntry> entries)
    {
        Owner = owner;
        Title = Loc.Get("PermsTitle");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)FindResource("PageBackground");
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(18, 12, 18, 16) };
        var stack = new StackPanel();
        card.Child = stack;

        // Que se toca: uno, o varios (con el nombre del primero y cuantos mas).
        var what = entries.Count == 1 ? entries[0].Name : Loc.Format("FilesItems", entries.Count);
        stack.Children.Add(new TextBlock { Text = what, FontWeight = FontWeights.SemiBold, Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"), TextTrimming = TextTrimming.CharacterEllipsis });

        // --- Permisos: una rejilla 3x3 de casillas ---
        stack.Children.Add(new TextBlock { Text = Loc.Get("PermsPermissions"), Style = (Style)FindResource("FieldLabel") });
        var grid = new Grid();
        for (var i = 0; i < 4; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(110) : new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] cols = [Loc.Get("PermsRead"), Loc.Get("PermsWrite"), Loc.Get("PermsExecute")];
        string[] rows = [Loc.Get("PermsOwner"), Loc.Get("PermsGroup"), Loc.Get("PermsOthers")];
        for (var c = 0; c < 3; c++)
        {
            var header = new TextBlock { Text = cols[c], Style = (Style)FindResource("HintText"), HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetRow(header, 0); Grid.SetColumn(header, c + 1);
            grid.Children.Add(header);
        }
        for (var r = 0; r < 3; r++)
        {
            var label = new TextBlock { Text = rows[r], Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            Grid.SetRow(label, r + 1); Grid.SetColumn(label, 0);
            grid.Children.Add(label);
            for (var c = 0; c < 3; c++)
            {
                var box = new CheckBox { Style = (Style)FindResource("Check"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
                box.Checked += (_, _) => FromBits();
                box.Unchecked += (_, _) => FromBits();
                Grid.SetRow(box, r + 1); Grid.SetColumn(box, c + 1);
                grid.Children.Add(box);
                _bits[r * 3 + c] = box;
            }
        }
        stack.Children.Add(grid);

        var octalRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        octalRow.Children.Add(new TextBlock { Text = Loc.Get("PermsOctal"), Style = (Style)FindResource("HintText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        _octal = new TextBox { Style = (Style)FindResource("Field"), Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
        _octal.TextChanged += (_, _) => FromOctal();
        octalRow.Children.Add(_octal);
        stack.Children.Add(octalRow);

        // --- Propietario y grupo ---
        stack.Children.Add(new TextBlock { Text = Loc.Get("PermsOwnership"), Style = (Style)FindResource("FieldLabel"), Margin = new Thickness(0, 16, 0, 4) });
        var og = new Grid();
        og.ColumnDefinitions.Add(new ColumnDefinition());
        og.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        og.ColumnDefinitions.Add(new ColumnDefinition());
        var ownerStack = new StackPanel();
        ownerStack.Children.Add(new TextBlock { Text = Loc.Get("PermsOwner"), Style = (Style)FindResource("HintText") });
        _owner = new TextBox { Style = (Style)FindResource("Field") };
        ownerStack.Children.Add(_owner);
        var groupStack = new StackPanel();
        groupStack.Children.Add(new TextBlock { Text = Loc.Get("PermsGroup"), Style = (Style)FindResource("HintText") });
        _group = new TextBox { Style = (Style)FindResource("Field") };
        groupStack.Children.Add(_group);
        Grid.SetColumn(ownerStack, 0); Grid.SetColumn(groupStack, 2);
        og.Children.Add(ownerStack); og.Children.Add(groupStack);
        stack.Children.Add(og);
        stack.Children.Add(new TextBlock { Text = Loc.Get("PermsOwnerHint"), Style = (Style)FindResource("HintText"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });

        _recursive = new CheckBox { Style = (Style)FindResource("Check"), Content = Loc.Get("PermsRecursive"), Margin = new Thickness(0, 12, 0, 0), Visibility = entries.Any(e => e.IsDirectory) ? Visibility.Visible : Visibility.Collapsed };
        stack.Children.Add(_recursive);

        // Valores de partida: los del primero (si todos coinciden se ven tal cual; si no, tambien: es lo que se aplicara).
        var first = entries[0];
        _syncing = true;
        if (first.Mode is { } mode)
            SetBits(mode);
        _octal.Text = first.Mode is { } m ? Convert.ToString(m, 8).PadLeft(3, '0') : string.Empty;
        _owner.Text = first.Owner ?? string.Empty;
        _group.Text = first.Group ?? string.Empty;
        _syncing = false;
        var originalMode = first.Mode;
        var originalOwner = _owner.Text;
        var originalGroup = _group.Text;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("Cancel"), IsCancel = true };
        var ok = new Button { Style = (Style)FindResource("IconButton"), Content = "", ToolTip = Loc.Get("PermsApply"), IsDefault = true };
        ok.Click += (_, _) =>
        {
            ChangeMode = Mode is not null && Mode != originalMode;
            ChangeOwner = (OwnerName.Length > 0 && OwnerName != originalOwner) || (GroupName.Length > 0 && GroupName != originalGroup);
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(card);
        root.Children.Add(buttons);
        Content = root;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
    }

    private void SetBits(int mode)
    {
        for (var i = 0; i < 9; i++)
            _bits[i].IsChecked = (mode & (1 << (8 - i))) != 0;
        Mode = mode;
    }

    private void FromBits()
    {
        if (_syncing)
            return;
        var mode = 0;
        for (var i = 0; i < 9; i++)
            if (_bits[i].IsChecked == true)
                mode |= 1 << (8 - i);
        Mode = mode;
        _syncing = true;
        _octal.Text = Convert.ToString(mode, 8).PadLeft(3, '0');
        _syncing = false;
    }

    private void FromOctal()
    {
        if (_syncing)
            return;
        var text = _octal.Text.Trim();
        if (text.Length is >= 3 and <= 4 && text.All(ch => ch is >= '0' and <= '7'))
        {
            _syncing = true;
            SetBits(Convert.ToInt32(text[^3..], 8));
            _syncing = false;
        }
    }
}
