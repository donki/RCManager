using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SocRcManager.Sessions;

/// <summary>
/// Un terminal de texto: emula lo justo de VT100/xterm para que un shell de Linux se vea bien
/// (colores, cursor, borrado, region de scroll, pantalla alternativa) y manda las teclas al SSH.
/// </summary>
/// <remarks>
/// <para><b>Propio, no de terceros.</b> No hay un emulador de terminal para WPF con licencia
/// permisiva que merezca la dependencia; lo que hace falta aqui —una rejilla de celdas con
/// colores y las secuencias CSI/OSC habituales— cabe en <see cref="TerminalBuffer"/>, que se prueba sin interfaz. Lo que no entiende, lo
/// ignora sin romperse.</para>
///
/// <para><b>Pintado.</b> Cada fila se dibuja con <see cref="FormattedText"/> por tramos del mismo
/// color, con fuente monoespaciada. Es suficiente para 200x60 celdas a la velocidad de un shell.</para>
///
/// <para><b>Teclado.</b> El texto va tal cual; las teclas especiales se traducen a sus secuencias
/// xterm; Ctrl+letra manda el codigo de control. Ctrl+Shift+V y el boton derecho pegan;
/// Ctrl+Shift+C copia la seleccion (arrastrar con el raton selecciona).</para>
/// </remarks>
public sealed class TerminalControl : FrameworkElement
{
    // Paleta xterm de 16 colores; los 256 se derivan.
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0, 0, 0), Color.FromRgb(205, 49, 49), Color.FromRgb(13, 188, 121), Color.FromRgb(229, 229, 16),
        Color.FromRgb(36, 114, 200), Color.FromRgb(188, 63, 188), Color.FromRgb(17, 168, 205), Color.FromRgb(229, 229, 229),
        Color.FromRgb(102, 102, 102), Color.FromRgb(241, 76, 76), Color.FromRgb(35, 209, 139), Color.FromRgb(245, 245, 67),
        Color.FromRgb(59, 142, 234), Color.FromRgb(214, 112, 214), Color.FromRgb(41, 184, 219), Color.FromRgb(255, 255, 255),
    ];

    private readonly TerminalBuffer _buffer = new();
    private int _viewOffset;   // filas de scrollback que se ven (0 = pantalla en vivo)

    private Typeface _typeface = new(new FontFamily("Cascadia Mono, Consolas, Courier New"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private double _fontSize = 14;
    private double _cellW = 8, _cellH = 17;

    private Brush _defaultFg = Brushes.Gainsboro;
    private Brush _defaultBg = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x22));

    private (int Row, int Col)? _selStart, _selEnd;

    public TerminalControl()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.IBeam;
        MeasureCell();
        _buffer.Reply += Send;
        _buffer.TitleChanged += title => TitleChanged?.Invoke(title);
    }

    /// <summary>Bytes que hay que mandar al otro lado (teclas y pegado).</summary>
    public event Action<byte[]>? Input;

    /// <summary>La rejilla ha cambiado de tamaño (columnas, filas).</summary>
    public event Action<int, int>? Resized;

    public int Rows => _buffer.Rows;
    public int Cols => _buffer.Cols;

    /// <summary>Tamaño de letra; al cambiar se recalcula la rejilla y se avisa al shell del tamaño nuevo.</summary>
    public double FontSize
    {
        get => _fontSize;
        set
        {
            if (Math.Abs(_fontSize - value) < 0.1)
                return;
            _fontSize = value;
            MeasureCell();
            if (ActualWidth > 0 && ActualHeight > 0)
                Resize(Math.Max(5, (int)(ActualHeight / _cellH)), Math.Max(20, (int)(ActualWidth / _cellW)));
            InvalidateVisual();
        }
    }

    public string Title => _buffer.Title;
    public event Action<string>? TitleChanged;

    public void SetColors(Brush foreground, Brush background)
    {
        _defaultFg = foreground;
        _defaultBg = background;
        InvalidateVisual();
    }

    // -----------------------------------------------------------------------
    //  Entrada de datos (lo que manda el servidor): la interpreta TerminalBuffer
    // -----------------------------------------------------------------------

    public void Feed(byte[] data, int count)
    {
        _buffer.Feed(data, count);
        _viewOffset = 0;
        InvalidateVisual();
    }

    // -----------------------------------------------------------------------
    //  Tamaño
    // -----------------------------------------------------------------------

    private void MeasureCell()
    {
        var ft = new FormattedText("W", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, _fontSize, Brushes.White, 1.0);
        _cellW = Math.Ceiling(ft.WidthIncludingTrailingWhitespace);
        _cellH = Math.Ceiling(ft.Height);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        var cols = Math.Max(20, (int)(sizeInfo.NewSize.Width / _cellW));
        var rows = Math.Max(5, (int)(sizeInfo.NewSize.Height / _cellH));
        if (cols != _buffer.Cols || rows != _buffer.Rows)
            Resize(rows, cols);
    }

    private void Resize(int rows, int cols)
    {
        _buffer.Resize(rows, cols);
        Resized?.Invoke(cols, rows);
        InvalidateVisual();
    }

    // -----------------------------------------------------------------------
    //  Pintado
    // -----------------------------------------------------------------------

    private static readonly (byte R, byte G, byte B)[] PaletteRgb = Palette.Select(c => (c.R, c.G, c.B)).ToArray();

    private Brush BrushFor(int index, bool foreground, bool bold)
    {
        if (index < 0) return foreground ? _defaultFg : _defaultBg;
        var (r, g, b) = TerminalBuffer.Rgb(index, foreground, bold, PaletteRgb);
        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    private TerminalCell CellAt(int r, int c) => _buffer.CellAt(r, c, _viewOffset);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(_defaultBg, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var sb = new StringBuilder();
        for (var r = 0; r < _buffer.Rows; r++)
        {
            var y = r * _cellH;
            var c = 0;
            while (c < _buffer.Cols)
            {
                var cell = CellAt(r, c);
                var start = c;
                sb.Clear();
                while (c < _buffer.Cols && TerminalCell.SameStyle(CellAt(r, c), cell))
                {
                    sb.Append(CellAt(r, c).Ch);
                    c++;
                }

                var selected = IsSelected(r, start, c - 1);
                var fg = BrushFor(cell.Inverse ? cell.Bg : cell.Fg, !cell.Inverse, cell.Bold);
                var bg = BrushFor(cell.Inverse ? cell.Fg : cell.Bg, cell.Inverse, false);
                if (cell.Inverse && cell.Bg < 0) fg = _defaultBg;
                if (cell.Inverse && cell.Fg < 0) bg = _defaultFg;
                if (selected) { bg = Brushes.SteelBlue; fg = Brushes.White; }

                var x = start * _cellW;
                if (!ReferenceEquals(bg, _defaultBg) || selected)
                    dc.DrawRectangle(bg, null, new Rect(x, y, (c - start) * _cellW, _cellH));

                var text = sb.ToString();
                if (text.Trim().Length > 0)
                {
                    var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(_typeface.FontFamily, FontStyles.Normal, cell.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                        _fontSize, fg, 1.0);
                    if (cell.Underline) ft.SetTextDecorations(TextDecorations.Underline);
                    dc.DrawText(ft, new Point(x, y));
                }
            }
        }

        if (_buffer.CursorVisible && _viewOffset == 0 && IsFocused)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(160, 0xE6, 0xE1, 0xE9)), null,
                new Rect(_buffer.CursorCol * _cellW, _buffer.CursorRow * _cellH, _cellW, _cellH));
    }

    // -----------------------------------------------------------------------
    //  Teclado, raton, portapapeles
    // -----------------------------------------------------------------------

    private void Send(string s) => Input?.Invoke(Encoding.UTF8.GetBytes(s));

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (string.IsNullOrEmpty(e.Text)) return;
        if (e.Text == "\r") return;   // Intro va por OnKeyDown
        Send(e.Text);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

        if (ctrl && shift && e.Key == Key.V) { Paste(); e.Handled = true; return; }
        if (ctrl && shift && e.Key == Key.C) { CopySelection(); e.Handled = true; return; }

        var seq = e.Key switch
        {
            Key.Enter => "\r",
            Key.Back => "\x7f",
            Key.Tab => "\t",
            Key.Escape => "\x1b",
            Key.Up => _buffer.AppCursorKeys ? "\x1bOA" : "\x1b[A",
            Key.Down => _buffer.AppCursorKeys ? "\x1bOB" : "\x1b[B",
            Key.Right => _buffer.AppCursorKeys ? "\x1bOC" : "\x1b[C",
            Key.Left => _buffer.AppCursorKeys ? "\x1bOD" : "\x1b[D",
            Key.Home => "\x1b[H",
            Key.End => "\x1b[F",
            Key.Insert => "\x1b[2~",
            Key.Delete => "\x1b[3~",
            Key.PageUp => shift ? null : "\x1b[5~",
            Key.PageDown => shift ? null : "\x1b[6~",
            Key.F1 => "\x1bOP", Key.F2 => "\x1bOQ", Key.F3 => "\x1bOR", Key.F4 => "\x1bOS",
            Key.F5 => "\x1b[15~", Key.F6 => "\x1b[17~", Key.F7 => "\x1b[18~", Key.F8 => "\x1b[19~",
            Key.F9 => "\x1b[20~", Key.F10 => "\x1b[21~", Key.F11 => "\x1b[23~", Key.F12 => "\x1b[24~",
            _ => null,
        };

        if (shift && e.Key == Key.PageUp) { _viewOffset = Math.Min(_buffer.ScrollbackCount, _viewOffset + _buffer.Rows / 2); InvalidateVisual(); e.Handled = true; return; }
        if (shift && e.Key == Key.PageDown) { _viewOffset = Math.Max(0, _viewOffset - _buffer.Rows / 2); InvalidateVisual(); e.Handled = true; return; }

        if (seq is not null)
        {
            Send(seq);
            e.Handled = true;
            return;
        }

        // Ctrl+letra: codigo de control (Ctrl+C = 3, Ctrl+D = 4, Ctrl+Z = 26, Ctrl+L = 12...).
        if (ctrl && !alt && e.Key >= Key.A && e.Key <= Key.Z)
        {
            Send(((char)(e.Key - Key.A + 1)).ToString());
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.Space) { Send("\0"); e.Handled = true; }
        if (ctrl && e.Key == Key.OemOpenBrackets) { Send("\x1b"); e.Handled = true; }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        _viewOffset = Math.Clamp(_viewOffset + (e.Delta > 0 ? 3 : -3), 0, _buffer.ScrollbackCount);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.ChangedButton == MouseButton.Right)
        {
            if (_selStart is not null && _selEnd is not null) CopySelection();
            else Paste();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            _selStart = _selEnd = CellFromPoint(e.GetPosition(this));
            CaptureMouse();
            InvalidateVisual();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            _selEnd = CellFromPoint(e.GetPosition(this));
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
            if (_selStart == _selEnd) { _selStart = _selEnd = null; InvalidateVisual(); }
        }
    }

    private (int Row, int Col) CellFromPoint(Point p) =>
        (Math.Clamp((int)(p.Y / _cellH), 0, _buffer.Rows - 1), Math.Clamp((int)(p.X / _cellW), 0, _buffer.Cols - 1));

    private bool IsSelected(int row, int fromCol, int toCol)
    {
        if (_selStart is null || _selEnd is null) return false;
        var (a, b) = Ordered();
        if (row < a.Row || row > b.Row) return false;
        var lo = row == a.Row ? a.Col : 0;
        var hi = row == b.Row ? b.Col : _buffer.Cols - 1;
        return fromCol >= lo && toCol <= hi;
    }

    private ((int Row, int Col) a, (int Row, int Col) b) Ordered()
    {
        var s = _selStart!.Value; var e = _selEnd!.Value;
        return s.Row < e.Row || (s.Row == e.Row && s.Col <= e.Col) ? (s, e) : (e, s);
    }

    private void CopySelection()
    {
        if (_selStart is null || _selEnd is null) return;
        var (a, b) = Ordered();
        var text = _buffer.TextBetween(a, b, _viewOffset);
        try { Clipboard.SetText(text); } catch (Exception) { }
        _selStart = _selEnd = null;
        InvalidateVisual();
    }

    private void Paste()
    {
        try
        {
            if (Clipboard.ContainsText())
                Send(Clipboard.GetText().Replace("\r\n", "\r").Replace('\n', '\r'));
        }
        catch (Exception) { }
    }

    // El texto seleccionado deja de estarlo con la siguiente salida: la seleccion se quita
    // al escribir para no dejar un resalte viejo sobre texto nuevo.
    public void ClearSelection() { _selStart = _selEnd = null; }
}
