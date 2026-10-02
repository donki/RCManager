using SocRcManager.Files;
using SocRcManager.Localization;
using SocRcManager.Models;

namespace SocRcManager.Tests;

/// <summary>El motor de transferencias del explorador, sin interfaz: un servidor en memoria y carpetas temporales.</summary>
public sealed class FileTransferTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly MemServer _server = new();
    private readonly RemoteSide _remote;
    private readonly LocalSide _local;
    private readonly FileTransfer _engine;
    private readonly List<TransferProgress> _reports = [];
    private int _opened;

    public FileTransferTests()
    {
        _remote = new RemoteSide(_server);
        _local = new LocalSide(_dir.Path);
        _engine = new FileTransfer(_remote, _ => { _opened++; return Task.FromResult<IRemoteFileSystem>(_server.Another()); }) { RetryDelay = TimeSpan.FromMilliseconds(1) };
    }

    public void Dispose() => _dir.Dispose();

    private static ConflictWindow.Answer NoAsk(string name) => throw new Xunit.Sdk.XunitException("No deberia preguntar por " + name);

    private void Report(TransferProgress p)
    {
        lock (_reports)
            _reports.Add(p);
    }

    /// <summary>El progreso llega por Progress (en otro hilo): se espera a que cuadre.</summary>
    private void WaitReportsUntil(Func<TransferProgress, bool> last)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            lock (_reports)
                if (_reports.Count > 0 && last(_reports[^1]))
                    return;
            if (DateTime.UtcNow > until)
                throw new TimeoutException("El progreso no llego: " + string.Join(" | ", _reports.Select(r => $"{r.Name} {r.Done}/{r.Total}")));
            Thread.Sleep(10);
        }
    }

    private static FileEntry Local(string path) =>
        Directory.Exists(path)
            ? new FileEntry(Path.GetFileName(path), path, true, 0, Directory.GetLastWriteTime(path))
            : new FileEntry(Path.GetFileName(path), path, false, new FileInfo(path).Length, File.GetLastWriteTime(path));

    [Fact]
    public async Task Subir_UnaCarpetaEntera_CreaLasCarpetasYSubeConFechas()
    {
        _dir.File("src/a.txt", "hola");
        _dir.File("src/sub/b.txt", "adios mundo");
        var when = new DateTime(2024, 5, 6, 7, 8, 9);
        File.SetLastWriteTime(Path.Combine(_dir.Path, "src", "a.txt"), when);

        var options = new Connection { TransferParallel = 1, TransferPreserveTimes = true };
        await _engine.RunAsync(_local, _remote, "/home/u", [Local(Path.Combine(_dir.Path, "src"))], options, NoAsk, Report, CancellationToken.None);

        Assert.True(_server.HasDir("/home/u/src"));
        Assert.True(_server.HasDir("/home/u/src/sub"));
        Assert.Equal("hola", _server.Text("/home/u/src/a.txt"));
        Assert.Equal("adios mundo", _server.Text("/home/u/src/sub/b.txt"));
        Assert.Equal(when, _server.ModifiedOf("/home/u/src/a.txt"));
        // Las carpetas antes que los ficheros, y todo por la conexion principal (una a la vez).
        Assert.True(_server.Log.IndexOf("mkdir /home/u/src/sub") < _server.Log.IndexOf("put /home/u/src/sub/b.txt"));
        Assert.Equal([_server], _engine.Pool);
        Assert.Equal(0, _opened);
        WaitReportsUntil(r => r.Done == 15);
        Assert.All(_reports, r => Assert.True(r.Upload));
        Assert.Equal(15, _reports[^1].Total);
        Assert.Equal(100, _reports[^1].Percent);
    }

    [Fact]
    public async Task Bajar_ConservaLaFechaEnElDisco_YSinConservar_No()
    {
        var when = new DateTime(2023, 1, 2, 3, 4, 5);
        _server.File("/home/u/r.log", "linea", when).File("/home/u/s.log", "otra", when);
        var r = (await _server.ListAsync("/home/u", default)).Single(e => e.Name == "r.log");
        var s = (await _server.ListAsync("/home/u", default)).Single(e => e.Name == "s.log");

        await _engine.RunAsync(_remote, _local, _dir.Path, [r], new Connection { TransferPreserveTimes = true }, NoAsk, Report, default);
        await _engine.RunAsync(_remote, _local, _dir.Path, [s], new Connection { TransferPreserveTimes = false }, NoAsk, Report, default);

        Assert.Equal("linea", File.ReadAllText(Path.Combine(_dir.Path, "r.log")));
        Assert.Equal(when, File.GetLastWriteTime(Path.Combine(_dir.Path, "r.log")));
        Assert.NotEqual(when, File.GetLastWriteTime(Path.Combine(_dir.Path, "s.log")));
        WaitReportsUntil(p => p.Name == "s.log" && p.Done == 4);
        Assert.All(_reports, p => Assert.False(p.Upload));
    }

    [Fact]
    public async Task VariosALaVez_AbreConexionesHastaElLimite_YLasReutiliza()
    {
        for (var i = 0; i < 5; i++)
            _dir.File($"f{i}.txt", new string('x', 100));
        var entries = Enumerable.Range(0, 5).Select(i => Local(Path.Combine(_dir.Path, $"f{i}.txt"))).ToList();
        _server.HoldTransfers();

        var run = _engine.RunAsync(_local, _remote, "/home/u", entries, new Connection { TransferParallel = 3 }, NoAsk, Report, default);
        var until = DateTime.UtcNow.AddSeconds(5);
        while (_server.MaxRunning < 3 && DateTime.UtcNow < until)
            await Task.Delay(10);
        _server.Release();
        await run;

        Assert.Equal(3, _server.MaxRunning);
        Assert.Equal(3, _engine.Pool.Count);   // la principal y dos mas
        Assert.Equal(2, _opened);
        Assert.Equal(5, Enumerable.Range(0, 5).Count(i => _server.HasFile($"/home/u/f{i}.txt")));
        WaitReportsUntil(p => p.Done == 500);
        lock (_reports)
            Assert.Contains(_reports, p => p.Active > 1 && p.Text.EndsWith($"×{p.Active}"));

        // Otra tanda: no abre ninguna conexion nueva.
        await _engine.RunAsync(_local, _remote, "/home/u", entries, new Connection { TransferParallel = 3, TransferOnConflict = 1 }, NoAsk, Report, default);
        Assert.Equal(2, _opened);
    }

    [Fact]
    public async Task Conflicto_Sobrescribir_Saltar_YPreguntar()
    {
        _dir.File("a.txt", "nuevo");
        _dir.File("b.txt", "nuevo");
        _server.File("/home/u/a.txt", "viejo").File("/home/u/b.txt", "viejo");
        var entries = new[] { Local(Path.Combine(_dir.Path, "a.txt")), Local(Path.Combine(_dir.Path, "b.txt")) };

        // Saltar: nada se sube, y el total no cuenta lo saltado.
        await _engine.RunAsync(_local, _remote, "/home/u", entries, new Connection { TransferOnConflict = 2 }, NoAsk, Report, default);
        Assert.Equal("viejo", _server.Text("/home/u/a.txt"));
        Assert.DoesNotContain(_server.Log, l => l.StartsWith("put"));

        // Preguntar: al primero «saltar», al segundo «sobrescribir».
        var asked = new List<string>();
        var answers = new Queue<ConflictWindow.Answer>([new(false, false, false), new(true, false, false)]);
        await _engine.RunAsync(_local, _remote, "/home/u", entries, new Connection { TransferOnConflict = 0 }, n => { asked.Add(n); return answers.Dequeue(); }, Report, default);
        Assert.Equal(["a.txt", "b.txt"], asked);
        Assert.Equal("viejo", _server.Text("/home/u/a.txt"));
        Assert.Equal("nuevo", _server.Text("/home/u/b.txt"));

        // Preguntar con «para todos»: una sola pregunta.
        _server.File("/home/u/b.txt", "viejo");
        asked.Clear();
        await _engine.RunAsync(_local, _remote, "/home/u", entries, new Connection { TransferOnConflict = 0 }, n => { asked.Add(n); return new(true, true, false); }, Report, default);
        Assert.Equal(["a.txt"], asked);
        Assert.Equal("nuevo", _server.Text("/home/u/a.txt"));
        Assert.Equal("nuevo", _server.Text("/home/u/b.txt"));

        // Sobrescribir (opcion fuera de rango se recorta a 2 = saltar; 1 = sobrescribir).
        _server.File("/home/u/a.txt", "viejo");
        await _engine.RunAsync(_local, _remote, "/home/u", entries[..1], new Connection { TransferOnConflict = 9 }, NoAsk, Report, default);
        Assert.Equal("viejo", _server.Text("/home/u/a.txt"));
        await _engine.RunAsync(_local, _remote, "/home/u", entries[..1], new Connection { TransferOnConflict = 1 }, NoAsk, Report, default);
        Assert.Equal("nuevo", _server.Text("/home/u/a.txt"));
    }

    [Fact]
    public async Task Conflicto_AlBajar_MiraElDisco_YCancelarPara()
    {
        _server.File("/home/u/x.txt", "remoto");
        _dir.File("x.txt", "local");
        var x = (await _server.ListAsync("/home/u", default)).Single();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _engine.RunAsync(_remote, _local, _dir.Path, [x], new Connection(), _ => new(false, false, true), Report, default));
        Assert.Equal("local", File.ReadAllText(Path.Combine(_dir.Path, "x.txt")));
        Assert.DoesNotContain(_server.Log, l => l.StartsWith("get"));
    }

    [Fact]
    public async Task Reintentos_ElFalloAMediasSeDescuentaDelProgreso()
    {
        _dir.File("r.bin", new string('z', 40));
        _server.FailTimes("/home/u/r.bin", 1);
        await _engine.RunAsync(_local, _remote, "/home/u", [Local(Path.Combine(_dir.Path, "r.bin"))], new Connection { TransferRetries = 1 }, NoAsk, Report, default);

        Assert.True(_server.HasFile("/home/u/r.bin"));
        Assert.Equal(2, _server.Log.Count(l => l == "put /home/u/r.bin"));
        WaitReportsUntil(p => p.Done == 40);
        lock (_reports)
            Assert.All(_reports, p => Assert.InRange(p.Done, 0, 40));   // nunca pasa del total

        // Sin reintentos, el fallo sube.
        _server.FailTimes("/home/u/r.bin", 1);
        var ex = await Assert.ThrowsAsync<IOException>(() =>
            _engine.RunAsync(_local, _remote, "/home/u", [Local(Path.Combine(_dir.Path, "r.bin"))], new Connection { TransferRetries = 0, TransferOnConflict = 1 }, NoAsk, Report, default));
        Assert.Equal("Connection reset", ex.Message);
    }

    [Fact]
    public async Task FechasYCarpetasQueYaExisten_NoParan()
    {
        _dir.File("d/a.txt", "1");
        _server.Folder("/home/u/d");
        _server.Fail("touch", new NotSupportedException("SETSTAT"));
        await _engine.RunAsync(_local, _remote, "/home/u", [Local(Path.Combine(_dir.Path, "d"))], new Connection { TransferPreserveTimes = true }, NoAsk, Report, default);
        Assert.Equal("1", _server.Text("/home/u/d/a.txt"));
        Assert.Contains("mkdir /home/u/d", _server.Log);
        Assert.Contains("touch /home/u/d/a.txt", _server.Log);
    }

    [Fact]
    public async Task Cancelar_ParaLoQueEstaEnCurso()
    {
        _dir.File("c.txt", "contenido");
        _server.HoldTransfers();
        using var cts = new CancellationTokenSource();
        var run = _engine.RunAsync(_local, _remote, "/home/u", [Local(Path.Combine(_dir.Path, "c.txt"))], new Connection(), NoAsk, Report, cts.Token);
        var until = DateTime.UtcNow.AddSeconds(5);
        while (_server.MaxRunning < 1 && DateTime.UtcNow < until)
            await Task.Delay(10);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.False(_server.HasFile("/home/u/c.txt"));

        // Con el token ya cancelado no se pregunta ni se crea nada.
        _server.File("/home/u/c.txt", "x");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _engine.RunAsync(_local, _remote, "/home/u", [Local(Path.Combine(_dir.Path, "c.txt"))], new Connection(), NoAsk, Report, cts.Token));
    }

    [Fact]
    public async Task Shutdown_CierraLasConexiones_AunqueAlgunaFalle()
    {
        // Sin usar: se cierra la principal.
        new FileTransfer(_remote, _ => throw new InvalidOperationException()).Shutdown();
        Assert.True(_server.Disposed);

        var other = new MemServer();
        var engine = new FileTransfer(new RemoteSide(other), _ => Task.FromResult<IRemoteFileSystem>(other.Another()));
        for (var i = 0; i < 2; i++)
            _dir.File($"s{i}.txt", "x");
        other.HoldTransfers();
        var run = engine.RunAsync(_local, engine.Remote, "/home/u", [Local(Path.Combine(_dir.Path, "s0.txt")), Local(Path.Combine(_dir.Path, "s1.txt"))], new Connection { TransferParallel = 2 }, NoAsk, Report, default);
        other.Release();
        await run;
        Assert.Equal(2, engine.Pool.Count);
        other.Fail("dispose", new IOException("ya cerrada"));
        engine.Shutdown();
        Assert.All(other.Connections, c => Assert.True(c.Disposed));
    }

    [Fact]
    public void Progreso_PorcentajeYTexto()
    {
        Assert.Equal(0, new TransferProgress(true, "a", 5, 0, 1).Percent);
        Assert.Equal(50, new TransferProgress(true, "a", 5, 10, 1).Percent);
        Assert.Equal(100, new TransferProgress(true, "a", 50, 10, 1).Percent);
        Assert.Equal(Loc.Format("FilesUploading", "a", "512 B", "1 KB"), new TransferProgress(true, "a", 512, 1024, 1).Text);
        Assert.Equal(Loc.Format("FilesDownloading", "b", "0 B", "2 KB") + "  ·  ×3", new TransferProgress(false, "b", 0, 2048, 3).Text);
    }
}
