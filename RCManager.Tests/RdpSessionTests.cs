using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using SocRcManager.Models;
using SocRcManager.Services;
using SocRcManager.Sessions;
using Rect = System.Drawing.Rectangle;

namespace SocRcManager.Tests;

/// <summary>Los ajustes que se le ponen al control RDP: funcion pura de la conexion.</summary>
public sealed class RdpSettingsTests
{
    private static Connection Rdp() => new()
    {
        Name = "Oficina",
        Host = "srv.example",
        Port = 3390,
        UserName = "ana",
    };

    [Fact]
    public void Con_los_valores_por_defecto()
    {
        var s = RdpSettings.From(Rdp(), "secreto", 1280, 720);
        Assert.Equal("srv.example", s.Server);
        Assert.Equal("ana", s.UserName);
        Assert.Null(s.Domain);
        Assert.Equal(3390, s.Port);
        Assert.Equal("secreto", s.Password);
        Assert.True(s.SmartSizing);
        Assert.Equal(32, s.ColorDepth);
        Assert.True(s.DisplayConnectionBar);
        Assert.Equal("Oficina", s.FullScreenTitle);
        Assert.Equal((1280, 720), (s.DesktopWidth, s.DesktopHeight));
        Assert.Equal(0u, s.AudioRedirectionMode);
        Assert.False(s.AudioCapture);
        Assert.Equal(2, s.KeyboardHookMode);
        Assert.True(s.RedirectPrinters);
        Assert.True(s.RedirectClipboard);
        Assert.True(s.RedirectDrives);
        Assert.True(s.RedirectSmartCards);
        Assert.False(s.RedirectPorts);
        Assert.False(s.RedirectDevices);
        // Todo activado: solo los dos bits de «activar» (suavizado y composicion).
        Assert.Equal(0x80 | 0x100, s.PerformanceFlags);
        Assert.Equal(1, s.BitmapPersistence);
        Assert.True(s.AutoReconnect);
        // El editor guarda 1 = avisar; mstscax lo llama 2.
        Assert.Equal(2u, s.AuthenticationLevel);
        Assert.False(s.AdminSession);
        Assert.Null(s.Gateway);
    }

    [Fact]
    public void Resolucion_fija_dominio_y_recursos()
    {
        var c = Rdp();
        c.Domain = "CORP";
        c.RdpWidth = 1024;
        c.RdpHeight = 768;
        c.RdpColorDepth = 16;
        c.RdpAudioMode = 7;
        c.RdpAudioCapture = true;
        c.RdpKeyboardMode = -3;
        c.RdpPrinters = false;
        c.RdpClipboard = false;
        c.RdpDrives = false;
        c.RdpSmartCards = false;
        c.RdpPorts = true;
        c.RdpDevices = true;
        c.RdpSmartSizing = false;
        c.RdpConnectionBar = false;
        c.RdpBitmapCache = false;
        c.RdpAutoReconnect = false;
        c.RdpAdminSession = true;
        var s = RdpSettings.From(c, "", 1280, 720);
        Assert.Equal("CORP", s.Domain);
        Assert.Equal((1024, 768), (s.DesktopWidth, s.DesktopHeight));
        Assert.Equal(16, s.ColorDepth);
        Assert.Equal(2u, s.AudioRedirectionMode);
        Assert.True(s.AudioCapture);
        Assert.Equal(0, s.KeyboardHookMode);
        Assert.False(s.RedirectPrinters);
        Assert.False(s.RedirectClipboard);
        Assert.False(s.RedirectDrives);
        Assert.False(s.RedirectSmartCards);
        Assert.True(s.RedirectPorts);
        Assert.True(s.RedirectDevices);
        Assert.False(s.SmartSizing);
        Assert.False(s.DisplayConnectionBar);
        Assert.Equal(0, s.BitmapPersistence);
        Assert.False(s.AutoReconnect);
        Assert.True(s.AdminSession);
    }

