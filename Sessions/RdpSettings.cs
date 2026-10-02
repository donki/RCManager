using SocRcManager.Models;
using SocRcManager.Services;

namespace SocRcManager.Sessions;

/// <summary>Puerta de enlace de Escritorio remoto (RD Gateway) tal como se le pasa al control.</summary>
/// <param name="UsageMethod">1 = siempre; 2 = detectar (no para direcciones locales).</param>
/// <param name="CredSharing">1 = las mismas credenciales que el escritorio; 0 = las suyas.</param>
/// <param name="UserName">Solo con credenciales propias (si no, null).</param>
public sealed record RdpGatewaySettings(string Host, uint UsageMethod, uint CredSharing, string? UserName, string? Domain, string? Password);

/// <summary>
/// Todo lo que se le pone al control RDP antes de conectar, calculado a partir de la conexion. Es
/// una funcion pura (<see cref="From"/>): el control solo copia los valores (<see cref="AxRdpControl"/>).
/// </summary>
public sealed record RdpSettings
{
    public required string Server { get; init; }
    public required string UserName { get; init; }
    /// <summary>Null si la conexion no lleva dominio (no se toca el del control).</summary>
    public string? Domain { get; init; }
    public int Port { get; init; }
    public required string Password { get; init; }

    // --- Pantalla ---
    public bool SmartSizing { get; init; }
    public int ColorDepth { get; init; }
    public bool DisplayConnectionBar { get; init; }
    public required string FullScreenTitle { get; init; }
    public int DesktopWidth { get; init; }
    public int DesktopHeight { get; init; }

    // --- Recursos locales ---
    public uint AudioRedirectionMode { get; init; }
    public bool AudioCapture { get; init; }
    public int KeyboardHookMode { get; init; }
    public bool RedirectPrinters { get; init; }
    public bool RedirectClipboard { get; init; }
    public bool RedirectDrives { get; init; }
    public bool RedirectSmartCards { get; init; }
    public bool RedirectPorts { get; init; }
    public bool RedirectDevices { get; init; }

    // --- Experiencia ---
    public int PerformanceFlags { get; init; }
    public int BitmapPersistence { get; init; }
    public bool AutoReconnect { get; init; }

    // --- Avanzado ---
    /// <summary>En el orden de mstscax: 0 = sin comprobar, 1 = si falla NO conectar, 2 = si falla avisar.</summary>
    public uint AuthenticationLevel { get; init; }
    public bool AdminSession { get; init; }
    /// <summary>Null = sin puerta de enlace.</summary>
    public RdpGatewaySettings? Gateway { get; init; }

    /// <summary>
    /// Los ajustes de la conexion para el control. <paramref name="tabWidth"/> y
    /// <paramref name="tabHeight"/> son el tamaño de la pestaña en pixeles fisicos: el escritorio
    /// se pide asi salvo que la conexion tenga una resolucion fija.
    /// </summary>
    public static RdpSettings From(Connection c, string password, int tabWidth, int tabHeight)
    {
        var (width, height) = c.RdpWidth > 0 && c.RdpHeight > 0 ? (c.RdpWidth, c.RdpHeight) : (tabWidth, tabHeight);

        // Los bits de TS_PERF_*: los que estan a 1 DESACTIVAN la cosa, salvo los dos ultimos.
        var flags = 0u;
        if (!c.RdpWallpaper) flags |= 0x01;            // TS_PERF_DISABLE_WALLPAPER
        if (!c.RdpWindowDrag) flags |= 0x02;           // TS_PERF_DISABLE_FULLWINDOWDRAG
        if (!c.RdpMenuAnimation) flags |= 0x04;        // TS_PERF_DISABLE_MENUANIMATIONS
        if (!c.RdpVisualStyles) flags |= 0x08;         // TS_PERF_DISABLE_THEMING
        if (c.RdpFontSmoothing) flags |= 0x80;         // TS_PERF_ENABLE_FONT_SMOOTHING
        if (c.RdpDesktopComposition) flags |= 0x100;   // TS_PERF_ENABLE_DESKTOP_COMPOSITION

        RdpGatewaySettings? gateway = null;
        if (c.RdpGatewayMode != 0 && c.RdpGatewayHost.Length > 0)
        {
            var same = c.RdpGatewaySameCredentials;
            gateway = new RdpGatewaySettings(
                c.RdpGatewayHost,
                c.RdpGatewayMode == 1 ? 1u : 2u,
                same ? 1u : 0u,
                same ? null : c.RdpGatewayUserName,
                same ? null : c.RdpGatewayDomain,
                same ? null : Secrets.Unprotect(c.RdpGatewayPasswordProtected));
        }

        return new RdpSettings
        {
            Server = c.Host,
            UserName = c.UserName,
            Domain = c.Domain.Length > 0 ? c.Domain : null,
            Port = c.Port,
            Password = password,
            SmartSizing = c.RdpSmartSizing,
            ColorDepth = c.RdpColorDepth is 15 or 16 or 24 or 32 ? c.RdpColorDepth : 32,
            DisplayConnectionBar = c.RdpConnectionBar,
            FullScreenTitle = c.Name,
            DesktopWidth = width,
            DesktopHeight = height,
            AudioRedirectionMode = (uint)Math.Clamp(c.RdpAudioMode, 0, 2),
            AudioCapture = c.RdpAudioCapture,
            KeyboardHookMode = Math.Clamp(c.RdpKeyboardMode, 0, 2),
            RedirectPrinters = c.RdpPrinters,
            RedirectClipboard = c.RdpClipboard,
            RedirectDrives = c.RdpDrives,
            RedirectSmartCards = c.RdpSmartCards,
            RedirectPorts = c.RdpPorts,
            RedirectDevices = c.RdpDevices,
            PerformanceFlags = (int)flags,
            BitmapPersistence = c.RdpBitmapCache ? 1 : 0,
            AutoReconnect = c.RdpAutoReconnect,
            // El editor guarda 0 conectar / 1 avisar / 2 no conectar, como el dialogo de mstsc;
            // mstscax los tiene al reves (1 = no conectar, 2 = avisar).
            AuthenticationLevel = c.RdpAuthLevel switch { 1 => 2u, 2 => 1u, _ => 0u },
            AdminSession = c.RdpAdminSession,
            Gateway = gateway,
        };
    }

    /// <summary>Tamaño en pixeles fisicos de un control de WPF (con la escala de la pantalla), 200 x 200 como poco.</summary>
    public static (int Width, int Height) PixelSize(double actualWidth, double actualHeight, double scaleX, double scaleY)
    {
        var w = (int)Math.Round(actualWidth * scaleX);
        var h = (int)Math.Round(actualHeight * scaleY);
        return (Math.Max(200, w), Math.Max(200, h));
    }
}
