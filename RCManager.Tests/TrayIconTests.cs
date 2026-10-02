using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>El icono del area de notificacion, con un area de notificacion de mentira y mensajes simulados.</summary>
public sealed class TrayIconTests : UiTest
{
    private readonly FakeTrayShell _shell = new();
    private readonly ITrayShell _saved = TrayIcon.Shell;
    private int _exits;

    public TrayIconTests() => TrayIcon.Shell = _shell;

    public override void Dispose()
    {
        TrayIcon.Shell = _saved;
        base.Dispose();
    }

    private static string Text(string key) => "[" + key + "]";

    /// <summary>Una ventana enseñada fuera de la pantalla con su icono.</summary>
    private (Window Window, TrayIcon Tray) Shown() => Ui.Run(() =>
    {
        var w = Ui.Show(new Window { Width = 300, Height = 200 });
        return (w, new TrayIcon(w, Text, () => _exits++));
    });

    private static IntPtr Hook(TrayIcon tray, int msg, long wParam, long lParam, out bool handled)
    {
        var h = false;
        var r = Ui.Run(() => tray.Hook(IntPtr.Zero, msg, new IntPtr(wParam), new IntPtr(lParam), ref h));
        handled = h;
        return r;
    }

    [Fact]
    public void Esconder_pone_el_icono_una_vez_y_volver_lo_quita()
    {
        var (w, tray) = Shown();
        Ui.Run(() =>
        {
            tray.HideToTray();
            tray.HideToTray();
        });
        Assert.True(Ui.Run(() => tray.Hidden));
        Assert.True(tray.IconShown);
        Assert.Equal([$"add {TrayIcon.WmTray:X} [AppTitle]"], _shell.Calls);

        Ui.Run(() => tray.Restore(activate: false));
        Assert.False(Ui.Run(() => tray.Hidden));
        Assert.False(tray.IconShown);
        Assert.Equal(["add 8001 [AppTitle]", "delete"], _shell.Calls);
        Assert.Equal(WindowState.Normal, Ui.Run(() => w.WindowState));

        // Volver otra vez (ya sin icono) no lo quita dos veces; con activar, se pide el primer plano.
        Ui.Run(() => tray.Restore());
        Assert.Equal(["add 8001 [AppTitle]", "delete", "activate"], _shell.Calls);
    }

    [Fact]
    public void En_modo_aislado_se_esconde_sin_icono()
    {
        var (_, tray) = Shown();
        Sandbox.IsOn = true;
        Ui.Run(tray.HideToTray);
        Assert.True(Ui.Run(() => tray.Hidden));
        Assert.False(tray.IconShown);
        Assert.Empty(_shell.Calls);
        Ui.Run(tray.Dispose);
        Assert.Empty(_shell.Calls);
    }

    [Fact]
    public void Globo_recorta_titulo_y_texto_a_lo_que_cabe()
    {
        var (_, tray) = Shown();
        Ui.Run(() => tray.Notify("corto", "texto"));
        Ui.Run(() => tray.Notify(new string('t', 80), new string('x', 300)));
        Assert.Equal($"add 8001 [AppTitle]", _shell.Calls[0]);
        Assert.Equal("balloon corto|texto", _shell.Calls[1]);
        Assert.Equal($"balloon {new string('t', 63)}|{new string('x', 255)}", _shell.Calls[2]);
        Assert.Equal(3, _shell.Calls.Count);   // el icono se pone una sola vez
    }

    [Fact]
    public void Gancho_minimizar_esconde_si_el_ajuste_lo_dice()
    {
        var (_, tray) = Shown();
        Hook(tray, TrayIcon.WmSysCommand, TrayIcon.ScMinimize | 0x2, 0, out var handled);
        Assert.True(handled);
        Assert.True(Ui.Run(() => tray.Hidden));

        Ui.Run(() => tray.Restore(false));
        tray.MinimizeToTray = false;
        Hook(tray, TrayIcon.WmSysCommand, TrayIcon.ScMinimize, 0, out handled);
        Assert.False(handled);   // minimiza Windows, como siempre
        Hook(tray, TrayIcon.WmSysCommand, 0xF060 /* SC_CLOSE */, 0, out handled);
        Assert.False(handled);
        Assert.False(Ui.Run(() => tray.Hidden));
    }

    [Fact]
    public void Gancho_clics_en_el_icono_y_menu()
    {
        var (_, tray) = Shown();
        Ui.Run(tray.HideToTray);

        // Clic izquierdo: vuelve.
        Assert.Equal(IntPtr.Zero, Hook(tray, TrayIcon.WmTray, 0, TrayIcon.WmLButtonUp, out var handled));
        Assert.True(handled);
        Assert.False(Ui.Run(() => tray.Hidden));

        // Doble clic, tambien.
        Ui.Run(tray.HideToTray);
        Hook(tray, TrayIcon.WmTray, 0, TrayIcon.WmLButtonDblClk, out _);
        Assert.False(Ui.Run(() => tray.Hidden));

        // Boton derecho: el menu «Abrir», separador, «Salir».
        Hook(tray, TrayIcon.WmTray, 0, TrayIcon.WmRButtonUp, out handled);
        Assert.True(handled);
        Assert.Equal([(TrayIcon.IdOpen, "[TrayOpen]"), (0, null), (TrayIcon.IdExit, "[TrayExit]")], _shell.LastMenu);

        // Otro evento del icono (mover el raton por encima): atendido pero sin hacer nada.
        _shell.Calls.Clear();
        Hook(tray, TrayIcon.WmTray, 0, 0x0200 /* WM_MOUSEMOVE */, out handled);
        Assert.True(handled);
        Assert.Empty(_shell.Calls);
    }

