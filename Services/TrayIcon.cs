using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SocRcManager.Services;

/// <summary>
/// Icono en el area de notificacion: al minimizar (si el ajuste lo dice) la ventana se esconde y
/// queda el icono; clic para volver, boton derecho para «Abrir» o «Salir». Shell_NotifyIcon y un
/// gancho en el procedimiento de la ventana (HwndSource) para cazar SC_MINIMIZE. Igual que el de
/// sOC Lucia: si se toca uno, mirar el otro.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int WmSysCommand = 0x0112, WmCommand = 0x0111, WmRButtonUp = 0x0205, WmLButtonUp = 0x0202, WmLButtonDblClk = 0x0203;
    private const int WmTray = 0x8001;   // WM_APP + 1
    private const int ScMinimize = 0xF020;
    private const int IdOpen = 1, IdExit = 2;

    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly Func<string, string> _text;
    private readonly Action _exit;
    private bool _shown;

    /// <summary>Si al minimizar se esconde en la bandeja (ajuste del usuario) o se minimiza como siempre.</summary>
    public bool MinimizeToTray { get; set; } = true;

    public TrayIcon(Window window, Func<string, string> text, Action exit)
    {
        _window = window;
        _text = text;
        _exit = exit;
        _hwnd = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(Hook);
        // Minimizar desde la barra de tareas o con Win+D no pasa por SC_MINIMIZE: se caza el cambio de estado.
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState == WindowState.Minimized && MinimizeToTray)
                window.Dispatcher.BeginInvoke(() => { window.WindowState = WindowState.Normal; HideToTray(); });
        };
    }

    public bool Hidden => _shown && !_window.IsVisible;

    /// <summary>Un globo en el area de notificacion (solo tiene sentido con la ventana escondida).</summary>
    public void Notify(string title, string text)
    {
        Add();
        var data = Data();
        data.uFlags = 0x10;   // NIF_INFO
        data.szInfoTitle = title.Length > 63 ? title[..63] : title;
        data.szInfo = text.Length > 255 ? text[..255] : text;
        data.dwInfoFlags = 0x1;   // NIIF_INFO
        Shell_NotifyIcon(1 /* NIM_MODIFY */, ref data);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmSysCommand when ((long)wParam & 0xFFF0) == ScMinimize && MinimizeToTray:
                HideToTray();
                handled = true;
                break;
            case WmTray:
                var evt = (int)((long)lParam & 0xFFFF);
                if (evt is WmLButtonUp or WmLButtonDblClk) Restore();
                else if (evt == WmRButtonUp) ShowMenu();
                handled = true;
                break;
            case WmCommand when (int)((long)wParam & 0xFFFF) == IdOpen:
                Restore();
                handled = true;
                break;
            case WmCommand when (int)((long)wParam & 0xFFFF) == IdExit:
                Remove();
                _exit();
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    public void HideToTray()
    {
        Add();
        _window.Hide();
    }

    public void Restore()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
        SetForegroundWindow(_hwnd);
        Remove();
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, 0, IdOpen, _text("TrayOpen"));
        AppendMenu(menu, 0x800 /* MF_SEPARATOR */, 0, null);
        AppendMenu(menu, 0, IdExit, _text("TrayExit"));
        GetCursorPos(out var p);
        SetForegroundWindow(_hwnd);   // sin esto el menu no se cierra al pinchar fuera
        TrackPopupMenu(menu, 0x0080 /* TPM_RIGHTBUTTON */, p.X, p.Y, 0, _hwnd, IntPtr.Zero);
        PostMessage(_hwnd, 0, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
    }

    private void Add()
    {
        if (_shown) return;
        var data = Data();
        data.uFlags = 0x1 | 0x2 | 0x4;   // NIF_MESSAGE | NIF_ICON | NIF_TIP
        data.uCallbackMessage = WmTray;
        data.hIcon = LoadAppIcon();
        data.szTip = _text("AppTitle");
        Shell_NotifyIcon(0 /* NIM_ADD */, ref data);
        _shown = true;
    }

    private void Remove()
    {
        if (!_shown) return;
        var data = Data();
        Shell_NotifyIcon(2 /* NIM_DELETE */, ref data);
        _shown = false;
    }

    private NotifyIconData Data() => new() { cbSize = Marshal.SizeOf<NotifyIconData>(), hWnd = _hwnd, uID = 1 };

    private static IntPtr LoadAppIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } path)
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

    public void Dispose() => Remove();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
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

/// <summary>«Arrancar con Windows»: una entrada en HKCU\…\Run con el exe y --tray (arranca escondido en la bandeja).</summary>
public static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "sOCLucia";
    /// <summary>Nombre de la entrada hasta 2026.9.20.3 (sOC AI Chat); apuntaba a un exe que ya no existe.</summary>
    private const string OldValueName = "sOCAIChat";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return false;
            if (key.GetValue(OldValueName) is string)
            {
                key.DeleteValue(OldValueName, throwOnMissingValue: false);
                Set(true);
                return true;
            }
            return key.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Al arrancar: si «Arrancar con Windows» esta puesto, la entrada pasa a apuntar a ESTE exe. Asi
    /// no se queda clavada en una copia vieja (una compilacion de pruebas, una carpeta que ya no
    /// existe) cuando la aplicacion se mueve o se actualiza. Las compilaciones Debug no tocan nada:
    /// si no, cada prueba secuestraria el arranque del usuario.
    /// </summary>
    public static void Refresh()
    {
#if !DEBUG
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            var current = key?.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(current) || Environment.ProcessPath is not { Length: > 0 } exe)
                return;
            var wanted = "\"" + exe + "\" --tray";
            if (!string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
                Set(true);
        }
        catch (Exception) { }
#endif
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;
            if (enabled && Environment.ProcessPath is { Length: > 0 } exe)
                key.SetValue(ValueName, "\"" + exe + "\" --tray");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception) { }
    }
}
