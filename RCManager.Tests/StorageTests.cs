using System.Security.Cryptography;
using System.Text.Json;
using SocRcManager.Models;
using SocRcManager.Services;

namespace SocRcManager.Tests;

public class SecretsTests
{
    [Fact]
    public void Ida_y_vuelta_con_DPAPI_y_marca_de_version()
    {
        var p = Secrets.Protect("contraseña ñ €");
        Assert.StartsWith("dpapi1:", p);
        Assert.DoesNotContain("contraseña", p);
        Assert.Equal("contraseña ñ €", Secrets.Unprotect(p));
        Assert.NotEqual(p, Secrets.Protect("contraseña ñ €"));   // cada vez un cifrado distinto
    }

    [Fact]
    public void Vacio_no_se_cifra()
    {
        Assert.Equal("", Secrets.Protect(""));
        Assert.Equal("", Secrets.Unprotect(""));
        Assert.Equal("", Secrets.Unprotect(null!));
    }

    [Fact]
    public void Sin_marca_o_de_otro_usuario_da_vacio()
    {
        Assert.Equal("", Secrets.Unprotect("en claro"));
        Assert.Equal("", Secrets.Unprotect("dpapi1:" + Convert.ToBase64String(new byte[64])));   // no es de este usuario
    }
}

public class VaultTests
{
    [Fact]
    public void Ida_y_vuelta_con_la_frase()
    {
        var enc = Vault.Encrypt("{\"a\":1} ñ", "frase larga");
        Assert.True(Vault.IsEncrypted(enc));
        Assert.StartsWith("enc1:", enc);
        Assert.Equal("{\"a\":1} ñ", Vault.Decrypt(enc, "frase larga"));
        // sal(16) + nonce(12) + etiqueta(16) + cifrado
        Assert.Equal(44 + "{\"a\":1} ñ"u8.Length, Convert.FromBase64String(enc[5..]).Length);
    }

    [Fact]
    public void Sal_y_nonce_aleatorios()
    {
        Assert.NotEqual(Vault.Encrypt("x", "p"), Vault.Encrypt("x", "p"));
    }

    [Fact]
    public void Frase_equivocada_lanza_CryptographicException()
    {
        var enc = Vault.Encrypt("secreto", "buena");
        Assert.ThrowsAny<CryptographicException>(() => Vault.Decrypt(enc, "mala"));
    }

    [Fact]
    public void Manipulado_no_descifra()
    {
        var bytes = Convert.FromBase64String(Vault.Encrypt("secreto", "p")[5..]);
        bytes[^1] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => Vault.Decrypt("enc1:" + Convert.ToBase64String(bytes), "p"));
    }

    [Fact]
    public void Sin_marca_lanza()
    {
        Assert.False(Vault.IsEncrypted("{}"));
        Assert.Throws<CryptographicException>(() => Vault.Decrypt("{}", "p"));
    }

    [Fact]
    public void Texto_vacio()
    {
        Assert.Equal("", Vault.Decrypt(Vault.Encrypt("", "p"), "p"));
    }
}