    [Theory]
    [InlineData(1024, 0)]
    [InlineData(0, 768)]
    public void Resolucion_a_medias_es_la_de_la_pestaña(int width, int height)
    {
        var c = Rdp();
        c.RdpWidth = width;
        c.RdpHeight = height;
        var s = RdpSettings.From(c, "", 900, 500);
        Assert.Equal((900, 500), (s.DesktopWidth, s.DesktopHeight));
    }

    [Theory]
    [InlineData(15, 15)]
    [InlineData(24, 24)]
    [InlineData(32, 32)]
    [InlineData(8, 32)]
    [InlineData(0, 32)]
    public void Profundidad_de_color_valida_o_32(int stored, int expected)
    {
        var c = Rdp();
        c.RdpColorDepth = stored;
        Assert.Equal(expected, RdpSettings.From(c, "", 800, 600).ColorDepth);
    }

    [Fact]
    public void Experiencia_todo_desactivado()
    {
        var c = Rdp();
        c.RdpWallpaper = false;
        c.RdpWindowDrag = false;
        c.RdpMenuAnimation = false;
        c.RdpVisualStyles = false;
        c.RdpFontSmoothing = false;
        c.RdpDesktopComposition = false;
        Assert.Equal(0x01 | 0x02 | 0x04 | 0x08, RdpSettings.From(c, "", 800, 600).PerformanceFlags);
    }

    [Theory]
    [InlineData(0, 0u)]
    [InlineData(1, 2u)]
    [InlineData(2, 1u)]
    [InlineData(9, 0u)]
    public void Nivel_de_autenticacion_en_el_orden_de_mstscax(int stored, uint expected)
    {
        var c = Rdp();
        c.RdpAuthLevel = stored;
        Assert.Equal(expected, RdpSettings.From(c, "", 800, 600).AuthenticationLevel);
    }

    [Fact]
    public void Puerta_de_enlace_sin_host_o_desactivada_no_se_usa()
    {
        var c = Rdp();
        c.RdpGatewayMode = 1;
        Assert.Null(RdpSettings.From(c, "", 800, 600).Gateway);
        c.RdpGatewayMode = 0;
        c.RdpGatewayHost = "gw.example";
        Assert.Null(RdpSettings.From(c, "", 800, 600).Gateway);
    }

    [Fact]
    public void Puerta_de_enlace_con_las_mismas_credenciales()
    {
        var c = Rdp();
        c.RdpGatewayMode = 1;
        c.RdpGatewayHost = "gw.example";
        c.RdpGatewayUserName = "no-se-usa";
        Assert.Equal(new RdpGatewaySettings("gw.example", 1, 1, null, null, null), RdpSettings.From(c, "", 800, 600).Gateway);
    }

    [Fact]
    public void Puerta_de_enlace_con_credenciales_propias_y_deteccion()
    {
        var c = Rdp();
        c.RdpGatewayMode = 2;
        c.RdpGatewayHost = "gw.example";
        c.RdpGatewaySameCredentials = false;
        c.RdpGatewayUserName = "gwuser";
        c.RdpGatewayDomain = "GW";
        c.RdpGatewayPasswordProtected = Secrets.Protect("gwpass");
        Assert.Equal(new RdpGatewaySettings("gw.example", 2, 0, "gwuser", "GW", "gwpass"), RdpSettings.From(c, "", 800, 600).Gateway);
    }

    [Theory]
    [InlineData(1000, 500, 1.0, 1.0, 1000, 500)]
    [InlineData(1000, 500, 1.5, 1.25, 1500, 625)]
    [InlineData(100.4, 100.6, 2.0, 2.0, 201, 201)]
    [InlineData(0, 0, 1.0, 1.0, 200, 200)]
    [InlineData(150, 900, 1.0, 1.0, 200, 900)]
    public void Tamaño_en_pixeles_fisicos(double w, double h, double sx, double sy, int ew, int eh) =>
        Assert.Equal((ew, eh), RdpSettings.PixelSize(w, h, sx, sy));
}

