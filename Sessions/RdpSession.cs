using System.Windows;
using System.Windows.Forms.Integration;
using SocRcManager.Models;
using SocRcManager.Services;

namespace SocRcManager.Sessions;

/// <summary>
/// Una pestaña RDP: el control de Escritorio remoto de Windows (mstscax, el mismo de mstsc.exe)
/// alojado en la pestaña.
/// </summary>
/// <remarks>
/// <para>Es ActiveX, asi que va dentro de un <see cref="WindowsFormsHost"/>. Se usa la interfaz 9
/// (Windows 8.1 en adelante), que trae <c>SmartSizing</c> —el escritorio se escala al tamaño de la
/// pestaña— y el portapapeles. El control es del sistema: no se distribuye nada.</para>
///
/// <para>La contraseña va por <c>AdvancedSettings9.ClearTextPassword</c> justo antes de conectar y
/// no se guarda en ningun otro sitio.</para>
/// </remarks>
public sealed class RdpSession : ISession
{
    private readonly Connection _connection;
    private readonly AxMSTSCLib.AxMsRdpClient9NotSafeForScripting _rdp = new();
    private readonly WindowsFormsHost _host;

    public RdpSession(Connection connection)
    {
        _connection = connection;
        _host = new WindowsFormsHost { Child = _rdp };
        View = _host;

        _rdp.OnDisconnected += (_, e) => Ended?.Invoke(Describe(e.discReason));
        _rdp.OnConnected += (_, _) => TitleChanged?.Invoke(_connection.Name);

        // Pantalla completa del propio control: la barra superior de mstsc (se esconde sola y trae
        // el boton de restaurar) pide salir por este evento; hay que obedecer poniendo FullScreen
        // a false, el control no lo hace solo.
        _rdp.OnRequestLeaveFullScreen += (_, _) => _rdp.FullScreen = false;
        _rdp.OnLeaveFullScreenMode += (_, _) =>
        {
            LeftFullScreen?.Invoke();
            // De vuelta a la pestaña: el escritorio se habia puesto a la resolucion de la pantalla
            // y hay que devolverlo al tamaño de la pestaña, si no se queda grande y con barras.
            _resize.Stop();
            _resize.Start();
        };

        // El escritorio remoto sigue al tamaño de la pestaña (resolucion dinamica, RDP 8.1+): al
        // redimensionar la ventana se le pide al servidor el tamaño nuevo, en pixeles fisicos, y
        // se ve nitido en vez de escalado. Con retardo, para no pedirlo veinte veces por arrastre.
        _resize = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _resize.Tick += (_, _) => { _resize.Stop(); ApplyDisplaySize(); };
        _host.SizeChanged += (_, _) =>
        {
            if (_connected && !_rdp.FullScreen)
            {
                _resize.Stop();
                _resize.Start();
            }
        };
        _rdp.OnConnected += (_, _) => { _connected = true; };
        _rdp.OnDisconnected += (_, _) => { _connected = false; };
    }

    private readonly System.Windows.Threading.DispatcherTimer _resize;
    private bool _connected;

    /// <summary>Tamaño del control en pixeles fisicos (el DPI de la pantalla ya aplicado).</summary>
    private (int Width, int Height) PixelSize()
    {
        var source = PresentationSource.FromVisual(_host);
        var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        var w = (int)Math.Round(_host.ActualWidth * scaleX);
        var h = (int)Math.Round(_host.ActualHeight * scaleY);
        return (Math.Max(200, w), Math.Max(200, h));
    }

    /// <summary>Pide al servidor el tamaño de escritorio que cabe ahora en la pestaña.</summary>
    private void ApplyDisplaySize()
    {
        if (!_connected || _rdp.FullScreen || !_connection.RdpSmartSizing || _connection.RdpWidth > 0)
            return;
        var (w, h) = PixelSize();
        try
        {
            if (_rdp.GetOcx() is MSTSCLib.IMsRdpClient9 client9)
                client9.UpdateSessionDisplaySettings((uint)w, (uint)h, (uint)w, (uint)h, 0, 1, 1);
        }
        catch (Exception)
        {
            // Servidor sin resolucion dinamica: se queda el SmartSizing (escalado) de la conexion.
        }
    }

