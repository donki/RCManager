namespace SocRcManager.Sessions;

/// <summary>
/// Lo que <see cref="RdpSession"/> usa del control de Escritorio remoto de Windows (mstscax). El de
/// verdad es <see cref="AxRdpControl"/>; las pruebas ponen un doble y simulan sus eventos.
/// </summary>
internal interface IRdpControl
{
    /// <summary>El control de Windows Forms que se cuelga del WindowsFormsHost de la pestaña.</summary>
    System.Windows.Forms.Control Control { get; }

    bool IsHandleCreated { get; }

    /// <summary>Pantalla completa del propio control (lanza si aun no tiene ventana).</summary>
    bool FullScreen { get; set; }

    /// <summary>Conectado o conectando (la propiedad Connected del control, distinta de 0).</summary>
    bool IsConnected { get; }

    /// <summary>Resolucion con la que se conecta.</summary>
    void SetDesktopSize(int width, int height);

    /// <summary>Todos los monitores (solo se puede cambiar antes de conectar).</summary>
    void SetUseMultimon(bool on);

    /// <summary>Copia los ajustes al control (antes de conectar).</summary>
    void Apply(RdpSettings settings);

    /// <summary>
    /// Pide al servidor otro tamaño de escritorio y otra escala (RDP 8.1+). Devuelve false si el
    /// control es demasiado viejo para pedirlo; lanza si el servidor no lo acepta ahora.
    /// </summary>
    bool UpdateDisplay(int width, int height, int scalePercent);

    void Connect();
    void Disconnect();
    void Focus();

    /// <summary>Medidas del monitor donde esta el control.</summary>
    System.Drawing.Rectangle ScreenBounds { get; }

    /// <summary>Medidas de todos los monitores, en el orden de Windows.</summary>
    IReadOnlyList<System.Drawing.Rectangle> AllScreens { get; }

    event Action? Connected;
    event Action? LoginComplete;
    event Action? AutoReconnected;
    /// <summary>Desconectado, con el motivo de mstscax (discReason).</summary>
    event Action<int>? Disconnected;
    /// <summary>La barra de conexion pide salir de la pantalla completa.</summary>
    event Action? RequestLeaveFullScreen;
    /// <summary>La barra de conexion pide cerrar; el control lo permite siempre y luego desconecta.</summary>
    event Action? ConfirmClose;
    event Action? RequestContainerMinimize;
    event Action? LeaveFullScreenMode;
    event Action? HandleDestroyed;
}
