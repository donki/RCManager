using System.Runtime.InteropServices;
using System.Windows;

namespace SocRcManager.Services;

/// <summary>
/// Lo que <see cref="TrayIcon"/> le pide a Windows: poner, cambiar y quitar el icono del area de
/// notificacion (Shell_NotifyIcon), el menu del boton derecho y pasar la ventana al frente. Detras de
/// una interfaz: las pruebas no ponen iconos en el area de notificacion de verdad.
/// </summary>
internal interface ITrayShell
{
    /// <summary>Pone el icono; sus clics llegan a la ventana con <paramref name="callbackMessage"/>.</summary>
    void Add(IntPtr hwnd, int callbackMessage, string tip);

    /// <summary>Un globo con ese titulo y texto (ya recortados a lo que cabe).</summary>
    void Balloon(IntPtr hwnd, string title, string text);

    void Delete(IntPtr hwnd);

    /// <summary>Menu emergente en el raton; lo elegido llega a la ventana como WM_COMMAND con su id. Texto null = separador.</summary>
    void ShowMenu(IntPtr hwnd, IReadOnlyList<(int Id, string? Text)> items);

    /// <summary>Activa la ventana y la pasa al frente.</summary>
    void Activate(Window window, IntPtr hwnd);
}

/// <summary>El area de notificacion de verdad (shell32 y user32).</summary>
internal sealed class Win32TrayShell : ITrayShell
{
    public void Add(IntPtr hwnd, int callbackMessage, string tip)
    {
        var data = ForAdd(hwnd, callbackMessage, LoadAppIcon(Environment.ProcessPath), tip);
        Shell_NotifyIcon(0 /* NIM_ADD */, ref data);
    }

    public void Balloon(IntPtr hwnd, string title, string text)
    {
        var data = ForBalloon(hwnd, title, text);
        Shell_NotifyIcon(1 /* NIM_MODIFY */, ref data);
    }

    public void Delete(IntPtr hwnd)
    {
        var data = Base(hwnd);
        Shell_NotifyIcon(2 /* NIM_DELETE */, ref data);
    }

    public void ShowMenu(IntPtr hwnd, IReadOnlyList<(int Id, string? Text)> items)
    {
        var menu = CreatePopupMenu();
        foreach (var (id, text) in items)
            AppendMenu(menu, text is null ? 0x800 /* MF_SEPARATOR */ : 0, id, text);
        GetCursorPos(out var p);
        SetForegroundWindow(hwnd);   // sin esto el menu no se cierra al pinchar fuera
        TrackPopupMenu(menu, 0x0080 /* TPM_RIGHTBUTTON */, p.X, p.Y, 0, hwnd, IntPtr.Zero);
        PostMessage(hwnd, 0, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
    }

    public void Activate(Window window, IntPtr hwnd)
    {
        window.Activate();
        SetForegroundWindow(hwnd);
    }

    /// <summary>Lo comun a todas las llamadas: la ventana duena y el id del icono.</summary>
    internal static NotifyIconData Base(IntPtr hwnd) => new() { cbSize = Marshal.SizeOf<NotifyIconData>(), hWnd = hwnd, uID = 1 };

    internal static NotifyIconData ForAdd(IntPtr hwnd, int callbackMessage, IntPtr icon, string tip)
    {
        var data = Base(hwnd);
        data.uFlags = 0x1 | 0x2 | 0x4;   // NIF_MESSAGE | NIF_ICON | NIF_TIP
        data.uCallbackMessage = callbackMessage;
        data.hIcon = icon;
        data.szTip = tip;
        return data;
    }

    internal static NotifyIconData ForBalloon(IntPtr hwnd, string title, string text)
    {
        var data = Base(hwnd);
        data.uFlags = 0x10;   // NIF_INFO
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = 0x1;   // NIIF_INFO
        return data;
    }

    /// <summary>El icono pequeño del exe (o el generico de Windows si no tiene).</summary>
    internal static IntPtr LoadAppIcon(string? path)
    {
        try
        {
            if (path is not null)
            {
                var large = new IntPtr[1];
                var small = new IntPtr[1];
                if (ExtractIconEx(path, 0, large, small, 1) > 0 && small[0] != IntPtr.Zero)
                    return small[0];
            }
        }
        catch (Exception) { }
        return LoadIcon(IntPtr.Zero, new IntPtr(32512));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, int flags, int id, string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool TrackPopupMenu(IntPtr menu, int flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int count);
}
