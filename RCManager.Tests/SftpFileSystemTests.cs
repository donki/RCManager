using System.Net;
using System.Net.Sockets;
using Renci.SshNet;
using Renci.SshNet.Common;
using SocRcManager.Files;
using SocRcManager.Models;

namespace SocRcManager.Tests;

/// <summary>
/// <see cref="SftpFileSystem"/> con los clientes de SSH.NET sustituidos por un servidor en memoria
/// (<see cref="MemorySftp"/>, <see cref="MemoryScp"/>): nunca se conecta a nada.
/// </summary>
public sealed class SftpFileSystemTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly MemorySftp _server = new();
    private MemoryScp? _scp;
    private readonly List<(ConnectionInfo Info, string Command)> _commands = [];
    private (int Status, string Error) _commandResult = (0, string.Empty);

    public SftpFileSystemTests()
    {
        _server.Dir("/home").Dir("/home/pepe", uid: 1000, gid: 1000)
            .File("/home/pepe/a.txt", "hola sftp")
            .Dir("/home/pepe/docs", uid: 1000, gid: 1000)
            .File("/home/pepe/docs/b.txt", "bbb")
            .Dir("/home/pepe/docs/sub")
            .File("/home/pepe/docs/sub/c.txt", "c");
    }

    public void Dispose() => _dir.Dispose();

    private SshClients Clients() => new(
        _ => _server.Client(),
        _ => _scp = new MemoryScp(_server),
        (info, command) => { _commands.Add((info, command)); return _commandResult; });

    private static Connection Conn(bool scp = false, int keepAlive = 0, int timeout = 20) => new()
    {
        Kind = ConnectionKind.Ssh, Host = "servidor", Port = 22, UserName = "pepe", UseScp = scp,
        FilesKeepAliveSeconds = keepAlive, FilesTimeoutSeconds = timeout,
    };

    private Task<SftpFileSystem> Open(bool scp = false, int keepAlive = 0, int timeout = 20) =>
        SftpFileSystem.ConnectAsync(Conn(scp, keepAlive, timeout), "pw", Clients(), CancellationToken.None);

    // --- Conexion ---

    [Fact]
    public async Task Conecta_con_sus_tiempos_y_entra_en_el_home()
    {
        using var fs = await Open(keepAlive: 15, timeout: 30);
        Assert.True(_server.Connected);
        Assert.Equal("/home/pepe", fs.InitialDirectory);
        Assert.Equal(TimeSpan.FromSeconds(30), _server.OperationTimeout);
        Assert.Equal(TimeSpan.FromSeconds(15), _server.KeepAliveInterval);
        Assert.True(fs.SupportsPermissions);
        Assert.Null(_scp);   // sin SCP no se abre una segunda conexion
    }

    [Fact]
    public async Task Sin_keep_alive_no_se_toca_y_el_tiempo_nunca_baja_de_5_s()
    {
        using var fs = await Open(keepAlive: 0, timeout: 1);
        Assert.Null(_server.KeepAliveInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), _server.OperationTimeout);
    }

    [Fact]
    public async Task Con_SCP_abre_las_dos_conexiones_y_al_cerrar_cierra_las_dos()
    {
        var fs = await Open(scp: true);
        Assert.NotNull(_scp);
        Assert.True(_scp.Connected);
        fs.Dispose();
        Assert.Equal(["connect", "disconnect", "dispose"], _scp.Log);
        Assert.Equal(["connect", "disconnect", "dispose"], _server.Log);
    }

    [Fact]
    public async Task Cerrar_no_lanza_aunque_la_conexion_ya_este_rota()
    {
        var fs = await Open(scp: true);
        _scp!.DisconnectError = new SshConnectionException("rota");
        _server.DisconnectError = new SshConnectionException("rota");
        fs.Dispose();
        // Aunque la desconexion falle, los clientes se liberan.
        Assert.True(_scp.Disposed);
        Assert.True(_server.Disposed);
    }

    [Fact]
    public async Task Si_no_conecta_lanza_y_no_abre_SCP()
    {
        _server.ConnectError = new SshAuthenticationException("Permission denied (password).");
        var ex = await Assert.ThrowsAsync<SshAuthenticationException>(() => Open(scp: true));
        Assert.Contains("Permission denied", ex.Message);
        Assert.Null(_scp);
    }

    [Fact]
    public async Task Sin_credenciales_ni_lo_intenta()
    {
        var c = Conn();
        c.PrivateKeyPath = string.Empty;
        await Assert.ThrowsAsync<InvalidOperationException>(() => SftpFileSystem.ConnectAsync(c, "", Clients(), CancellationToken.None));
        Assert.Empty(_server.Log);
    }

    [Fact]
    public async Task Cancelada_antes_de_conectar_no_conecta()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SftpFileSystem.ConnectAsync(Conn(), "pw", Clients(), cts.Token));
        Assert.False(_server.Connected);
    }

    [Fact]
    public void Los_clientes_de_verdad_se_crean_sin_conectar()
    {
        var info = SshAuth.Build(Conn(), "pw");
        using var sftp = (IDisposable)SshClients.Real.Sftp(info);
        Assert.IsType<SftpClient>(sftp);
        Assert.False(((SftpClient)sftp).IsConnected);
        using var scp = SshClients.Real.Scp(info);
        Assert.IsAssignableFrom<ScpClient>(scp);
        Assert.False(((ScpClient)scp).IsConnected);
    }

    /// <summary>Un puerto local que contesta algo que no es SSH y cuelga: SSH.NET no llega ni a autenticarse.</summary>
    private static void WithNotSsh(Action<Connection> test)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    await client.GetStream().WriteAsync("HTTP/1.0 400 Nada que ver\r\n\r\n"u8.ToArray());
                }
            }
            catch (Exception) { }
        });
        try
        {
            var c = Conn();
            c.Host = "127.0.0.1";
            c.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            c.FilesTimeoutSeconds = 5;
            test(c);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void La_orden_por_SSH_falla_si_al_otro_lado_no_hay_un_servidor_SSH() =>
        WithNotSsh(c => Assert.ThrowsAny<Exception>(() => SshClients.RunCommand(SshAuth.Build(c, "pw"), "true")));

    [Fact]
    public void Conectar_de_verdad_a_algo_que_no_es_SSH_falla() =>
        WithNotSsh(c => Assert.ThrowsAny<Exception>(() => SftpFileSystem.ConnectAsync(c, "pw", CancellationToken.None).GetAwaiter().GetResult()));

    // --- Listado y stat ---

    [Fact]
    public async Task Lista_sin_punto_ni_punto_punto_con_permisos_y_uid()
    {
        using var fs = await Open();
        var list = await fs.ListAsync("/home/pepe", CancellationToken.None);
        Assert.Equal(["docs", "a.txt"], list.Select(e => e.Name));
        Assert.True(list[0].IsDirectory);
        Assert.Equal(("rwxr-xr-x", "1000", "1000"), (list[0].ModeText, list[0].Owner, list[0].Group));
        Assert.Equal(("/home/pepe/a.txt", 9L, "rw-r--r--"), (list[1].FullPath, list[1].Size, list[1].ModeText));
    }

    [Fact]
    public async Task Enlace_a_un_directorio_es_directorio_y_uno_roto_se_queda_como_fichero()
    {
        _server.Links["/home/pepe/www"] = "/home/pepe/docs";
        _server.Links["/home/pepe/roto"] = null;
        using var fs = await Open();
        var list = await fs.ListAsync("/home/pepe", CancellationToken.None);
        Assert.True(list.Single(e => e.Name == "www").IsDirectory);
        Assert.False(list.Single(e => e.Name == "roto").IsDirectory);
    }

    [Fact]
    public async Task Listar_lo_que_no_existe_o_no_se_puede_lanza()
    {
        _server.Dir("/root");
        _server.Denied.Add("/root");
        using var fs = await Open();
        await Assert.ThrowsAsync<SftpPathNotFoundException>(() => fs.ListAsync("/no/existe", CancellationToken.None));
        await Assert.ThrowsAsync<SftpPermissionDeniedException>(() => fs.ListAsync("/root", CancellationToken.None));
    }

    [Fact]
    public async Task Stat_de_fichero_directorio_y_lo_que_no_existe()
    {
        _server.Modified["/home/pepe/a.txt"] = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        using var fs = await Open();
        var a = await fs.StatAsync("/home/pepe/a.txt", CancellationToken.None);
        Assert.Equal(("a.txt", "/home/pepe/a.txt", false, 9L), (a!.Name, a.FullPath, a.IsDirectory, a.Size));
        Assert.Equal(new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc).ToLocalTime(), a.Modified);
        var d = await fs.StatAsync("/home/pepe/docs", CancellationToken.None);
        Assert.True(d!.IsDirectory);
        Assert.Equal("docs", d.Name);
        Assert.Null(await fs.StatAsync("/home/pepe/nada", CancellationToken.None));
    }

    [Fact]
    public async Task Stat_sin_permiso_lanza_no_dice_que_no_existe()
    {
        _server.Denied.Add("/home/pepe/a.txt");
        using var fs = await Open();
        await Assert.ThrowsAsync<SftpPermissionDeniedException>(() => fs.StatAsync("/home/pepe/a.txt", CancellationToken.None));
    }

    // --- Transferencias ---

    [Fact]
    public async Task Baja_por_SFTP_con_progreso_en_incrementos()
    {
        using var fs = await Open();
        var local = Path.Combine(_dir.Path, "a.txt");
        var progress = new ProgressLog();
        await fs.DownloadAsync("/home/pepe/a.txt", local, progress, CancellationToken.None);
        Assert.Equal("hola sftp", File.ReadAllText(local));
        Assert.Equal([4L, 4L, 1L], progress.Reports);   // lo nuevo de cada aviso, no el acumulado
    }

    [Fact]
    public async Task Sube_por_SFTP_con_progreso_y_sobrescribe()
    {
        using var fs = await Open();
        var local = _dir.File("nuevo.txt", "0123456789");
        var progress = new ProgressLog();
        await fs.UploadAsync(local, "/home/pepe/a.txt", progress, CancellationToken.None);
        Assert.Equal("0123456789", _server.Text("/home/pepe/a.txt"));
        Assert.Equal([4L, 4L, 2L], progress.Reports);
    }

    [Fact]
    public async Task Baja_y_sube_por_SCP_si_la_conexion_lo_pide()
    {
        using var fs = await Open(scp: true);
        var local = Path.Combine(_dir.Path, "a.txt");
        var down = new ProgressLog();
        await fs.DownloadAsync("/home/pepe/a.txt", local, down, CancellationToken.None);
        Assert.Equal("hola sftp", File.ReadAllText(local));
        Assert.Equal(9L, down.Total);
        Assert.Equal([4L, 4L, 1L], down.Reports);

        File.WriteAllText(local, "por scp");
        var up = new ProgressLog();
        await fs.UploadAsync(local, "/home/pepe/scp.txt", up, CancellationToken.None);
        Assert.Equal("por scp", _server.Text("/home/pepe/scp.txt"));
        Assert.Equal([4L, 3L], up.Reports);

        Assert.Equal(["connect", "scp get /home/pepe/a.txt", "scp put /home/pepe/scp.txt"], _scp!.Log);
        Assert.DoesNotContain(_server.Log, l => l.StartsWith("get ") || l.StartsWith("put "));
        Assert.False(_scp.HasListeners);   // se quita de los eventos al acabar
    }

    [Fact]
    public async Task Un_fallo_de_SCP_llega_y_se_quita_de_los_eventos()
    {
        using var fs = await Open(scp: true);
        await Assert.ThrowsAsync<ScpException>(() => fs.DownloadAsync("/home/pepe/nada", Path.Combine(_dir.Path, "x"), new ProgressLog(), CancellationToken.None));
        Assert.False(_scp!.HasListeners);
    }

    [Fact]
    public async Task Bajar_lo_que_no_existe_lanza()
    {
        using var fs = await Open();
        await Assert.ThrowsAsync<SftpPathNotFoundException>(() => fs.DownloadAsync("/home/pepe/nada", Path.Combine(_dir.Path, "x"), new ProgressLog(), CancellationToken.None));
    }

    [Fact]
    public async Task Subir_un_fichero_local_que_no_existe_lanza_sin_tocar_el_servidor()
    {
        using var fs = await Open();
        await Assert.ThrowsAsync<FileNotFoundException>(() => fs.UploadAsync(Path.Combine(_dir.Path, "no-esta"), "/home/pepe/x", new ProgressLog(), CancellationToken.None));
        Assert.DoesNotContain(_server.Log, l => l.StartsWith("put "));
    }

    [Fact]
    public async Task Subir_a_un_directorio_que_no_existe_lanza()
    {
        using var fs = await Open();
        var local = _dir.File("x.txt", "x");
        await Assert.ThrowsAsync<SftpPathNotFoundException>(() => fs.UploadAsync(local, "/no/hay/x.txt", new ProgressLog(), CancellationToken.None));
    }

    [Fact]
    public async Task Cancelada_antes_de_empezar_no_transfiere_nada()
    {
        using var fs = await Open();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var local = _dir.File("x.txt", "x");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fs.DownloadAsync("/home/pepe/a.txt", Path.Combine(_dir.Path, "y"), new ProgressLog(), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fs.UploadAsync(local, "/home/pepe/x.txt", new ProgressLog(), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fs.ListAsync("/home/pepe", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fs.DeleteFileAsync("/home/pepe/a.txt", cts.Token));
        Assert.Equal(["connect"], _server.Log);
        Assert.False(File.Exists(Path.Combine(_dir.Path, "y")));
    }

    // --- Directorios, borrar, renombrar, fecha ---

    [Fact]
    public async Task Crea_renombra_y_borra()
    {
        using var fs = await Open();
        await fs.CreateDirectoryAsync("/home/pepe/nueva", CancellationToken.None);
        Assert.Contains("/home/pepe/nueva", _server.Dirs);
        await Assert.ThrowsAsync<SshException>(() => fs.CreateDirectoryAsync("/home/pepe/nueva", CancellationToken.None));

        await fs.RenameAsync("/home/pepe/a.txt", "/home/pepe/nueva/a2.txt", CancellationToken.None);
        Assert.Equal("hola sftp", _server.Text("/home/pepe/nueva/a2.txt"));
        Assert.False(_server.Files.ContainsKey("/home/pepe/a.txt"));
        await Assert.ThrowsAsync<SftpPathNotFoundException>(() => fs.RenameAsync("/home/pepe/a.txt", "/home/pepe/z", CancellationToken.None));

        await Assert.ThrowsAsync<SshException>(() => fs.DeleteDirectoryAsync("/home/pepe/nueva", CancellationToken.None));   // no esta vacio
        await fs.DeleteFileAsync("/home/pepe/nueva/a2.txt", CancellationToken.None);
        await fs.DeleteDirectoryAsync("/home/pepe/nueva", CancellationToken.None);
        Assert.DoesNotContain("/home/pepe/nueva", _server.Dirs);
    }

    [Fact]
    public async Task Borrar_un_directorio_con_todo_lo_de_dentro()
    {
        using var fs = await Open();
        var docs = (await fs.ListAsync("/home/pepe", CancellationToken.None)).Single(e => e.Name == "docs");
        await FileRules.DeleteRecursiveAsync(new RemoteSide(fs), docs, CancellationToken.None);
        Assert.DoesNotContain(_server.Dirs, d => d.StartsWith("/home/pepe/docs"));
        Assert.DoesNotContain(_server.Files.Keys, f => f.StartsWith("/home/pepe/docs"));
        Assert.True(_server.Files.ContainsKey("/home/pepe/a.txt"));
        // Primero lo de dentro, luego el directorio.
        var order = _server.Log.Where(l => l.StartsWith("rm")).ToList();
        Assert.Equal(["rm /home/pepe/docs/sub/c.txt", "rmdir /home/pepe/docs/sub", "rm /home/pepe/docs/b.txt", "rmdir /home/pepe/docs"], order);
    }

    [Fact]
    public async Task Borrar_sin_permiso_lanza()
    {
        _server.Denied.Add("/home/pepe/a.txt");
        using var fs = await Open();
        await Assert.ThrowsAsync<SftpPermissionDeniedException>(() => fs.DeleteFileAsync("/home/pepe/a.txt", CancellationToken.None));
        Assert.True(_server.Files.ContainsKey("/home/pepe/a.txt"));
    }

    [Fact]
    public async Task Pone_la_fecha_de_modificacion()
    {
        using var fs = await Open();
        var when = new DateTime(2025, 12, 24, 20, 0, 0, DateTimeKind.Local);
        await fs.SetModifiedAsync("/home/pepe/a.txt", when, CancellationToken.None);
        Assert.Equal(when, _server.Modified["/home/pepe/a.txt"]);
    }

    // --- Permisos y propietario ---

    [Fact]
    public async Task Chmod_con_los_bits()
    {
        using var fs = await Open();
        await fs.ChangeModeAsync("/home/pepe/a.txt", 0b111_101_000, CancellationToken.None);
        Assert.Equal(0b111_101_000, _server.Attrs["/home/pepe/a.txt"].Mode);
        Assert.Contains("chmod 750 /home/pepe/a.txt", _server.Log);
    }

    [Fact]
    public async Task Chown_por_numero_va_por_SFTP_sin_abrir_SSH()
    {
        using var fs = await Open();
        await fs.ChangeOwnerAsync("/home/pepe/a.txt", "33", "", CancellationToken.None);
        Assert.Equal((33, 1000), (_server.Attrs["/home/pepe/a.txt"].Uid, _server.Attrs["/home/pepe/a.txt"].Gid));
        await fs.ChangeOwnerAsync("/home/pepe/a.txt", "0", "50", CancellationToken.None);
        Assert.Equal((0, 50), (_server.Attrs["/home/pepe/a.txt"].Uid, _server.Attrs["/home/pepe/a.txt"].Gid));
        Assert.Equal(0b110_100_100, _server.Attrs["/home/pepe/a.txt"].Mode);   // los permisos no se tocan
        Assert.Empty(_commands);
    }

    [Fact]
    public async Task Chown_por_nombre_lanza_chown_por_SSH_con_comillas()
    {
        using var fs = await Open();
        await fs.ChangeOwnerAsync("/home/pepe/a b's.txt", "www-data", "web", CancellationToken.None);
        await fs.ChangeOwnerAsync("/home/pepe/a.txt", "www-data", "", CancellationToken.None);
        await fs.ChangeOwnerAsync("/home/pepe/a.txt", "1000", "staff", CancellationToken.None);   // numero con grupo por nombre: por SSH
        Assert.Equal(["chown 'www-data:web' '/home/pepe/a b'\\''s.txt'", "chown 'www-data' '/home/pepe/a.txt'", "chown '1000:staff' '/home/pepe/a.txt'"],
            _commands.Select(c => c.Command));
        var info = _commands[0].Info;
        Assert.Equal(("servidor", 22, "pepe"), (info.Host, info.Port, info.Username));
        Assert.DoesNotContain(_server.Log, l => l.StartsWith("setstat"));
    }

    [Fact]
    public async Task Chown_que_falla_lanza_con_lo_que_dijo_el_servidor()
    {
        using var fs = await Open();
        _commandResult = (1, "chown: invalid user: 'nadie'\n");
        var ex = await Assert.ThrowsAsync<IOException>(() => fs.ChangeOwnerAsync("/home/pepe/a.txt", "nadie", "", CancellationToken.None));
        Assert.Equal("chown: invalid user: 'nadie'", ex.Message);
        _commandResult = (-1, "  \n");
        ex = await Assert.ThrowsAsync<IOException>(() => fs.ChangeOwnerAsync("/home/pepe/a.txt", "nadie", "", CancellationToken.None));
        Assert.Equal("chown: -1", ex.Message);
    }

    [Fact]
    public async Task Permisos_recursivos_bajan_a_todo_lo_de_dentro()
    {
        using var fs = await Open();
        var docs = (await fs.ListAsync("/home/pepe", CancellationToken.None)).Single(e => e.Name == "docs");
        var change = PermissionChange.From(0b111_000_000, docs.Mode, "7", "1000", "8", "1000", recursive: true);
        await FileRules.ApplyPermissionsAsync(new RemoteSide(fs), docs, change, CancellationToken.None);
        foreach (var p in new[] { "/home/pepe/docs", "/home/pepe/docs/b.txt", "/home/pepe/docs/sub", "/home/pepe/docs/sub/c.txt" })
            Assert.Equal((0b111_000_000, 7, 8), _server.Attrs[p]);
        Assert.Equal(0b110_100_100, _server.Attrs["/home/pepe/a.txt"].Mode);
    }

    // --- Autenticacion ---

    [Fact]
    public void Teclado_interactivo_contesta_la_contraseña_a_cada_pregunta()
    {
        var info = SshAuth.Build(Conn(), "secreta");
        var kbd = info.AuthenticationMethods.OfType<KeyboardInteractiveAuthenticationMethod>().Single();
        var prompts = new[] { new AuthenticationPrompt(1, false, "Password: "), new AuthenticationPrompt(2, false, "Verification: ") };
        var handler = (EventHandler<AuthenticationPromptEventArgs>?)typeof(KeyboardInteractiveAuthenticationMethod)
            .GetField("AuthenticationPrompt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(kbd);
        handler!.Invoke(kbd, new AuthenticationPromptEventArgs("pepe", "", "", prompts));
        Assert.All(prompts, p => Assert.Equal("secreta", p.Response));
    }
}
