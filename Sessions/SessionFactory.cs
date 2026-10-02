using SocRcManager.Models;

namespace SocRcManager.Sessions;

/// <summary>
/// Crea la sesion de una conexion segun su tipo. La ventana principal pasa por aqui para que las
/// pruebas puedan abrir pestañas con una sesion de mentira (sin servidores de verdad).
/// </summary>
internal static class SessionFactory
{
    internal static Func<Connection, ISession> Create { get; set; } = ForKind;

    /// <summary>La sesion de verdad: SSH, ficheros (SFTP/SCP o FTP/FTPS) o escritorio remoto.</summary>
    internal static ISession ForKind(Connection connection) => connection.Kind switch
    {
        ConnectionKind.Ssh => new SshSession(connection),
        ConnectionKind.Sftp or ConnectionKind.Ftp => new FileSession(connection),
        _ => new RdpSession(connection),
    };
}
