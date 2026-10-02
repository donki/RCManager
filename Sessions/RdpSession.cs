using System.Windows;
using System.Windows.Forms.Integration;
using System.Windows.Threading;
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
/// pestaña— y el portapapeles. El control es del sistema: no se distribuye nada. La sesion lo maneja
/// a traves de <see cref="IRdpControl"/> (<see cref="AxRdpControl"/>), y los ajustes de la conexion
/// salen de <see cref="RdpSettings.From"/>.</para>
///
/// <para>La contraseña va por <c>AdvancedSettings9.ClearTextPassword</c> justo antes de conectar y
/// no se guarda en ningun otro sitio.</para>
/// </remarks>
public sealed class RdpSession : ISession
{
    private readonly Connection _connection;
    private readonly IRdpControl _rdp;
    private readonly WindowsFormsHost _host;

    public RdpSession(Connection connection) : this(connection, new AxRdpControl())
    {
    }

    internal RdpSession(Connection connection, IRdpControl rdp)
    {
        _connection = connection;
        _rdp = rdp;
        _host = new WindowsFormsHost { Child = _rdp.Control };
        View = _host;

        _rdp.Disconnected += OnDisconnected;
        _rdp.Connected += () =>
        {
            TitleChanged?.Invoke(_connection.Name);
            _connected = true;
        };

        // Pantalla completa del propio control: la barra superior de mstsc (se esconde sola y trae
        // el boton de restaurar) pide salir por este evento; hay que obedecer poniendo FullScreen
        // a false, el control no lo hace solo.
        _rdp.RequestLeaveFullScreen += () => _rdp.FullScreen = false;
        // Los otros dos botones de esa barra tampoco hacen nada solos: cerrar pregunta al programa
        // si puede (el control lo permite y desconecta, y OnDisconnected cierra la pestaña), y
        // minimizar pide al contenedor que se minimice.
        _rdp.ConfirmClose += () => _closing = true;
        _rdp.RequestContainerMinimize += () => MinimizeRequested?.Invoke();
        _rdp.LeaveFullScreenMode += OnLeftFullScreen;

        // El escritorio remoto sigue al tamaño de la pestaña (resolucion dinamica, RDP 8.1+): al
        // redimensionar la ventana se le pide al servidor el tamaño nuevo, en pixeles fisicos, y
        // se ve nitido en vez de escalado. Con retardo, para no pedirlo veinte veces por arrastre.
        ResizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        ResizeTimer.Tick += (_, _) => OnResizeTick();
        // Reintentos de la escala tras entrar: un segundo entre uno y otro, y nueve como mucho.
        ScaleRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        ScaleRetryTimer.Tick += (_, _) => OnScaleRetryTick();
        _host.SizeChanged += (_, _) => OnHostResized();
        // Al sacar la pestaña a su ventana (o devolverla) el control se mueve sin destruirse: si algun
        // dia se destruyese con la sesion abierta, la sesion se habria cortado. Queda en el registro.
        _rdp.HandleDestroyed += () =>
        {
            if (_connected && !_closing)
                AppLog.Write($"RDP {_connection.Name}: el control se ha destruido con la sesion abierta");
        };
        // El zoom guardado (escala del escritorio) se le pide al servidor al entrar: el no lo
        // recuerda y, sin esto, la sesion se abre siempre al 100 %. Se pide **despues del inicio de
        // sesion**, no al conectar: mientras no hay sesion iniciada el servidor rechaza el cambio de
        // escala en silencio, que es lo que hacia que no se restaurase. Y aun asi tarda un poco en
        // aceptarlo, de ahi los reintentos.
        _rdp.LoginComplete += StartScaleRetries;
        _rdp.AutoReconnected += StartScaleRetries;
    }

    /// <summary>Retardo del redimensionado (las pruebas lanzan su Tick).</summary>
    internal DispatcherTimer ResizeTimer { get; }

    /// <summary>Reintentos de la escala (las pruebas lanzan su Tick).</summary>
    internal DispatcherTimer ScaleRetryTimer { get; }

    private int _scaleTries;
    private bool _connected;
    private enum Reconnect { None, FullScreenAllMonitors, Tab }
    private Reconnect _reconnect;
    private bool _multiMonitorSession;
    private bool _closing;

