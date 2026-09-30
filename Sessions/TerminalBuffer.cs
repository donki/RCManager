using System.Text;

namespace SocRcManager.Sessions;

/// <summary>Una celda de la rejilla del terminal: caracter y atributos.</summary>
public struct TerminalCell
{
    public char Ch;
    public int Fg;   // -1 = por defecto; 0..255 paleta xterm; 256 + 0xRRGGBB color directo
    public int Bg;   // -1 = por defecto
    public bool Bold;
    public bool Underline;
    public bool Inverse;

    public static TerminalCell Default() => new() { Ch = ' ', Fg = -1, Bg = -1 };

    /// <summary>Mismos atributos (el caracter da igual): se pintan en el mismo tramo.</summary>
    public static bool SameStyle(TerminalCell a, TerminalCell b) =>
        a.Fg == b.Fg && a.Bg == b.Bg && a.Bold == b.Bold && a.Underline == b.Underline && a.Inverse == b.Inverse;
}

/// <summary>
/// El estado del terminal sin nada de pintar: la rejilla de celdas, el cursor, el scrollback y el
/// interprete de secuencias VT100/xterm (CSI, OSC, SGR, modos privados, pantalla alternativa).
/// <see cref="TerminalControl"/> lo pinta y le pasa lo que llega del SSH.
/// </summary>
/// <remarks>
/// Separado del control para poder probarlo sin interfaz: es el mismo codigo que estaba dentro de
/// <see cref="TerminalControl"/>, sin cambios de comportamiento.
/// </remarks>
public sealed class TerminalBuffer
{
    public const int DefaultCols = 80;
    public const int DefaultRows = 24;
    public const int ScrollbackMax = 3000;

    private TerminalCell[,] _screen = new TerminalCell[DefaultRows, DefaultCols];
    private TerminalCell[,]? _savedScreen;   // pantalla principal mientras esta la alternativa
    private readonly List<TerminalCell[]> _scrollback = [];

    private int _rows = DefaultRows;
    private int _cols = DefaultCols;
    private int _curRow, _curCol;
    private int _savedRow, _savedCol;
    private int _scrollTop, _scrollBottom = DefaultRows - 1;
    private bool _autoWrap = true;
    private bool _pendingWrap;

    private TerminalCell _attr = TerminalCell.Default();

    private readonly StringBuilder _escape = new();
    private enum State { Ground, Escape, Csi, Osc, Charset }
    private State _state = State.Ground;

    private readonly Decoder _utf8 = Encoding.UTF8.GetDecoder();

    public TerminalBuffer() => Clear();

    /// <summary>Respuestas que el terminal manda al otro lado (posicion del cursor, identificacion).</summary>
    public event Action<string>? Reply;

    public event Action<string>? TitleChanged;

    public int Rows => _rows;
    public int Cols => _cols;
    public int CursorRow => _curRow;
    public int CursorCol => _curCol;
    public bool CursorVisible { get; private set; } = true;
    public bool AppCursorKeys { get; private set; }
    public bool AlternateScreen => _savedScreen is not null;
    public int ScrollbackCount => _scrollback.Count;
    public string Title { get; private set; } = string.Empty;

    /// <summary>Atributos con los que se escribe ahora (los de la ultima SGR).</summary>
    public TerminalCell CurrentAttributes => _attr;

    // -----------------------------------------------------------------------
    //  Entrada de datos (lo que manda el servidor)
    // -----------------------------------------------------------------------

    /// <summary>Bytes UTF-8 del servidor; una secuencia partida entre dos llamadas se junta.</summary>
    public void Feed(byte[] data, int count)
    {
        var chars = new char[_utf8.GetCharCount(data, 0, count)];
        var n = _utf8.GetChars(data, 0, count, chars, 0);
        for (var i = 0; i < n; i++)
            Process(chars[i]);
    }

