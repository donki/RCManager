using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SocRcManager.Localization;
using SocRcManager.Services;

namespace SocRcManager.Files;

/// <summary>
/// Editor de texto para ficheros del servidor: se baja a memoria, se edita y Ctrl+S lo sube tal
/// cual (misma codificacion y mismos finales de linea con los que llego). Ctrl+F busca. Al cerrar
/// con cambios sin guardar, pregunta.
/// </summary>
public sealed class TextEditorWindow : Window
{
    private readonly IRemoteFileSystem _fs;
    private readonly FileEntry _entry;
    private readonly Encoding _encoding;
    private readonly bool _crlf;
    internal readonly TextBox _text;
    internal readonly TextBox _find;
    internal readonly Border _findBar;
    internal readonly TextBlock _status;
    private bool _dirty;

    /// <summary>Hay cambios sin guardar.</summary>
    internal bool IsDirty => _dirty;

    public event Action? Saved;

    /// <summary>Letra del editor; se guarda para la proxima vez (es un ajuste de la aplicacion, no de la conexion).</summary>
    internal void SetFontSize(double size)
    {
        _text.FontSize = Math.Clamp(size, 9, 28);
        if (AppSettings.Current is { } settings)
        {
            settings.EditorFontSize = _text.FontSize;
            settings.Save();
        }
    }

    private TextEditorWindow(Window owner, IRemoteFileSystem fs, FileEntry entry, string content, Encoding encoding, bool crlf)
    {
        Owner = owner;
        _fs = fs;
        _entry = entry;
        _encoding = encoding;
        _crlf = crlf;
        Title = entry.Name;
        Width = 900;
        Height = 640;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("PageBackground");
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        var root = new DockPanel();

        // Barra de arriba: ruta, guardar, buscar.
        var top = new DockPanel { Margin = new Thickness(10, 8, 10, 4) };
        var save = new Button { Style = (Style)FindResource("IconButton"), Content = "", ToolTip = Loc.Get("EditorSave"), Margin = new Thickness(0, 0, 6, 0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(save, "EditorSaveButton");
        save.Click += async (_, _) => await SaveAsync();
        var findButton = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("EditorFind") };
        System.Windows.Automation.AutomationProperties.SetAutomationId(findButton, "EditorFindButton");
        findButton.Click += (_, _) => ShowFind();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(save);
        buttons.Children.Add(findButton);
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(buttons);
        top.Children.Add(new TextBlock { Text = entry.FullPath, Style = (Style)FindResource("HintText"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        // Buscador (Ctrl+F): escondido hasta que hace falta.
        _find = new TextBox { Style = (Style)FindResource("Field"), Width = 260, Height = 30, Margin = new Thickness(0, 0, 6, 0) };
        _find.KeyDown += (_, e) => { if (OnFindKey(e.Key, Keyboard.Modifiers)) e.Handled = true; };
        var prev = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("EditorFindPrev") };
        System.Windows.Automation.AutomationProperties.SetAutomationId(prev, "EditorFindPrevButton");
        prev.Click += (_, _) => FindNext(backwards: true);
        var next = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("EditorFindNext") };
        System.Windows.Automation.AutomationProperties.SetAutomationId(next, "EditorFindNextButton");
        next.Click += (_, _) => FindNext(backwards: false);
        var close = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("Close") };
        System.Windows.Automation.AutomationProperties.SetAutomationId(close, "EditorFindCloseButton");
        close.Click += (_, _) => HideFind();
        var findPanel = new StackPanel { Orientation = Orientation.Horizontal };
        findPanel.Children.Add(new TextBlock { Text = Loc.Get("EditorFind"), Style = (Style)FindResource("HintText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        findPanel.Children.Add(_find);
        findPanel.Children.Add(prev);
        findPanel.Children.Add(next);
        findPanel.Children.Add(close);
        _findBar = new Border { Child = findPanel, Padding = new Thickness(10, 4, 10, 6), Visibility = Visibility.Collapsed };
        DockPanel.SetDock(_findBar, Dock.Top);
        root.Children.Add(_findBar);

        // Estado abajo: linea/columna, codificacion, guardado.
        _status = new TextBlock { Style = (Style)FindResource("HintText"), Margin = new Thickness(10, 4, 10, 6) };
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);

        _text = new TextBox
        {
            Text = content,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = Math.Clamp(AppSettings.Current?.EditorFontSize ?? 13, 9, 28),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Foreground = (Brush)FindResource("TextPrimary"),
            Background = (Brush)FindResource("CardBackground"),
            CaretBrush = (Brush)FindResource("TextPrimary"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(10, 0, 10, 0),
            IsUndoEnabled = true,
        };
        _text.TextChanged += (_, _) => { _dirty = true; UpdateStatus(); };
        _text.SelectionChanged += (_, _) => UpdateStatus();
        root.Children.Add(_text);

        Content = root;
        UpdateStatus();

        PreviewKeyDown += (_, e) => { if (OnShortcut(e.Key, Keyboard.Modifiers)) e.Handled = true; };
        Closing += (_, e) =>
        {
            if (!_dirty)
                return;
            var answer = SaveQuestionWindow.Ask(this, entry.Name);
            if (answer is null) { e.Cancel = true; return; }
            if (answer == true)
            {
                e.Cancel = true;
                _ = SaveAsync(thenClose: true);
            }
        };
        Loaded += (_, _) => _text.Focus();
    }

    /// <summary>Baja el fichero y abre el editor. Falla con un mensaje si no parece texto.</summary>
    public static async Task<TextEditorWindow> OpenRemoteAsync(Window owner, IRemoteFileSystem fs, FileEntry entry)
    {
        var temp = Path.Combine(Path.GetTempPath(), "sOCRCManager", "editor", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        try
        {
            await fs.DownloadAsync(entry.FullPath, temp, new Progress<long>(), CancellationToken.None);
            var bytes = await File.ReadAllBytesAsync(temp);
            var text = FileRules.DecodeText(bytes) ?? throw new InvalidOperationException(Loc.Get("EditorNotText"));
            return new TextEditorWindow(owner, fs, entry, text.Content, text.Encoding, text.Crlf);
        }
        finally
        {
            try { File.Delete(temp); } catch (Exception) { }
        }
    }

    /// <summary>Atajos con Ctrl: S guarda, F busca, + y - cambian la letra. true si era uno de ellos.</summary>
    internal bool OnShortcut(Key key, ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Control) == 0)
            return false;
        switch (key)
        {
            case Key.S: _ = SaveAsync(); return true;
            case Key.F: ShowFind(); return true;
            case Key.OemPlus or Key.Add: SetFontSize(_text.FontSize + 1); return true;
            case Key.OemMinus or Key.Subtract: SetFontSize(_text.FontSize - 1); return true;
            default: return false;
        }
    }

    /// <summary>Teclas en la casilla de buscar: Intro busca (con Mayus, hacia atras), Esc cierra el buscador.</summary>
    internal bool OnFindKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.Enter) { FindNext((modifiers & ModifierKeys.Shift) != 0); return true; }
        if (key == Key.Escape) { HideFind(); return true; }
        return false;
    }

    internal async Task SaveAsync(bool thenClose = false)
    {
        var temp = Path.Combine(Path.GetTempPath(), "sOCRCManager", "editor", Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllBytesAsync(temp, FileRules.EncodeText(_text.Text, _encoding, _crlf));
            _status.Text = Loc.Get("EditorSaving");
            await _fs.UploadAsync(temp, _entry.FullPath, new Progress<long>(), CancellationToken.None);
            _dirty = false;
            UpdateStatus(Loc.Format("EditorSaved", DateTime.Now.ToString("T")));
            Saved?.Invoke();
            if (thenClose)
                Close();
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message.ReplaceLineEndings(" ");
        }
        finally
        {
            try { File.Delete(temp); } catch (Exception) { }
        }
    }

    private void UpdateStatus(string? note = null)
    {
        var line = Math.Max(0, _text.GetLineIndexFromCharacterIndex(_text.CaretIndex));
        var col = Math.Max(0, _text.CaretIndex - Math.Max(0, _text.GetCharacterIndexFromLineIndex(line)));
        _status.Text = StatusText(_dirty, line, col, _encoding, _crlf, note);
        Title = (_dirty ? "● " : string.Empty) + _entry.Name;
    }

    /// <summary>La linea de estado: «● » si hay cambios, linea y columna (desde 0), codificacion, finales de linea y una nota.</summary>
    internal static string StatusText(bool dirty, int line, int column, Encoding encoding, bool crlf, string? note) =>
        $"{(dirty ? "● " : string.Empty)}{Loc.Format("EditorPosition", line + 1, column + 1)}  ·  {(encoding is UTF8Encoding ? "UTF-8" : "Latin-1")}  ·  {(crlf ? "CRLF" : "LF")}{(note is null ? string.Empty : "  ·  " + note)}";

    private void HideFind()
    {
        _findBar.Visibility = Visibility.Collapsed;
        _text.Focus();
    }

    internal void ShowFind()
    {
        _findBar.Visibility = Visibility.Visible;
        if (_text.SelectionLength > 0 && !_text.SelectedText.Contains('\n'))
            _find.Text = _text.SelectedText;
        _find.Focus();
        _find.SelectAll();
    }

    internal void FindNext(bool backwards)
    {
        var needle = _find.Text;
        if (needle.Length == 0)
            return;
        var index = TextSearch.Find(_text.Text, needle, _text.SelectionStart, _text.SelectionLength, backwards);
        if (index < 0)
        {
            _status.Text = Loc.Format("EditorNotFound", needle);
            return;
        }
        _text.Select(index, needle.Length);
        _text.ScrollToLine(_text.GetLineIndexFromCharacterIndex(index));
        UpdateStatus();
    }
}

/// <summary>«¿Guardar los cambios de X?»: si / no / cancelar (null).</summary>
public sealed class SaveQuestionWindow : Window
{
    private bool? _answer;

