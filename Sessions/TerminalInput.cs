using System.Windows.Input;

namespace SocRcManager.Sessions;

/// <summary>Teclas del terminal traducidas a lo que espera un shell (secuencias xterm y codigos de control).</summary>
public static class TerminalKeys
{
    /// <summary>
    /// Lo que se manda al pulsar <paramref name="key"/>, o null si no manda nada (el texto normal
    /// llega por la entrada de texto, no por aqui). <paramref name="appCursorKeys"/> es el modo de
    /// cursor de aplicacion (DECCKM), que cambia las flechas.
    /// </summary>
    public static string? Sequence(Key key, bool ctrl, bool shift, bool alt, bool appCursorKeys)
    {
        var seq = key switch
        {
            Key.Enter => "\r",
            Key.Back => "\x7f",
            Key.Tab => "\t",
            Key.Escape => "\x1b",
            Key.Up => appCursorKeys ? "\x1bOA" : "\x1b[A",
            Key.Down => appCursorKeys ? "\x1bOB" : "\x1b[B",
            Key.Right => appCursorKeys ? "\x1bOC" : "\x1b[C",
            Key.Left => appCursorKeys ? "\x1bOD" : "\x1b[D",
            Key.Home => "\x1b[H",
            Key.End => "\x1b[F",
            Key.Insert => "\x1b[2~",
            Key.Delete => "\x1b[3~",
            // Con Mayus, las de pagina desplazan el historial del terminal: no van al shell.
            Key.PageUp => shift ? null : "\x1b[5~",
            Key.PageDown => shift ? null : "\x1b[6~",
            Key.F1 => "\x1bOP", Key.F2 => "\x1bOQ", Key.F3 => "\x1bOR", Key.F4 => "\x1bOS",
            Key.F5 => "\x1b[15~", Key.F6 => "\x1b[17~", Key.F7 => "\x1b[18~", Key.F8 => "\x1b[19~",
            Key.F9 => "\x1b[20~", Key.F10 => "\x1b[21~", Key.F11 => "\x1b[23~", Key.F12 => "\x1b[24~",
            _ => null,
        };
        if (seq is not null || !ctrl)
            return seq;

        // Ctrl+letra: codigo de control (Ctrl+C = 3, Ctrl+D = 4, Ctrl+Z = 26, Ctrl+L = 12...).
        if (!alt && key >= Key.A && key <= Key.Z)
            return ((char)(key - Key.A + 1)).ToString();
        return key switch
        {
            Key.Space => "\0",
            Key.OemOpenBrackets => "\x1b",
            _ => null,
        };
    }
}

/// <summary>
/// La seleccion del terminal con el raton: de la celda donde se pulso a la celda donde esta ahora,
/// en orden de lectura (filas enteras entre medias).
/// </summary>
public sealed class TerminalSelection
{
    private (int Row, int Col)? _start, _end;

    public bool IsActive => _start is not null && _end is not null;

    /// <summary>Empieza en una celda (pulsar).</summary>
    public void Begin((int Row, int Col) cell) => _start = _end = cell;

    /// <summary>Lleva el otro extremo a una celda (arrastrar).</summary>
    public void Extend((int Row, int Col) cell)
    {
        if (_start is not null)
            _end = cell;
    }

    /// <summary>Al soltar: pulsar y soltar en la misma celda no selecciona nada. Devuelve si queda seleccion.</summary>
    public bool Finish()
    {
        if (_start == _end)
            Clear();
        return IsActive;
    }

    public void Clear() => _start = _end = null;

    /// <summary>Los dos extremos en orden de lectura.</summary>
    public ((int Row, int Col) From, (int Row, int Col) To) Ordered()
    {
        var s = _start!.Value;
        var e = _end!.Value;
        return s.Row < e.Row || (s.Row == e.Row && s.Col <= e.Col) ? (s, e) : (e, s);
    }

    /// <summary>Si el tramo de la fila <paramref name="row"/> de <paramref name="fromCol"/> a <paramref name="toCol"/> esta entero dentro.</summary>
    public bool Covers(int row, int fromCol, int toCol, int cols)
    {
        if (!IsActive)
            return false;
        var (a, b) = Ordered();
        if (row < a.Row || row > b.Row)
            return false;
        var lo = row == a.Row ? a.Col : 0;
        var hi = row == b.Row ? b.Col : cols - 1;
        return fromCol >= lo && toCol <= hi;
    }
}
