using System.Windows;
using System.Windows.Interop;

namespace SocRcManager.Services;

/// <summary>
/// Icono en el area de notificacion: al minimizar (si el ajuste lo dice) la ventana se esconde y
/// queda el icono; clic para volver, boton derecho para «Abrir» o «Salir». Shell_NotifyIcon (via
/// <see cref="ITrayShell"/>) y un gancho en el procedimiento de la ventana (HwndSource) para cazar
/// SC_MINIMIZE. Igual que el de sOC Lucia: si se toca uno, mirar el otro.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    internal const int WmSysCommand = 0x0112, WmCommand = 0x0111, WmRButtonUp = 0x0205, WmLButtonUp = 0x0202, WmLButtonDblClk = 0x0203;
    internal const int WmTray = 0x8001;   // WM_APP + 1
    internal const int ScMinimize = 0xF020;
    internal const int IdOpen = 1, IdExit = 2;

    /// <summary>El area de notificacion que se usa (las pruebas ponen una de mentira).</summary>
    internal static ITrayShell Shell { get; set; } = new Win32TrayShell();

    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly Func<string, string> _text;
    private readonly Action _exit;
    private readonly ITrayShell _shell = Shell;
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

    public bool Hidden => !_window.IsVisible;

    /// <summary>Si el icono esta puesto en el area de notificacion.</summary>
    internal bool IconShown => _shown;

    /// <summary>Un globo en el area de notificacion (solo tiene sentido con la ventana escondida).</summary>
    public void Notify(string title, string text)
    {
        Add();
        _shell.Balloon(_hwnd, title.Length > 63 ? title[..63] : title, text.Length > 255 ? text[..255] : text);
    }

    internal IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
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

    /// <summary>De vuelta de la bandeja. Sin <paramref name="activate"/> (modo aislado) no se le quita el foco a nadie.</summary>
    public void Restore(bool activate = true)
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        if (activate)
            _shell.Activate(_window, _hwnd);
        Remove();
    }

    /// <summary>El menu del boton derecho: «Abrir», separador y «Salir».</summary>
    internal IReadOnlyList<(int Id, string? Text)> MenuItems() =>
        [(IdOpen, _text("TrayOpen")), (0, null), (IdExit, _text("TrayExit"))];

    private void ShowMenu() => _shell.ShowMenu(_hwnd, MenuItems());

    private void Add()
    {
        // Modo aislado de pruebas: la ventana se esconde igual, pero sin icono en el area de
        // notificacion, que es del Windows de verdad (constitucion general 8.4).
        if (_shown || Sandbox.IsOn) return;
        _shell.Add(_hwnd, WmTray, _text("AppTitle"));
        _shown = true;
    }

    private void Remove()
    {
        if (!_shown) return;
        _shell.Delete(_hwnd);
        _shown = false;
    }

    public void Dispose() => Remove();
}
