namespace SocRcManager.Models;

public enum ConnectionKind
{
    Rdp,
    Ssh,
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

    /// <summary>RDP: el escritorio se adapta al tamaño de la pestaña (si no, 1920x1080 con barras).</summary>
    public bool RdpSmartSizing { get; set; } = true;

    /// <summary>RDP: llevar el portapapeles.</summary>
    public bool RdpClipboard { get; set; } = true;

    /// <summary>
    /// RDP: las unidades de este PC se ven en el remoto («C en <i>equipo</i>» en Este equipo,
    /// <c>\tsclient\C</c>): asi se copian y mueven ficheros con el Explorador en los dos sentidos.
    /// (Copiar y pegar ficheros por el portapapeles va con <see cref="RdpClipboard"/>.)
    /// </summary>
    public bool RdpDrives { get; set; } = true;

    public DateTime? LastConnectedAt { get; set; }

    public int DefaultPort => Kind == ConnectionKind.Ssh ? 22 : 3389;

    public string Caption => Port == DefaultPort ? Host : $"{Host}:{Port}";

    public Connection Clone() => (Connection)MemberwiseClone();
}