    public FrameworkElement View { get; }

    public string Title => _connection.Name;

    public event Action<string>? TitleChanged;
    public event Action<string?>? Ended;

    public Task ConnectAsync(string password)
    {
        _rdp.Server = _connection.Host;
        _rdp.UserName = _connection.UserName;
        if (_connection.Domain.Length > 0)
            _rdp.Domain = _connection.Domain;

        var c = _connection;
        var advanced = (MSTSCLib.IMsRdpClientAdvancedSettings8)_rdp.AdvancedSettings9;
        advanced.RDPPort = c.Port;
        advanced.ClearTextPassword = password;
        advanced.EnableCredSspSupport = true;

        // --- Pantalla ---
        advanced.SmartSizing = c.RdpSmartSizing;
        _rdp.ColorDepth = c.RdpColorDepth is 15 or 16 or 24 or 32 ? c.RdpColorDepth : 32;
        // Pantalla completa gestionada por el control, con la barra de conexion de mstsc arriba
        // (se oculta sola; al acercar el raton al borde superior vuelve, con minimizar/restaurar/cerrar).
        advanced.ContainerHandledFullScreen = 0;
        advanced.DisplayConnectionBar = c.RdpConnectionBar;
        advanced.PinConnectionBar = false;
        advanced.ConnectionBarShowMinimizeButton = true;
        advanced.ConnectionBarShowRestoreButton = true;
        _rdp.FullScreenTitle = c.Name;

        // Tamaño del escritorio: el fijo elegido o, si no, el de la pestaña ahora mismo en pixeles
        // fisicos. Con «ajustar a la pestaña» luego sigue a la ventana (resolucion dinamica) y,
        // si el servidor no lo admite, SmartSizing lo escala.
        var (width, height) = c.RdpWidth > 0 && c.RdpHeight > 0 ? (c.RdpWidth, c.RdpHeight) : PixelSize();
        _rdp.DesktopWidth = width;
        _rdp.DesktopHeight = height;

        // --- Recursos locales ---
        advanced.AudioRedirectionMode = (uint)Math.Clamp(c.RdpAudioMode, 0, 2);
        advanced.AudioCaptureRedirectionMode = c.RdpAudioCapture;
        ((MSTSCLib.IMsRdpClientSecuredSettings2)_rdp.SecuredSettings2).KeyboardHookMode = Math.Clamp(c.RdpKeyboardMode, 0, 2);
        advanced.EnableWindowsKey = 1;
        advanced.RedirectPrinters = c.RdpPrinters;
        // Portapapeles (texto, imagenes y ficheros: Ctrl+C / Ctrl+V entre los dos Exploradores) y
        // unidades del PC dentro del remoto (para copiar y mover con el Explorador).
        advanced.RedirectClipboard = c.RdpClipboard;
        advanced.RedirectDrives = c.RdpDrives;
        advanced.RedirectSmartCards = c.RdpSmartCards;
        advanced.RedirectPorts = c.RdpPorts;
        advanced.RedirectDevices = c.RdpDevices;
        if (_rdp.GetOcx() is MSTSCLib.IMsRdpClientNonScriptable5 nonScriptable)
        {
            nonScriptable.RedirectDynamicDrives = c.RdpDrives;     // tambien los USB que se enchufen durante la sesion
            nonScriptable.RedirectDynamicDevices = c.RdpDevices;
            nonScriptable.UseMultimon = c.RdpMultiMonitor;
            nonScriptable.WarnAboutClipboardRedirection = false;
            nonScriptable.WarnAboutPrinterRedirection = false;
            nonScriptable.WarnAboutSendingCredentials = false;
        }

        // --- Experiencia --- (los bits de TS_PERF_*: los que estan a 1 DESACTIVAN la cosa, salvo los dos ultimos)
        var flags = 0u;
        if (!c.RdpWallpaper) flags |= 0x01;            // TS_PERF_DISABLE_WALLPAPER
        if (!c.RdpWindowDrag) flags |= 0x02;           // TS_PERF_DISABLE_FULLWINDOWDRAG
        if (!c.RdpMenuAnimation) flags |= 0x04;        // TS_PERF_DISABLE_MENUANIMATIONS
        if (!c.RdpVisualStyles) flags |= 0x08;         // TS_PERF_DISABLE_THEMING
        if (c.RdpFontSmoothing) flags |= 0x80;         // TS_PERF_ENABLE_FONT_SMOOTHING
        if (c.RdpDesktopComposition) flags |= 0x100;   // TS_PERF_ENABLE_DESKTOP_COMPOSITION
        advanced.PerformanceFlags = (int)flags;
        advanced.BitmapPersistence = c.RdpBitmapCache ? 1 : 0;
        advanced.EnableAutoReconnect = c.RdpAutoReconnect;
        advanced.MaxReconnectAttempts = 20;

        // --- Avanzado ---
        // Ojo al orden de mstscax: 0 = sin comprobar, 1 = si falla NO conectar, 2 = si falla avisar
        // (el editor guarda 0 conectar / 1 avisar / 2 no conectar, como el dialogo de mstsc).
        advanced.AuthenticationLevel = c.RdpAuthLevel switch { 1 => 2u, 2 => 1u, _ => 0u };
        advanced.ConnectToAdministerServer = c.RdpAdminSession;
        advanced.ConnectToServerConsole = false;

        var gateway = (MSTSCLib.IMsRdpClientTransportSettings2)_rdp.TransportSettings2;
        if (c.RdpGatewayMode != 0 && c.RdpGatewayHost.Length > 0)
        {
            gateway.GatewayHostname = c.RdpGatewayHost;
            gateway.GatewayUsageMethod = c.RdpGatewayMode == 1 ? 1u : 2u;   // 1 = siempre, 2 = detectar (no para direcciones locales)
            gateway.GatewayProfileUsageMethod = 1;                            // ajustes explicitos, no los del sistema
            gateway.GatewayCredsSource = 0;                                   // usuario y contraseña
            gateway.GatewayUserSelectedCredsSource = 0;
            gateway.GatewayCredSharing = c.RdpGatewaySameCredentials ? 1u : 0u;
            if (!c.RdpGatewaySameCredentials)
            {
                gateway.GatewayUsername = c.RdpGatewayUserName;
                gateway.GatewayDomain = c.RdpGatewayDomain;
                gateway.GatewayPassword = Secrets.Unprotect(c.RdpGatewayPasswordProtected);
            }
        }
        else
        {
            gateway.GatewayUsageMethod = 0;
        }

        _rdp.Connect();
        return Task.CompletedTask;
    }

