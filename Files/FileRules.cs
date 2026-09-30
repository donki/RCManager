using System.IO;
using System.Text;

namespace SocRcManager.Files;

/// <summary>
/// Reglas del explorador de ficheros que no dependen de la interfaz: tamaños legibles, que se
/// esconde, que se abre en el editor, como se lee y se escribe un fichero de texto, y los recorridos
/// recursivos (borrar, planificar una transferencia, aplicar permisos).
/// </summary>
public static class FileRules
{
    /// <summary>Hasta aqui se edita dentro; mas grande, se abre fuera.</summary>
    public const long MaxEditableBytes = 4 * 1024 * 1024;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".conf", ".cfg", ".ini", ".env", ".json", ".yaml", ".yml", ".xml", ".toml", ".csv",
        ".sh", ".bash", ".zsh", ".ps1", ".bat", ".cmd", ".py", ".js", ".ts", ".css", ".html", ".htm", ".php", ".sql",
        ".c", ".h", ".cpp", ".hpp", ".cs", ".java", ".go", ".rs", ".rb", ".pl", ".lua", ".properties", ".service",
        ".gitignore", ".htaccess", ".crontab", ".dockerfile",
    };

    /// <summary>«512 B», «1,5 KB», «3 MB», «1,25 GB» (con la cultura actual).</summary>
    public static string SizeText(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB",
    };

    /// <summary>Oculto: empieza por punto y, en este PC, tambien lo marcado como oculto o de sistema.</summary>
    public static bool IsHidden(FileEntry entry, bool local)
    {
        if (entry.Name.StartsWith('.'))
            return true;
        if (!local)
            return false;
        try { return (File.GetAttributes(entry.FullPath) & (FileAttributes.Hidden | FileAttributes.System)) != 0; }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Se abre en el editor integrado: por la extension o, si no tiene (Makefile, README) o es un
    /// fichero de configuracion que empieza por punto (.bashrc, .profile), se intenta y el editor
    /// comprueba el contenido (sin bytes nulos).
    /// </summary>
    public static bool LooksLikeText(FileEntry entry)
    {
        if (entry.IsDirectory || entry.Size > MaxEditableBytes)
            return false;
        var ext = Path.GetExtension(entry.Name);
        if (ext.Length == 0)
            return !entry.Name.StartsWith('.') || entry.Name.Length > 1;   // Makefile, README…
        if (ext.Length == entry.Name.Length)
            return entry.Name.Length > 1;   // .bashrc, .profile, .vimrc: todo el nombre es la «extension»
        return TextExtensions.Contains(ext);
    }

    /// <summary>El texto de un fichero tal como llego.</summary>
    public sealed record DecodedText(string Content, Encoding Encoding, bool Crlf);

    /// <summary>
    /// UTF-8 (con o sin BOM) si el contenido es UTF-8 valido; si no, Latin-1, que no rompe nada. Los
    /// finales de linea quedan en \r\n para el TextBox y se recuerda si eran \r\n. Si hay bytes
    /// nulos en los primeros 8 KB no es texto: null.
    /// </summary>
    public static DecodedText? DecodeText(byte[] bytes)
    {
        if (bytes.Take(8192).Contains((byte)0))
            return null;

        Encoding encoding;
        string content;
        try
        {
            encoding = new UTF8Encoding(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, throwOnInvalidBytes: true);
            content = encoding.GetString(bytes.AsSpan(encoding.GetPreamble().Length == 3 && bytes.Length >= 3 && bytes[0] == 0xEF ? 3 : 0));
        }
        catch (DecoderFallbackException)
        {
            encoding = Encoding.Latin1;
            content = encoding.GetString(bytes);
        }
        var crlf = content.Contains("\r\n");
        // El TextBox trabaja con \r\n; se normaliza y al guardar se devuelve el final original.
        content = content.Replace("\r\n", "\n").Replace("\n", "\r\n");
        return new DecodedText(content, encoding, crlf);
    }

    /// <summary>Lo contrario: el texto del editor con la codificacion (y el BOM) y los finales de linea con los que llego.</summary>
    public static byte[] EncodeText(string text, Encoding encoding, bool crlf)
    {
        if (!crlf)
            text = text.Replace("\r\n", "\n");
        return encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
    }

    /// <summary>Borra un fichero, o un directorio con todo lo de dentro (los remotos no lo hacen solos).</summary>
    public static async Task DeleteRecursiveAsync(IFileSide side, FileEntry entry, CancellationToken cancellationToken)
    {
        if (!entry.IsDirectory)
        {
            await side.DeleteFileAsync(entry.FullPath, cancellationToken);
            return;
        }
        if (side.IsLocal)
        {
            await side.DeleteDirectoryAsync(entry.FullPath, cancellationToken);
            return;
        }
        foreach (var child in await side.ListAsync(entry.FullPath, cancellationToken))
            await DeleteRecursiveAsync(side, child, cancellationToken);
        await side.DeleteDirectoryAsync(entry.FullPath, cancellationToken);
    }

    /// <summary>Recorre lo que hay que transferir (directorios incluidos) y devuelve los bytes totales.</summary>
    public static async Task<long> PlanAsync(IFileSide from, IFileSide to, FileEntry entry, string targetDirectory, List<(FileEntry, string)> plan, CancellationToken token)
    {
        var target = to.Combine(targetDirectory, entry.Name);
        plan.Add((entry, target));
        if (!entry.IsDirectory)
            return entry.Size;
        long total = 0;
        foreach (var child in await from.ListAsync(entry.FullPath, token))
            total += await PlanAsync(from, to, child, target, plan, token);
        return total;
    }

    /// <summary>chmod/chown de una entrada y, si se pide, de todo lo que cuelga de ella.</summary>
    public static async Task ApplyPermissionsAsync(RemoteSide remote, FileEntry entry, PermissionChange change, CancellationToken cancellationToken)
    {
        if (change.ChangeMode && change.Mode is { } mode)
            await remote.Fs.ChangeModeAsync(entry.FullPath, mode, cancellationToken);
        if (change.ChangeOwner)
            await remote.Fs.ChangeOwnerAsync(entry.FullPath, change.Owner, change.Group, cancellationToken);
        if (change.Recursive && entry.IsDirectory)
            foreach (var child in await remote.ListAsync(entry.FullPath, cancellationToken))
                await ApplyPermissionsAsync(remote, child, change, cancellationToken);
    }
}
