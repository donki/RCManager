using MSTSCLib;

namespace SocRcManager.Sessions;

/// <summary>
/// El control de Escritorio remoto de Windows (mstscax, interfaz 9: Windows 8.1 en adelante) detras
/// de <see cref="IRdpControl"/>. Solo copia valores y pasa eventos: lo que se decide esta en
/// <see cref="RdpSession"/> y en <see cref="RdpSettings"/>.
/// </summary>
internal sealed class AxRdpControl : IRdpControl
{
    private readonly AxMSTSCLib.AxMsRdpClient9NotSafeForScripting _ax = new();

    public AxRdpControl()
    {
        _ax.OnConnected += (_, _) => Connected?.Invoke();
        _ax.OnLoginComplete += (_, _) => LoginComplete?.Invoke();
        _ax.OnAutoReconnected += (_, _) => AutoReconnected?.Invoke();
        _ax.OnDisconnected += (_, e) => Disconnected?.Invoke(e.discReason);
        _ax.OnRequestLeaveFullScreen += (_, _) => RequestLeaveFullScreen?.Invoke();
        _ax.OnConfirmClose += (_, e) => { e.pfAllowClose = true; ConfirmClose?.Invoke(); };
        _ax.OnRequestContainerMinimize += (_, _) => RequestContainerMinimize?.Invoke();
        _ax.OnLeaveFullScreenMode += (_, _) => LeaveFullScreenMode?.Invoke();
        _ax.HandleDestroyed += (_, _) => HandleDestroyed?.Invoke();
    }

    public System.Windows.Forms.Control Control => _ax;
    public bool IsHandleCreated => _ax.IsHandleCreated;
    public bool FullScreen { get => _ax.FullScreen; set => _ax.FullScreen = value; }
    public bool IsConnected => _ax.Connected != 0;

    public void SetDesktopSize(int width, int height)
    {
        _ax.DesktopWidth = width;
        _ax.DesktopHeight = height;
    }

    public void SetUseMultimon(bool on)
    {
        if (_ax.GetOcx() is IMsRdpClientNonScriptable5 ns)
            ns.UseMultimon = on;
    }

    public void Apply(RdpSettings s)
    {
        _ax.Server = s.Server;
        _ax.UserName = s.UserName;
        if (s.Domain is not null)
            _ax.Domain = s.Domain;

        var advanced = (IMsRdpClientAdvancedSettings8)_ax.AdvancedSettings9;
        advanced.RDPPort = s.Port;
        advanced.ClearTextPassword = s.Password;
        advanced.EnableCredSspSupport = true;

        // --- Pantalla ---
        advanced.SmartSizing = s.SmartSizing;
        _ax.ColorDepth = s.ColorDepth;
        // Pantalla completa gestionada por el control, con la barra de conexion de mstsc arriba
        // (se oculta sola; al acercar el raton al borde superior vuelve, con minimizar/restaurar/cerrar).
        advanced.ContainerHandledFullScreen = 0;
        advanced.DisplayConnectionBar = s.DisplayConnectionBar;
        advanced.PinConnectionBar = false;
        advanced.ConnectionBarShowMinimizeButton = true;
        advanced.ConnectionBarShowRestoreButton = true;
        _ax.FullScreenTitle = s.FullScreenTitle;
        SetDesktopSize(s.DesktopWidth, s.DesktopHeight);

        // --- Recursos locales ---
        advanced.AudioRedirectionMode = s.AudioRedirectionMode;
        advanced.AudioCaptureRedirectionMode = s.AudioCapture;
        ((IMsRdpClientSecuredSettings2)_ax.SecuredSettings2).KeyboardHookMode = s.KeyboardHookMode;
        advanced.EnableWindowsKey = 1;
        advanced.RedirectPrinters = s.RedirectPrinters;
        // Portapapeles (texto, imagenes y ficheros: Ctrl+C / Ctrl+V entre los dos Exploradores) y
        // unidades del PC dentro del remoto (para copiar y mover con el Explorador).
        advanced.RedirectClipboard = s.RedirectClipboard;
        advanced.RedirectDrives = s.RedirectDrives;
        advanced.RedirectSmartCards = s.RedirectSmartCards;
        advanced.RedirectPorts = s.RedirectPorts;
        advanced.RedirectDevices = s.RedirectDevices;
        if (_ax.GetOcx() is IMsRdpClientNonScriptable5 nonScriptable)
        {
            nonScriptable.RedirectDynamicDrives = s.RedirectDrives;     // tambien los USB que se enchufen durante la sesion
            nonScriptable.RedirectDynamicDevices = s.RedirectDevices;
            // Todos los monitores NO se pide aqui: con UseMultimon el control se va solo a pantalla
            // completa al conectar. Se pide al reconectar desde el boton de pantalla completa.
            nonScriptable.UseMultimon = false;
            nonScriptable.WarnAboutClipboardRedirection = false;
            nonScriptable.WarnAboutPrinterRedirection = false;
            nonScriptable.WarnAboutSendingCredentials = false;
        }

        // --- Experiencia ---
        advanced.PerformanceFlags = s.PerformanceFlags;
        advanced.BitmapPersistence = s.BitmapPersistence;
        advanced.EnableAutoReconnect = s.AutoReconnect;
        advanced.MaxReconnectAttempts = 20;

        // --- Avanzado ---
        advanced.AuthenticationLevel = s.AuthenticationLevel;
        advanced.ConnectToAdministerServer = s.AdminSession;
        advanced.ConnectToServerConsole = false;

        var gateway = (IMsRdpClientTransportSettings2)_ax.TransportSettings2;
        if (s.Gateway is { } g)
        {
            gateway.GatewayHostname = g.Host;
            gateway.GatewayUsageMethod = g.UsageMethod;
            gateway.GatewayProfileUsageMethod = 1;      // ajustes explicitos, no los del sistema
            gateway.GatewayCredsSource = 0;             // usuario y contraseña
            gateway.GatewayUserSelectedCredsSource = 0;
            gateway.GatewayCredSharing = g.CredSharing;
            if (g.CredSharing == 0)
            {
                gateway.GatewayUsername = g.UserName!;
                gateway.GatewayDomain = g.Domain!;
                gateway.GatewayPassword = g.Password!;
            }
        }
        else
        {
            gateway.GatewayUsageMethod = 0;
        }
    }

    public bool UpdateDisplay(int width, int height, int scalePercent)
    {
        if (_ax.GetOcx() is not IMsRdpClient9 client9)
            return false;
        client9.UpdateSessionDisplaySettings((uint)width, (uint)height, (uint)width, (uint)height, 0, (uint)scalePercent, 100);
        return true;
    }

    public void Connect() => _ax.Connect();
    public void Disconnect() => _ax.Disconnect();
    public void Focus() => _ax.Focus();

    public System.Drawing.Rectangle ScreenBounds => System.Windows.Forms.Screen.FromControl(_ax).Bounds;
    public IReadOnlyList<System.Drawing.Rectangle> AllScreens => [.. System.Windows.Forms.Screen.AllScreens.Select(s => s.Bounds)];

    public event Action? Connected;
    public event Action? LoginComplete;
    public event Action? AutoReconnected;
    public event Action<int>? Disconnected;
    public event Action? RequestLeaveFullScreen;
    public event Action? ConfirmClose;
    public event Action? RequestContainerMinimize;
    public event Action? LeaveFullScreenMode;
    public event Action? HandleDestroyed;
}
