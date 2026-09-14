using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Connections.Models;

namespace Connections.Services;

/// <summary>
/// Las conexiones y las carpetas, en un JSON del perfil del usuario.
/// </summary>
/// <remarks>
/// <para><c>%LOCALAPPDATA%\sOCConnections\connections.json</c>. Es un fichero legible a proposito
/// —salvo las contraseñas, que van cifradas (<see cref="Secrets"/>)— para que se pueda copiar,
/// versionar o arreglar a mano. Se escribe entero en cada cambio, primero a un temporal y luego se
/// sustituye: un corte a media escritura no puede dejar el fichero a trozos.</para>
///
/// <para>Al guardar se hace una copia <c>connections.bak</c> del anterior: es la red de seguridad
/// contra un borrado de carpeta con veinte servidores dentro.</para>
/// </remarks>
public sealed class Store
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCConnections");

    private static readonly string FilePath = Path.Combine(Folder, "connections.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public List<Connection> Connections { get; private set; } = [];

    /// <summary>Carpetas sin ninguna conexion dentro, que si no se perderian al guardar.</summary>
    public List<string> EmptyFolders { get; private set; } = [];

    public static string Location => FilePath;

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return;

            var data = JsonSerializer.Deserialize<FileData>(File.ReadAllText(FilePath), Json);
            Connections = data?.Connections ?? [];
            EmptyFolders = data?.EmptyFolders ?? [];
        }
        catch (Exception)
        {
            // Un JSON roto no puede dejar la aplicacion sin arrancar: se empieza vacio y el fichero
            // sigue ahi (y su .bak) para recuperarlo a mano.
            Connections = [];
            EmptyFolders = [];
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);

        // Las carpetas con algo dentro ya no hace falta recordarlas aparte.
        var used = new HashSet<string>(Connections.Select(c => c.Folder).Where(f => f.Length > 0), StringComparer.OrdinalIgnoreCase);
        EmptyFolders = EmptyFolders.Where(f => f.Length > 0 && !used.Contains(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new FileData { Connections = Connections, EmptyFolders = EmptyFolders }, Json));
        if (File.Exists(FilePath))
            File.Copy(FilePath, FilePath[..^5] + ".bak", overwrite: true);
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>Todas las rutas de carpeta que existen, incluidas las intermedias, ordenadas.</summary>
    public IReadOnlyList<string> AllFolders()
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Connections.Select(c => c.Folder).Concat(EmptyFolders))
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 1; i <= parts.Length; i++)
                set.Add(string.Join('/', parts.Take(i)));
        }

        return set.ToList();
    }

    /// <summary>Renombra o mueve una carpeta y todo lo que cuelga de ella.</summary>
    public void RenameFolder(string oldPath, string newPath)
    {
        foreach (var c in Connections)
            c.Folder = Rebase(c.Folder, oldPath, newPath);
        EmptyFolders = EmptyFolders.Select(f => Rebase(f, oldPath, newPath)).ToList();
    }

    /// <summary>Borra una carpeta con todo lo de dentro. Devuelve cuantas conexiones se han ido.</summary>
    public int DeleteFolder(string path)
    {
        var removed = Connections.RemoveAll(c => IsInside(c.Folder, path));
        EmptyFolders.RemoveAll(f => IsInside(f, path));
        return removed;
    }

    public static bool IsInside(string folder, string path) =>
        string.Equals(folder, path, StringComparison.OrdinalIgnoreCase) ||
        folder.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase);

    private static string Rebase(string folder, string oldPath, string newPath) =>
        IsInside(folder, oldPath) ? newPath + folder[oldPath.Length..] : folder;

    private sealed class FileData
    {
        public List<Connection> Connections { get; set; } = [];
        public List<string> EmptyFolders { get; set; } = [];
    }
}