    private void OnDisconnected(int reason)
    {
        // Reconexion para entrar o salir de «todos los monitores»: no es un cierre. El control
        // solo reparte los monitores al conectar (y con UseMultimon se va solo a pantalla
        // completa), asi que la pestaña conecta sin multimonitor y la pantalla completa con el.
        if (_reconnect != Reconnect.None)
        {
            var toFullScreen = _reconnect == Reconnect.FullScreenAllMonitors;
            _reconnect = Reconnect.None;
            _host.Dispatcher.BeginInvoke(() => ReconnectNow(toFullScreen), DispatcherPriority.Background);
        }
        else
        {
            Ended?.Invoke(ConnectionErrors.DescribeRdpDisconnect(reason));
        }
        _connected = false;
        ScaleRetryTimer.Stop();
    }

    private void ReconnectNow(bool toFullScreen)
    {
        try
        {
            _rdp.SetUseMultimon(toFullScreen);
            var (w, h) = toFullScreen ? (FullScreenBounds().Width, FullScreenBounds().Height)
                : _connection.RdpWidth > 0 && _connection.RdpHeight > 0 ? (_connection.RdpWidth, _connection.RdpHeight) : PixelSize();
            _rdp.SetDesktopSize(w, h);
            _rdp.FullScreen = toFullScreen;
            _multiMonitorSession = toFullScreen;
            _rdp.Connect();
        }
        catch (Exception ex)
        {
            Ended?.Invoke(ex.Message);
        }
    }

    private void OnLeftFullScreen()
    {
        LeftFullScreen?.Invoke();
        // De vuelta de todos los monitores: se reconecta en la pestaña con uno solo (UseMultimon
        // no se puede cambiar en caliente y el control se iria solo a pantalla completa).
        if (_multiMonitorSession && _connected && !_closing)
        {
            _multiMonitorSession = false;
            _reconnect = Reconnect.Tab;
            try { _rdp.Disconnect(); } catch (Exception) { }
            return;
        }
        // De vuelta a la pestaña: el escritorio se habia puesto a la resolucion de la pantalla
        // y hay que devolverlo al tamaño de la pestaña, si no se queda grande y con barras.
        ResizeTimer.Stop();
        ResizeTimer.Start();
    }

    private void OnHostResized()
    {
        if (_connected && !_rdp.FullScreen)
        {
            ResizeTimer.Stop();
            ResizeTimer.Start();
        }
    }

    internal void OnResizeTick()
    {
        ResizeTimer.Stop();
        ApplyDisplaySize();
    }

    internal void OnScaleRetryTick()
    {
        if (++_scaleTries > 9 || ApplyScale())
            ScaleRetryTimer.Stop();
    }

    /// <summary>Tamaño del control en pixeles fisicos (el DPI de la pantalla ya aplicado).</summary>
    private (int Width, int Height) PixelSize()
    {
        var m = PresentationSource.FromVisual(_host)?.CompositionTarget?.TransformToDevice;
        return RdpSettings.PixelSize(_host.ActualWidth, _host.ActualHeight, m?.M11 ?? 1.0, m?.M22 ?? 1.0);
    }

    /// <summary>Pide al servidor el tamaño de escritorio que cabe ahora en la pestaña.</summary>
    private void ApplyDisplaySize()
    {
        if (!_connected || _rdp.FullScreen)
            return;
        if (!_connection.RdpSmartSizing || _connection.RdpWidth > 0)
            return;
        var (w, h) = PixelSize();
        try
        {
            _rdp.UpdateDisplay(w, h, _connection.RdpScalePercent);
        }
        catch (Exception)
        {
            // Servidor sin resolucion dinamica: se queda el SmartSizing (escalado) de la conexion.
        }
    }

    /// <summary>Pantalla de la pantalla completa: la elegida en la conexion o la que tiene el control.</summary>
    private System.Drawing.Rectangle FullScreenBounds()
    {
        var screens = _rdp.AllScreens;
        var chosen = _connection.FullScreenScreen;
        return chosen >= 1 && chosen <= screens.Count ? screens[chosen - 1] : _rdp.ScreenBounds;
    }

    /// <summary>Con todos los monitores en pantalla completa el control lleva la geometria: no se le pisa.</summary>
    private bool MultiMonitorNow => _connection.RdpMultiMonitor && _rdp.FullScreen;

    /// <summary>
    /// Empieza a pedir la escala guardada. Justo despues de entrar el servidor todavia puede decir
    /// que no, asi que se reintenta unas cuantas veces hasta que la acepta (o se deja estar).
    /// </summary>
    private void StartScaleRetries()
    {
        if (_connection.RdpScalePercent == 100)
            return;
        _scaleTries = 0;
        ScaleRetryTimer.Stop();
        if (ApplyScale())
            return;
        ScaleRetryTimer.Start();
    }

