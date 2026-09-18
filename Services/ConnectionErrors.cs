using System.Net.Sockets;
using SocRcManager.Localization;
using SocRcManager.Models;

namespace SocRcManager.Services;

/// <summary>
/// Traduce la excepcion de una conexion que no ha entrado (SSH, SFTP/SCP, FTP/FTPS) a una frase
/// que diga que ha pasado de verdad: nombre que no resuelve, puerto cerrado, sin respuesta,
/// credenciales, TLS… Los mensajes de las librerias son para programadores.
/// </summary>
public static class ConnectionErrors
{
    public static string Describe(Exception ex, Connection connection)
    {
        // La causa de fondo suele venir envuelta: se busca el socket o la autenticacion por dentro.
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            switch (e)
            {
                case SocketException s:
                    return s.SocketErrorCode switch
                    {
                        SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => Loc.Format("ErrHostNotFound", connection.Host),
                        SocketError.ConnectionRefused => Loc.Format("ErrRefused", connection.Host, connection.Port),
                        SocketError.TimedOut => Loc.Format("ErrTimeout", connection.Host, connection.Port),
                        SocketError.HostUnreachable or SocketError.NetworkUnreachable or SocketError.HostDown => Loc.Format("ErrUnreachable", connection.Host),
                        SocketError.ConnectionReset or SocketError.ConnectionAborted => Loc.Get("ErrReset"),
                        _ => Loc.Format("ErrNetwork", s.Message),
                    };
                case Renci.SshNet.Common.SshAuthenticationException:
                case FluentFTP.Exceptions.FtpAuthenticationException:
                    return Loc.Format("ErrAuth", connection.UserName);
                case Renci.SshNet.Common.SshOperationTimeoutException:
                case TimeoutException:
                    return Loc.Format("ErrTimeout", connection.Host, connection.Port);
                case Renci.SshNet.Common.SshConnectionException sc:
                    return Loc.Format("ErrSsh", sc.Message);
                case System.Security.Authentication.AuthenticationException tls:
                    return Loc.Format("ErrTls", tls.Message);
                case FluentFTP.Exceptions.FtpSecurityNotAvailableException:
                    return Loc.Get("ErrTlsNotOffered");
                case FluentFTP.Exceptions.FtpCommandException cmd:
                    return Loc.Format("ErrServer", cmd.CompletionCode, cmd.Message);
                case FluentFTP.Exceptions.FtpMissingSocketException:
                    return Loc.Get("ErrReset");
            }
        }
        return ex.Message.ReplaceLineEndings(" ");
    }
}
