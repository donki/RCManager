using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Renci.SshNet.Common;
using SocRcManager.Models;
using SocRcManager.Sessions;

namespace SocRcManager.Tests;

/// <summary>SSH.NET por dentro: sin credenciales, el redimensionado por reflexion y un fallo de conexion (sin servidor).</summary>
public sealed class SshShellTests : IDisposable
{
    private readonly TempDir _dir = new();

    public SshShellTests() => Ui.Run(() => Lang.Set("es"));

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Sin_contraseña_ni_clave_no_hay_con_que_entrar()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SshShell.ConnectAsync(new Connection { Host = "127.0.0.1", UserName = "ana" }, "", 80, 24));
        Assert.Equal(Localization.Loc.Get("SshNoCredentials"), ex.Message);
    }

    [Fact]
    public void Redimensionar_pide_window_change_al_canal()
    {
        var stream = new FakeShellStream();
        SshShell.Resize(stream, 132, 43);
        Assert.Equal([(132u, 43u)], stream.WindowChanges);
    }

    [Fact]
    public void Redimensionar_sin_canal_o_con_canal_que_falla_no_rompe()
    {
        SshShell.Resize(new MemoryStream(), 80, 24);
        SshShell.Resize(new BrokenChannelStream(), 80, 24);
    }

    [Fact]
    public async Task Conectar_a_un_puerto_cerrado_falla()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var c = new Connection { Host = "127.0.0.1", Port = port, UserName = "ana" };
        await Assert.ThrowsAnyAsync<Exception>(() => SshShell.ConnectAsync(c, "pw", 80, 24));
    }

    private sealed class BrokenChannelStream : MemoryStream
    {
#pragma warning disable CS0414, IDE0052 // lo lee la reflexion del redimensionado
        private readonly Broken _channel = new();
#pragma warning restore CS0414, IDE0052

        private sealed class Broken
        {
            public void SendWindowChangeRequest(uint columns, uint rows, uint width, uint height) => throw new IOException("canal cerrado");
        }
    }
}

/// <summary>La pestaña SSH con un shell de mentira: lo que llega se pinta, las teclas salen, y el fin de la sesion.</summary>
public sealed class SshSessionTests : UiTest
{
    private readonly FakeShell _shell = new();
    private readonly Connection _c = new() { Name = "Linux", Kind = ConnectionKind.Ssh, Host = "srv.invalid", Port = 22, UserName = "ana" };
    private readonly List<(string Password, int Cols, int Rows)> _opened = [];

    private SshSession Create() => Ui.Run(() => new SshSession(_c, (c, pw, cols, rows) =>
    {
        Assert.Same(_c, c);
        _opened.Add((pw, cols, rows));
        return Task.FromResult<ISshShell>(_shell);
    }));

    private SshSession Connected()
    {
        var s = Create();
        Ui.RunAsync(() => s.ConnectAsync("pw"));
        return s;
    }

    [Theory]
    [InlineData(14, 14)]
    [InlineData(40, 28)]
    [InlineData(5, 9)]
    public void La_letra_de_la_conexion_dentro_de_los_limites(double stored, double expected)
    {
        _c.FontSize = stored;
        var s = Create();
        Assert.Equal(expected, Ui.Run(() => s.Terminal.FontSize));
        Assert.Same(s.Terminal, s.View);
    }

    [Fact]
    public void Lo_basico_de_la_pestaña()
    {
        var s = Create();
        Assert.Equal("Linux", s.Title);
        Assert.False(s.HasNativeFullScreen);
        Assert.True(s.CanZoom);
        var left = 0;
        s.LeftFullScreen += () => left++;
        s.LeftFullScreen -= () => left++;
        Ui.Run(() =>
        {
            s.EnterFullScreen(1);
            s.LeaveFullScreen();
            s.Focus();
        });
        Assert.Equal(0, left);
    }

    [Theory]
    [InlineData(14, 1, "15 pt", 15)]
    [InlineData(14, -2, "12 pt", 12)]
    [InlineData(28, 1, "28 pt", 28)]
    [InlineData(9, -1, "9 pt", 9)]
    public void Zoom_cambia_la_letra_y_la_guarda(double start, int steps, string text, double expected)
    {
        _c.FontSize = start;
        var s = Create();
        Assert.Equal(text, Ui.Run(() => s.Zoom(steps)));
        Assert.Equal(expected, _c.FontSize);
    }

    [Fact]
    public void Conectar_abre_el_shell_con_el_tamaño_del_terminal()
    {
        var s = Connected();
        Assert.Equal([("pw", 80, 24)], _opened);
        Assert.NotNull(s.ReadLoop);
        s.Disconnect();
    }