/// <summary>El almacen en una carpeta temporal (nunca en %LOCALAPPDATA%).</summary>
public sealed class StoreTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _original = Store.Folder;

    public StoreTests() => Store.Folder = Path.Combine(_dir.Path, "datos");

    public void Dispose()
    {
        Store.Folder = _original;
        _dir.Dispose();
    }

    private static Connection C(string name, string folder = "", string password = "") =>
        new() { Name = name, Host = name + ".lan", Folder = folder, PasswordProtected = password.Length > 0 ? Secrets.Protect(password) : "" };

    [Fact]
    public void Location_esta_en_la_carpeta_de_datos()
    {
        Assert.Equal(Path.Combine(_dir.Path, "datos", "connections.json"), Store.Location);
    }

    [Fact]
    public void Load_sin_fichero_deja_todo_vacio()
    {
        var s = new Store();
        s.Load();
        Assert.Empty(s.Connections);
        Assert.Empty(s.EmptyFolders);
    }

    [Fact]
    public void Save_y_Load_ida_y_vuelta_con_bak_y_aviso()
    {
        var s = new Store();
        var saved = 0;
        s.Saved += () => saved++;
        s.Connections.Add(C("a", "X/Y", "pw"));
        s.Connections.Add(new Connection { Name = "ssh", Host = "h", Kind = ConnectionKind.Ssh, Port = 22 });
        s.EmptyFolders.AddRange(["Vacia", "x/y", "", "Vacia", "VACIA"]);
        s.Save();

        Assert.Equal(1, saved);
        Assert.True(File.Exists(Store.Location));
        Assert.False(File.Exists(Path.Combine(Store.Folder, "connections.bak")));   // la primera vez no hay anterior
        Assert.Equal(["Vacia"], s.EmptyFolders);   // sin vacias, repetidas ni las que ya tienen conexiones
        Assert.Contains("\"Ssh\"", File.ReadAllText(Store.Location));   // enums como texto: legible a mano
        Assert.DoesNotContain("pw\"", File.ReadAllText(Store.Location));

        s.Save();
        Assert.True(File.Exists(Path.Combine(Store.Folder, "connections.bak")));
        Assert.False(File.Exists(Store.Location + ".tmp"));

        var t = new Store();
        t.Load();
        Assert.Equal(2, t.Connections.Count);
        Assert.Equal("pw", Secrets.Unprotect(t.Connections[0].PasswordProtected));
        Assert.Equal(ConnectionKind.Ssh, t.Connections[1].Kind);
        Assert.Equal(["Vacia"], t.EmptyFolders);
        Assert.Equal(s.ModifiedAt, t.ModifiedAt);
    }

    [Fact]
    public void Load_de_un_JSON_roto_empieza_vacio_sin_romper()
    {
        var s = new Store();
        s.Connections.Add(C("a"));
        s.Save();
        File.WriteAllText(Store.Location, "{ roto");
        var t = new Store();
        t.Load();
        Assert.Empty(t.Connections);
        Assert.Empty(t.EmptyFolders);
    }

    [Fact]
    public void Load_de_un_fichero_sin_fecha_usa_la_del_fichero()
    {
        Directory.CreateDirectory(Store.Folder);
        File.WriteAllText(Store.Location, "{\"Connections\":[{\"Name\":\"a\",\"Kind\":\"Ftp\"}]}");
        var when = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Store.Location, when);
        var s = new Store();
        s.Load();
        Assert.Equal(ConnectionKind.Ftp, Assert.Single(s.Connections).Kind);
        Assert.Equal(when, s.ModifiedAt.UtcDateTime);
    }

    [Fact]
    public void Load_de_null_deja_vacio()
    {
        Directory.CreateDirectory(Store.Folder);
        File.WriteAllText(Store.Location, "null");
        var s = new Store();
        s.Load();
        Assert.Empty(s.Connections);
    }

    [Fact]
    public void Portable_lleva_las_contraseñas_en_claro_e_Import_las_vuelve_a_proteger()
    {
        var s = new Store();
        s.Connections.Add(C("a", "F", "secreta"));
        s.Connections.Add(C("b"));
        s.EmptyFolders.Add("Otra");
        s.Save();
        var json = s.ExportPortable();
        Assert.Contains("\"secreta\"", json);
        Assert.StartsWith("dpapi1:", s.Connections[0].PasswordProtected);   // el original no se toca
        Assert.Equal(s.ModifiedAt, Store.ModifiedAtOf(json));

        var t = new Store();
        t.ImportPortable(json);
        Assert.Equal(2, t.Connections.Count);
        Assert.Equal("secreta", Secrets.Unprotect(t.Connections[0].PasswordProtected));
        Assert.Equal("", t.Connections[1].PasswordProtected);
        Assert.Equal(["Otra"], t.EmptyFolders);
        Assert.Equal(s.ModifiedAt, t.ModifiedAt);
        Assert.True(File.Exists(Path.Combine(Store.Folder, "connections.bak")));

        var u = new Store();
        u.Load();
        Assert.Equal("secreta", Secrets.Unprotect(u.Connections[0].PasswordProtected));
    }

    [Fact]
    public void ImportPortable_de_null_deja_vacio()
    {
        var s = new Store();
        s.Connections.Add(C("a"));
        s.ImportPortable("null");
        Assert.Empty(s.Connections);
        Assert.True(File.Exists(Store.Location));
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("null")]
    [InlineData("{}")]
    public void ModifiedAtOf_de_algo_raro_es_el_minimo(string json)
    {
        Assert.Equal(json == "{}" ? default : DateTimeOffset.MinValue, Store.ModifiedAtOf(json));
    }

    [Fact]
    public void AllFolders_incluye_las_intermedias_sin_repetir_y_ordenadas()
    {
        var s = new Store();
        s.Connections.Add(C("a", "Clientes/Acme/Prod"));
        s.Connections.Add(C("b", "clientes/acme"));
        s.Connections.Add(C("c"));
        s.EmptyFolders.Add("Zeta//Sub/");
        Assert.Equal(["Clientes", "Clientes/Acme", "Clientes/Acme/Prod", "Zeta", "Zeta/Sub"], s.AllFolders());
    }

    [Fact]
    public void RenameFolder_mueve_lo_de_dentro_y_no_toca_parecidos()
    {
        var s = new Store();
        s.Connections.Add(C("a", "Clientes/Acme"));
        s.Connections.Add(C("b", "Clientes/Acme/Prod"));
        s.Connections.Add(C("c", "Clientes/AcmeBis"));
        s.Connections.Add(C("d", "Otros"));
        s.EmptyFolders.AddRange(["clientes/acme/Vacia", "Clientes/AcmeBis/V"]);
        s.RenameFolder("Clientes/Acme", "Antiguos/ACME");
        Assert.Equal(["Antiguos/ACME", "Antiguos/ACME/Prod", "Clientes/AcmeBis", "Otros"], s.Connections.Select(c => c.Folder));
        Assert.Equal(["Antiguos/ACME/Vacia", "Clientes/AcmeBis/V"], s.EmptyFolders);
    }

    [Fact]
    public void DeleteFolder_borra_lo_de_dentro_y_cuenta_las_conexiones()
    {
        var s = new Store();
        s.Connections.Add(C("a", "A"));
        s.Connections.Add(C("b", "A/B"));
        s.Connections.Add(C("c", "AB"));
        s.EmptyFolders.AddRange(["A/Vacia", "Otra"]);
        Assert.Equal(2, s.DeleteFolder("a"));
        Assert.Equal(["c"], s.Connections.Select(c => c.Name));
        Assert.Equal(["Otra"], s.EmptyFolders);
    }

    [Theory]
    [InlineData("A", "A", true)]
    [InlineData("a/b", "A", true)]
    [InlineData("AB", "A", false)]
    [InlineData("", "A", false)]
    [InlineData("A", "A/B", false)]
    public void IsInside(string folder, string path, bool expected)
    {
        Assert.Equal(expected, Store.IsInside(folder, path));
    }
}

