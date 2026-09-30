namespace SocRcManager.Services;

/// <summary>«servidor:puerto» en sus dos partes, como lo escriben mstsc y los gestores de conexiones.</summary>
public static class HostAddress
{
    /// <summary>
    /// Separa el puerto si viene («srv:3390», «[fe80::1]:3390»); si no, el de por defecto. Una IPv6
    /// sin corchetes («fe80::1») es toda servidor: sus «:» no son un puerto.
    /// </summary>
    public static (string Host, int Port) Split(string? address, int defaultPort)
    {
        var text = (address ?? string.Empty).Trim();
        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            if (close > 0 && close + 1 < text.Length && text[close + 1] == ':' && int.TryParse(text[(close + 2)..], out var port6) && port6 > 0)
                return (text[..(close + 1)], port6);
            return (text, defaultPort);
        }

        var colon = text.LastIndexOf(':');
        if (colon > 0 && text.IndexOf(':') == colon && int.TryParse(text[(colon + 1)..], out var port) && port > 0)
            return (text[..colon], port);
        return (text, defaultPort);
    }
}
