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
