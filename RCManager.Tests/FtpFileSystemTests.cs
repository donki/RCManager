using System.Text;
using SocRcManager.Files;
using SocRcManager.Models;

namespace SocRcManager.Tests;

/// <summary><see cref="FtpFileSystem"/> contra <see cref="FakeFtpServer"/> en 127.0.0.1: sin red ni servidores reales.</summary>
public sealed class FtpFileSystemTests : IDisposable
{
    private readonly FakeFtpServer _server = new();
    private readonly TempDir _dir = new();

    public FtpFileSystemTests()
    {
        _server.Dirs.Add("/home");
        _server.Dirs.Add("/home/docs");
        _server.Files["/home/a.txt"] = Encoding.UTF8.GetBytes("hola ftp");
        _server.Modified["/home/a.txt"] = new DateTime(2026, 9, 1, 12, 0, 0);
    }

    public void Dispose()
    {
        _server.Dispose();
        _dir.Dispose();
    }

    private Connection Conn(string user = "pepe", int keepAlive = 0) => new()
    {
        Kind = ConnectionKind.Ftp, Host = "127.0.0.1", Port = _server.Port, UserName = user,
        FilesKeepAliveSeconds = keepAlive, FilesTimeoutSeconds = 5,
    };

    private sealed class Sink : IProgress<long>
    {
        public long Total;
        public void Report(long value) => Interlocked.Add(ref Total, value);
    }

