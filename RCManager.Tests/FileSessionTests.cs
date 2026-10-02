using System.Net;
using System.Net.Sockets;
using System.Windows.Controls;
using SocRcManager.Files;
using SocRcManager.Models;
using SocRcManager.Sessions;

namespace SocRcManager.Tests;

/// <summary>La pestaña de ficheros con un sistema de ficheros remoto en memoria (y el de verdad contra el FTP de mentira).</summary>
public sealed class FileSessionTests : UiTest
{
    private readonly FakeRemote _fs = new FakeRemote()
        .Dir("/", FakeRemote.D("/", "srv"), FakeRemote.F("/", "leeme.txt"))
        .Dir("/srv/www", FakeRemote.F("/srv/www", "index.html"));
    private readonly List<(Connection Connection, string Password)> _opened = [];

    private Connection Conn(ConnectionKind kind = ConnectionKind.Sftp) => new()
    {
        Name = "Web",
        Kind = kind,
        Host = "files.invalid",
        UserName = "ana",
        LocalPath = Dir.Path,   // nunca la carpeta del usuario
    };

    private FileSession Create(Connection c) => Ui.Run(() => new FileSession(c, (conn, pw, _) =>
    {
        _opened.Add((conn, pw));
        return Task.FromResult<IRemoteFileSystem>(_fs);
    }));

    [Fact]
    public void Mientras_conecta_enseña_el_indicador_con_el_servidor()
    {
        var s = Create(Conn());
        Ui.Run(() =>
        {
            var host = Assert.IsType<ContentControl>(s.View);
            var panel = Assert.IsType<StackPanel>(host.Content);
            Assert.True(Assert.IsType<ProgressBar>(panel.Children[0]).IsIndeterminate);
            Assert.Equal(Localization.Loc.Format("Connecting", "files.invalid"), Assert.IsType<TextBlock>(panel.Children[1]).Text);
        });
        Assert.Equal("Web", s.Title);
        Assert.True(s.CanZoom);
        Assert.False(s.HasNativeFullScreen);
        var left = 0;
        s.LeftFullScreen += () => left++;
        s.LeftFullScreen -= () => left++;
        s.EnterFullScreen(1);
        s.LeaveFullScreen();
        Assert.Equal(0, left);
    }

    [Theory]
    [InlineData(ConnectionKind.Sftp, false, 0, "SFTP")]
    [InlineData(ConnectionKind.Sftp, true, 0, "SCP")]
    [InlineData(ConnectionKind.Ftp, false, 0, "FTP")]
    [InlineData(ConnectionKind.Ftp, false, 1, "FTPS")]
    [InlineData(ConnectionKind.Ftp, false, 2, "FTPS")]
    public void Conectado_enseña_el_explorador_en_la_raiz_y_el_protocolo(ConnectionKind kind, bool scp, int ftps, string protocol)
    {
        var c = Conn(kind);
        c.UseScp = scp;
        c.FtpsMode = ftps;
        var s = Create(c);
        var titles = new List<string>();
        s.TitleChanged += titles.Add;
        Ui.RunAsync(() => s.ConnectAsync("pw"));
        Assert.Equal([(c, "pw")], _opened);
        Assert.Same(s.Browser, Ui.Run(() => ((ContentControl)s.View).Content));
        Assert.Contains("list /", _fs.Log);
        Assert.Equal([protocol], titles);
    }

    [Fact]
    public void Conectado_va_a_la_carpeta_de_la_conexion_con_su_letra()
    {
        var c = Conn();
        c.RemotePath = "/srv/www";
        c.FontSize = 40;
        var s = Create(c);
        Ui.RunAsync(() => s.ConnectAsync(""));
        Assert.Equal("list /srv/www", _fs.Log.Last());
        Assert.Equal(28, Ui.Run(() => s.Browser.PaneFontSize));
        Ui.Run(s.Focus);
    }

    [Theory]
    [InlineData(14, 1, "15 pt", 15)]
    [InlineData(14, -3, "11 pt", 11)]
    [InlineData(28, 2, "28 pt", 28)]
    [InlineData(9, -1, "9 pt", 9)]
    public void Zoom_cambia_la_letra_de_los_paneles_y_la_guarda(double start, int steps, string text, double expected)
    {
        var c = Conn();
        c.FontSize = start;
        var s = Create(c);
        Ui.RunAsync(() => s.ConnectAsync(""));
        Assert.Equal(text, Ui.Run(() => s.Zoom(steps)));
        Assert.Equal(expected, c.FontSize);
        Assert.Equal(expected, Ui.Run(() => s.Browser.PaneFontSize));
    }

    [Fact]
    public void Editar_un_fichero_que_ya_no_esta_no_abre_nada()
    {
        var s = Create(Conn());
        Ui.RunAsync(() => s.ConnectAsync(""));
        Ui.RunAsync(() => s.EditFileAsync("/no-esta.txt"));
        Assert.Empty(Ui.Shown);
    }

    [Fact]
    public void Desconectar_cierra_el_servidor_y_acaba_sin_error()
    {
        var s = Create(Conn());
        var ended = new List<string?>();
        s.Ended += ended.Add;
        Ui.RunAsync(() => s.ConnectAsync(""));
        Ui.Run(s.Disconnect);
        Assert.True(_fs.Disposed);
        Assert.Equal([null], ended);
    }

    [Fact]
    public void Un_fallo_al_conectar_deja_el_indicador()
    {
        var c = Conn();
        var s = Ui.Run(() => new FileSession(c, (_, _, _) => Task.FromException<IRemoteFileSystem>(new IOException("sin conexion"))));
        var error = Ui.Run(() => s.ConnectAsync("pw").ContinueWith(t => t.Exception?.InnerException)).Result;
        Assert.Equal("sin conexion", error?.Message);
        Assert.IsType<StackPanel>(Ui.Run(() => ((ContentControl)s.View).Content));
    }

    // ------------------------------------------------------------------ La conexion de verdad (sin servidores reales)

    [Fact]
    public void FTP_de_verdad_contra_el_servidor_de_mentira()
    {
        using var server = new FakeFtpServer();
        server.Dirs.Add("/home");
        server.Dirs.Add("/home/docs");
        var c = Conn(ConnectionKind.Ftp);
        c.Host = "127.0.0.1";
        c.Port = server.Port;
        c.UserName = "pepe";
        c.FilesTimeoutSeconds = 5;
        c.RemotePath = "/home";
        var s = Ui.Run(() => new FileSession(c));
        var titles = new List<string>();
        s.TitleChanged += titles.Add;
        Ui.RunAsync(() => s.ConnectAsync("pw"));
        Assert.Equal(["FTP"], titles);
        Assert.True(server.Received("USER pepe"));
        Ui.Run(s.Disconnect);
    }

    [Fact]
    public async Task SFTP_de_verdad_a_un_puerto_cerrado_falla()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var c = Conn();
        c.Host = "127.0.0.1";
        c.Port = port;
        c.FilesTimeoutSeconds = 5;
        await Assert.ThrowsAnyAsync<Exception>(() => FileSession.OpenAsync(c, "pw", CancellationToken.None));
    }
}
