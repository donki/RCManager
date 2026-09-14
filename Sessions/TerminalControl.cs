using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Connections.Sessions;

/// <summary>
/// Un terminal de texto: emula lo justo de VT100/xterm para que un shell de Linux se vea bien
/// (colores, cursor, borrado, region de scroll, pantalla alternativa) y manda las teclas al SSH.
/// </summary>
/// <remarks>
/// <para><b>Propio, no de terceros.</b> No hay un emulador de terminal para WPF con licencia
/// permisiva que merezca la dependencia; lo que hace falta aqui —una rejilla de celdas con
/// colores y las secuencias CSI/OSC habituales— cabe en este fichero. Lo que no entiende, lo
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
    private const int DefaultCols = 80;
    private const int DefaultRows = 24;

    // Paleta xterm de 16 colores; los 256 se derivan.
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0, 0, 0), Color.FromRgb(205, 49, 49), Color.FromRgb(13, 188, 121), Color.FromRgb(229, 229, 16),
        Color.FromRgb(36, 114, 200), Color.FromRgb(188, 63, 188), Color.FromRgb(17, 168, 205), Color.FromRgb(229, 229, 229),
        Color.FromRgb(102, 102, 102), Color.FromRgb(241, 76, 76), Color.FromRgb(35, 209, 139), Color.FromRgb(245, 245, 67),
        Color.FromRgb(59, 142, 234), Color.FromRgb(214, 112, 214), Color.FromRgb(41, 184, 219), Color.FromRgb(255, 255, 255),
    ];

    private struct Cell
    {
        public char Ch;
        public int Fg;   // -1 = por defecto
        public int Bg;   // -1 = por defecto
        public bool Bold;
        public bool Underline;
        public bool Inverse;
    }

    private Cell[,] _screen = new Cell[DefaultRows, DefaultCols];
    private Cell[,]? _savedScreen;   // pantalla principal mientras esta la alternativa
    private readonly List<Cell[]> _scrollback = [];
    private const int ScrollbackMax = 3000;

    private int _rows = DefaultRows;
    private int _cols = DefaultCols;
    private int _curRow, _curCol;
    private int _savedRow, _savedCol;
    private int _scrollTop, _scrollBottom = DefaultRows - 1;
    private bool _cursorVisible = true;
    private bool _autoWrap = true;
    private bool _pendingWrap;
    private bool _appCursorKeys;
    private int _viewOffset;   // filas de scrollback que se ven (0 = pantalla en vivo)

    private Cell _attr = Default();

    private readonly StringBuilder _escape = new();
    private enum State { Ground, Escape, Csi, Osc, Charset }
    private State _state = State.Ground;

    private Typeface _typeface = new(new FontFamily("Cascadia Mono, Consolas, Courier New"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private double _fontSize = 14;
    private double _cellW = 8, _cellH = 17;

    private Brush _defaultFg = Brushes.Gainsboro;
    private Brush _defaultBg = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x22));

    private (int Row, int Col)? _selStart, _selEnd;
    private readonly Decoder _utf8 = Encoding.UTF8.GetDecoder();

    public TerminalControl()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.IBeam;
        MeasureCell();
        Clear();
    }

    /// <summary>Bytes que hay que mandar al otro lado (teclas y pegado).</summary>
    public event Action<byte[]>? Input;

    /// <summary>La rejilla ha cambiado de tamaño (columnas, filas).</summary>
    public event Action<int, int>? Resized;

    public int Rows => _rows;
    public int Cols => _cols;

    public string Title { get; private set; } = string.Empty;
    public event Action<string>? TitleChanged;

    public void SetColors(Brush foreground, Brush background)
    {
        _defaultFg = foreground;
        _defaultBg = background;
        InvalidateVisual();
    }

    // -----------------------------------------------------------------------
    //  Entrada de datos (lo que manda el servidor)
    // -----------------------------------------------------------------------

    public void Feed(byte[] data, int count)
    {
        var chars = new char[_utf8.GetCharCount(data, 0, count)];
        var n = _utf8.GetChars(data, 0, count, chars, 0);
        for (var i = 0; i < n; i++)
            Process(chars[i]);
        _viewOffset = 0;
        InvalidateVisual();
    }

    private void Process(char c)
    {
        switch (_state)
        {
            case State.Ground:
                if (c == 0x1B) { _state = State.Escape; _escape.Clear(); return; }
                Print(c);
                return;

            case State.Escape:
                switch (c)
                {
                    case '[': _state = State.Csi; _escape.Clear(); return;
                    case ']': _state = State.Osc; _escape.Clear(); return;
                    case '(': case ')': case '*': case '+': _state = State.Charset; return;
                    case '7': _savedRow = _curRow; _savedCol = _curCol; break;
                    case '8': _curRow = _savedRow; _curCol = _savedCol; _pendingWrap = false; break;
                    case 'M': ReverseIndex(); break;
                    case 'D': LineFeed(); break;
                    case 'E': _curCol = 0; LineFeed(); break;
                    case 'c': Clear(); break;
                    case '=': case '>': break;   // teclado numerico: da igual
                }
                _state = State.Ground;
                return;

            case State.Charset:
                _state = State.Ground;   // solo ASCII: se ignora el juego pedido
                return;

            case State.Csi:
                if (c >= 0x40 && c <= 0x7E)
                {
                    Csi(_escape.ToString(), c);
                    _state = State.Ground;
                }
                else if (c == 0x1B)
                {
                    _state = State.Escape; _escape.Clear();
                }
                else
                {
                    _escape.Append(c);
                }
                return;

            case State.Osc:
                if (c == 0x07) { Osc(_escape.ToString()); _state = State.Ground; }
                else if (c == 0x1B) { _state = State.Escape; Osc(_escape.ToString()); _escape.Clear(); }   // ESC \ (ST)
                else _escape.Append(c);
                return;
        }
    }

    private void Print(char c)
    {
        switch (c)
        {
            case '\r': _curCol = 0; _pendingWrap = false; return;
            case '\n': case '\v': case '\f': LineFeed(); return;
            case '\b': if (_curCol > 0) _curCol--; _pendingWrap = false; return;
            case '\t': _curCol = Math.Min(_cols - 1, (_curCol / 8 + 1) * 8); return;
            case '\a': return;
            case '\0': return;
        }

        if (char.IsControl(c))
            return;

        if (_pendingWrap && _autoWrap)
        {
            _curCol = 0;
            LineFeed();
            _pendingWrap = false;
        }

        var cell = _attr;
        cell.Ch = c;
        _screen[_curRow, _curCol] = cell;

        if (_curCol < _cols - 1)
            _curCol++;
        else
            _pendingWrap = true;
    }

    private void LineFeed()
    {
        _pendingWrap = false;
        if (_curRow == _scrollBottom)
            ScrollUp(1);
        else if (_curRow < _rows - 1)
            _curRow++;
    }

    private void ReverseIndex()
    {
        if (_curRow == _scrollTop)
            ScrollDown(1);
        else if (_curRow > 0)
            _curRow--;
    }

    private void ScrollUp(int n)
    {
        for (var k = 0; k < n; k++)
        {
            if (_scrollTop == 0 && _savedScreen is null)
            {
                var line = new Cell[_cols];
                for (var c = 0; c < _cols; c++) line[c] = _screen[0, c];
                _scrollback.Add(line);
                if (_scrollback.Count > ScrollbackMax) _scrollback.RemoveAt(0);
            }

            for (var r = _scrollTop; r < _scrollBottom; r++)
                for (var c = 0; c < _cols; c++)
                    _screen[r, c] = _screen[r + 1, c];
            ClearRow(_scrollBottom);
        }
    }

    private void ScrollDown(int n)
    {
        for (var k = 0; k < n; k++)
        {
            for (var r = _scrollBottom; r > _scrollTop; r--)
                for (var c = 0; c < _cols; c++)
                    _screen[r, c] = _screen[r - 1, c];
            ClearRow(_scrollTop);
        }
    }

    private void ClearRow(int r, int from = 0, int to = int.MaxValue)
    {
        var blank = Default();
        blank.Bg = _attr.Bg;
        for (var c = from; c <= Math.Min(to, _cols - 1); c++)
            _screen[r, c] = blank;
    }

    private void Clear()
    {
        for (var r = 0; r < _rows; r++) ClearRow(r);
        _curRow = _curCol = 0;
        _pendingWrap = false;
    }

    private static Cell Default() => new() { Ch = ' ', Fg = -1, Bg = -1 };

    private void Csi(string parameters, char final)
    {
        var priv = parameters.StartsWith('?');
        if (priv) parameters = parameters[1..];
        var args = parameters.Split(';').Select(p => int.TryParse(p, out var v) ? v : 0).ToArray();
        int P(int i, int def = 1) => i < args.Length && args[i] > 0 ? args[i] : def;

        switch (final)
        {
            case 'A': _curRow = Math.Max(_scrollTop <= _curRow ? _scrollTop : 0, _curRow - P(0)); _pendingWrap = false; break;
            case 'B': _curRow = Math.Min(_curRow <= _scrollBottom ? _scrollBottom : _rows - 1, _curRow + P(0)); _pendingWrap = false; break;
            case 'C': _curCol = Math.Min(_cols - 1, _curCol + P(0)); _pendingWrap = false; break;
            case 'D': _curCol = Math.Max(0, _curCol - P(0)); _pendingWrap = false; break;
            case 'E': _curCol = 0; _curRow = Math.Min(_rows - 1, _curRow + P(0)); break;
            case 'F': _curCol = 0; _curRow = Math.Max(0, _curRow - P(0)); break;
            case 'G': case '`': _curCol = Math.Clamp(P(0) - 1, 0, _cols - 1); _pendingWrap = false; break;
            case 'd': _curRow = Math.Clamp(P(0) - 1, 0, _rows - 1); _pendingWrap = false; break;
            case 'H': case 'f':
                _curRow = Math.Clamp(P(0) - 1, 0, _rows - 1);
                _curCol = Math.Clamp(P(1) - 1, 0, _cols - 1);
                _pendingWrap = false;
                break;
            case 'J':
                switch (P(0, 0))
                {
                    case 0: ClearRow(_curRow, _curCol); for (var r = _curRow + 1; r < _rows; r++) ClearRow(r); break;
                    case 1: ClearRow(_curRow, 0, _curCol); for (var r = 0; r < _curRow; r++) ClearRow(r); break;
                    case 2: case 3: for (var r = 0; r < _rows; r++) ClearRow(r); break;
                }
                break;
            case 'K':
                switch (P(0, 0))
                {
                    case 0: ClearRow(_curRow, _curCol); break;
                    case 1: ClearRow(_curRow, 0, _curCol); break;
                    case 2: ClearRow(_curRow); break;
                }
                break;
            case 'L': InsertLines(P(0)); break;
            case 'M': DeleteLines(P(0)); break;
            case 'P': DeleteChars(P(0)); break;
            case '@': InsertChars(P(0)); break;
            case 'X': ClearRow(_curRow, _curCol, _curCol + P(0) - 1); break;
            case 'S': ScrollUp(P(0)); break;
            case 'T': ScrollDown(P(0)); break;
            case 'r':
                _scrollTop = Math.Clamp(P(0) - 1, 0, _rows - 1);
                _scrollBottom = Math.Clamp(P(1, _rows) - 1, _scrollTop, _rows - 1);
                _curRow = 0; _curCol = 0;
                break;
            case 'm': Sgr(args); break;
            case 'h': case 'l':
                if (priv) PrivateMode(args, final == 'h');
                break;
            case 's': _savedRow = _curRow; _savedCol = _curCol; break;
            case 'u': _curRow = _savedRow; _curCol = _savedCol; break;
            case 'n':
                if (P(0, 0) == 6) Send($"\x1b[{_curRow + 1};{_curCol + 1}R");
                break;
            case 'c': Send("\x1b[?1;2c"); break;
            case 't': break;   // tamaño de ventana: no
        }
    }

    private void PrivateMode(int[] args, bool set)
    {
        foreach (var a in args)
        {
            switch (a)
            {
                case 1: _appCursorKeys = set; break;
                case 7: _autoWrap = set; break;
                case 25: _cursorVisible = set; break;
                case 47: case 1047: case 1049:
                    if (set && _savedScreen is null)
                    {
                        _savedScreen = _screen;
                        _savedRow = _curRow; _savedCol = _curCol;
                        _screen = new Cell[_rows, _cols];
                        Clear();
                    }
                    else if (!set && _savedScreen is not null)
                    {
                        _screen = _savedScreen;
                        _savedScreen = null;
                        _curRow = _savedRow; _curCol = _savedCol;
                    }
                    break;
            }
        }
    }

    private void Sgr(int[] args)
    {
        if (args.Length == 0) args = [0];
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case 0: _attr = Default(); break;
                case 1: _attr.Bold = true; break;
                case 4: _attr.Underline = true; break;
                case 7: _attr.Inverse = true; break;
                case 22: _attr.Bold = false; break;
                case 24: _attr.Underline = false; break;
                case 27: _attr.Inverse = false; break;
                case >= 30 and <= 37: _attr.Fg = a - 30; break;
                case 39: _attr.Fg = -1; break;
                case >= 40 and <= 47: _attr.Bg = a - 40; break;
                case 49: _attr.Bg = -1; break;
                case >= 90 and <= 97: _attr.Fg = a - 90 + 8; break;
                case >= 100 and <= 107: _attr.Bg = a - 100 + 8; break;
                case 38 or 48:
                    var isFg = a == 38;
                    if (i + 2 < args.Length && args[i + 1] == 5)
                    {
                        if (isFg) _attr.Fg = args[i + 2]; else _attr.Bg = args[i + 2];
                        i += 2;
                    }
                    else if (i + 4 < args.Length && args[i + 1] == 2)
                    {
                        var idx = 256 + ((args[i + 2] & 0xFF) << 16 | (args[i + 3] & 0xFF) << 8 | (args[i + 4] & 0xFF));
                        if (isFg) _attr.Fg = idx; else _attr.Bg = idx;
                        i += 4;
                    }
                    break;
            }
        }
    }

    private void Osc(string text)
    {
        // 0/2: titulo de la ventana. Lo demas (colores, hyperlinks) se ignora.
        var semi = text.IndexOf(';');
        if (semi > 0 && (text[..semi] == "0" || text[..semi] == "2"))
        {
            Title = text[(semi + 1)..];
            TitleChanged?.Invoke(Title);
        }
    }

    private void InsertLines(int n)
    {
        if (_curRow < _scrollTop || _curRow > _scrollBottom) return;
        for (var k = 0; k < n; k++)
        {
            for (var r = _scrollBottom; r > _curRow; r--)
                for (var c = 0; c < _cols; c++) _screen[r, c] = _screen[r - 1, c];
            ClearRow(_curRow);
        }
    }

    private void DeleteLines(int n)
    {
        if (_curRow < _scrollTop || _curRow > _scrollBottom) return;
        for (var k = 0; k < n; k++)
        {
            for (var r = _curRow; r < _scrollBottom; r++)
                for (var c = 0; c < _cols; c++) _screen[r, c] = _screen[r + 1, c];
            ClearRow(_scrollBottom);
        }
    }

    private void DeleteChars(int n)
    {
        for (var c = _curCol; c < _cols; c++)
            _screen[_curRow, c] = c + n < _cols ? _screen[_curRow, c + n] : Default();
    }

    private void InsertChars(int n)
    {
        for (var c = _cols - 1; c >= _curCol; c--)
            _screen[_curRow, c] = c - n >= _curCol ? _screen[_curRow, c - n] : Default();
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
        if (cols != _cols || rows != _rows)
            Resize(rows, cols);
    }

    private void Resize(int rows, int cols)
    {
        var fresh = new Cell[rows, cols];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
                fresh[r, c] = r < _rows && c < _cols ? _screen[r, c] : Default();

        _screen = fresh;
        _savedScreen = null;
        _rows = rows; _cols = cols;
        _scrollTop = 0; _scrollBottom = rows - 1;
        _curRow = Math.Min(_curRow, rows - 1);
        _curCol = Math.Min(_curCol, cols - 1);
        Resized?.Invoke(cols, rows);
        InvalidateVisual();
    }

    // -----------------------------------------------------------------------
    //  Pintado
    // -----------------------------------------------------------------------

    private Brush BrushFor(int index, bool foreground, bool bold)
    {
        if (index < 0) return foreground ? _defaultFg : _defaultBg;
        if (index < 16) return new SolidColorBrush(Palette[bold && foreground && index < 8 ? index + 8 : index]);
        if (index < 232)
        {
            var i = index - 16;
            var r = i / 36; var g = i / 6 % 6; var b = i % 6;
            return new SolidColorBrush(Color.FromRgb((byte)(r == 0 ? 0 : r * 40 + 55), (byte)(g == 0 ? 0 : g * 40 + 55), (byte)(b == 0 ? 0 : b * 40 + 55)));
        }
        if (index < 256)
        {
            var v = (byte)((index - 232) * 10 + 8);
            return new SolidColorBrush(Color.FromRgb(v, v, v));
        }
        var rgb = index - 256;
        return new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(_defaultBg, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var sb = new StringBuilder();
        for (var r = 0; r < _rows; r++)
        {
            var y = r * _cellH;
            var c = 0;
            while (c < _cols)
            {
                var cell = CellAt(r, c);
                var start = c;
                sb.Clear();
                while (c < _cols && Same(CellAt(r, c), cell))
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

        if (_cursorVisible && _viewOffset == 0 && IsFocused)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(160, 0xE6, 0xE1, 0xE9)), null,
                new Rect(_curCol * _cellW, _curRow * _cellH, _cellW, _cellH));
    }

    private Cell CellAt(int r, int c)
    {
        if (_viewOffset == 0) return _screen[r, c];
        var idx = _scrollback.Count - _viewOffset + r;
        if (idx < _scrollback.Count)
        {
            var line = _scrollback[idx];
            return c < line.Length ? line[c] : Default();
        }
        return _screen[idx - _scrollback.Count, c];
    }

    private static bool Same(Cell a, Cell b) =>
        a.Fg == b.Fg && a.Bg == b.Bg && a.Bold == b.Bold && a.Underline == b.Underline && a.Inverse == b.Inverse;

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
            Key.Up => _appCursorKeys ? "\x1bOA" : "\x1b[A",
            Key.Down => _appCursorKeys ? "\x1bOB" : "\x1b[B",
            Key.Right => _appCursorKeys ? "\x1bOC" : "\x1b[C",
            Key.Left => _appCursorKeys ? "\x1bOD" : "\x1b[D",
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

        if (shift && e.Key == Key.PageUp) { _viewOffset = Math.Min(_scrollback.Count, _viewOffset + _rows / 2); InvalidateVisual(); e.Handled = true; return; }
        if (shift && e.Key == Key.PageDown) { _viewOffset = Math.Max(0, _viewOffset - _rows / 2); InvalidateVisual(); e.Handled = true; return; }

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
        _viewOffset = Math.Clamp(_viewOffset + (e.Delta > 0 ? 3 : -3), 0, _scrollback.Count);
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
        (Math.Clamp((int)(p.Y / _cellH), 0, _rows - 1), Math.Clamp((int)(p.X / _cellW), 0, _cols - 1));

    private bool IsSelected(int row, int fromCol, int toCol)
    {
        if (_selStart is null || _selEnd is null) return false;
        var (a, b) = Ordered();
        if (row < a.Row || row > b.Row) return false;
        var lo = row == a.Row ? a.Col : 0;
        var hi = row == b.Row ? b.Col : _cols - 1;
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
        var sb = new StringBuilder();
        for (var r = a.Row; r <= b.Row; r++)
        {
            var lo = r == a.Row ? a.Col : 0;
            var hi = r == b.Row ? b.Col : _cols - 1;
            var line = new StringBuilder();
            for (var c = lo; c <= hi; c++) line.Append(CellAt(r, c).Ch);
            sb.Append(line.ToString().TrimEnd());
            if (r < b.Row) sb.Append('\n');
        }
        try { Clipboard.SetText(sb.ToString()); } catch (Exception) { }
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
