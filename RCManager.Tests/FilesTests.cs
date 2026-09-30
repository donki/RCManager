using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FluentFTP;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using SocRcManager.Files;
using SocRcManager.Localization;
using SocRcManager.Models;

namespace SocRcManager.Tests;

public class UnixModeTests
{
    [Theory]
    [InlineData(0b111_101_101, "755", "rwxr-xr-x")]
    [InlineData(0b110_100_100, "644", "rw-r--r--")]
    [InlineData(0, "000", "---------")]
    [InlineData(0b000_000_111, "007", "------rwx")]
    [InlineData(0b111_111_111, "777", "rwxrwxrwx")]
    [InlineData(0b100_010_001, "421", "r---w---x")]
    public void Octal_texto_y_casillas(int mode, string octal, string text)
    {
        Assert.Equal(octal, UnixMode.ToOctal(mode));
        Assert.Equal(text, UnixMode.ToText(mode));
        Assert.Equal(mode, UnixMode.ParseOctal(octal));
        var bits = UnixMode.ToBits(mode);
        Assert.Equal(9, bits.Length);
        Assert.Equal(text.Select(ch => ch != '-'), bits);
        Assert.Equal(mode, UnixMode.FromBits(bits));
        Assert.Equal(text, new FileEntry("f", "/f", false, 0, null, mode).ModeText);
    }