    public void Feed(string text)
    {
        foreach (var c in text)
            Process(c);
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
                var line = new TerminalCell[_cols];
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
        var blank = TerminalCell.Default();
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
                if (P(0, 0) == 6) Reply?.Invoke($"\x1b[{_curRow + 1};{_curCol + 1}R");
                break;
            case 'c': Reply?.Invoke("\x1b[?1;2c"); break;
            case 't': break;   // tamaño de ventana: no
        }
    }

    private void PrivateMode(int[] args, bool set)
    {
        foreach (var a in args)
        {
            switch (a)
            {
                case 1: AppCursorKeys = set; break;
                case 7: _autoWrap = set; break;
                case 25: CursorVisible = set; break;
                case 47: case 1047: case 1049:
                    if (set && _savedScreen is null)
                    {
                        _savedScreen = _screen;
                        _savedRow = _curRow; _savedCol = _curCol;
                        _screen = new TerminalCell[_rows, _cols];
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
                case 0: _attr = TerminalCell.Default(); break;
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
            _screen[_curRow, c] = c + n < _cols ? _screen[_curRow, c + n] : TerminalCell.Default();
    }

    private void InsertChars(int n)
    {
        for (var c = _cols - 1; c >= _curCol; c--)
            _screen[_curRow, c] = c - n >= _curCol ? _screen[_curRow, c - n] : TerminalCell.Default();
    }

    // -----------------------------------------------------------------------
    //  Tamaño y lectura
    // -----------------------------------------------------------------------

    /// <summary>Cambia la rejilla conservando lo que cabe; vuelve a la pantalla principal y quita la region de scroll.</summary>
    public void Resize(int rows, int cols)
    {
        var fresh = new TerminalCell[rows, cols];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
                fresh[r, c] = r < _rows && c < _cols ? _screen[r, c] : TerminalCell.Default();

        _screen = fresh;
        _savedScreen = null;
        _rows = rows; _cols = cols;
        _scrollTop = 0; _scrollBottom = rows - 1;
        _curRow = Math.Min(_curRow, rows - 1);
        _curCol = Math.Min(_curCol, cols - 1);
    }

    /// <summary>La celda que se ve en (fila, columna) con <paramref name="viewOffset"/> filas de scrollback por encima (0 = en vivo).</summary>
    public TerminalCell CellAt(int r, int c, int viewOffset = 0)
    {
        if (viewOffset == 0) return _screen[r, c];
        var idx = _scrollback.Count - viewOffset + r;
        if (idx < _scrollback.Count)
        {
            var line = _scrollback[idx];
            return c < line.Length ? line[c] : TerminalCell.Default();
        }
        return _screen[idx - _scrollback.Count, c];
    }

    /// <summary>El texto entre dos celdas (en orden de lectura), sin espacios al final de cada fila.</summary>
    public string TextBetween((int Row, int Col) a, (int Row, int Col) b, int viewOffset = 0)
    {
        var sb = new StringBuilder();
        for (var r = a.Row; r <= b.Row; r++)
        {
            var lo = r == a.Row ? a.Col : 0;
            var hi = r == b.Row ? b.Col : _cols - 1;
            var line = new StringBuilder();
            for (var c = lo; c <= hi; c++) line.Append(CellAt(r, c, viewOffset).Ch);
            sb.Append(line.ToString().TrimEnd());
            if (r < b.Row) sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Color de un indice: los 16 de la paleta (<paramref name="palette"/>, con los brillantes
    /// para el texto en negrita), el cubo 6x6x6, la escala de grises y el color directo (256 + RGB).
    /// </summary>
    public static (byte R, byte G, byte B) Rgb(int index, bool foreground, bool bold, (byte R, byte G, byte B)[] palette)
    {
        if (index < 16) return palette[bold && foreground && index < 8 ? index + 8 : index];
        if (index < 232)
        {
            var i = index - 16;
            var r = i / 36; var g = i / 6 % 6; var b = i % 6;
            return ((byte)(r == 0 ? 0 : r * 40 + 55), (byte)(g == 0 ? 0 : g * 40 + 55), (byte)(b == 0 ? 0 : b * 40 + 55));
        }
        if (index < 256)
        {
            var v = (byte)((index - 232) * 10 + 8);
            return (v, v, v);
        }
        var rgb = index - 256;
        return ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