    /// <summary>
    /// Pide al servidor el escritorio con el tamaño que tiene y la escala guardada. Devuelve false
    /// si no se ha podido (el servidor no admite resolucion dinamica, o aun no esta listo).
    /// </summary>
    private bool ApplyScale()
    {
        if (!_connected || MultiMonitorNow)
            return false;
        try
        {
            var (w, h) = _rdp.FullScreen
                ? (_rdp.ScreenBounds.Width, _rdp.ScreenBounds.Height)
                : _connection.RdpWidth > 0 && _connection.RdpHeight > 0 ? (_connection.RdpWidth, _connection.RdpHeight) : PixelSize();
            if (w <= 0 || h <= 0)
                return false;   // la pestaña todavia no tiene tamaño: se reintenta
            // Control viejo (sin escala que pedir): tampoco se insiste.
            _rdp.UpdateDisplay(w, h, _connection.RdpScalePercent);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public FrameworkElement View { get; }

    public string Title => _connection.Name;

    public event Action<string>? TitleChanged;
    public event Action<string?>? Ended;

    public Task ConnectAsync(string password)
    {
        // Tamaño del escritorio: el fijo elegido o, si no, el de la pestaña ahora mismo en pixeles
        // fisicos. Con «ajustar a la pestaña» luego sigue a la ventana (resolucion dinamica) y,
        // si el servidor no lo admite, SmartSizing lo escala.
        var (width, height) = PixelSize();
        _rdp.Apply(RdpSettings.From(_connection, password, width, height));
        _rdp.Connect();
        return Task.CompletedTask;
    }

    public void Focus() => _rdp.Focus();

    public bool HasNativeFullScreen => true;

    /// <summary>Si esta en pantalla completa. El control lanza si aun no tiene ventana (pestaña nunca vista): entonces no.</summary>
    public bool IsFullScreen
    {
        get
        {
            try { return _rdp.IsHandleCreated && _rdp.FullScreen; }
            catch (Exception) { return false; }
        }
    }

    public void LeaveFullScreen()
    {
        try { if (_rdp.FullScreen) _rdp.FullScreen = false; } catch (Exception) { }
    }

    private static readonly int[] Scales = [100, 125, 150, 175, 200];

    public bool CanZoom => true;

    /// <summary>Escala del escritorio remoto (100..200 %): letra e iconos mas grandes sin perder nitidez (RDP 8.1+).</summary>
    public string Zoom(int steps)
    {
        var index = Math.Clamp(Array.IndexOf(Scales, _connection.RdpScalePercent) + steps, 0, Scales.Length - 1);
        _connection.RdpScalePercent = Scales[index];
        // Si el servidor no lo coge a la primera (acaba de entrar, esta ocupado), se insiste igual
        // que al abrir la sesion.
        _scaleTries = 0;
        ScaleRetryTimer.Stop();
        if (!ApplyScale())
            ScaleRetryTimer.Start();
        return $"{_connection.RdpScalePercent} %";
    }

    public event Action? LeftFullScreen;

    /// <summary>El boton de minimizar de la barra de conexion en pantalla completa.</summary>
    public event Action? MinimizeRequested;

    /// <summary>
    /// A toda la pantalla con el control de Windows. Ademas se le pide al servidor que cambie la
    /// resolucion del escritorio a la de la pantalla (RDP 8.1+): sin eso se escalaria la
    /// resolucion con la que se conecto, y saldria borroso.
    /// </summary>
    public void EnterFullScreen(int screen)
    {
        try
        {
            // Todos los monitores: el control solo los reparte al conectar, asi que si la sesion
            // entro con uno (en la pestaña) hay que reconectar ya en pantalla completa. Con un
            // solo monitor local no hay nada que repartir.
            if (_connection.RdpMultiMonitor && _rdp.AllScreens.Count > 1)
            {
                if (_multiMonitorSession)
                {
                    _rdp.FullScreen = true;
                    return;
                }
                if (_connected)
                {
                    _reconnect = Reconnect.FullScreenAllMonitors;
                    _rdp.Disconnect();
                    return;
                }
            }
            // El control se pone a pantalla completa en el monitor donde esta: si se pidio otro, la
            // ventana se ha movido antes (MainWindow); aqui se usa el monitor del control.
            _rdp.FullScreen = true;
            var bounds = _rdp.ScreenBounds;
            _rdp.UpdateDisplay(bounds.Width, bounds.Height, _connection.RdpScalePercent);
        }
        catch (Exception)
        {
            // Servidores antiguos no admiten el cambio de resolucion en caliente: se queda escalado.
        }
    }

    public void Disconnect()
    {
        _closing = true;
        _reconnect = Reconnect.None;
        try
        {
            if (_rdp.IsConnected)
                _rdp.Disconnect();
        }
        catch (Exception)
        {
            // Ya estaba cerrado.
        }
    }
}
