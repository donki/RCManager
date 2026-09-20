using System.IO;
using System.Xml.Linq;
using SocRcManager.Models;

namespace SocRcManager.Services;

/// <summary>
/// Importa un fichero <c>.rdm</c> exportado por Remote Desktop Manager (Devolutions).
/// </summary>
/// <remarks>
/// <para>Es XML: <c>RDMExport/Connections/Connection</c>. Cada entrada lleva <c>ConnectionType</c>
/// (<c>Group</c> para las carpetas, <c>RDPConfigured</c>, <c>SSHShell</c>…) y <c>Group</c>, la ruta
/// de carpetas con barra invertida (<c>Clientes\Acme</c>), que aqui pasa a ser la del arbol. Las
/// carpetas se crean aunque queden vacias, para que el arbol sea el mismo que en RDM.</para>
///
/// <para><b>Las contraseñas no se importan.</b> RDM las guarda cifradas con su propia clave
/// (<c>SafePassword</c>) y no hay forma honrada de leerlas; se piden al conectar y, si se quiere,
/// se guardan desde la ficha. Las entradas de ficheros de RDM (FTP nativo, FTPS, SFTP, SCP) pasan a
/// conexiones de ficheros; lo que no es nada de eso se cuenta y se deja fuera.</para>
/// </remarks>
public static class RdmImport
{
    public sealed record Result(List<Connection> Connections, List<string> Folders, int Skipped);

    public static Result Read(string path)
    {
        var doc = XDocument.Load(path);
        var entries = doc.Root?.Element("Connections")?.Elements("Connection") ?? [];

        var connections = new List<Connection>();
        var folders = new List<string>();
        var skipped = 0;

        foreach (var e in entries)
        {
            var type = (string?)e.Element("ConnectionType") ?? string.Empty;
            var folder = Folder((string?)e.Element("Group"));
            var name = ((string?)e.Element("Name") ?? string.Empty).Trim();

            switch (type)
            {
                case "Group":
                    if (folder.Length > 0)
                        folders.Add(folder);
                    break;

                case "RDPConfigured":
                {
                    var rdp = e.Element("RDP");
                    var (host, port) = HostPort((string?)e.Element("Url"), 3389);
                    connections.Add(new Connection
                    {
                        Name = name.Length > 0 ? name : host,
                        Kind = ConnectionKind.Rdp,
                        Folder = folder,
                        Host = host,
                        Port = port,
                        UserName = ((string?)rdp?.Element("UserName") ?? string.Empty).Trim(),
                        Domain = ((string?)rdp?.Element("Domain") ?? string.Empty).Trim(),
                        Notes = ((string?)e.Element("Description") ?? string.Empty).Trim(),
                    });
                    break;
                }

                case "SSHShell":
                {
                    var term = e.Element("Terminal");
                    var (host, port) = HostPort((string?)term?.Element("Host"), 22);
                    var explicitPort = (string?)term?.Element("HostPort");
                    if (int.TryParse(explicitPort, out var p) && p > 0)
                        port = p;
                    connections.Add(new Connection
                    {
                        Name = name.Length > 0 ? name : host,
                        Kind = ConnectionKind.Ssh,
                        Folder = folder,
                        Host = host,
                        Port = port,
                        UserName = ((string?)term?.Element("Username") ?? string.Empty).Trim(),
                        PrivateKeyPath = ((string?)term?.Element("PrivateKeyFileName") ?? string.Empty).Trim(),
                        Notes = ((string?)e.Element("Description") ?? string.Empty).Trim(),
                    });
                    break;
                }

                case "Ftp":
                case "FTP":
                case "FtpNative":
                case "FTPNative":
                case "Ftps":
                case "FTPS":
                case "Sftp":
                case "SFTP":
                case "Scp":
                case "SCP":
                {
                    // RDM guarda los datos de ficheros en un elemento con el nombre del tipo (Ftp, Sftp…)
                    // o en <Ftp>; se buscan los campos en el primero que los tenga.
                    var box = new[] { e.Element(type), e.Element("Ftp"), e.Element("FtpNative"), e.Element("Sftp"), e.Element("SFTP") }.FirstOrDefault(x => x is not null) ?? e;
                    string Pick(params string[] names) => names.Select(n => ((string?)box.Element(n) ?? (string?)e.Element(n) ?? string.Empty).Trim()).FirstOrDefault(v => v.Length > 0) ?? string.Empty;
                    var protocol = Pick("Protocol", "FtpType", "Type", "ConnectionMode").ToUpperInvariant();
                    var isSsh = type.StartsWith("S", StringComparison.OrdinalIgnoreCase) || protocol.Contains("SFTP") || protocol.Contains("SCP");
                    var ftps = type.Equals("Ftps", StringComparison.OrdinalIgnoreCase) || protocol.Contains("FTPS") || protocol.Contains("SSL") || protocol.Contains("TLS");
                    var (host, port) = HostPort(Pick("Host", "HostName", "Url"), isSsh ? 22 : (ftps && protocol.Contains("IMPLICIT") ? 990 : 21));
                    if (int.TryParse(Pick("Port", "HostPort"), out var fp) && fp > 0)
                        port = fp;
                    connections.Add(new Connection
                    {
                        Name = name.Length > 0 ? name : host,
                        Kind = isSsh ? ConnectionKind.Sftp : ConnectionKind.Ftp,
                        Folder = folder,
                        Host = host,
                        Port = port,
                        UserName = Pick("Username", "UserName", "User"),
                        PrivateKeyPath = isSsh ? Pick("PrivateKeyFileName", "PrivateKeyPath") : string.Empty,
                        UseScp = isSsh && (type.Equals("Scp", StringComparison.OrdinalIgnoreCase) || protocol.Contains("SCP")),
                        // 0 = FTP sin cifrar, 1 = FTPS explicito, 2 = FTPS implicito (ver Connection.FtpsMode).
                        FtpsMode = !isSsh && ftps ? (protocol.Contains("IMPLICIT") || port == 990 ? 2 : 1) : 0,
                        RemotePath = Pick("RemotePath", "InitialRemoteDirectory", "RemoteDirectory", "DefaultRemotePath") is { Length: > 0 } rp ? rp : "/",
                        LocalPath = Pick("LocalPath", "InitialLocalDirectory", "LocalDirectory"),
                        Notes = ((string?)e.Element("Description") ?? string.Empty).Trim(),
                    });
                    break;
                }

                default:
                    skipped++;
                    break;
            }
        }

        return new Result(connections.Where(c => c.Host.Length > 0).ToList(), folders, skipped);
    }

    private static string Folder(string? group) =>
        string.Join('/', (group ?? string.Empty).Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));

    private static (string Host, int Port) HostPort(string? url, int defaultPort)
    {
        var text = (url ?? string.Empty).Trim();
        var colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out var port) && port > 0)
            return (text[..colon], port);
        return (text, defaultPort);
    }
}
