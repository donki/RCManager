using System.Runtime.InteropServices;
using System.Windows;

namespace SocRcManager.Services;

/// <summary>Un rectangulo en pixeles fisicos de la pantalla.</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}

/// <summary>Un monitor: entero y su area de trabajo (sin la barra de tareas), en pixeles fisicos.</summary>
public readonly record struct MonitorInfo(PixelRect Bounds, PixelRect WorkingArea, bool Primary);

/// <summary>
/// Lo que la ventana principal le pide al escritorio de Windows: monitores, el raton, arrastrar y
/// soltar, capturar el raton y pasar al frente. Detras de una interfaz para que las pruebas no
/// muevan ventanas a la pantalla, no se queden el raton ni le quiten el foco a nadie.
/// </summary>
internal interface IDesktop
{
    /// <summary>Los monitores, en el orden de Windows (Screen.AllScreens).</summary>
    IReadOnlyList<MonitorInfo> Monitors();

    /// <summary>Donde esta el raton (pixeles fisicos).</summary>
    (int X, int Y) CursorPosition();

    /// <summary>La ventana de nivel superior que hay bajo ese punto (o cero).</summary>
    IntPtr RootWindowAt(int x, int y);

    /// <summary>Arrastrar y soltar (DragDrop.DoDragDrop): vuelve al soltar o cancelar.</summary>
    DragDropEffects DoDragDrop(DependencyObject source, object data, DragDropEffects allowed);

    /// <summary>Se queda el raton para ese elemento.</summary>
    void CaptureMouse(UIElement element);

    /// <summary>Activa la ventana y la pasa al frente aunque Windows se resista.</summary>
    void ForceForeground(Window window);

    /// <summary>Abre un menu emergente (ya colocado).</summary>
    void OpenMenu(System.Windows.Controls.ContextMenu menu);
}

/// <summary>El escritorio que usa la aplicacion (las pruebas ponen uno de mentira).</summary>
internal static class Desktop
{
    internal static IDesktop Current { get; set; } = new WindowsDesktop();
}

/// <summary>El escritorio de verdad: llamadas de una linea a WPF, WinForms y user32.</summary>
internal sealed class WindowsDesktop : IDesktop
{
    public IReadOnlyList<MonitorInfo> Monitors() =>
        System.Windows.Forms.Screen.AllScreens.Select(s => new MonitorInfo(ToRect(s.Bounds), ToRect(s.WorkingArea), s.Primary)).ToList();

    private static PixelRect ToRect(System.Drawing.Rectangle r) => new(r.Left, r.Top, r.Width, r.Height);

    public (int X, int Y) CursorPosition() => GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);

    public IntPtr RootWindowAt(int x, int y) => GetAncestor(WindowFromPoint(new NativePoint { X = x, Y = y }), 2 /* GA_ROOT */);

    public DragDropEffects DoDragDrop(DependencyObject source, object data, DragDropEffects allowed) => DragDrop.DoDragDrop(source, data, allowed);

    public void CaptureMouse(UIElement element) => element.CaptureMouse();

    public void ForceForeground(Window window)
    {
        window.Activate();
        // Por si Windows no deja pasar al frente: un instante encima de todo.
        window.Topmost = true;
        window.Topmost = false;
        SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle);
        window.Focus();
    }

    public void OpenMenu(System.Windows.Controls.ContextMenu menu) => menu.IsOpen = true;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint p);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint p);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
}
