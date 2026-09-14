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
/// se guardan desde la ficha. Lo que no es RDP ni SSH se cuenta y se deja fuera.</para>
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
