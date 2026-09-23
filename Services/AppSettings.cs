using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SocRcManager.Services;

/// <summary>
/// Ajustes de la aplicacion: donde se guardan las conexiones y con que cuenta.
/// </summary>
/// <remarks>
/// <c>%LOCALAPPDATA%\sOCRCManager\settings.json</c>. Los tokens de la cuenta y la frase de cifrado
/// van protegidos con DPAPI (<see cref="Secrets"/>): legibles solo por este usuario en este equipo.
/// </remarks>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCRCManager", "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public StorageMode Storage { get; set; } = StorageMode.Local;

    /// <summary>Correo de la cuenta con la que se entro, solo para enseñarlo.</summary>
    public string AccountEmail { get; set; } = string.Empty;

    /// <summary>Tokens OAuth serializados y protegidos con DPAPI.</summary>
    public string TokensProtected { get; set; } = string.Empty;

    /// <summary>Frase de cifrado del fichero en la nube, protegida con DPAPI.</summary>
    public string PassphraseProtected { get; set; } = string.Empty;

    /// <summary>Cuando se sincronizo por ultima vez con exito.</summary>
    public DateTimeOffset? LastSyncAt { get; set; }

    // El arbol tal como se dejo al cerrar: carpetas abiertas (null = nunca guardado, todo abierto),
    // conexion seleccionada y ancho del panel.
    public List<string>? ExpandedFolders { get; set; }
    public Guid? SelectedConnectionId { get; set; }
    public double TreeWidth { get; set; }

    /// <summary>Letra del editor de texto integrado (Ctrl + y Ctrl -), comun a todas las conexiones.</summary>
    public double EditorFontSize { get; set; } = 13;

    /// <summary>
    /// Al minimizar, la ventana se esconde y queda el icono en el area de notificacion (junto al
    /// reloj). Apagado, se minimiza a la barra de tareas como cualquier ventana.
    /// </summary>
    public bool TrayOnMinimize { get; set; } = true;

    /// <summary>La instancia viva de la ventana principal, para quien no la tiene a mano (el editor).</summary>
    public static AppSettings? Current { get; set; }

    [JsonIgnore]
    public OAuthTokens? Tokens
    {
        get
        {
            var json = Secrets.Unprotect(TokensProtected);
            return json.Length == 0 ? null : JsonSerializer.Deserialize<OAuthTokens>(json);
        }
        set => TokensProtected = value is null ? string.Empty : Secrets.Protect(JsonSerializer.Serialize(value));
    }

    [JsonIgnore]
    public string Passphrase
    {
        get => Secrets.Unprotect(PassphraseProtected);
        set => PassphraseProtected = Secrets.Protect(value);
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch (Exception)
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