    [Fact]
    public void Lo_que_llega_se_pinta_en_el_terminal_y_cambia_el_titulo()
    {
        var s = Connected();
        var titles = new List<string>();
        s.TitleChanged += titles.Add;
        _shell.Shell.Receive("hola mundo\r\n");
        _shell.Shell.Receive("\x1b]0;ana@srv: ~\x07$ ");
        Ui.WaitUntil(() => s.Terminal.RowText(1) == "$");
        Assert.Equal("hola mundo", Ui.Run(() => s.Terminal.RowText(0)));
        Assert.Equal(["ana@srv: ~"], titles);
        s.Disconnect();
    }

    [Fact]
    public void Las_teclas_salen_por_el_shell()
    {
        var s = Connected();
        Ui.Run(() =>
        {
            s.Terminal.HandleKey(Key.L, ModifierKeys.Control);
            s.Terminal.HandleKey(Key.Up, ModifierKeys.None);
        });
        Assert.Equal(["\x0c", "\x1b[A"], _shell.Shell.Written.Select(b => System.Text.Encoding.UTF8.GetString(b)));
        Assert.Equal(2, _shell.Shell.Flushes);
        s.Disconnect();
    }

    [Fact]
    public void Una_peticion_del_servidor_se_contesta_por_el_shell()
    {
        var s = Connected();
        _shell.Shell.Receive("\x1b[6n");   // ¿donde esta el cursor?
        Ui.WaitUntil(() => _shell.Shell.Written.Count > 0);
        Assert.Equal("\x1b[1;1R", System.Text.Encoding.UTF8.GetString(_shell.Shell.Written[0]));
        s.Disconnect();
    }

    [Fact]
    public void Error_al_escribir_no_rompe_y_sin_shell_no_se_manda_nada()
    {
        var s = Create();
        Ui.Run(() => s.Terminal.HandleKey(Key.Enter, ModifierKeys.None));   // aun sin conectar
        Ui.RunAsync(() => s.ConnectAsync("pw"));
        _shell.Shell.WriteError = new IOException("cerrado");
        Ui.Run(() => s.Terminal.HandleKey(Key.Enter, ModifierKeys.None));
        Assert.Empty(_shell.Shell.Written);
        s.Disconnect();
    }

    [Fact]
    public void Redimensionar_el_terminal_avisa_al_servidor()
    {
        var s = Create();
        var holder = Ui.Run(() =>
        {
            var b = new Border { Width = 400, Height = 300, Child = s.View };
            Ui.Show(new Window { Content = b, SizeToContent = SizeToContent.WidthAndHeight });
            return b;
        });
        Ui.RunAsync(() => s.ConnectAsync("pw"));
        Ui.Run(() => { holder.Width = 900; holder.Height = 600; });
        var (cols, rows) = Ui.Run(() => (s.Terminal.Cols, s.Terminal.Rows));
        Assert.Equal((uint)cols, _shell.Shell.WindowChanges.Last().Cols);
        Assert.Equal((uint)rows, _shell.Shell.WindowChanges.Last().Rows);
        Assert.True(cols > 80 || rows > 24);
        s.Disconnect();
    }

    [Fact]
    public void El_servidor_cierra_y_la_sesion_se_acaba_sin_error()
    {
        var s = Connected();
        var ended = new List<string?>();
        s.Ended += ended.Add;
        _shell.Shell.End();
        Assert.True(s.ReadLoop!.Wait(5000));
        Ui.Flush();
        Assert.Equal([null], ended);
    }

    [Fact]
    public void Un_fallo_de_lectura_acaba_la_sesion_con_el_motivo()
    {
        var s = Connected();
        var ended = new List<string?>();
        s.Ended += ended.Add;
        _shell.Shell.Fail(new SshConnectionException("conexion perdida"));
        Assert.True(s.ReadLoop!.Wait(5000));
        Ui.Flush();
        Assert.Equal(["conexion perdida"], ended);
    }

    [Fact]
    public void Desconectar_cierra_todo_sin_avisar_de_fin()
    {
        var s = Connected();
        var ended = new List<string?>();
        s.Ended += ended.Add;
        _shell.CloseError = new SshConnectionException("ya cerrado");
        s.Disconnect();
        Assert.True(s.ReadLoop!.Wait(5000));
        Ui.Flush();
        Assert.Empty(ended);
        Assert.True(_shell.Shell.Disposed);
        Assert.Equal(1, _shell.Closed);

        s.Disconnect();   // otra vez: no hay nada que cerrar
        Assert.Equal(1, _shell.Closed);
    }

    [Fact]
    public void Desconectar_sin_haber_conectado()
    {
        var s = Create();
        s.Disconnect();
        Assert.Equal(0, _shell.Closed);
    }

    [Fact]
    public void La_sesion_de_verdad_sin_credenciales_no_conecta()
    {
        var s = Ui.Run(() => new SshSession(new Connection { Name = "x", Kind = ConnectionKind.Ssh, Host = "127.0.0.1", UserName = "ana" }));
        var error = Ui.Run(() => s.ConnectAsync("").ContinueWith(t => t.Exception?.InnerException)).Result;
        Assert.IsType<InvalidOperationException>(error);
    }
}