    [Fact]
    public void Gancho_ordenes_del_menu_abrir_y_salir()
    {
        var (_, tray) = Shown();
        Ui.Run(tray.HideToTray);
        Hook(tray, TrayIcon.WmCommand, TrayIcon.IdOpen, 0, out var handled);
        Assert.True(handled);
        Assert.False(Ui.Run(() => tray.Hidden));

        Ui.Run(tray.HideToTray);
        Hook(tray, TrayIcon.WmCommand, TrayIcon.IdExit, 0, out handled);
        Assert.True(handled);
        Assert.Equal(1, _exits);
        Assert.False(tray.IconShown);

        // Otra orden cualquiera: no es nuestra.
        Hook(tray, TrayIcon.WmCommand, 99, 0, out handled);
        Assert.False(handled);
        Hook(tray, 0x0010 /* WM_CLOSE */, 0, 0, out handled);
        Assert.False(handled);
    }

    [Fact]
    public void El_gancho_esta_puesto_en_la_ventana_de_verdad()
    {
        var (w, tray) = Shown();
        var hwnd = Ui.Run(() => new WindowInteropHelper(w).Handle);
        // Un mensaje de verdad del icono (clic izquierdo) llega al gancho por el procedimiento de la ventana.
        Ui.Run(tray.HideToTray);
        Ui.Run(() => SendMessage(hwnd, TrayIcon.WmTray, IntPtr.Zero, new IntPtr(TrayIcon.WmLButtonUp)));
        Assert.False(Ui.Run(() => tray.Hidden));
        Assert.Contains("delete", _shell.Calls);
    }

    [Fact]
    public void Minimizar_desde_fuera_la_esconde_salvo_que_el_ajuste_diga_que_no()
    {
        // Ventana con handle pero sin enseñar (no se ve nada al minimizarla): el cambio de estado la manda a la bandeja.
        var (w, tray) = Ui.Run(() =>
        {
            var win = new Window();
            Ui.Hide(win);
            new WindowInteropHelper(win).EnsureHandle();
            return (win, new TrayIcon(win, Text, () => { }));
        });
        Minimize(w);
        Assert.Equal(WindowState.Normal, Ui.Run(() => w.WindowState));
        Assert.True(tray.IconShown);

        Ui.Run(() => tray.Restore(false));
        Ui.Run(w.Hide);
        tray.MinimizeToTray = false;
        Minimize(w);
        Assert.Equal(WindowState.Minimized, Ui.Run(() => w.WindowState));
        Assert.False(tray.IconShown);
    }

    /// <summary>Minimizar como desde la barra de tareas: WPF avisa del cambio de estado (aqui, a mano: la ventana no se ve).</summary>
    private static void Minimize(Window w) => Ui.Run(() =>
    {
        w.WindowState = WindowState.Minimized;
        Peek.Call(w, "OnStateChanged", EventArgs.Empty);
    });

    [Fact]
    public void Datos_de_Shell_NotifyIcon()
    {
        var hwnd = new IntPtr(1234);
        var b = Win32TrayShell.Base(hwnd);
        Assert.Equal(Marshal.SizeOf<Win32TrayShell.NotifyIconData>(), b.cbSize);
        Assert.Equal(hwnd, b.hWnd);
        Assert.Equal(1, b.uID);

        var add = Win32TrayShell.ForAdd(hwnd, TrayIcon.WmTray, new IntPtr(5), "sOC");
        Assert.Equal(0x7, add.uFlags);
        Assert.Equal(TrayIcon.WmTray, add.uCallbackMessage);
        Assert.Equal(new IntPtr(5), add.hIcon);
        Assert.Equal("sOC", add.szTip);

        var balloon = Win32TrayShell.ForBalloon(hwnd, "Titulo", "Texto");
        Assert.Equal(0x10, balloon.uFlags);
        Assert.Equal(("Titulo", "Texto", 1), (balloon.szInfoTitle, balloon.szInfo, balloon.dwInfoFlags));
    }

    [Fact]
    public void Icono_del_exe_o_el_generico()
    {
        // Sin ruta, con una que no existe o con un fichero sin iconos: el de Windows; nunca cero.
        var generic = Win32TrayShell.LoadAppIcon(null);
        Assert.NotEqual(IntPtr.Zero, generic);
        Assert.Equal(generic, Win32TrayShell.LoadAppIcon(Path.Combine(Dir.Path, "no-existe.exe")));
        Assert.Equal(generic, Win32TrayShell.LoadAppIcon(Dir.File("vacio.txt", "x")));
        // El exe de la aplicacion lleva el suyo.
        var exe = Path.ChangeExtension(typeof(App).Assembly.Location, ".exe");
        Assert.NotEqual(IntPtr.Zero, Win32TrayShell.LoadAppIcon(exe));
    }

    [Fact]
    public void Sin_icono_puesto_cambiarlo_o_quitarlo_no_hace_nada()
    {
        // Las llamadas de verdad que no ponen icono: con una ventana que no tiene ninguno, Windows dice que no y ya.
        var real = new Win32TrayShell();
        var (w, _) = Shown();
        var hwnd = Ui.Run(() => new WindowInteropHelper(w).Handle);
        real.Balloon(hwnd, "t", "x");
        real.Delete(hwnd);

        // Activar una ventana que aun no se ha enseñado no la enseña.
        Ui.Run(() =>
        {
            var hidden = new Window();
            real.Activate(hidden, IntPtr.Zero);
            Assert.False(hidden.IsVisible);
            Assert.False(hidden.IsActive);
            hidden.Close();
        });
    }

    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
