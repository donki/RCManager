namespace SocRcManager.Services;

/// <summary>Posicion y tamaño de una ventana suelta (unidades de WPF), para recordarla.</summary>
public sealed record WindowBounds(double Left, double Top, double Width, double Height, bool Maximized = false)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

/// <summary>Un rectangulo de pantalla (el area de trabajo de un monitor, sin la barra de tareas).</summary>
public readonly record struct ScreenArea(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

/// <summary>
/// Donde va una pestaña que se saca a su propia ventana, y que hacer con las sueltas al cerrar la
/// principal. Sin interfaz: lo usa la ventana y lo prueban las pruebas.
/// </summary>
public static class SessionPlacement
{
    /// <summary>Lo minimo de la ventana que tiene que verse en algun monitor para darla por visible (la barra de titulo, para poder cogerla).</summary>
    public const double MinVisibleWidth = 120, MinVisibleHeight = 40;

    public const double MinWidth = 480, MinHeight = 320;

    /// <summary>
    /// Ajusta unas medidas guardadas a los monitores de ahora: si la ventana se ve lo bastante en
    /// alguno, se deja donde estaba (multimonitor: cada una en el suyo); si no (se desenchufo ese
    /// monitor, cambio la resolucion), se lleva al primero, centrada, y se encoge si no cabe.
    /// </summary>
    public static WindowBounds Fit(WindowBounds bounds, IReadOnlyList<ScreenArea> screens)
    {
        var width = Math.Max(MinWidth, bounds.Width);
        var height = Math.Max(MinHeight, bounds.Height);
        var b = bounds with { Width = width, Height = height };
        if (screens.Count == 0)
            return b;

        foreach (var s in screens)
        {
            // Lo que se ve de la franja de arriba (la barra de titulo) en este monitor.
            var visibleW = Math.Min(b.Right, s.Right) - Math.Max(b.Left, s.Left);
            var visibleH = Math.Min(b.Top + MinVisibleHeight, s.Bottom) - Math.Max(b.Top, s.Top);
            if (visibleW >= MinVisibleWidth && visibleH >= MinVisibleHeight)
                return b;
        }

        var main = screens[0];
        width = Math.Min(width, main.Width);
        height = Math.Min(height, main.Height);
        return b with
        {
            Left = main.Left + (main.Width - width) / 2,
            Top = main.Top + (main.Height - height) / 2,
            Width = width,
            Height = height,
        };
    }

    /// <summary>
    /// Ventana nueva para una pestaña que se suelta en <paramref name="cursorX"/>/<paramref name="cursorY"/>:
    /// el raton queda sobre su barra de titulo, un poco a la derecha de la esquina, como al arrastrar
    /// una pestaña del navegador. Con el tamaño que tenia la pestaña.
    /// </summary>
    public static WindowBounds AtCursor(double cursorX, double cursorY, double width, double height, IReadOnlyList<ScreenArea> screens) =>
        Fit(new WindowBounds(cursorX - 60, cursorY - 14, width, height), screens);

    /// <summary>Sin raton (boton «Sacar a una ventana»): un poco desplazada de la principal.</summary>
    public static WindowBounds NextTo(WindowBounds main, double width, double height, int alreadyDetached, IReadOnlyList<ScreenArea> screens)
    {
        var offset = 48 + 32 * (alreadyDetached % 8);
        return Fit(new WindowBounds(main.Left + offset, main.Top + offset, width, height), screens);
    }

    /// <summary>
    /// Donde va la ventana de una pestaña que se saca: soltada con el raton, alli (con el tamaño que
    /// tuvo su ventana la ultima vez, o el de la pestaña); con el boton, donde estuvo la ultima vez
    /// (maximizada si lo estaba) o, la primera vez, junto a la principal.
    /// </summary>
    public static WindowBounds ForDetach((double X, double Y)? cursor, WindowBounds? saved, double tabWidth, double tabHeight,
        WindowBounds main, int alreadyDetached, IReadOnlyList<ScreenArea> screens)
    {
        if (cursor is { } c)
            return AtCursor(c.X, c.Y, saved?.Width ?? tabWidth, saved?.Height ?? tabHeight, screens);
        if (saved is not null)
            return Fit(saved, screens);
        return NextTo(main, tabWidth, tabHeight, alreadyDetached, screens);
    }

    /// <summary>Que hacer al soltar una pestaña (o la barra de una ventana suelta) que se arrastraba.</summary>
    public enum DropAction
    {
        /// <summary>Nada (cancelado con Esc, o soltada en su sitio).</summary>
        None,

        /// <summary>Sale de la principal a una ventana propia donde se solto.</summary>
        Detach,

        /// <summary>Vuelve a la principal como pestaña.</summary>
        Attach,

        /// <summary>La ventana suelta se lleva a donde se solto.</summary>
        MoveWindow,
    }

    /// <param name="fromDetached">Se arrastraba la barra de una ventana suelta (si no, una pestaña de la principal).</param>
    /// <param name="cancelled">Se cancelo con Esc.</param>
    /// <param name="overMain">Se solto sobre la ventana principal.</param>
    /// <param name="overDetached">Se solto sobre una ventana suelta (otra o la misma).</param>
    public static DropAction AfterDrag(bool fromDetached, bool cancelled, bool overMain, bool overDetached)
    {
        if (cancelled)
            return DropAction.None;
        if (fromDetached)
            return overMain ? DropAction.Attach : overDetached ? DropAction.None : DropAction.MoveWindow;
        return overMain || overDetached ? DropAction.None : DropAction.Detach;
    }

    /// <summary>Que hacer con las ventanas sueltas al cerrar la principal.</summary>
    public enum CloseAction
    {
        /// <summary>No hay sueltas, o el ajuste dice cerrarlas sin preguntar: se cierra todo.</summary>
        CloseAll,

        /// <summary>Hay sueltas y el ajuste dice preguntar.</summary>
        Ask,
    }

    public static CloseAction OnMainClosing(int detachedCount, bool askSetting, bool forced) =>
        detachedCount > 0 && askSetting && !forced ? CloseAction.Ask : CloseAction.CloseAll;

    /// <summary>Clave con la que se recuerda la ventana suelta de una conexion.</summary>
    public static string Key(Guid connectionId) => connectionId.ToString("N");
}