/// <summary>La pestaña RDP con un control de mentira: conectar, eventos del control, escala, pantalla completa y monitores.</summary>
public sealed class RdpSessionTests : UiTest
{
    private readonly FakeRdpControl _rdp = new();
    private readonly Connection _c = new() { Name = "Oficina", Host = "srv.example", UserName = "ana" };

    private RdpSession Create() => Ui.Run(() => new RdpSession(_c, _rdp));

    /// <summary>La sesion dentro de una ventana (fuera de la pantalla) con la pestaña de ese tamaño.</summary>
    private (RdpSession Session, Window Window, Border Holder) Shown(double width = 640, double height = 400)
    {
        var session = Create();
        var (window, holder) = Ui.Run(() =>
        {
            var holder = new Border { Width = width, Height = height, Child = session.View };
            var w = Ui.Show(new Window { Content = holder, SizeToContent = SizeToContent.WidthAndHeight });
            return (w, holder);
        });
        return (session, window, holder);
    }

    private static (int, int) Physical(FrameworkElement view, double w, double h) => Ui.Run(() =>
    {
        var m = PresentationSource.FromVisual(view)!.CompositionTarget!.TransformToDevice;
        return RdpSettings.PixelSize(w, h, m.M11, m.M22);
    });

    [Fact]
    public void La_vista_aloja_el_control_y_lo_basico()
    {
        var s = Create();
        var host = Assert.IsType<WindowsFormsHost>(s.View);
        Assert.Same(_rdp.Control, Ui.Run(() => host.Child));
        Assert.Equal("Oficina", s.Title);
        Assert.True(s.HasNativeFullScreen);
        Assert.True(s.CanZoom);
        s.Focus();
        Assert.Equal(1, _rdp.Focused);
    }

    [Fact]
    public void Conectar_pone_los_ajustes_con_el_tamaño_de_la_pestaña_y_conecta()
    {
        var (s, _, _) = Shown(640, 400);
        Ui.RunAsync(() => s.ConnectAsync("pw"));
        var expected = Physical(s.View, 640, 400);
        Assert.NotNull(_rdp.Applied);
        Assert.Equal("pw", _rdp.Applied!.Password);
        Assert.Equal("srv.example", _rdp.Applied.Server);
        Assert.Equal(expected, (_rdp.Applied.DesktopWidth, _rdp.Applied.DesktopHeight));
        Assert.Equal(["connect"], _rdp.Log);
    }

    [Fact]
    public void Conectar_sin_ventana_pide_lo_minimo_y_con_resolucion_fija_la_fija()
    {
        var s = Create();
        Ui.RunAsync(() => s.ConnectAsync(""));
        Assert.Equal((200, 200), (_rdp.Applied!.DesktopWidth, _rdp.Applied.DesktopHeight));

        _c.RdpWidth = 1600;
        _c.RdpHeight = 900;
        Ui.RunAsync(() => s.ConnectAsync(""));
        Assert.Equal((1600, 900), (_rdp.Applied!.DesktopWidth, _rdp.Applied.DesktopHeight));
    }

    [Fact]
    public void Al_conectar_cambia_el_titulo()
    {
        var s = Create();
        var titles = new List<string>();
        s.TitleChanged += titles.Add;
        Ui.Run(_rdp.RaiseConnected);
        Assert.Equal(["Oficina"], titles);
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(2, null)]
    [InlineData(516, "RdpNoConnection")]
    [InlineData(2825, "RdpAuthFailed")]
    public void Al_desconectar_se_acaba_con_el_motivo(int reason, string? key)
    {
        var s = Create();
        var ended = new List<string?>();
        s.Ended += ended.Add;
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(() => _rdp.RaiseDisconnected(reason));
        Assert.Equal([key is null ? null : Localization.Loc.Get(key)], ended);
    }

    [Fact]
    public void Motivo_desconocido_lleva_el_numero()
    {
        var s = Create();
        string? reason = null;
        s.Ended += r => reason = r;
        Ui.Run(() => _rdp.RaiseDisconnected(12345));
        Assert.Contains("12345", reason);
    }