public sealed class AppSettingsTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _original = AppSettings.FilePath;

    public AppSettingsTests() => AppSettings.FilePath = Path.Combine(_dir.Path, "sub", "settings.json");

    public void Dispose()
    {
        AppSettings.FilePath = _original;
        _dir.Dispose();
    }

    [Fact]
    public void Sin_fichero_da_los_valores_por_defecto()
    {
        var s = AppSettings.Load();
        Assert.Equal(StorageMode.Local, s.Storage);
        Assert.True(s.TrayOnMinimize);
        Assert.Equal(13, s.EditorFontSize);
        Assert.Null(s.ExpandedFolders);
        Assert.Null(s.Tokens);
        Assert.Equal("", s.Passphrase);
    }

    [Fact]
    public void Guardar_y_cargar_con_secretos_protegidos()
    {
        var id = Guid.NewGuid();
        var s = new AppSettings
        {
            Storage = StorageMode.OneDrive,
            AccountEmail = "a@b.c",
            ExpandedFolders = ["X", "X/Y"],
            SelectedConnectionId = id,
            TreeWidth = 321.5,
            EditorFontSize = 16,
            TrayOnMinimize = false,
            LastSyncAt = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero),
            Tokens = new OAuthTokens { AccessToken = "at", RefreshToken = "rt", Scope = "s1 s2" },
            Passphrase = "frase secreta",
        };
        s.Save();

        var text = File.ReadAllText(AppSettings.FilePath);
        Assert.Contains("\"OneDrive\"", text);
        Assert.DoesNotContain("frase secreta", text);
        Assert.DoesNotContain("\"rt\"", text);

        var t = AppSettings.Load();
        Assert.Equal(StorageMode.OneDrive, t.Storage);
        Assert.Equal("a@b.c", t.AccountEmail);
        Assert.Equal(["X", "X/Y"], t.ExpandedFolders!);
        Assert.Equal(id, t.SelectedConnectionId);
        Assert.Equal(321.5, t.TreeWidth);
        Assert.Equal(16, t.EditorFontSize);
        Assert.False(t.TrayOnMinimize);
        Assert.Equal(s.LastSyncAt, t.LastSyncAt);
        Assert.Equal("frase secreta", t.Passphrase);
        Assert.Equal("rt", t.Tokens!.RefreshToken);
        Assert.True(t.Tokens.Has("S2"));

        t.Tokens = null;
        Assert.Equal("", t.TokensProtected);
        Assert.Null(t.Tokens);
    }

    [Theory]
    [InlineData("{ roto")]
    [InlineData("null")]
    public void Fichero_roto_da_los_valores_por_defecto(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppSettings.FilePath)!);
        File.WriteAllText(AppSettings.FilePath, content);
        Assert.Equal(StorageMode.Local, AppSettings.Load().Storage);
    }

    [Fact]
    public void Current_es_la_instancia_viva()
    {
        var s = new AppSettings();
        AppSettings.Current = s;
        Assert.Same(s, AppSettings.Current);
        AppSettings.Current = null;
    }
}

