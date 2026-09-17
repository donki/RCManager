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
    /// <summary>Hasta aqui se edita dentro; mas grande, se abre fuera.</summary>
    public const long MaxBytes = 4 * 1024 * 1024;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".conf", ".cfg", ".ini", ".env", ".json", ".yaml", ".yml", ".xml", ".toml", ".csv",
        ".sh", ".bash", ".zsh", ".ps1", ".bat", ".cmd", ".py", ".js", ".ts", ".css", ".html", ".htm", ".php", ".sql",
        ".c", ".h", ".cpp", ".hpp", ".cs", ".java", ".go", ".rs", ".rb", ".pl", ".lua", ".properties", ".service",
        ".gitignore", ".htaccess", ".crontab", ".dockerfile",
    };

    /// <summary>Se decide por la extension y, si no dice nada, por el contenido (sin bytes nulos).</summary>
    public static bool LooksLikeText(FileEntry entry)
    {
        if (entry.IsDirectory || entry.Size > MaxBytes)
            return false;
        var ext = Path.GetExtension(entry.Name);
        if (ext.Length == 0)
            return !entry.Name.StartsWith('.') || entry.Name.Length > 1;   // Makefile, README, .bashrc…
        return TextExtensions.Contains(ext);
    }

    private readonly IRemoteFileSystem _fs;
    private readonly FileEntry _entry;
    private readonly Encoding _encoding;
    private readonly bool _crlf;
    private readonly TextBox _text;
    private readonly TextBox _find;
    private readonly Border _findBar;
    private readonly TextBlock _status;
    private bool _dirty;

    public event Action? Saved;

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
        save.Click += async (_, _) => await SaveAsync();
        var findButton = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("EditorFind") };
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
        _find.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { FindNext((Keyboard.Modifiers & ModifierKeys.Shift) != 0); e.Handled = true; }
            else if (e.Key == Key.Escape) { _findBar.Visibility = Visibility.Collapsed; _text.Focus(); e.Handled = true; }
        };
        var prev = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("EditorFindPrev") };
        prev.Click += (_, _) => FindNext(backwards: true);
        var next = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("EditorFindNext") };
        next.Click += (_, _) => FindNext(backwards: false);
        var close = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("Close") };
        close.Click += (_, _) => { _findBar.Visibility = Visibility.Collapsed; _text.Focus(); };
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
            FontSize = 13,
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

        PreviewKeyDown += async (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
                return;
            if (e.Key == Key.S) { e.Handled = true; await SaveAsync(); }
            else if (e.Key == Key.F) { e.Handled = true; ShowFind(); }
            else if (e.Key == Key.OemPlus || e.Key == Key.Add) { e.Handled = true; _text.FontSize = Math.Min(28, _text.FontSize + 1); }
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract) { e.Handled = true; _text.FontSize = Math.Max(9, _text.FontSize - 1); }
        };
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
            if (bytes.Take(8192).Contains((byte)0))
                throw new InvalidOperationException(Loc.Get("EditorNotText"));

            // UTF-8 (con o sin BOM) si el contenido es UTF-8 valido; si no, Latin-1, que no rompe nada.
            Encoding encoding;
            string content;
            try
            {
                encoding = new UTF8Encoding(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, throwOnInvalidBytes: true);
                content = encoding.GetString(bytes.AsSpan(encoding.GetPreamble().Length == 3 && bytes.Length >= 3 && bytes[0] == 0xEF ? 3 : 0));
            }
            catch (DecoderFallbackException)
            {
                encoding = Encoding.Latin1;
                content = encoding.GetString(bytes);
            }
            var crlf = content.Contains("\r\n");
            // El TextBox trabaja con \r\n; se normaliza y al guardar se devuelve el final original.
            content = content.Replace("\r\n", "\n").Replace("\n", "\r\n");
            return new TextEditorWindow(owner, fs, entry, content, encoding, crlf);
        }
        finally
        {
            try { File.Delete(temp); } catch (Exception) { }
        }
    }

    private async Task SaveAsync(bool thenClose = false)
    {
        var temp = Path.Combine(Path.GetTempPath(), "sOCRCManager", "editor", Guid.NewGuid().ToString("N"));
        try
        {
            var content = _text.Text;
            if (!_crlf)
                content = content.Replace("\r\n", "\n");
            await File.WriteAllBytesAsync(temp, _encoding.GetPreamble().Concat(_encoding.GetBytes(content)).ToArray());
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
        _status.Text = $"{(_dirty ? "● " : string.Empty)}{Loc.Format("EditorPosition", line + 1, col + 1)}  ·  {(_encoding is UTF8Encoding ? "UTF-8" : "Latin-1")}  ·  {(_crlf ? "CRLF" : "LF")}{(note is null ? string.Empty : "  ·  " + note)}";
        Title = (_dirty ? "● " : string.Empty) + _entry.Name;
    }

    private void ShowFind()
    {
        _findBar.Visibility = Visibility.Visible;
        if (_text.SelectionLength > 0 && !_text.SelectedText.Contains('\n'))
            _find.Text = _text.SelectedText;
        _find.Focus();
        _find.SelectAll();
    }

    private void FindNext(bool backwards)
    {
        var needle = _find.Text;
        if (needle.Length == 0)
            return;
        var text = _text.Text;
        int index;
        if (backwards)
        {
            var from = Math.Max(0, _text.SelectionStart - 1);
            index = from > 0 ? text.LastIndexOf(needle, from, StringComparison.CurrentCultureIgnoreCase) : -1;
            if (index < 0) index = text.LastIndexOf(needle, StringComparison.CurrentCultureIgnoreCase);
        }
        else
        {
            var from = _text.SelectionStart + _text.SelectionLength;
            index = text.IndexOf(needle, Math.Min(from, text.Length), StringComparison.CurrentCultureIgnoreCase);
            if (index < 0) index = text.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase);
        }
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
        discard.Click += (_, _) => { _answer = false; DialogResult = true; };
        var cancel = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Style = (Style)FindResource("IconButton"), Content = "", ToolTip = Loc.Get("EditorSave"), IsDefault = true };
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
        return w.ShowDialog() == true ? w._answer : null;
    }
}
