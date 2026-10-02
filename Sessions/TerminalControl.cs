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

    private readonly TerminalSelection _selection = new();
    private bool _selecting;   // arrastrando con el boton izquierdo para seleccionar

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

    /// <summary>Filas del historial que se ven por encima de la pantalla (0 = la pantalla en vivo).</summary>
    internal int ViewOffset => _viewOffset;

    /// <summary>Medidas de una celda con la letra actual.</summary>
    internal (double Width, double Height) CellSize => (_cellW, _cellH);

    /// <summary>La seleccion con el raton.</summary>
    internal TerminalSelection Selection => _selection;

    /// <summary>El texto de una fila tal como se ve (con el desplazamiento del historial).</summary>
    internal string RowText(int row)
    {
        var sb = new StringBuilder();
        for (var c = 0; c < _buffer.Cols; c++)
            sb.Append(CellAt(row, c).Ch);
        return sb.ToString().TrimEnd();
    }

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
                // El tramo se corta tambien donde empieza o acaba la seleccion: si no, un trozo
                // seleccionado dentro de un texto del mismo color no se resaltaba.
                var selected = _selection.Covers(r, c, c, _buffer.Cols);
                sb.Clear();
                while (c < _buffer.Cols && TerminalCell.SameStyle(CellAt(r, c), cell) && _selection.Covers(r, c, c, _buffer.Cols) == selected)
                {
                    sb.Append(CellAt(r, c).Ch);
                    c++;
                }

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
        if (HandleKey(e.Key, Keyboard.Modifiers))
            e.Handled = true;
    }

    /// <summary>Una tecla pulsada con esos modificadores. Devuelve si el terminal la ha usado.</summary>
    internal bool HandleKey(Key key, ModifierKeys modifiers)
    {
        var ctrl = (modifiers & ModifierKeys.Control) != 0;
        var shift = (modifiers & ModifierKeys.Shift) != 0;
        var alt = (modifiers & ModifierKeys.Alt) != 0;

        if (ctrl && shift && key == Key.V) { Paste(); return true; }
        if (ctrl && shift && key == Key.C) { CopySelection(); return true; }
        // Mayus+RePag / Mayus+AvPag: media pantalla del historial arriba o abajo.
        if (shift && key == Key.PageUp) { ScrollView(_buffer.Rows / 2); return true; }
        if (shift && key == Key.PageDown) { ScrollView(-_buffer.Rows / 2); return true; }

        if (TerminalKeys.Sequence(key, ctrl, shift, alt, _buffer.AppCursorKeys) is not { } seq)
            return false;
        Send(seq);
        return true;
    }

    private void ScrollView(int rows)
    {
        _viewOffset = Math.Clamp(_viewOffset + rows, 0, _buffer.ScrollbackCount);
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Wheel(e.Delta);
        e.Handled = true;
    }

    /// <summary>La rueda: tres filas del historial por paso.</summary>
    internal void Wheel(int delta) => ScrollView(delta > 0 ? 3 : -3);

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (PointerDown(e.ChangedButton, e.GetPosition(this)))
            e.Handled = true;
    }

    /// <summary>
    /// Pulsar en el terminal: el derecho copia la seleccion o, si no la hay, pega; el izquierdo
    /// empieza a seleccionar. Devuelve si el clic queda usado.
    /// </summary>
    internal bool PointerDown(MouseButton button, Point position)
    {
        Focus();
        if (button == MouseButton.Right)
        {
            if (_selection.IsActive) CopySelection();
            else Paste();
            return true;
        }

        if (button == MouseButton.Left)
        {
            _selection.Begin(CellFromPoint(position));
            _selecting = true;
            CaptureMouse();
            InvalidateVisual();
        }
        return false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.LeftButton == MouseButtonState.Pressed)
            PointerMove(e.GetPosition(this));
    }

    /// <summary>Arrastrar con el izquierdo pulsado: la seleccion llega hasta aqui.</summary>
    internal void PointerMove(Point position)
    {
        if (!_selecting)
            return;
        _selection.Extend(CellFromPoint(position));
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        PointerUp();
    }

    /// <summary>Soltar: si no se ha movido de celda, no queda nada seleccionado.</summary>
    internal void PointerUp()
    {
        if (!_selecting)
            return;
        _selecting = false;
        ReleaseMouseCapture();
        if (!_selection.Finish())
            InvalidateVisual();
    }

    // Si otro se queda el raton a medio arrastre, la seleccion se queda donde estaba.
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _selecting = false;
    }

    private (int Row, int Col) CellFromPoint(Point p) =>
        (Math.Clamp((int)(p.Y / _cellH), 0, _buffer.Rows - 1), Math.Clamp((int)(p.X / _cellW), 0, _buffer.Cols - 1));

    /// <summary>Portapapeles: leer texto (null si no hay) y escribirlo. Las pruebas no tocan el de Windows.</summary>
    internal static Func<string?> ReadClipboard { get; set; } = () => Clipboard.ContainsText() ? Clipboard.GetText() : null;

    internal static Action<string> WriteClipboard { get; set; } = Clipboard.SetText;

    private void CopySelection()
    {
        if (!_selection.IsActive) return;
        var (a, b) = _selection.Ordered();
        var text = _buffer.TextBetween(a, b, _viewOffset);
        try { WriteClipboard(text); } catch (Exception) { }
        _selection.Clear();
        InvalidateVisual();
    }

    private void Paste()
    {
        try
        {
            if (ReadClipboard() is { } text)
                Send(text.Replace("\r\n", "\r").Replace('\n', '\r'));
        }
        catch (Exception) { }
    }

    // El texto seleccionado deja de estarlo con la siguiente salida: la seleccion se quita
    // al escribir para no dejar un resalte viejo sobre texto nuevo.
    public void ClearSelection() => _selection.Clear();
}
