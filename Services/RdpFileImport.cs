using System.IO;
using SocRcManager.Models;

namespace SocRcManager.Services;

/// <summary>
/// Importa ficheros <c>.rdp</c> del cliente de Escritorio remoto de Windows (mstsc): una conexion
/// por fichero, con el nombre del fichero y las mismas opciones que se eligieron en mstsc.
/// </summary>
/// <remarks>
/// El formato es una linea por opcion, <c>clave:tipo:valor</c> (<c>s</c> texto, <c>i</c> entero,
/// <c>b</c> binario). La contraseña (<c>password 51:b:…</c>) va cifrada con DPAPI del usuario que
/// la guardo; no se intenta leer: se pide al conectar. Las claves que no tienen equivalente aqui se
/// ignoran sin protestar, que mstsc escribe muchas.
/// </remarks>
public static class RdpFileImport
{
    public static Connection Read(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            var first = line.IndexOf(':');
            if (first <= 0)
                continue;
            var second = line.IndexOf(':', first + 1);
            if (second < 0)
                continue;
            values[line[..first].Trim()] = line[(second + 1)..];
        }

        string S(string key) => values.TryGetValue(key, out var v) ? v.Trim() : string.Empty;
        int I(string key, int fallback) => int.TryParse(S(key), out var v) ? v : fallback;
        bool B(string key, bool fallback) => values.ContainsKey(key) ? I(key, fallback ? 1 : 0) != 0 : fallback;

        var (host, port) = HostPort(S("full address"), I("server port", 3389));
        var c = new Connection
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Kind = ConnectionKind.Rdp,
            Host = host,
            Port = port,
            UserName = S("username"),
            Domain = S("domain"),
        };
        // Un «DOMINIO\usuario» o «usuario@dominio» en el usuario: se separa como hace mstsc.
        if (c.Domain.Length == 0 && c.UserName.Contains('\\'))
        {
            var parts = c.UserName.Split('\\', 2);
            c.Domain = parts[0];
            c.UserName = parts[1];
        }

        // --- Pantalla
        c.RdpSmartSizing = B("smart sizing", false) || B("dynamic resolution", false) || I("screen mode id", 1) == 2;
        if (!c.RdpSmartSizing)
        {
            c.RdpWidth = I("desktopwidth", 0);
            c.RdpHeight = I("desktopheight", 0);
        }
        var bpp = I("session bpp", 32);
        c.RdpColorDepth = bpp is 15 or 16 or 24 or 32 ? bpp : 32;
        c.RdpMultiMonitor = B("use multimon", false);
        c.RdpConnectionBar = B("displayconnectionbar", true);

        // --- Recursos locales
        c.RdpAudioMode = Math.Clamp(I("audiomode", 0), 0, 2);
        c.RdpAudioCapture = B("audiocapturemode", false);
        c.RdpKeyboardMode = Math.Clamp(I("keyboardhook", 2), 0, 2);
        c.RdpPrinters = B("redirectprinters", true);
        c.RdpClipboard = B("redirectclipboard", true);
        c.RdpSmartCards = B("redirectsmartcards", true);
        c.RdpPorts = B("redirectcomports", false);
        c.RdpDevices = S("devicestoredirect").Length > 0 || B("redirectdevices", false);
        c.RdpDrives = S("drivestoredirect").Length > 0 || B("redirectdrives", false);

        // --- Rendimiento
        c.RdpWallpaper = !B("disable wallpaper", false);
        c.RdpFontSmoothing = B("allow font smoothing", true);
        c.RdpDesktopComposition = B("allow desktop composition", true);
        c.RdpWindowDrag = !B("disable full window drag", false);
        c.RdpMenuAnimation = !B("disable menu anims", false);
        c.RdpVisualStyles = !B("disable themes", false);
        c.RdpBitmapCache = B("bitmapcachepersistenable", true);
        c.RdpAutoReconnect = B("autoreconnection enabled", true);

        // --- Avanzadas
        c.RdpAuthLevel = Math.Clamp(I("authentication level", 1), 0, 2);
        c.RdpAdminSession = B("administrative session", false);
        c.RdpGatewayHost = S("gatewayhostname");
        // gatewayusagemethod: 0 no usar, 1 siempre, 2 no en local, 4 detectar (se trata como «siempre»).
        c.RdpGatewayMode = c.RdpGatewayHost.Length == 0 ? 0 : I("gatewayusagemethod", 0) switch { 0 => 0, 2 => 2, _ => 1 };
        c.RdpGatewaySameCredentials = I("promptcredentialonce", 1) != 0;
        c.Notes = S("alternate full address") is { Length: > 0 } alt && !string.Equals(alt, S("full address"), StringComparison.OrdinalIgnoreCase)
            ? "alternate full address: " + alt
            : string.Empty;
        return c;
    }

    private static (string Host, int Port) HostPort(string address, int defaultPort)
    {
        var text = address.Trim();
        var colon = text.LastIndexOf(':');
        if (colon > 0 && !text.Contains(']') && int.TryParse(text[(colon + 1)..], out var port) && port > 0)
            return (text[..colon], port);
        return (text, defaultPort);
    }
}
