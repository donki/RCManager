using System.Text;
using SocRcManager.Files;
using SocRcManager.Models;

namespace SocRcManager.Tests;

/// <summary>Mas de <see cref="FtpFileSystem"/> contra <see cref="FakeFtpServer"/> (que no sabe TLS): FTPS, fallos y el keep-alive.</summary>
public sealed class FtpFileSystemMoreTests : IDisposable
{
    private readonly FakeFtpServer _server = new();
    private readonly TempDir _dir = new();

    public FtpFileSystemMoreTests()
    {
        _server.Dirs.Add("/home");
        _server.Files["/home/a.txt"] = Encoding.UTF8.GetBytes("hola ftp");
    }

    public void Dispose()
    {
        _server.Dispose();
        _dir.Dispose();
    }

    private Connection Conn(int ftps = 0, int keepAlive = 0) => new()
    {
        Kind = ConnectionKind.Ftp, Host = "127.0.0.1", Port = _server.Port, UserName = "pepe",
        FtpsMode = ftps, FilesKeepAliveSeconds = keepAlive, FilesTimeoutSeconds = 5,
    };

    [Fact]
    public async Task FTPS_explicito_contra_un_servidor_sin_TLS_falla_sin_mandar_la_contraseña()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => FtpFileSystem.ConnectAsync(Conn(ftps: 1), "pw", CancellationToken.None));
        Assert.True(_server.Received("AUTH TLS"));
        Assert.False(_server.Received("PASS"));   // nunca sigue en claro
    }

    [Fact]
    public async Task FTPS_implicito_contra_un_servidor_sin_TLS_falla_sin_mandar_la_contraseña()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => FtpFileSystem.ConnectAsync(Conn(ftps: 2), "pw", CancellationToken.None));
        Assert.False(_server.Received("USER"));
        Assert.False(_server.Received("PASS"));
    }

    [Fact]
    public async Task Subir_un_fichero_local_que_no_existe_lanza_con_el_nombre()
    {
        using var fs = await FtpFileSystem.ConnectAsync(Conn(), "pw", CancellationToken.None);
        var ex = await Assert.ThrowsAsync<IOException>(() => fs.UploadAsync(Path.Combine(_dir.Path, "no-esta.txt"), "/home/nuevo.txt", new ProgressLog(), CancellationToken.None));
        Assert.Equal("FTP: nuevo.txt", ex.Message);
        Assert.False(_server.Files.ContainsKey("/home/nuevo.txt"));
    }

    [Fact]
    public async Task Mientras_se_usa_no_manda_el_sigo_aqui()
    {
        using var fs = await FtpFileSystem.ConnectAsync(Conn(keepAlive: 1), "pw", CancellationToken.None);
        // Mas de un segundo usandola sin parar: cada aviso del temporizador ve que se acaba de usar.
        var until = DateTime.UtcNow.AddMilliseconds(1600);
        while (DateTime.UtcNow < until)
            await fs.ListAsync("/home", CancellationToken.None);
        Assert.False(_server.Received("NOOP"));
    }
}
