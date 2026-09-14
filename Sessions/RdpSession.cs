using System.Windows;
using System.Windows.Forms.Integration;
using SocRcManager.Models;

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
        _rdp.OnLeaveFullScreenMode += (_, _) => LeftFullScreen?.Invoke();
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

        var advanced = (MSTSCLib.IMsRdpClientAdvancedSettings8)_rdp.AdvancedSettings9;
        advanced.RDPPort = _connection.Port;
        advanced.ClearTextPassword = password;
        advanced.SmartSizing = _connection.RdpSmartSizing;
        advanced.EnableCredSspSupport = true;
        advanced.AuthenticationLevel = 0;   // no parar por el certificado del servidor: se avisa, no se bloquea
        advanced.RedirectClipboard = _connection.RdpClipboard;

        // Pantalla completa gestionada por el control, con la barra de conexion de mstsc arriba
        // (se oculta sola; al acercar el raton al borde superior vuelve, con minimizar/restaurar/cerrar).
        advanced.ContainerHandledFullScreen = 0;
        advanced.DisplayConnectionBar = true;
        advanced.PinConnectionBar = false;
        advanced.ConnectToServerConsole = false;
        _rdp.FullScreenTitle = _connection.Name;

        // Tamaño del escritorio: el de la pestaña ahora mismo (con SmartSizing luego se escala).
        var width = Math.Max(800, (int)_host.ActualWidth);
        var height = Math.Max(600, (int)_host.ActualHeight);
        _rdp.DesktopWidth = width;
        _rdp.DesktopHeight = height;
        _rdp.ColorDepth = 32;

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