    [Fact]
    public void La_barra_de_mstsc_pide_salir_de_pantalla_completa_y_minimizar()
    {
        var s = Create();
        var minimized = 0;
        s.MinimizeRequested += () => minimized++;
        Ui.Run(() =>
        {
            _rdp.FullScreen = true;
            _rdp.RaiseRequestLeaveFullScreen();
            _rdp.RaiseRequestContainerMinimize();
        });
        Assert.False(_rdp.FullScreen);
        Assert.Equal(1, minimized);
    }

    [Fact]
    public void Control_destruido_con_la_sesion_abierta_queda_en_el_registro()
    {
        Create();
        Ui.Run(_rdp.RaiseHandleDestroyed);   // sin conectar: nada
        Assert.False(File.Exists(AppLog.FilePath));
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(_rdp.RaiseHandleDestroyed);
        Assert.Contains("RDP Oficina: el control se ha destruido con la sesion abierta", File.ReadAllText(AppLog.FilePath));
    }

    [Fact]
    public void Control_destruido_al_cerrar_desde_la_barra_no_es_un_fallo()
    {
        Create();
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(_rdp.RaiseConfirmClose);
        Ui.Run(_rdp.RaiseHandleDestroyed);
        Assert.False(File.Exists(AppLog.FilePath));
    }

    [Fact]
    public void Control_destruido_tras_desconectar_no_es_un_fallo()
    {
        var s = Create();
        Ui.Run(_rdp.RaiseConnected);
        s.Disconnect();
        Ui.Run(_rdp.RaiseHandleDestroyed);
        Assert.False(File.Exists(AppLog.FilePath));
    }

    // ------------------------------------------------------------------ Escala (zoom) tras iniciar sesion

    [Fact]
    public void Escala_al_100_no_se_pide()
    {
        var s = Create();
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(_rdp.RaiseLoginComplete);
        Assert.Empty(_rdp.Updates);
        Assert.False(s.ScaleRetryTimer.IsEnabled);
    }

    [Fact]
    public void Escala_guardada_se_pide_al_iniciar_sesion_con_el_tamaño_de_la_pestaña()
    {
        _c.RdpScalePercent = 150;
        var (s, _, _) = Shown(700, 450);
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(_rdp.RaiseLoginComplete);
        var (w, h) = Physical(s.View, 700, 450);
        Assert.Equal([(w, h, 150)], _rdp.Updates);
        Assert.False(s.ScaleRetryTimer.IsEnabled);
    }

    [Fact]
    public void Escala_con_resolucion_fija_y_tras_reconexion_automatica()
    {
        _c.RdpScalePercent = 125;
        _c.RdpWidth = 1280;
        _c.RdpHeight = 1024;
        Create();
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(_rdp.RaiseAutoReconnected);
        Assert.Equal([(1280, 1024, 125)], _rdp.Updates);
    }

    [Fact]
    public void Escala_en_pantalla_completa_con_el_tamaño_del_monitor()
    {
        _c.RdpScalePercent = 175;
        _rdp.ScreenBounds = new Rect(1920, 0, 2560, 1440);
        Create();
        Ui.Run(() => { _rdp.RaiseConnected(); _rdp.FullScreen = true; _rdp.RaiseLoginComplete(); });
        Assert.Equal([(2560, 1440, 175)], _rdp.Updates);
    }

    [Fact]
    public void Escala_rechazada_se_reintenta_hasta_que_la_acepta()
    {
        _c.RdpScalePercent = 150;
        var s = Create();
        var answers = new Queue<bool>([false, false, true]);
        _rdp.UpdateResult = () => answers.Dequeue() ? true : throw new InvalidOperationException("todavia no");
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(_rdp.RaiseLoginComplete);
        Assert.True(s.ScaleRetryTimer.IsEnabled);
        Ui.Run(s.OnScaleRetryTick);
        Assert.True(s.ScaleRetryTimer.IsEnabled);
        Ui.Run(s.OnScaleRetryTick);
        Assert.False(s.ScaleRetryTimer.IsEnabled);
        Assert.Equal(3, _rdp.Updates.Count);
    }

