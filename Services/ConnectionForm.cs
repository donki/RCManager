using SocRcManager.Models;

namespace SocRcManager.Services;

/// <summary>
/// Las reglas del editor de conexiones (<see cref="ConnectionWindow"/>) sin la ventana: que posicion
/// de cada lista corresponde a que valor, el puerto por defecto, que se ve segun el tipo y como se
/// leen los numeros que escribe el usuario.
/// </summary>
public static class ConnectionForm
{
    /// <summary>
    /// Tamaños de la lista de Pantalla, en el mismo orden que sus elementos (0 = ajustar a la
    /// ventana). La lista lleva uno mas al final: «a medida» (<see cref="CustomSizeIndex"/>).
    /// </summary>
    public static readonly IReadOnlyList<(int W, int H)> Sizes =
    [
        (0, 0), (1024, 768), (1280, 800), (1366, 768), (1600, 900), (1920, 1080), (1920, 1200), (2560, 1440),
    ];

    /// <summary>Posicion de «a medida» en la lista de tamaños (la ultima).</summary>
    public static int CustomSizeIndex => Sizes.Count;

    /// <summary>Tamaño minimo (ancho y alto) de una pantalla a medida.</summary>
    public const int MinCustomSize = 200;

    /// <summary>Lo que hay en la casilla del puerto cuando es el de algun tipo (y se puede cambiar solo).</summary>
    private static readonly string[] DefaultPorts = ["3389", "22", "21", "990"];

    public static int KindIndex(ConnectionKind kind) => kind switch
    {
        ConnectionKind.Ssh => 1,
        ConnectionKind.Sftp => 2,
        ConnectionKind.Ftp => 3,
        _ => 0,
    };

    public static ConnectionKind KindAt(int index) => index switch
    {
        1 => ConnectionKind.Ssh,
        2 => ConnectionKind.Sftp,
        3 => ConnectionKind.Ftp,
        _ => ConnectionKind.Rdp,
    };

    /// <summary>Puerto por defecto del tipo (FTPS implicito, el 990).</summary>
    public static int DefaultPort(ConnectionKind kind, int ftpsMode) => new Connection { Kind = kind, FtpsMode = ftpsMode }.DefaultPort;

    /// <summary>
    /// Si el puerto escrito sigue al tipo: vacio o el de algun tipo (el usuario no ha puesto uno
    /// suyo). Entonces, al cambiar de tipo o de FTPS, se cambia por el nuevo por defecto.
    /// </summary>
    public static bool PortFollowsKind(string text) => text.Length == 0 || DefaultPorts.Contains(text);

    /// <summary>El puerto escrito, o el por defecto si no es un puerto valido (1-65535).</summary>
    public static int ParsePort(string text, int fallback) =>
        int.TryParse(text.Trim(), out var port) && port is > 0 and < 65536 ? port : fallback;

    /// <summary>Segundos entre mensajes para mantener viva la conexion (0 = nunca); si no se entiende, 30.</summary>
    public static int ParseKeepAlive(string text) => int.TryParse(text.Trim(), out var s) && s >= 0 ? s : 30;

    /// <summary>Segundos de espera de la conexion (al menos 5); si no, 20.</summary>
    public static int ParseTimeout(string text) => int.TryParse(text.Trim(), out var s) && s >= 5 ? s : 20;

    /// <summary>Posicion en la lista de tamaños del de la conexion; si no esta en la lista, «a medida».</summary>
    public static int SizeIndex(int width, int height, bool smartSizing)
    {
        if (smartSizing && width == 0)
            return 0;
        for (var i = 1; i < Sizes.Count; i++)
            if (Sizes[i] == (width, height))
                return i;
        return CustomSizeIndex;
    }

    /// <summary>Ancho y alto a medida, o null si no son numeros o son menores que <see cref="MinCustomSize"/>.</summary>
    public static (int W, int H)? ParseCustomSize(string width, string height) =>
        int.TryParse(width.Trim(), out var w) && int.TryParse(height.Trim(), out var h) && w >= MinCustomSize && h >= MinCustomSize
            ? (w, h)
            : null;

    public static int ColorIndex(int depth) => depth switch { 15 => 0, 16 => 1, 24 => 2, _ => 3 };

    public static int ColorDepthAt(int index) => index switch { 0 => 15, 1 => 16, 2 => 24, _ => 32 };

    /// <summary>Que partes del editor se ven con cada tipo de conexion.</summary>
    public sealed record Fields(bool Ssh, bool Domain, bool Ftp, bool Files, bool Scp, bool RdpTabs, bool Transfers);

    /// <summary>
    /// Solo RDP tiene dominio y las pestañas de mstsc; SSH y SFTP llevan clave privada; SFTP y FTP,
    /// las rutas y la pestaña de transferencias; FTP, el modo FTPS y sus opciones; SFTP, la casilla de SCP.
    /// </summary>
    public static Fields FieldsFor(ConnectionKind kind) => new(
        Ssh: kind is ConnectionKind.Ssh or ConnectionKind.Sftp,
        Domain: kind == ConnectionKind.Rdp,
        Ftp: kind == ConnectionKind.Ftp,
        Files: kind is ConnectionKind.Sftp or ConnectionKind.Ftp,
        Scp: kind == ConnectionKind.Sftp,
        RdpTabs: kind == ConnectionKind.Rdp,
        Transfers: kind is ConnectionKind.Sftp or ConnectionKind.Ftp);

    /// <summary>Que pestaña puede quedar elegida al cambiar de tipo.</summary>
    public enum Tab
    {
        General,
        Transfers,
        Rdp,
    }

    /// <summary>
    /// Si al cambiar de tipo hay que volver a General porque la pestaña elegida ya no se ve (las de
    /// RDP en una conexion de ficheros, la de transferencias en RDP, cualquiera en SSH).
    /// </summary>
    public static bool BackToGeneral(ConnectionKind kind, Tab selected)
    {
        var fields = FieldsFor(kind);
        return selected switch
        {
            Tab.General => false,
            Tab.Transfers => !fields.Transfers,
            _ => !fields.RdpTabs,
        };
    }
}