    [Theory]
    [InlineData("0755", 0b111_101_101)]    // cuatro cifras: cuentan las tres ultimas
    [InlineData("1777", 0b111_111_111)]
    [InlineData(" 644 ", 0b110_100_100)]
    public void ParseOctal_valido(string text, int mode)
    {
        Assert.Equal(mode, UnixMode.ParseOctal(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("75")]        // a medio escribir
    [InlineData("75575")]
    [InlineData("758")]       // 8 no es octal
    [InlineData("7a5")]
    [InlineData("-755")]
    public void ParseOctal_invalido(string text)
    {
        Assert.Null(UnixMode.ParseOctal(text));
    }

    [Fact]
    public void Bit_de_cada_casilla()
    {
        Assert.Equal(0x100, UnixMode.Bit(0));   // lectura del propietario
        Assert.Equal(0x001, UnixMode.Bit(8));   // ejecucion de otros
    }

    [Theory]
    [InlineData(0b110_100_100, 644)]
    [InlineData(0b111_101_101, 755)]
    [InlineData(0, 0)]
    [InlineData(0b000_000_111, 7)]
    public void Chmod_de_FTP_ida_y_vuelta(int mode, int ftp)
    {
        Assert.Equal(ftp, UnixMode.ToFtpChmod(mode));
        Assert.Equal(mode, UnixMode.FromFtpChmod(ftp));
    }

    [Fact]
    public void FileEntry_sin_modo_no_tiene_texto()
    {
        Assert.Equal("", new FileEntry("f", "/f", false, 0, null).ModeText);
    }
}

public class PermissionChangeTests
{
    [Fact]
    public void Nada_cambiado_no_pide_nada()
    {
        var c = PermissionChange.From(0b110_100_100, 0b110_100_100, "root", "root", "wheel", "wheel", false);
        Assert.False(c.ChangeMode);
        Assert.False(c.ChangeOwner);
    }

    [Fact]
    public void Modo_nuevo_o_sin_modo()
    {
        Assert.True(PermissionChange.From(0b111_101_101, 0b110_100_100, "", "", "", "", false).ChangeMode);
        Assert.True(PermissionChange.From(0b111_101_101, null, "", "", "", "", false).ChangeMode);
        Assert.False(PermissionChange.From(null, 0b110_100_100, "", "", "", "", false).ChangeMode);
    }

    [Theory]
    [InlineData("pepe", "root", "", "wheel", true)]    // propietario nuevo
    [InlineData("root", "root", "staff", "wheel", true)]  // grupo nuevo
    [InlineData("", "root", "", "wheel", false)]        // vacio: no tocar
    [InlineData("root", "root", "wheel", "wheel", false)]
    public void Propietario_y_grupo(string owner, string originalOwner, string group, string originalGroup, bool expected)
    {
        var c = PermissionChange.From(null, null, owner, originalOwner, group, originalGroup, true);
        Assert.Equal(expected, c.ChangeOwner);
        Assert.True(c.Recursive);
        Assert.Equal((owner, group), (c.Owner, c.Group));
    }
}

public class RemotePathTests
{
    [Theory]
    [InlineData("/home", "a", "/home/a")]
    [InlineData("/", "a", "/a")]
    [InlineData("/home/", "a", "/home/a")]
    [InlineData("", "a", "/a")]
    public void Combine(string dir, string name, string expected) => Assert.Equal(expected, RemotePath.Combine(dir, name));

    [Theory]
    [InlineData("/home/pepe", "/home")]
    [InlineData("/home/pepe/", "/home")]
    [InlineData("/home", "/")]
    [InlineData("/", "/")]
    [InlineData("relativo", "/")]
    public void Parent(string path, string expected) => Assert.Equal(expected, RemotePath.Parent(path));

    [Theory]
    [InlineData("/home/pepe", "pepe")]
    [InlineData("/home/pepe/", "pepe")]
    [InlineData("suelto", "suelto")]
    [InlineData("/", "")]
    public void Name(string path, string expected) => Assert.Equal(expected, RemotePath.Name(path));
}

public sealed class FileRulesTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly TempDir _dir = new();

    public FileRulesTests() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        _dir.Dispose();
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1024 * 1024 - 1, "1024 KB")]
    [InlineData(1024 * 1024, "1 MB")]
    [InlineData(5L * 1024 * 1024 + 300 * 1024, "5.3 MB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    [InlineData(1024L * 1024 * 1024 * 5 / 4, "1.25 GB")]
    public void SizeText(long bytes, string expected) => Assert.Equal(expected, FileRules.SizeText(bytes));

    [Fact]
    public void SizeText_con_la_coma_de_la_cultura()
    {
        CultureInfo.CurrentCulture = new CultureInfo("es-ES");
        Assert.Equal("1,5 KB", FileRules.SizeText(1536));
    }

    [Fact]
    public void Ocultos_por_punto_y_en_local_por_atributo()
    {
        Assert.True(FileRules.IsHidden(new FileEntry(".ssh", "/home/.ssh", true, 0, null), local: false));
        Assert.False(FileRules.IsHidden(new FileEntry("ssh", "/home/ssh", true, 0, null), local: false));

        var visible = _dir.File("visible.txt", "x");
        var hidden = _dir.File("oculto.txt", "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        Assert.False(FileRules.IsHidden(new FileEntry("visible.txt", visible, false, 1, null), local: true));
        Assert.True(FileRules.IsHidden(new FileEntry("oculto.txt", hidden, false, 1, null), local: true));
        Assert.False(FileRules.IsHidden(new FileEntry("no-existe", Path.Combine(_dir.Path, "no-existe"), false, 1, null), local: true));
        Assert.False(FileRules.IsHidden(new FileEntry("malo", "C:\\<>|?", false, 1, null), local: true));
    }

    [Theory]
    [InlineData("notas.txt", true)]
    [InlineData("config.YAML", true)]
    [InlineData("script.sh", true)]
    [InlineData("Makefile", true)]
    [InlineData("README", true)]
    [InlineData(".gitignore", true)]
    [InlineData(".bashrc", true)]      // ficheros de configuracion con punto: al editor
    [InlineData(".profile", true)]
    [InlineData(".", false)]
    [InlineData("foto.jpg", false)]
    [InlineData("paquete.tar.gz", false)]
    [InlineData("programa.exe", false)]
    public void LooksLikeText(string name, bool expected)
    {
        Assert.Equal(expected, FileRules.LooksLikeText(new FileEntry(name, "/x/" + name, false, 100, null)));
    }

    [Fact]
    public void LooksLikeText_ni_directorios_ni_ficheros_grandes()
    {
        Assert.False(FileRules.LooksLikeText(new FileEntry("dir.txt", "/dir.txt", true, 0, null)));
        Assert.True(FileRules.LooksLikeText(new FileEntry("a.log", "/a.log", false, FileRules.MaxEditableBytes, null)));
        Assert.False(FileRules.LooksLikeText(new FileEntry("a.log", "/a.log", false, FileRules.MaxEditableBytes + 1, null)));
    }

    [Fact]
    public void DecodeText_UTF8_sin_BOM_y_LF()
    {
        var t = FileRules.DecodeText(Encoding.UTF8.GetBytes("año\nfin\n"))!;
        Assert.Equal("año\r\nfin\r\n", t.Content);
        Assert.False(t.Crlf);
        Assert.Empty(t.Encoding.GetPreamble());
        Assert.Equal(Encoding.UTF8.GetBytes("año\nfin\n"), FileRules.EncodeText(t.Content, t.Encoding, t.Crlf));
    }

    [Fact]
    public void DecodeText_UTF8_con_BOM_y_CRLF_vuelve_igual()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("a\r\nb")).ToArray();
        var t = FileRules.DecodeText(bytes)!;
        Assert.Equal("a\r\nb", t.Content);
        Assert.True(t.Crlf);
        Assert.Equal(3, t.Encoding.GetPreamble().Length);
        Assert.Equal(bytes, FileRules.EncodeText(t.Content, t.Encoding, t.Crlf));
    }

    [Fact]
    public void DecodeText_Latin1_si_no_es_UTF8()
    {
        var bytes = Encoding.Latin1.GetBytes("caf\u00e9\n");
        var t = FileRules.DecodeText(bytes)!;
        Assert.Equal("café\r\n", t.Content);
        Assert.Same(Encoding.Latin1, t.Encoding);
        Assert.Equal(bytes, FileRules.EncodeText(t.Content, t.Encoding, t.Crlf));
    }

    [Fact]
    public void DecodeText_con_bytes_nulos_no_es_texto()
    {
        Assert.Null(FileRules.DecodeText([0x41, 0x00, 0x42]));
        // Un nulo mas alla de los primeros 8 KB no cuenta.
        var late = Enumerable.Repeat((byte)'a', 9000).Append((byte)0).ToArray();
        Assert.NotNull(FileRules.DecodeText(late));
    }

    [Fact]
    public void DecodeText_vacio()
    {
        var t = FileRules.DecodeText([])!;
        Assert.Equal("", t.Content);
        Assert.False(t.Crlf);
    }

    [Fact]
    public async Task Borrar_en_remoto_vacia_antes_lo_de_dentro()
    {
        var fs = new FakeRemote()
            .Dir("/d", FakeRemote.F("/d", "a"), FakeRemote.D("/d", "sub"))
            .Dir("/d/sub", FakeRemote.F("/d/sub", "b"));
        await FileRules.DeleteRecursiveAsync(new RemoteSide(fs), FakeRemote.D("/", "d"), CancellationToken.None);
        Assert.Equal(["list /d", "rm /d/a", "list /d/sub", "rm /d/sub/b", "rmdir /d/sub", "rmdir /d"], fs.Log);
    }

    [Fact]
    public async Task Borrar_un_fichero_remoto()
    {
        var fs = new FakeRemote();
        await FileRules.DeleteRecursiveAsync(new RemoteSide(fs), FakeRemote.F("/", "x"), CancellationToken.None);
        Assert.Equal(["rm /x"], fs.Log);
    }

    [Fact]
    public async Task Borrar_en_local_de_una_vez()
    {
        var dir = Path.Combine(_dir.Path, "borrar");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "sub", "f.txt"), "x");
        var side = new LocalSide(_dir.Path);
        await FileRules.DeleteRecursiveAsync(side, new FileEntry("borrar", dir, true, 0, null), CancellationToken.None);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public async Task Plan_de_transferencia_recorre_y_suma()
    {
        var fs = new FakeRemote()
            .Dir("/src/dir", FakeRemote.F("/src/dir", "a", 100), FakeRemote.D("/src/dir", "sub"))
            .Dir("/src/dir/sub", FakeRemote.F("/src/dir/sub", "b", 50));
        var plan = new List<(FileEntry, string)>();
        var total = await FileRules.PlanAsync(new RemoteSide(fs), new LocalSide(_dir.Path), FakeRemote.D("/src", "dir"), @"C:\dest", plan, CancellationToken.None);
        Assert.Equal(150, total);
        Assert.Equal([@"C:\dest\dir", @"C:\dest\dir\a", @"C:\dest\dir\sub", @"C:\dest\dir\sub\b"], plan.Select(p => p.Item2));
    }

    [Fact]
    public async Task Plan_de_un_fichero_suelto()
    {
        var plan = new List<(FileEntry, string)>();
        var total = await FileRules.PlanAsync(new LocalSide(_dir.Path), new RemoteSide(new FakeRemote()), new FileEntry("f.bin", @"C:\f.bin", false, 42, null), "/up", plan, CancellationToken.None);
        Assert.Equal(42, total);
        Assert.Equal("/up/f.bin", Assert.Single(plan).Item2);
    }

    [Fact]
    public async Task Permisos_recursivos_en_todo_lo_de_dentro()
    {
        var fs = new FakeRemote()
            .Dir("/w", FakeRemote.F("/w", "a"), FakeRemote.D("/w", "s"))
            .Dir("/w/s", FakeRemote.F("/w/s", "b"));
        var change = new PermissionChange(0b111_101_101, true, "www", "www", true, true);
        await FileRules.ApplyPermissionsAsync(new RemoteSide(fs), FakeRemote.D("/", "w"), change, CancellationToken.None);
        Assert.Equal([
            "chmod 755 /w", "chown www:www /w", "list /w",
            "chmod 755 /w/a", "chown www:www /w/a",
            "chmod 755 /w/s", "chown www:www /w/s", "list /w/s",
            "chmod 755 /w/s/b", "chown www:www /w/s/b",
        ], fs.Log);
    }

    [Fact]
    public async Task Permisos_solo_lo_pedido_y_sin_bajar()
    {
        var fs = new FakeRemote().Dir("/w", FakeRemote.F("/w", "a"));
        await FileRules.ApplyPermissionsAsync(new RemoteSide(fs), FakeRemote.D("/", "w"), new PermissionChange(0b110_100_100, true, "", "", false, false), CancellationToken.None);
        Assert.Equal(["chmod 644 /w"], fs.Log);

        fs.Log.Clear();
        await FileRules.ApplyPermissionsAsync(new RemoteSide(fs), FakeRemote.F("/", "f"), new PermissionChange(null, true, "u", "", true, true), CancellationToken.None);
        Assert.Equal(["chown u: /f"], fs.Log);   // sin modo no hay chmod; un fichero no se recorre
    }
}

