namespace SocRcManager.Models;

public enum ConnectionKind
{
    Rdp,
    Ssh,

    /// <summary>Ficheros por SSH: se navega por SFTP; las transferencias por SFTP o, si se pide, por SCP.</summary>
    Sftp,

    /// <summary>Ficheros por FTP, o FTPS explicito/implicito.</summary>
    Ftp,
}

/// <summary>
/// Una conexion guardada: a donde, como y con quien.
/// </summary>
/// <remarks>
/// <para><see cref="Folder"/> es la ruta de carpetas separada por «/» («Clientes/Acme»): el arbol se
/// monta a partir de esas rutas y no hay que mantener una jerarquia aparte. Una carpeta vacia se
/// guarda como <see cref="Store.EmptyFolders"/>.</para>
///
/// <para>La contraseña va cifrada con DPAPI para el usuario de Windows (<see cref="Services.Secrets"/>):
/// el fichero solo la puede leer quien haya iniciado sesion en este equipo con esta cuenta. Fuera
/// de aqui no sale nada.</para>
/// </remarks>
public sealed class Connection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ConnectionKind Kind { get; set; } = ConnectionKind.Rdp;
    public string Folder { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 3389;
    public string UserName { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;

    /// <summary>Contraseña cifrada (<c>dpapi1:…</c>). Vacia si no se guarda.</summary>
    public string PasswordProtected { get; set; } = string.Empty;

    /// <summary>SSH: clave privada (ruta a un fichero OpenSSH/PEM). Vacia si se entra con contraseña.</summary>
    public string PrivateKeyPath { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    // ------------------------------------------------------------------ Ficheros (SFTP/SCP y FTP/FTPS)

    /// <summary>SFTP: transferir por SCP (el protocolo antiguo) en vez de por SFTP. El listado va siempre por SFTP.</summary>
    public bool UseScp { get; set; }

    /// <summary>FTP: 0 = sin cifrar, 1 = FTPS explicito (AUTH TLS, puerto 21), 2 = FTPS implicito (puerto 990).</summary>
    public int FtpsMode { get; set; }

    /// <summary>Directorio remoto con el que se abre el explorador (vacio = el que de el servidor).</summary>
    public string RemotePath { get; set; } = string.Empty;

    /// <summary>Directorio de este PC con el que se abre el explorador (vacio = el perfil del usuario).</summary>
    public string LocalPath { get; set; } = string.Empty;

    // --- Transferencias (SFTP/SCP y FTP/FTPS) ---

    /// <summary>Cuantos ficheros a la vez (cada uno con su conexion al servidor), 1..8.</summary>
    public int TransferParallel { get; set; } = 2;

    /// <summary>Si el fichero ya existe en el destino: 0 = preguntar, 1 = sobrescribir, 2 = saltar.</summary>
    public int TransferOnConflict { get; set; }

    /// <summary>Poner al fichero transferido la fecha de modificacion del original.</summary>
    public bool TransferPreserveTimes { get; set; } = true;

    /// <summary>Reintentos por fichero cuando una transferencia falla (0..5).</summary>
    public int TransferRetries { get; set; } = 1;

    /// <summary>Enseñar ficheros ocultos (los que empiezan por punto y, en local, los marcados como ocultos).</summary>
    public bool FilesShowHidden { get; set; }

    /// <summary>Segundos entre señales de «sigo aqui» para que el servidor o un cortafuegos no corte la conexion (0 = no mandar).</summary>
    public int FilesKeepAliveSeconds { get; set; } = 30;

    /// <summary>Segundos de espera al conectar y en cada operacion.</summary>
    public int FilesTimeoutSeconds { get; set; } = 20;

    /// <summary>FTP: modo pasivo (el cliente abre las conexiones de datos; lo normal detras de un router) o activo.</summary>
    public bool FtpPassive { get; set; } = true;

    /// <summary>FTP: nombres de fichero en UTF-8 (lo normal hoy) o en Latin-1 (servidores viejos).</summary>
    public bool FtpUtf8 { get; set; } = true;

    // --- Pantalla y zoom (todas las clases) ---

    /// <summary>En que pantalla ponerse a pantalla completa: 0 = la de la ventana, 1..n = esa pantalla.</summary>
    public int FullScreenScreen { get; set; }

    /// <summary>SSH y ficheros: tamaño de letra (zoom de la pestaña).</summary>
    public double FontSize { get; set; } = 14;

    /// <summary>RDP: escala del escritorio remoto en porcentaje (100, 125, 150, 175, 200), el zoom de la pestaña.</summary>
    public int RdpScalePercent { get; set; } = 100;

    // ------------------------------------------------------------------ RDP: las opciones del
    // cliente de Windows (mstsc), pestaña a pestaña. Los valores por defecto son los de mstsc,
    // salvo las unidades (aqui si, para mover ficheros) y la pantalla (ajustada a la pestaña).

    // --- Pantalla ---

    /// <summary>El escritorio remoto sigue al tamaño de la pestaña (resolucion dinamica; si el servidor no puede, escalado).</summary>
    public bool RdpSmartSizing { get; set; } = true;

    /// <summary>Tamaño fijo del escritorio cuando no se ajusta a la pestaña (0 = el de la pestaña al conectar).</summary>
    public int RdpWidth { get; set; }
    public int RdpHeight { get; set; }

    /// <summary>Bits por pixel: 15, 16, 24 o 32.</summary>
    public int RdpColorDepth { get; set; } = 32;

    /// <summary>Pantalla completa en todos los monitores.</summary>
    public bool RdpMultiMonitor { get; set; }

    /// <summary>La barra de conexion de arriba en pantalla completa.</summary>
    public bool RdpConnectionBar { get; set; } = true;

    // --- Recursos locales ---

    /// <summary>Audio remoto: 0 = reproducir en este PC, 1 = en el remoto, 2 = no reproducir.</summary>
    public int RdpAudioMode { get; set; }

    /// <summary>Grabar desde este PC (microfono) en el remoto.</summary>
    public bool RdpAudioCapture { get; set; }

    /// <summary>Teclas de Windows (Alt+Tab…): 0 = en este PC, 1 = en el remoto, 2 = en el remoto solo a pantalla completa.</summary>
    public int RdpKeyboardMode { get; set; } = 2;

    public bool RdpPrinters { get; set; } = true;

    /// <summary>Portapapeles: texto, imagenes y ficheros (Ctrl+C en un Explorador, Ctrl+V en el otro).</summary>
    public bool RdpClipboard { get; set; } = true;

    /// <summary>
    /// Las unidades de este PC se ven en el remoto («C en <i>equipo</i>» en Este equipo,
    /// <c>\\tsclient\C</c>): asi se copian y mueven ficheros con el Explorador en los dos sentidos.
    /// </summary>
    public bool RdpDrives { get; set; } = true;

    public bool RdpSmartCards { get; set; } = true;

    /// <summary>Puertos serie.</summary>
    public bool RdpPorts { get; set; }

    /// <summary>Otros dispositivos Plug and Play (camaras, reproductores…).</summary>
    public bool RdpDevices { get; set; }

    // --- Experiencia ---

    public bool RdpWallpaper { get; set; } = true;
    public bool RdpFontSmoothing { get; set; } = true;
    public bool RdpDesktopComposition { get; set; } = true;
    public bool RdpWindowDrag { get; set; } = true;
    public bool RdpMenuAnimation { get; set; } = true;
    public bool RdpVisualStyles { get; set; } = true;
    public bool RdpBitmapCache { get; set; } = true;
    public bool RdpAutoReconnect { get; set; } = true;

    // --- Avanzado ---

    /// <summary>Certificado del servidor: 0 = conectar sin avisar, 1 = avisar, 2 = no conectar.</summary>
    public int RdpAuthLevel { get; set; } = 1;

    /// <summary>Sesion de administracion (la consola, /admin).</summary>
    public bool RdpAdminSession { get; set; }

    /// <summary>Puerta de enlace de Escritorio remoto: 0 = no usar, 1 = usar siempre, 2 = no usarla para direcciones locales.</summary>
    public int RdpGatewayMode { get; set; }
    public string RdpGatewayHost { get; set; } = string.Empty;

    /// <summary>La puerta de enlace usa el mismo usuario y contraseña que el escritorio remoto.</summary>
    public bool RdpGatewaySameCredentials { get; set; } = true;
    public string RdpGatewayUserName { get; set; } = string.Empty;
    public string RdpGatewayDomain { get; set; } = string.Empty;

    /// <summary>Contraseña de la puerta de enlace, cifrada (<c>dpapi1:…</c>).</summary>
    public string RdpGatewayPasswordProtected { get; set; } = string.Empty;

    public DateTime? LastConnectedAt { get; set; }

    public int DefaultPort => Kind switch
    {
        ConnectionKind.Ssh or ConnectionKind.Sftp => 22,
        ConnectionKind.Ftp => FtpsMode == 2 ? 990 : 21,
        _ => 3389,
    };

    /// <summary>Va por SSH (terminal o ficheros): contraseña o clave privada.</summary>
    public bool IsSsh => Kind is ConnectionKind.Ssh or ConnectionKind.Sftp;

    /// <summary>Es un explorador de ficheros, no un escritorio ni un terminal.</summary>
    public bool IsFiles => Kind is ConnectionKind.Sftp or ConnectionKind.Ftp;

    public string Caption => Port == DefaultPort ? Host : $"{Host}:{Port}";

    public Connection Clone() => (Connection)MemberwiseClone();
}