    private SaveQuestionWindow(Window owner, string name)
    {
        Owner = owner;
        Title = Loc.Get("EditorSave");
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("PageBackground");
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(18, 14, 18, 16) };
        card.Child = new TextBlock { Text = Loc.Format("EditorUnsaved", name), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextPrimary") };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var discard = new Button { Style = (Style)FindResource("OutlineButton"), Content = Loc.Get("EditorDiscard"), Margin = new Thickness(0, 0, 8, 0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(discard, "DiscardButton");
        discard.Click += (_, _) => { _answer = false; DialogResult = true; };
        var cancel = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(cancel, "CancelButton");
        var save = new Button { Style = (Style)FindResource("IconButton"), Content = "", ToolTip = Loc.Get("EditorSave"), IsDefault = true };
        System.Windows.Automation.AutomationProperties.SetAutomationId(save, "SaveButton");
        save.Click += (_, _) => { _answer = true; DialogResult = true; };
        buttons.Children.Add(discard);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(card);
        root.Children.Add(buttons);
        Content = root;
    }

    /// <summary>true = guardar, false = descartar, null = cancelar.</summary>
    public static bool? Ask(Window owner, string name)
    {
        var w = new SaveQuestionWindow(owner, name);
        return Dialogs.ShowModal(w) == true ? w._answer : null;
    }
}
