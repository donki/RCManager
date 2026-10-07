using System.Text.Json;
using System.Text.Json.Serialization;
using SocRcManager.Models;

namespace SocRcManager.Services;

/// <summary>
/// Exportar conexiones (todas o una rama del arbol) a un fichero <c>.rcm</c> para incorporarlas en
/// otro sOC Remote Connections Manager, y leer ese fichero al importar.
/// </summary>
/// <remarks>
/// <para><b>Las contraseñas</b> estan protegidas con DPAPI, que solo vale para este usuario en este
/// equipo. Por eso, al exportar, o se da una <b>frase</b> —y entonces el contenido entero, con las
/// contraseñas en claro dentro, va cifrado con ella (<see cref="Vault"/>, AES-256-GCM)— o se exporta
/// <b>sin contraseñas</b> (el resto va en claro, legible). Al importar se vuelven a proteger con
/// DPAPI para el usuario de alli.</para>
///
/// <para><b>Una rama</b> sale con su propio nombre como carpeta de arriba: exportar
/// «Clientes/Acme» deja «Acme/…» en el fichero, y al importarlo aparece «Acme» en la raiz del arbol
/// del otro equipo (luego se puede arrastrar donde se quiera).</para>
///
/// <para>Formato: <c>{"format":"socrcm","version":1,"encrypted":…,"data":…}</c>, con <c>data</c>
/// la cadena <c>enc1:…</c> o el objeto con las conexiones y las carpetas vacias. Al importar, cada
/// conexion recibe un id nuevo: importar dos veces no pisa nada.</para>
/// </remarks>
public static class ConnectionExport
{
    public const string Extension = ".rcm";
    private const string Format = "socrcm";
    private const int Version = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Lo que sale: las conexiones de la rama, con las carpetas ya relativas a ella.</summary>
    public sealed record Content(List<Connection> Connections, List<string> Folders);

    private sealed class Payload
    {
        public List<Connection> Connections { get; set; } = [];
        public List<string> Folders { get; set; } = [];
    }

    private sealed class FileData
    {
        public string Format { get; set; } = string.Empty;
        public int Version { get; set; }
        public bool Encrypted { get; set; }
        public JsonElement Data { get; set; }
    }

    /// <summary>
    /// Las conexiones y carpetas vacias de <paramref name="branch"/> («» = todo), con las rutas
    /// relativas al padre de la rama y copias de las conexiones (no se toca el almacen).
    /// </summary>
    public static Content Select(IEnumerable<Connection> connections, IEnumerable<string> emptyFolders, string branch)
    {
        branch = branch.Trim('/');
        var slash = branch.LastIndexOf('/');
        var parent = slash >= 0 ? branch[..slash] : string.Empty;
        string Relative(string folder) => parent.Length == 0 ? folder : folder[(parent.Length + 1)..];
        bool InBranch(string folder) => branch.Length == 0 || Store.IsInside(folder, branch);

        var list = connections.Where(c => InBranch(c.Folder)).Select(c =>
        {
            var copy = c.Clone();
            copy.Folder = Relative(c.Folder);
            return copy;
        }).ToList();
        var folders = emptyFolders.Where(InBranch).Select(Relative).ToList();
        // Una rama sin conexiones dentro sale igual, como carpeta vacia.
        if (branch.Length > 0 && list.Count == 0 && !folders.Contains(Relative(branch), StringComparer.OrdinalIgnoreCase))
            folders.Add(Relative(branch));
        return new Content(list, folders);
    }

    /// <summary>
    /// El fichero. Con <paramref name="passphrase"/>, todo cifrado y con las contraseñas; sin ella,
    /// en claro y sin ninguna contraseña.
    /// </summary>
    public static string Write(Content content, string? passphrase)
    {
        var withPasswords = !string.IsNullOrEmpty(passphrase);
        var payload = new Payload
        {
            Folders = content.Folders,
            Connections = content.Connections.Select(c =>
            {
                var copy = c.Clone();
                copy.PasswordProtected = withPasswords ? Secrets.Unprotect(c.PasswordProtected) : string.Empty;
                copy.RdpGatewayPasswordProtected = withPasswords ? Secrets.Unprotect(c.RdpGatewayPasswordProtected) : string.Empty;
                copy.LastConnectedAt = null;
                return copy;
            }).ToList(),
        };

        var data = withPasswords
            ? JsonSerializer.SerializeToElement(Vault.Encrypt(JsonSerializer.Serialize(payload, Json), passphrase!), Json)
            : JsonSerializer.SerializeToElement(payload, Json);
        return JsonSerializer.Serialize(new FileData { Format = Format, Version = Version, Encrypted = withPasswords, Data = data }, Json);
    }

    /// <summary>El fichero va cifrado: al importarlo hay que pedir la frase.</summary>
    public static bool IsEncrypted(string file) => Parse(file).Encrypted;

    /// <summary>
    /// Lee el fichero: conexiones con id nuevo y contraseñas protegidas con DPAPI para este usuario.
    /// Lanza <see cref="System.IO.InvalidDataException"/> si no es un fichero de exportacion y
    /// <see cref="System.Security.Cryptography.CryptographicException"/> si la frase no es la buena.
    /// </summary>
    public static Content Read(string file, string? passphrase)
    {
        var data = Parse(file);
        var json = data.Encrypted
            ? Vault.Decrypt(data.Data.GetString() ?? string.Empty, passphrase ?? string.Empty)
            : data.Data.GetRawText();
        var payload = JsonSerializer.Deserialize<Payload>(json, Json) ?? new Payload();
        foreach (var c in payload.Connections)
        {
            c.Id = Guid.NewGuid();
            c.Folder = c.Folder.Trim('/');
            c.PasswordProtected = Secrets.Protect(c.PasswordProtected);
            c.RdpGatewayPasswordProtected = Secrets.Protect(c.RdpGatewayPasswordProtected);
        }
        return new Content(payload.Connections, payload.Folders.Select(f => f.Trim('/')).Where(f => f.Length > 0).ToList());
    }

    private static FileData Parse(string file)
    {
        try
        {
            var data = JsonSerializer.Deserialize<FileData>(file, Json);
            if (data is { Format: Format, Version: >= 1 })
                return data;
        }
        catch (JsonException)
        {
        }
        throw new System.IO.InvalidDataException("No es un fichero de conexiones exportado de sOC Remote Connections Manager.");
    }
}