    [Fact]
    public async Task Entra_lista_con_permisos_y_detecta_Unix()
    {
        using var fs = await FtpFileSystem.ConnectAsync(Conn(), "pw", CancellationToken.None);
        Assert.Equal("/home", fs.InitialDirectory);
        Assert.False(fs.SupportsPermissions);   // hasta ver un listado no se sabe
        var list = await fs.ListAsync("/home", CancellationToken.None);
        Assert.Equal(["docs", "a.txt"], list.Select(e => e.Name));
        Assert.True(list[0].IsDirectory);
        Assert.Equal("rwxr-xr-x", list[0].ModeText);
        var a = list[1];
        Assert.Equal((8L, "rw-r--r--"), (a.Size, a.ModeText));
        Assert.Null(a.Owner);   // FluentFTP no saca el propietario de MLSD
        Assert.Equal(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc).ToLocalTime(), a.Modified);   // UTC del servidor a hora local
        Assert.True(fs.SupportsPermissions);
        Assert.True(_server.Received("USER pepe"));
    }

    [Fact]
    public async Task Listado_LIST_al_estilo_de_ls_con_propietario()
    {
        _server.Mlsd = false;
        using var fs = await FtpFileSystem.ConnectAsync(Conn(), "pw", CancellationToken.None);
        var list = await fs.ListAsync("/home", CancellationToken.None);
        Assert.Equal(["docs", "a.txt"], list.Select(e => e.Name));
        Assert.Equal(("rwxr-xr-x", "ftp", "ftp"), (list[0].ModeText, list[0].Owner, list[0].Group));
        Assert.Equal(("rw-r--r--", "pepe", "users", 8L), (list[1].ModeText, list[1].Owner, list[1].Group, list[1].Size));
        Assert.True(fs.SupportsPermissions);
    }

    [Fact]
    public void AsLocal_de_cada_tipo_de_fecha()
    {
        var utc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        Assert.Equal(utc.ToLocalTime(), FtpFileSystem.AsLocal(utc));
        Assert.Equal(DateTimeKind.Local, FtpFileSystem.AsLocal(utc).Kind);
        var local = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
        Assert.Equal(local, FtpFileSystem.AsLocal(local));
        var plain = new DateTime(2026, 1, 2, 3, 4, 5);
        Assert.Equal((plain.Ticks, DateTimeKind.Local), (FtpFileSystem.AsLocal(plain).Ticks, FtpFileSystem.AsLocal(plain).Kind));
    }

    [Fact]
    public async Task Servidor_de_Windows_sin_permisos()
    {
        _server.Unix = false;
        using var fs = await FtpFileSystem.ConnectAsync(Conn(), "pw", CancellationToken.None);
        var list = await fs.ListAsync("/home", CancellationToken.None);
        Assert.All(list, e => Assert.Null(e.Mode));
        Assert.False(fs.SupportsPermissions);
    }

    [Fact]
    public async Task Sin_usuario_entra_como_anonimo()
    {
        _server.Password = "anonymous@";
        using var fs = await FtpFileSystem.ConnectAsync(Conn(user: ""), "ignorada", CancellationToken.None);
        Assert.True(_server.Received("USER anonymous"));
    }

    [Fact]
    public async Task Contraseña_mala_lanza()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => FtpFileSystem.ConnectAsync(Conn(), "mala", CancellationToken.None));
    }

    [Fact]
    public async Task Stat_de_fichero_directorio_y_lo_que_no_existe()
    {
        using var fs = await FtpFileSystem.ConnectAsync(Conn(), "pw", CancellationToken.None);
        var a = await fs.StatAsync("/home/a.txt", CancellationToken.None);
        Assert.NotNull(a);
        Assert.Equal(("a.txt", false, 8L), (a.Name, a.IsDirectory, a.Size));
        Assert.NotNull(a.Modified);
        var d = await fs.StatAsync("/home/docs", CancellationToken.None);
        Assert.True(d!.IsDirectory);
        Assert.Null(d.Modified);
        Assert.Null(await fs.StatAsync("/home/no-existe", CancellationToken.None));
    }

    [Fact]
    public async Task Bajar_y_subir_con_progreso()
    {
        using var fs = await FtpFileSystem.ConnectAsync(Conn(), "pw", CancellationToken.None);
        var local = Path.Combine(_dir.Path, "a.txt");
        var down = new Sink();
        await fs.DownloadAsync("/home/a.txt", local, down, CancellationToken.None);
        Assert.Equal("hola ftp", File.ReadAllText(local));

        File.WriteAllText(local, "subido");
        var up = new Sink();
        await fs.UploadAsync(local, "/home/nuevo.txt", up, CancellationToken.None);
        Assert.Equal("subido", Encoding.UTF8.GetString(_server.Files["/home/nuevo.txt"]));
        await Task.Delay(200);   // los Progress<T> avisan por el contexto: se deja que lleguen
        Assert.Equal(8, down.Total);
        Assert.Equal(6, up.Total);
    }

    [Fact]
    public async Task Bajar_lo_que_no_existe_lanza()
    {
        _server.FailRetr = true;
        using var fs = await FtpFileSystem.ConnectAsync(Conn(), "pw", CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() => fs.DownloadAsync("/home/a.txt", Path.Combine(_dir.Path, "x"), new Sink(), CancellationToken.None));
    }

    [Fact]
    public async Task Crear_renombrar_fecha_y_borrar()
    {
        using var fs = await FtpFileSystem.ConnectAsync(Conn(), "pw", CancellationToken.None);
        await fs.CreateDirectoryAsync("/home/nueva", CancellationToken.None);
        Assert.Contains("/home/nueva", _server.Dirs);
        await fs.RenameAsync("/home/a.txt", "/home/b.txt", CancellationToken.None);
        Assert.True(_server.Files.ContainsKey("/home/b.txt"));
        var when = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
        await fs.SetModifiedAsync("/home/b.txt", when, CancellationToken.None);
        Assert.Equal(when.ToUniversalTime(), DateTime.SpecifyKind(_server.Modified["/home/b.txt"], DateTimeKind.Utc));   // MFMT va en UTC
        await fs.SetModifiedAsync("/home/b.txt", when.AddHours(1).ToUniversalTime(), CancellationToken.None);
        Assert.Equal(when.AddHours(1).ToUniversalTime(), DateTime.SpecifyKind(_server.Modified["/home/b.txt"], DateTimeKind.Utc));
        await fs.DeleteFileAsync("/home/b.txt", CancellationToken.None);
        Assert.False(_server.Files.ContainsKey("/home/b.txt"));
        await fs.DeleteDirectoryAsync("/home/nueva", CancellationToken.None);
        Assert.DoesNotContain("/home/nueva", _server.Dirs);
    }

    [Fact]
    public async Task Chmod_y_chown_por_SITE()
    {
        using var fs = await FtpFileSystem.ConnectAsync(Conn(), "pw", CancellationToken.None);
        await fs.ChangeModeAsync("/home/a.txt", 0b111_101_101, CancellationToken.None);
        Assert.True(_server.Received("SITE CHMOD 755 /home/a.txt"));
        await fs.ChangeOwnerAsync("/home/a.txt", "www", "web", CancellationToken.None);
        Assert.True(_server.Received("SITE CHOWN www:web /home/a.txt"));
        await fs.ChangeOwnerAsync("/home/a.txt", "solo", "", CancellationToken.None);
        Assert.True(_server.Received("SITE CHOWN solo /home/a.txt"));
    }

    [Fact]
    public async Task Chown_que_el_servidor_no_entiende_lanza_con_su_mensaje()
    {
        _server.ChownAllowed = false;
        using var fs = await FtpFileSystem.ConnectAsync(Conn(), "pw", CancellationToken.None);
        var ex = await Assert.ThrowsAsync<IOException>(() => fs.ChangeOwnerAsync("/home/a.txt", "www", "", CancellationToken.None));
        Assert.Contains("SITE CHOWN not understood", ex.Message);
    }

    [Fact]
    public async Task Sigo_aqui_cuando_esta_parada_y_adios_al_cerrar()
    {
        var fs = await FtpFileSystem.ConnectAsync(Conn(keepAlive: 1), "pw", CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!_server.Received("NOOP") && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        Assert.True(_server.Received("NOOP"));
        fs.Dispose();
        Assert.True(_server.Received("QUIT"));
    }

    [Fact]
    public async Task Operaciones_a_la_vez_no_se_cruzan()
    {
        using var fs = await FtpFileSystem.ConnectAsync(Conn(), "pw", CancellationToken.None);
        var lists = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => fs.ListAsync("/home", CancellationToken.None)));
        Assert.All(lists, l => Assert.Equal(2, l.Count));
    }
}