    public void Focus() => _rdp.Focus();

    public bool HasNativeFullScreen => true;

    public event Action? LeftFullScreen;

    /// <summary>
    /// A toda la pantalla con el control de Windows. Ademas se le pide al servidor que cambie la
    /// resolucion del escritorio a la de la pantalla (RDP 8.1+): sin eso se escalaria la
    /// resolucion con la que se conecto, y saldria borroso.
    /// </summary>
    public void EnterFullScreen()
    {
        try
        {
            _rdp.FullScreen = true;
            var screen = System.Windows.Forms.Screen.FromControl(_rdp).Bounds;
            if (_rdp.GetOcx() is MSTSCLib.IMsRdpClient9 client9)
                client9.UpdateSessionDisplaySettings((uint)screen.Width, (uint)screen.Height, (uint)screen.Width, (uint)screen.Height, 0, 1, 1);
        }
        catch (Exception)
        {
            // Servidores antiguos no admiten el cambio de resolucion en caliente: se queda escalado.
        }
    }

    public void Disconnect()
    {
        try
        {
            if (_rdp.Connected != 0)
                _rdp.Disconnect();
        }
        catch (Exception)
        {
            // Ya estaba cerrado.
        }
    }

    private static string? Describe(int reason) => reason switch
    {
        1 or 2 or 3 => null,                     // cierre local o pedido por el usuario
        260 => Localization.Loc.Get("RdpDnsError"),
        516 => Localization.Loc.Get("RdpNoConnection"),
        2308 => Localization.Loc.Get("RdpSocketClosed"),
        2825 => Localization.Loc.Get("RdpAuthFailed"),
        _ => Localization.Loc.Format("RdpDisconnected", reason),
    };
}