public sealed class AppLogTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _original = AppLog.Folder;

    public AppLogTests() => AppLog.Folder = Path.Combine(_dir.Path, "log");

    public void Dispose()
    {
        AppLog.Folder = _original;
        _dir.Dispose();
    }

    [Fact]
    public void Escribe_con_fecha_y_crea_la_carpeta()
    {
        AppLog.Write("primera");
        AppLog.Write("segunda\nlinea");
        var lines = File.ReadAllText(AppLog.FilePath);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} primera", lines);
        Assert.Contains("segunda\nlinea", lines);
        Assert.Equal(Path.Combine(AppLog.Folder, "errors.log"), AppLog.FilePath);
    }

    [Fact]
    public void Pasado_de_1_MB_se_guarda_como_old_y_empieza_otro()
    {
        Directory.CreateDirectory(AppLog.Folder);
        File.WriteAllText(AppLog.FilePath, new string('x', 1024 * 1024 + 1));
        AppLog.Write("nuevo");
        Assert.True(new FileInfo(Path.Combine(AppLog.Folder, "errors.old.log")).Length > 1024 * 1024);
        Assert.EndsWith("nuevo" + Environment.NewLine, File.ReadAllText(AppLog.FilePath));
        Assert.True(new FileInfo(AppLog.FilePath).Length < 100);
    }

    [Fact]
    public void Si_no_puede_escribir_no_lanza()
    {
        // La «carpeta» es un fichero: CreateDirectory falla y el registro se calla.
        var file = _dir.File("soy-un-fichero", "x");
        AppLog.Folder = file;
        AppLog.Write("se pierde");
        Assert.Equal("x", File.ReadAllText(file));
    }
}