    [Fact]
    public void Escala_rechazada_siempre_se_deja_estar_tras_nueve_intentos()
    {
        _c.RdpScalePercent = 200;
        var s = Create();
        _rdp.UpdateResult = () => throw new InvalidOperationException("no");
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(_rdp.RaiseLoginComplete);
        for (var i = 0; i < 9; i++)
        {
            Assert.True(s.ScaleRetryTimer.IsEnabled);
            Ui.Run(s.OnScaleRetryTick);
        }
        Assert.True(s.ScaleRetryTimer.IsEnabled);
        Ui.Run(s.OnScaleRetryTick);   // el decimo ya no pregunta
        Assert.False(s.ScaleRetryTimer.IsEnabled);
        Assert.Equal(10, _rdp.Updates.Count);
    }

    [Fact]
    public void El_reintento_de_la_escala_va_solo_con_el_temporizador()
    {
        _c.RdpScalePercent = 150;
        var s = Create();
        var calls = 0;
        _rdp.UpdateResult = () => ++calls > 1 ? true : throw new InvalidOperationException("todavia no");
        Ui.Run(() => { s.ScaleRetryTimer.Interval = TimeSpan.FromMilliseconds(10); });
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(_rdp.RaiseLoginComplete);
        Ui.WaitUntil(() => !s.ScaleRetryTimer.IsEnabled);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Escala_sin_conectar_o_en_todos_los_monitores_espera()
    {
        _c.RdpScalePercent = 150;
        var s = Create();
        Ui.Run(_rdp.RaiseLoginComplete);   // aun no conectado
        Assert.Empty(_rdp.Updates);
        Assert.True(s.ScaleRetryTimer.IsEnabled);

        _c.RdpMultiMonitor = true;
        Ui.Run(() => { _rdp.RaiseConnected(); _rdp.FullScreen = true; s.OnScaleRetryTick(); });
        Assert.Empty(_rdp.Updates);
        Assert.True(s.ScaleRetryTimer.IsEnabled);
    }

    [Fact]
    public void Escala_con_monitor_sin_tamaño_se_reintenta()
    {
        _c.RdpScalePercent = 150;
        _rdp.ScreenBounds = Rect.Empty;
        var s = Create();
        Ui.Run(() => { _rdp.RaiseConnected(); _rdp.FullScreen = true; _rdp.RaiseLoginComplete(); });
        Assert.Empty(_rdp.Updates);
        Assert.True(s.ScaleRetryTimer.IsEnabled);
    }

    [Fact]
    public void Control_viejo_sin_escala_no_insiste()
    {
        _c.RdpScalePercent = 150;
        var s = Create();
        _rdp.UpdateResult = () => false;
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(_rdp.RaiseLoginComplete);
        Assert.Single(_rdp.Updates);
        Assert.False(s.ScaleRetryTimer.IsEnabled);
    }

    [Fact]
    public void Desconectar_para_los_reintentos()
    {
        _c.RdpScalePercent = 150;
        var s = Create();
        Ui.Run(_rdp.RaiseLoginComplete);
        Assert.True(s.ScaleRetryTimer.IsEnabled);
        Ui.Run(() => _rdp.RaiseDisconnected(1));
        Assert.False(s.ScaleRetryTimer.IsEnabled);
    }

    [Theory]
    [InlineData(100, 1, "125 %")]
    [InlineData(125, 2, "175 %")]
    [InlineData(200, 1, "200 %")]
    [InlineData(100, -1, "100 %")]
    [InlineData(150, -1, "125 %")]
    [InlineData(130, 1, "100 %")]   // un valor que no esta en la lista vuelve al principio
    [InlineData(130, -1, "100 %")]
    public void Zoom_por_pasos(int start, int steps, string expected)
    {
        _c.RdpScalePercent = start;
        var s = Create();
        Assert.Equal(expected, Ui.Run(() => s.Zoom(steps)));
        Assert.Equal(int.Parse(expected[..3]), _c.RdpScalePercent);
    }

    [Fact]
    public void Zoom_conectado_se_pide_al_momento_y_sin_conectar_se_reintenta()
    {
        var s = Create();
        Ui.Run(() => s.Zoom(1));
        Assert.Empty(_rdp.Updates);
        Assert.True(s.ScaleRetryTimer.IsEnabled);

        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(() => s.Zoom(1));
        Assert.Equal(150, Assert.Single(_rdp.Updates).Scale);
        Assert.False(s.ScaleRetryTimer.IsEnabled);
    }

    // ------------------------------------------------------------------ El escritorio sigue al tamaño de la pestaña

    [Fact]
    public void Redimensionar_la_pestaña_conectada_pide_el_tamaño_nuevo_con_retardo()
    {
        _c.RdpScalePercent = 125;
        var (s, _, holder) = Shown(600, 400);
        Ui.Run(() => holder.Width = 610);   // sin conectar: no se pide nada
        Assert.False(s.ResizeTimer.IsEnabled);

        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(() => holder.Width = 800);
        Assert.True(s.ResizeTimer.IsEnabled);
        Ui.WaitUntil(() => !s.ResizeTimer.IsEnabled);
        var (w, h) = Physical(s.View, 800, 400);
        Assert.Equal([(w, h, 125)], _rdp.Updates);
    }

    [Fact]
    public void Redimensionar_en_pantalla_completa_no_hace_nada()
    {
        var (s, _, holder) = Shown(600, 400);
        Ui.Run(() => { _rdp.RaiseConnected(); _rdp.FullScreen = true; });
        Ui.Run(() => holder.Width = 800);
        Assert.False(s.ResizeTimer.IsEnabled);
        Ui.Run(s.OnResizeTick);
        Assert.Empty(_rdp.Updates);
    }

    [Fact]
    public void El_tamaño_no_se_pide_sin_ajustar_a_la_pestaña_ni_con_resolucion_fija_ni_sin_conectar()
    {
        var s = Create();
        Ui.Run(s.OnResizeTick);   // sin conectar
        Ui.Run(_rdp.RaiseConnected);
        _c.RdpSmartSizing = false;
        Ui.Run(s.OnResizeTick);
        _c.RdpSmartSizing = true;
        _c.RdpWidth = 1024;
        Ui.Run(s.OnResizeTick);
        Assert.Empty(_rdp.Updates);

        _c.RdpWidth = 0;
        Ui.Run(s.OnResizeTick);
        Assert.Equal([(200, 200, 100)], _rdp.Updates);
    }

    [Fact]
    public void Servidor_sin_resolucion_dinamica_se_queda_escalado()
    {
        var s = Create();
        _rdp.UpdateResult = () => throw new System.Runtime.InteropServices.COMException("E_FAIL");
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(s.OnResizeTick);
        Assert.Single(_rdp.Updates);
        Assert.False(s.ResizeTimer.IsEnabled);
    }

    // ------------------------------------------------------------------ Pantalla completa del control

    [Fact]
    public void Pantalla_completa_con_un_monitor_pide_la_resolucion_del_monitor()
    {
        _c.RdpScalePercent = 150;
        _rdp.ScreenBounds = new Rect(0, 0, 3840, 2160);
        var s = Create();
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(() => s.EnterFullScreen(0));
        Assert.True(_rdp.FullScreen);
        Assert.True(s.IsFullScreen);
        Assert.Equal([(3840, 2160, 150)], _rdp.Updates);
    }

    [Fact]
    public void Pantalla_completa_en_servidor_antiguo_se_queda_escalada()
    {
        var s = Create();
        _rdp.UpdateResult = () => throw new System.Runtime.InteropServices.COMException("E_FAIL");
        Ui.Run(() => s.EnterFullScreen(0));
        Assert.True(_rdp.FullScreen);
    }

    [Fact]
    public void Todos_los_monitores_con_uno_solo_es_pantalla_completa_normal()
    {
        _c.RdpMultiMonitor = true;
        var s = Create();
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(() => s.EnterFullScreen(0));
        Assert.DoesNotContain("disconnect", _rdp.Log);
        Assert.True(_rdp.FullScreen);
    }

    [Fact]
    public void Todos_los_monitores_sin_conectar_es_pantalla_completa_normal()
    {
        _c.RdpMultiMonitor = true;
        _rdp.AllScreens = [new Rect(0, 0, 1920, 1080), new Rect(1920, 0, 1920, 1080)];
        var s = Create();
        Ui.Run(() => s.EnterFullScreen(0));
        Assert.DoesNotContain("disconnect", _rdp.Log);
        Assert.True(_rdp.FullScreen);
    }

    [Theory]
    [InlineData(0, "size 1920x1080")]   // la del control
    [InlineData(2, "size 2560x1440")]   // la elegida en la conexion
    [InlineData(5, "size 1920x1080")]   // una que ya no existe: la del control
    public void Todos_los_monitores_reconecta_en_pantalla_completa_y_vuelve_a_la_pestaña(int chosen, string size)
    {
        _c.RdpMultiMonitor = true;
        _c.FullScreenScreen = chosen;
        _rdp.AllScreens = [new Rect(0, 0, 1920, 1080), new Rect(1920, 0, 2560, 1440)];
        var (s, _, _) = Shown(640, 400);
        var ended = new List<string?>();
        var left = 0;
        s.Ended += ended.Add;
        s.LeftFullScreen += () => left++;
        Ui.Run(_rdp.RaiseConnected);

        Ui.Run(() => s.EnterFullScreen(0));
        Assert.Equal(["disconnect"], _rdp.Log);
        Ui.Run(() => _rdp.RaiseDisconnected(1));   // no es un cierre: reconecta (en segundo plano)
        Assert.Empty(ended);
        Assert.Equal(["disconnect", "multimon True", size, "full True", "connect"], _rdp.Log);

        // Ya en todos los monitores: volver a pedirla solo la pone.
        Ui.Run(_rdp.RaiseConnected);
        _rdp.Log.Clear();
        Ui.Run(() => { _rdp.FullScreen = false; s.EnterFullScreen(0); });
        Assert.Equal(["full False", "full True"], _rdp.Log);

        // Salir de la pantalla completa: reconecta en la pestaña con un monitor.
        _rdp.Log.Clear();
        Ui.Run(_rdp.RaiseLeaveFullScreenMode);
        Assert.Equal(1, left);
        Assert.Equal(["disconnect"], _rdp.Log);
        Ui.Run(() => _rdp.RaiseDisconnected(1));
        var (w, h) = Physical(s.View, 640, 400);
        Assert.Equal(["disconnect", "multimon False", $"size {w}x{h}", "full False", "connect"], _rdp.Log);
        Assert.Empty(ended);

        // Y la siguiente desconexion ya es un cierre.
        Ui.Run(() => _rdp.RaiseDisconnected(1));
        Assert.Equal([null], ended);
    }

    [Fact]
    public void Vuelta_a_la_pestaña_con_resolucion_fija_y_desconexion_que_falla()
    {
        _c.RdpMultiMonitor = true;
        _c.RdpWidth = 1280;
        _c.RdpHeight = 800;
        _rdp.AllScreens = [new Rect(0, 0, 1920, 1080), new Rect(1920, 0, 1920, 1080)];
        var s = Create();
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(() => s.EnterFullScreen(0));
        Ui.Run(() => _rdp.RaiseDisconnected(1));
        Ui.Run(_rdp.RaiseConnected);
        _rdp.Log.Clear();
        _rdp.DisconnectError = new InvalidOperationException("ya cerrado");
        Ui.Run(_rdp.RaiseLeaveFullScreenMode);
        Ui.Run(() => _rdp.RaiseDisconnected(1));
        Assert.Equal(["disconnect", "multimon False", "size 1280x800", "full False", "connect"], _rdp.Log);
    }

    [Fact]
    public void Reconexion_que_falla_acaba_la_sesion_con_el_error()
    {
        _c.RdpMultiMonitor = true;
        _rdp.AllScreens = [new Rect(0, 0, 1920, 1080), new Rect(1920, 0, 1920, 1080)];
        var s = Create();
        var ended = new List<string?>();
        s.Ended += ended.Add;
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(() => s.EnterFullScreen(0));
        _rdp.ConnectError = new InvalidOperationException("no se puede conectar");
        Ui.Run(() => _rdp.RaiseDisconnected(1));
        Assert.Equal(["no se puede conectar"], ended);
    }

    [Fact]
    public void Salir_de_pantalla_completa_de_un_monitor_devuelve_el_tamaño_de_la_pestaña()
    {
        var s = Create();
        var left = 0;
        s.LeftFullScreen += () => left++;
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(_rdp.RaiseLeaveFullScreenMode);
        Assert.Equal(1, left);
        Assert.True(s.ResizeTimer.IsEnabled);
        Assert.DoesNotContain("disconnect", _rdp.Log);
    }

    [Fact]
    public void Salir_de_todos_los_monitores_al_cerrar_no_reconecta()
    {
        _c.RdpMultiMonitor = true;
        _rdp.AllScreens = [new Rect(0, 0, 1920, 1080), new Rect(1920, 0, 1920, 1080)];
        var s = Create();
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(() => s.EnterFullScreen(0));
        Ui.Run(() => _rdp.RaiseDisconnected(1));
        Ui.Run(_rdp.RaiseConnected);
        _rdp.Log.Clear();
        Ui.Run(_rdp.RaiseConfirmClose);
        Ui.Run(_rdp.RaiseLeaveFullScreenMode);
        Assert.Empty(_rdp.Log);
    }

    [Fact]
    public void Desconectar_cancela_la_reconexion_pendiente()
    {
        _c.RdpMultiMonitor = true;
        _rdp.AllScreens = [new Rect(0, 0, 1920, 1080), new Rect(1920, 0, 1920, 1080)];
        var s = Create();
        var ended = new List<string?>();
        s.Ended += ended.Add;
        Ui.Run(_rdp.RaiseConnected);
        Ui.Run(() => s.EnterFullScreen(0));
        s.Disconnect();   // ya desconectando: IsConnected sigue a true en el control
        Ui.Run(() => _rdp.RaiseDisconnected(1));
        Assert.Equal([null], ended);
        Assert.Equal(["disconnect", "disconnect"], _rdp.Log);
    }

    [Fact]
    public void Pantalla_completa_que_lanza_no_rompe()
    {
        var s = Create();
        _rdp.FullScreenThrows = true;
        Ui.Run(() => s.EnterFullScreen(0));
        Assert.False(s.IsFullScreen);
        s.LeaveFullScreen();
    }

    [Fact]
    public void Esta_en_pantalla_completa_solo_con_ventana()
    {
        var s = Create();
        _rdp.FullScreen = true;
        Assert.True(s.IsFullScreen);
        _rdp.IsHandleCreated = false;
        Assert.False(s.IsFullScreen);
    }

    [Fact]
    public void Salir_de_pantalla_completa_a_peticion()
    {
        var s = Create();
        s.LeaveFullScreen();
        Assert.Empty(_rdp.Log);   // no estaba: no se toca
        _rdp.FullScreen = true;
        s.LeaveFullScreen();
        Assert.False(_rdp.FullScreen);
    }

    [Fact]
    public void Desconectar_solo_si_esta_conectado_y_sin_fallar()
    {
        var s = Create();
        s.Disconnect();
        Assert.Empty(_rdp.Log);
        _rdp.IsConnected = true;
        _rdp.DisconnectError = new InvalidOperationException("ya cerrado");
        s.Disconnect();
        Assert.Equal(["disconnect"], _rdp.Log);
    }
}