public sealed class FileSidesTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void LocalSide_empieza_en_la_carpeta_pedida_o_en_el_perfil()
    {
        Assert.Equal(_dir.Path, new LocalSide(_dir.Path).InitialDirectory);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(profile, new LocalSide("").InitialDirectory);
        Assert.Equal(profile, new LocalSide(Path.Combine(_dir.Path, "no-existe")).InitialDirectory);
        Assert.True(new LocalSide("").IsLocal);
    }

    [Fact]
    public async Task LocalSide_lista_carpetas_y_ficheros()
    {
        Directory.CreateDirectory(Path.Combine(_dir.Path, "sub"));
        _dir.File("a.txt", "hola");
        var side = new LocalSide(_dir.Path);
        var list = await side.ListAsync(_dir.Path, CancellationToken.None);
        Assert.Equal(2, list.Count);
        Assert.True(list[0].IsDirectory);
        Assert.Equal("sub", list[0].Name);
        Assert.Equal(("a.txt", 4L), (list[1].Name, list[1].Size));
        Assert.NotNull(list[1].Modified);
    }

    [Fact]
    public async Task LocalSide_ruta_vacia_son_las_unidades()
    {
        var drives = await new LocalSide("").ListAsync("", CancellationToken.None);
        Assert.NotEmpty(drives);
        Assert.All(drives, d => Assert.True(d.IsDirectory));
        Assert.Contains(drives, d => d.FullPath.StartsWith(Path.GetPathRoot(Environment.SystemDirectory)!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LocalSide_padre_y_combinar()
    {
        var side = new LocalSide(_dir.Path);
        Assert.Equal("", side.Parent(""));
        Assert.Equal("", side.Parent(@"C:\"));   // desde la raiz, a «Este equipo»
        Assert.Equal(@"C:\Windows", side.Parent(@"C:\Windows\System32"));
        Assert.Equal(@"C:\a\b", side.Combine(@"C:\a", "b"));
    }

    [Fact]
    public async Task LocalSide_crear_renombrar_y_borrar()
    {
        var side = new LocalSide(_dir.Path);
        var d = Path.Combine(_dir.Path, "nueva");
        await side.CreateDirectoryAsync(d, CancellationToken.None);
        Assert.True(Directory.Exists(d));
        await side.RenameAsync(d, d + "2", CancellationToken.None);
        Assert.True(Directory.Exists(d + "2"));

        var f = _dir.File("f.txt", "1");
        var g = _dir.File("g.txt", "2");
        await side.RenameAsync(f, g, CancellationToken.None);   // sobrescribe
        Assert.Equal("1", File.ReadAllText(g));
        Assert.False(File.Exists(f));

        await side.DeleteFileAsync(g, CancellationToken.None);
        Assert.False(File.Exists(g));
        File.WriteAllText(Path.Combine(d + "2", "dentro"), "x");
        await side.DeleteDirectoryAsync(d + "2", CancellationToken.None);   // con lo de dentro
        Assert.False(Directory.Exists(d + "2"));
    }

    [Fact]
    public async Task RemoteSide_pasa_todo_al_servidor()
    {
        var fs = new FakeRemote().Dir("/x", FakeRemote.F("/x", "a"));
        var side = new RemoteSide(fs);
        Assert.False(side.IsLocal);
        Assert.Same(fs, side.Fs);
        Assert.Equal("/home/test", side.InitialDirectory);
        Assert.Equal("a", Assert.Single(await side.ListAsync("/x", CancellationToken.None)).Name);
        Assert.Equal("/x", side.Parent("/x/a"));
        Assert.Equal("/x/b", side.Combine("/x", "b"));
        await side.CreateDirectoryAsync("/n", CancellationToken.None);
        await side.DeleteFileAsync("/x/a", CancellationToken.None);
        await side.DeleteDirectoryAsync("/n", CancellationToken.None);
        await side.RenameAsync("/p", "/q", CancellationToken.None);
        Assert.Equal(["list /x", "mkdir /n", "rm /x/a", "rmdir /n", "mv /p /q"], fs.Log);
    }
}

public class FtpListingTests
{
    private static FtpListItem Item(string name, FtpObjectType type = FtpObjectType.File, long size = 0, int chmod = 0, string raw = "", string owner = "", string group = "", DateTime? modified = null)
    {
        var i = new FtpListItem
        {
            Name = name,
            FullName = "/pub/" + name,
            Type = type,
            Size = size,
            Chmod = chmod,
            RawPermissions = raw,
            RawOwner = owner,
            RawGroup = group,
            Modified = modified ?? DateTime.MinValue,
        };
        return i;
    }

    [Fact]
    public void Listado_Unix_con_permisos_propietario_y_fecha()
    {
        var when = new DateTime(2026, 9, 1, 12, 0, 0);
        var list = FtpFileSystem.ToEntries([
            Item("."), Item(".."),
            Item("docs", FtpObjectType.Directory, chmod: 755, raw: "drwxr-xr-x", owner: "ftp", group: "ftp", modified: when),
            Item("a.txt", size: 12, chmod: 644, raw: "-rw-r--r--", owner: "pepe", group: "users"),
            Item("enlace", FtpObjectType.Link, chmod: 777, raw: "lrwxrwxrwx"),
        ]);
        Assert.Equal(["docs", "a.txt", "enlace"], list.Select(e => e.Name));
        var docs = list[0];
        Assert.True(docs.IsDirectory);
        Assert.Equal("/pub/docs", docs.FullPath);
        Assert.Equal("rwxr-xr-x", docs.ModeText);
        Assert.Equal(("ftp", "ftp"), (docs.Owner, docs.Group));
        Assert.Equal(when, docs.Modified);
        Assert.Equal((12L, "rw-r--r--", "pepe"), (list[1].Size, list[1].ModeText, list[1].Owner));
        Assert.Null(list[1].Modified);   // el servidor no dio fecha
        Assert.True(list[2].IsDirectory);   // un enlace se trata como directorio
    }

    [Fact]
    public void Listado_de_Windows_sin_permisos()
    {
        var e = Assert.Single(FtpFileSystem.ToEntries([Item("a.txt", size: 3)]));
        Assert.Null(e.Mode);
        Assert.Null(e.Owner);
        Assert.Null(e.Group);
        Assert.Equal("", e.ModeText);
    }

    [Fact]
    public void Permisos_a_cero_con_texto_rwx_cuentan()
    {
        var e = Assert.Single(FtpFileSystem.ToEntries([Item("x", chmod: 0, raw: "----------")]));
        Assert.Equal(0, e.Mode);
        Assert.Equal("---------", e.ModeText);
    }
}

public class SftpTests
{
    /// <summary>Un ISftpFile de mentira: cada propiedad sale del diccionario (o su valor por defecto).</summary>
    public class FileProxy : DispatchProxy
    {
        public Dictionary<string, object?> Values { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name.StartsWith("get_") ? targetMethod.Name[4..] : targetMethod.Name;
            if (Values.TryGetValue(name, out var v))
                return v;
            return targetMethod.ReturnType.IsValueType && targetMethod.ReturnType != typeof(void) ? Activator.CreateInstance(targetMethod.ReturnType) : null;
        }
    }

    private static ISftpFile File(Dictionary<string, object?> values)
    {
        var f = DispatchProxy.Create<ISftpFile, FileProxy>();
        foreach (var (k, v) in values)
            ((FileProxy)(object)f).Values[k] = v;
        return f;
    }

    [Fact]
    public void ToEntry_con_bits_uid_y_gid()
    {
        var when = new DateTime(2026, 1, 2, 3, 4, 5);
        var f = File(new()
        {
            ["Name"] = "run.sh", ["FullName"] = "/opt/run.sh", ["Length"] = 77L, ["LastWriteTime"] = when,
            ["OwnerCanRead"] = true, ["OwnerCanWrite"] = true, ["OwnerCanExecute"] = true,
            ["GroupCanRead"] = true, ["GroupCanExecute"] = true,
            ["OthersCanExecute"] = true,
            ["UserId"] = 1000, ["GroupId"] = 50,
        });
        var e = SftpFileSystem.ToEntry(f, isDirectory: false);
        Assert.Equal(("run.sh", "/opt/run.sh", 77L, (DateTime?)when), (e.Name, e.FullPath, e.Size, e.Modified));
        Assert.Equal("rwxr-x--x", e.ModeText);
        Assert.Equal(("1000", "50"), (e.Owner, e.Group));
        Assert.False(e.IsDirectory);
    }

    [Fact]
    public void ToEntry_sin_permisos_y_como_directorio()
    {
        var e = SftpFileSystem.ToEntry(File(new() { ["Name"] = "d", ["FullName"] = "/d", ["GroupCanWrite"] = true, ["OthersCanRead"] = true, ["OthersCanWrite"] = true }), isDirectory: true);
        Assert.True(e.IsDirectory);
        Assert.Equal("----w-rw-", e.ModeText);
        Assert.Equal(("0", "0"), (e.Owner, e.Group));
    }

    [Theory]
    [InlineData("simple", "'simple'")]
    [InlineData("con espacio", "'con espacio'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("$(rm -rf /)", "'$(rm -rf /)'")]
    [InlineData("", "''")]
    public void Quote_para_el_shell(string text, string expected) => Assert.Equal(expected, SftpFileSystem.Quote(text));

    [Fact]
    public void SshAuth_con_contraseña_ofrece_contraseña_y_teclado()
    {
        var info = SshAuth.Build(new Connection { Host = "h", Port = 2222, UserName = "u", FilesTimeoutSeconds = 3 }, "pw");
        Assert.Equal(("h", 2222, "u"), (info.Host, info.Port, info.Username));
        Assert.Equal([typeof(PasswordAuthenticationMethod), typeof(KeyboardInteractiveAuthenticationMethod)], info.AuthenticationMethods.Select(m => m.GetType()));
        Assert.Equal(TimeSpan.FromSeconds(5), info.Timeout);   // nunca menos de 5 s
    }

    [Fact]
    public void SshAuth_con_clave_primero_la_clave()
    {
        using var dir = new TempDir();
        using var rsa = RSA.Create(2048);
        var key = dir.File("id_rsa", rsa.ExportRSAPrivateKeyPem());
        var c = new Connection { Host = "h", Port = 22, UserName = "u", PrivateKeyPath = key, FilesTimeoutSeconds = 30 };

        var onlyKey = SshAuth.Build(c, "");
        Assert.IsType<PrivateKeyAuthenticationMethod>(Assert.Single(onlyKey.AuthenticationMethods));
        Assert.Equal(TimeSpan.FromSeconds(30), onlyKey.Timeout);

        var both = SshAuth.Build(c, "frase");
        Assert.Equal([typeof(PrivateKeyAuthenticationMethod), typeof(PasswordAuthenticationMethod), typeof(KeyboardInteractiveAuthenticationMethod)],
            both.AuthenticationMethods.Select(m => m.GetType()));
    }

    [Fact]
    public void SshAuth_sin_nada_con_que_entrar_lanza()
    {
        Lang.Set("en");
        var c = new Connection { Host = "h", UserName = "u", PrivateKeyPath = @"C:\no\existe\id_rsa" };
        var ex = Assert.Throws<InvalidOperationException>(() => SshAuth.Build(c, ""));
        Assert.Equal(Loc.Get("SshNoCredentials"), ex.Message);
    }
}
