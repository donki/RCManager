using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>Argumentos de arranque (los mismos que se pasan a la instancia que ya estaba abierta).</summary>
public sealed class StartupArgsTests
{
    [Fact]
    public void SinArgumentos_NoPideNada()
    {
        var a = StartupArgs.Parse([]);
        Assert.Empty(a.Open);
        Assert.Null(a.Edit);
        Assert.Null(a.Size);
        Assert.False(a.Tray);
        Assert.False(a.HasWork);
    }

    [Fact]
    public void VariosOpen_YElRestoDeOpciones()
    {
        var a = StartupArgs.Parse(["--open", "Uno", "--OPEN", "Dos con espacios", "--edit", "Tres", "--edit-tab", "2",
            "--edit-file", "Ficheros", "/etc/hosts", "--size", "1200X800", "--tray"]);
        Assert.Equal(["Uno", "Dos con espacios"], a.Open);
        Assert.Equal("Tres", a.Edit);
        Assert.Equal(2, a.EditTab);
        Assert.Equal("Ficheros", a.EditFileConnection);
        Assert.Equal("/etc/hosts", a.EditFilePath);
        Assert.Equal((1200, 800), a.Size);
        Assert.True(a.Tray);
        Assert.True(a.HasWork);
    }

    [Fact]
    public void TrayAlFinal_SeEntiende()
    {
        // Antes el bucle no miraba el ultimo argumento: «--tray» solo no se leia.
        Assert.True(StartupArgs.Parse(["--tray"]).Tray);
        Assert.False(StartupArgs.Parse(["--tray"]).HasWork);
    }

    [Theory]
    [InlineData("--open")]
    [InlineData("--edit")]
    [InlineData("--edit-tab")]
    [InlineData("--size")]
    public void ValorQueFaltaAlFinal_SeIgnora(string key)
    {
        var a = StartupArgs.Parse([key]);
        Assert.Empty(a.Open);
        Assert.Null(a.Edit);
        Assert.Null(a.Size);
        Assert.Equal(0, a.EditTab);
    }

    [Fact]
    public void EditFileSinRuta_SeIgnora()
    {
        var a = StartupArgs.Parse(["--edit-file", "Solo la conexion"]);
        Assert.Null(a.EditFileConnection);
        Assert.Null(a.EditFilePath);
    }

    [Theory]
    [InlineData("800")]
    [InlineData("0x600")]
    [InlineData("-5x600")]
    [InlineData("anchoxalto")]
    [InlineData("800x600x2")]
    public void TamañoMalo_SeIgnora(string size) => Assert.Null(StartupArgs.Parse(["--size", size]).Size);

    [Fact]
    public void EditTabMalo_EsCero()
    {
        Assert.Equal(0, StartupArgs.Parse(["--edit-tab", "x"]).EditTab);
        Assert.Equal(0, StartupArgs.Parse(["--edit-tab", "-3"]).EditTab);
    }

    [Fact]
    public void LoQueNoSeEntiende_SeSalta()
    {
        var a = StartupArgs.Parse(["raro", "--open", "Uno", "--otra-cosa"]);
        Assert.Equal(["Uno"], a.Open);
    }
}

/// <summary>Instancia unica: decision, nombres, protocolo y el ida y vuelta real por la tuberia.</summary>
public sealed class SingleInstanceTests
{
    private static readonly Version V1 = new(2026, 9, 30, 0), V2 = new(2026, 10, 1, 0);

    [Fact]
    public void Decide_MismaVersionOMasVieja_SeEnseña()
    {
        Assert.Equal(SingleInstance.Answer.Show, SingleInstance.Decide(V2, V2, hasOpenSessions: false));
        Assert.Equal(SingleInstance.Answer.Show, SingleInstance.Decide(V2, V1, hasOpenSessions: true));
        Assert.Equal(SingleInstance.Answer.Show, SingleInstance.Decide(V2, null, hasOpenSessions: false));
    }

    [Fact]
    public void Decide_LaNuevaManda_SalvoSesionesAbiertas()
    {
        Assert.Equal(SingleInstance.Answer.Yield, SingleInstance.Decide(V1, V2, hasOpenSessions: false));
        Assert.Equal(SingleInstance.Answer.ShowAndOfferUpdate, SingleInstance.Decide(V1, V2, hasOpenSessions: true));
    }

    [Fact]
    public void Nombre_NoDependeDelExe_SiDelUsuarioSesionYModoAislado()
    {
        var a = SingleInstance.NameFor(null, "S-1-5-21-1", 1);
        Assert.Equal(a, SingleInstance.NameFor(null, "S-1-5-21-1", 1));
        Assert.NotEqual(a, SingleInstance.NameFor(null, "S-1-5-21-2", 1));
        Assert.NotEqual(a, SingleInstance.NameFor(null, "S-1-5-21-1", 2));
        Assert.StartsWith("sOCRCManager-", a);
        Assert.DoesNotContain("\\", a);

        // El modo aislado nunca comparte nombre con la instancia de verdad, y depende de su carpeta
        // (mayusculas y barra final dan igual).
        var sandbox = SingleInstance.NameFor(@"C:\Temp\Prueba", "S-1-5-21-1", 1);
        Assert.NotEqual(a, sandbox);
        Assert.Contains("-sandbox-", sandbox);
        Assert.Equal(sandbox, SingleInstance.NameFor(@"c:\temp\prueba\", "S-1-5-21-1", 1));
        Assert.NotEqual(sandbox, SingleInstance.NameFor(@"C:\Temp\Otra", "S-1-5-21-1", 1));

        // Sin usuario ni sesion explicitos: los del proceso.
        Assert.StartsWith("sOCRCManager-", SingleInstance.NameFor(null));
    }

    [Fact]
    public void Peticion_IdaYVuelta()
    {
        var r = new SingleInstance.Request("2026.10.1.0", ["--open", "Servidor «uno»"], @"C:\Program Files\x.exe");
        var back = SingleInstance.DecodeRequest(SingleInstance.Encode(r))!;
        Assert.Equal(r.Version, back.Version);
        Assert.Equal(r.Args, back.Args);
        Assert.Equal(r.Exe, back.Exe);
        Assert.DoesNotContain("\n", SingleInstance.Encode(r));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("[1,2]")]
    public void Peticion_Rota_EsNull(string? line) => Assert.Null(SingleInstance.DecodeRequest(line));

    [Fact]
    public void Peticion_SinCampos_SeCompleta()
    {
        var r = SingleInstance.DecodeRequest("{}")!;
        Assert.Empty(r.Args);
        Assert.Equal(string.Empty, r.Version);
        Assert.Null(r.Exe);
    }

    [Theory]
    [InlineData("Shown", SingleInstance.Reply.Shown)]
    [InlineData("Yield\r", SingleInstance.Reply.Yield)]
    public void Respuesta_SeLee(string line, SingleInstance.Reply expected) => Assert.Equal(expected, SingleInstance.DecodeReply(line));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Otra")]
    [InlineData("7")]
    public void Respuesta_Rara_EsNull(string? line) => Assert.Null(SingleInstance.DecodeReply(line));

    private static string UniqueName() => "sOCRCManager-pruebas-" + Guid.NewGuid().ToString("N");

    private static SingleInstance.Request Req(params string[] args) => new("2026.10.1.0", args, null);

    /// <summary>El mutex es por hilo: la «otra instancia» corre en un hilo propio, como otro proceso.</summary>
    private static T OnOtherThread<T>(Func<T> f)
    {
        T result = default!;
        var t = new Thread(() => result = f());
        t.Start();
        t.Join();
        return result;
    }

    [Fact]
    public void Sola_EsLaPrimera()
    {
        using var first = new SingleInstance(UniqueName());
        Assert.Equal(SingleInstance.Outcome.First, first.Start(Req(), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        Assert.True(first.IsOwner);
        Assert.True(first.TryClaim());
        Assert.StartsWith("Local\\", first.MutexName);
    }

    [Fact]
    public void Segunda_LePasaLaPeticionALaPrimera_YSeVa()
    {
        var name = UniqueName();
        using var first = new SingleInstance(name);
        Assert.True(first.TryClaim());
        SingleInstance.Request? received = null;
        var after = new ManualResetEventSlim();
        first.Listen(r => { received = r; return (SingleInstance.Reply.Shown, () => after.Set()); });

        var outcome = OnOtherThread(() =>
        {
            using var second = new SingleInstance(name);
            return second.Start(Req("--open", "Servidor"), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3));
        });

        Assert.Equal(SingleInstance.Outcome.HandedOver, outcome);
        Assert.Equal(["--open", "Servidor"], received!.Args);
        Assert.True(after.Wait(TimeSpan.FromSeconds(5)), "No se ejecuta lo de despues de contestar");

        // Y sigue escuchando para la siguiente.
        Assert.Equal(SingleInstance.Outcome.HandedOver, OnOtherThread(() =>
        {
            using var third = new SingleInstance(name);
            return third.Start(Req(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3));
        }));
    }

    [Fact]
    public void PrimeraColgada_LaSegundaArrancaIgual()
    {
        var name = UniqueName();
        using var first = new SingleInstance(name);
        Assert.True(first.TryClaim());
        // No contesta (como cuando el hilo de la interfaz no responde a tiempo).
        first.Listen(_ => (null, null));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var outcome = OnOtherThread(() =>
        {
            using var second = new SingleInstance(name);
            return second.Start(Req(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        });
        Assert.Equal(SingleInstance.Outcome.StartAnyway, outcome);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), $"Tarda demasiado: {watch.Elapsed}");
    }

    [Fact]
    public void PrimeraSinTuberia_SeEsperaYSeArrancaIgual()
    {
        var name = UniqueName();
        using var first = new SingleInstance(name);
        Assert.True(first.TryClaim());   // tiene el mutex pero nunca escucha

        var outcome = OnOtherThread(() =>
        {
            using var second = new SingleInstance(name);
            return second.Start(Req(), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        });
        Assert.Equal(SingleInstance.Outcome.StartAnyway, outcome);
    }

    [Fact]
    public void PrimeraMasVieja_Cede_YLaSegundaSeQuedaElMutex()
    {
        var name = UniqueName();
        var ready = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        // La primera en su hilo: al ceder, suelta el mutex (como al cerrarse).
        var firstThread = new Thread(() =>
        {
            using var first = new SingleInstance(name);
            first.TryClaim();
            first.Listen(_ => (SingleInstance.Reply.Yield, () => release.Set()));
            ready.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            Thread.Sleep(200);
        });
        firstThread.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));

        using var second = new SingleInstance(name);
        Assert.Equal(SingleInstance.Outcome.First, second.Start(Req(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3)));
        Assert.True(second.IsOwner);
        firstThread.Join();
    }

    [Fact]
    public void PrimeraMuerta_SinSoltarElMutex_LaSegundaLoCoge()
    {
        var name = UniqueName();
        // Un hilo que coge el mutex y acaba sin soltarlo: queda abandonado, como un proceso muerto.
        var t = new Thread(() =>
        {
            var m = new Mutex(true, "Local\\" + name);
            GC.KeepAlive(m);
        });
        t.Start();
        t.Join();

        using var second = new SingleInstance(name);
        Assert.Equal(SingleInstance.Outcome.First, second.Start(Req(), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Dispose_SueltaElMutex()
    {
        var name = UniqueName();
        var first = new SingleInstance(name);
        Assert.True(first.TryClaim());
        first.Listen(_ => (SingleInstance.Reply.Shown, null));
        first.Listen(_ => (SingleInstance.Reply.Shown, null));   // la segunda vez no hace nada
        first.Dispose();
        Assert.False(first.IsOwner);
        first.Dispose();   // dos veces, sin romperse

        Assert.Equal(SingleInstance.Outcome.First, OnOtherThread(() =>
        {
            using var again = new SingleInstance(name);
            return again.Start(Req(), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }));
    }
}

/// <summary>Donde va una ventana suelta y que hacer con ellas al cerrar la principal.</summary>
public sealed class SessionPlacementTests
{
    private static readonly ScreenArea Left = new(0, 0, 1920, 1040);
    private static readonly ScreenArea Right = new(1920, 0, 2560, 1400);

    [Fact]
    public void Fit_VisibleEnSuMonitor_SeQuedaDondeEstaba()
    {
        var onRight = new WindowBounds(2500, 300, 900, 600);
        Assert.Equal(onRight, SessionPlacement.Fit(onRight, [Left, Right]));
        var onLeft = new WindowBounds(100, 100, 800, 500, Maximized: true);
        Assert.Equal(onLeft, SessionPlacement.Fit(onLeft, [Left, Right]));
    }

    [Fact]
    public void Fit_MonitorDesenchufado_VaAlPrincipalCentrada()
    {
        var b = SessionPlacement.Fit(new WindowBounds(2500, 300, 900, 600), [Left]);
        Assert.Equal(900, b.Width);
        Assert.Equal(600, b.Height);
        Assert.Equal((1920 - 900) / 2.0, b.Left);
        Assert.Equal((1040 - 600) / 2.0, b.Top);
    }

    [Fact]
    public void Fit_MasGrandeQueLaPantalla_SeEncoge()
    {
        var b = SessionPlacement.Fit(new WindowBounds(-5000, -5000, 3000, 2000), [Left, Right]);
        Assert.Equal(Left.Width, b.Width);
        Assert.Equal(Left.Height, b.Height);
        Assert.Equal(0, b.Left);
        Assert.Equal(0, b.Top);
    }

    [Fact]
    public void Fit_BarraDeTituloFueraPorArriba_SeRecoloca()
    {
        // Se ve el cuerpo pero no la barra de titulo: no se podria coger con el raton.
        var b = SessionPlacement.Fit(new WindowBounds(100, -200, 800, 600), [Left]);
        Assert.True(b.Top >= 0);
    }

    [Fact]
    public void Fit_ApenasAsomaPorUnLado_SeRecoloca()
    {
        var b = SessionPlacement.Fit(new WindowBounds(1900, 100, 800, 600), [Left]);   // solo 20 px dentro
        Assert.True(b.Right <= Left.Right && b.Left >= 0);
    }

    [Fact]
    public void Fit_TamañoMinimo()
    {
        var b = SessionPlacement.Fit(new WindowBounds(100, 100, 50, 20), [Left]);
        Assert.Equal(SessionPlacement.MinWidth, b.Width);
        Assert.Equal(SessionPlacement.MinHeight, b.Height);
        // Sin monitores (no deberia pasar) se devuelve tal cual, con el minimo.
        Assert.Equal(SessionPlacement.MinWidth, SessionPlacement.Fit(new WindowBounds(1, 2, 3, 4), []).Width);
    }

    [Fact]
    public void AtCursor_ElRatonQuedaSobreLaBarraDeTitulo()
    {
        var b = SessionPlacement.AtCursor(2600, 500, 900, 600, [Left, Right]);
        Assert.InRange(2600, b.Left, b.Right);
        Assert.InRange(500, b.Top, b.Top + SessionPlacement.MinVisibleHeight);
        Assert.Equal(900, b.Width);
    }

    [Fact]
    public void NextTo_SeEscalonanYNoSeSalen()
    {
        var main = new WindowBounds(100, 100, 1200, 800);
        var a = SessionPlacement.NextTo(main, 800, 600, 0, [Left]);
        var b = SessionPlacement.NextTo(main, 800, 600, 1, [Left]);
        Assert.True(b.Left > a.Left && b.Top > a.Top);
        Assert.Equal(a, SessionPlacement.NextTo(main, 800, 600, 8, [Left]));
    }

    [Fact]
    public void AlCerrarLaPrincipal_SoloPreguntaSiHaySueltasYElAjusteLoPide()
    {
        Assert.Equal(SessionPlacement.CloseAction.Ask, SessionPlacement.OnMainClosing(2, askSetting: true, forced: false));
        Assert.Equal(SessionPlacement.CloseAction.CloseAll, SessionPlacement.OnMainClosing(0, askSetting: true, forced: false));
        Assert.Equal(SessionPlacement.CloseAction.CloseAll, SessionPlacement.OnMainClosing(2, askSetting: false, forced: false));
        Assert.Equal(SessionPlacement.CloseAction.CloseAll, SessionPlacement.OnMainClosing(2, askSetting: true, forced: true));
    }

    [Fact]
    public void Clave_PorConexion()
    {
        var id = Guid.NewGuid();
        Assert.Equal(SessionPlacement.Key(id), SessionPlacement.Key(id));
        Assert.Equal(32, SessionPlacement.Key(id).Length);
    }

    [Fact]
    public void AjustesGuardanLasVentanasSueltas()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rcm-placement-" + Guid.NewGuid().ToString("N"));
        var old = AppSettings.FilePath;
        try
        {
            AppSettings.FilePath = Path.Combine(dir, "settings.json");
            var s = new AppSettings { AskBeforeClosingDetached = false };
            s.DetachedWindows["k"] = new WindowBounds(10.5, 20, 800, 600, Maximized: true);
            s.Save();
            var back = AppSettings.Load();
            Assert.False(back.AskBeforeClosingDetached);
            Assert.Equal(new WindowBounds(10.5, 20, 800, 600, true), back.DetachedWindows["k"]);
            Assert.True(new AppSettings().AskBeforeClosingDetached);
        }
        finally
        {
            AppSettings.FilePath = old;
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}

/// <summary>Colocacion al sacar una pestaña, que hacer al soltar un arrastre y --tray.</summary>
public sealed class DetachDecisionTests
{
    private static readonly ScreenArea Screen = new(0, 0, 1920, 1040);
    private static readonly WindowBounds Main = new(100, 100, 1200, 800);

    [Fact]
    public void ForDetach_ConRaton_DondeSeSuelta_ConElTamañoRecordado()
    {
        var saved = new WindowBounds(1500, 900, 700, 500, Maximized: true);
        var b = SessionPlacement.ForDetach((900, 400), saved, 1000, 600, Main, 0, [Screen]);
        Assert.Equal(700, b.Width);
        Assert.Equal(500, b.Height);
        Assert.InRange(900, b.Left, b.Right);
        Assert.False(b.Maximized);

        var fresh = SessionPlacement.ForDetach((900, 400), null, 1000, 600, Main, 0, [Screen]);
        Assert.Equal(1000, fresh.Width);
    }

    [Fact]
    public void ForDetach_ConBoton_DondeEstuvo_OJuntoALaPrincipal()
    {
        var saved = new WindowBounds(300, 200, 700, 500, Maximized: true);
        Assert.Equal(saved, SessionPlacement.ForDetach(null, saved, 1000, 600, Main, 0, [Screen]));

        var first = SessionPlacement.ForDetach(null, null, 1000, 600, Main, 0, [Screen]);
        Assert.Equal(SessionPlacement.NextTo(Main, 1000, 600, 0, [Screen]), first);
        Assert.True(first.Left > Main.Left);
    }

    [Theory]
    // desde la principal
    [InlineData(false, false, false, false, SessionPlacement.DropAction.Detach)]
    [InlineData(false, false, true, false, SessionPlacement.DropAction.None)]
    [InlineData(false, false, false, true, SessionPlacement.DropAction.None)]
    [InlineData(false, true, false, false, SessionPlacement.DropAction.None)]
    // desde una ventana suelta
    [InlineData(true, false, true, false, SessionPlacement.DropAction.Attach)]
    [InlineData(true, false, false, false, SessionPlacement.DropAction.MoveWindow)]
    [InlineData(true, false, false, true, SessionPlacement.DropAction.None)]
    [InlineData(true, true, true, false, SessionPlacement.DropAction.None)]
    public void AfterDrag(bool fromDetached, bool cancelled, bool overMain, bool overDetached, SessionPlacement.DropAction expected) =>
        Assert.Equal(expected, SessionPlacement.AfterDrag(fromDetached, cancelled, overMain, overDetached));

    [Theory]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "--tray" }, false)]
    [InlineData(new[] { "--tray", "--open", "X" }, true)]
    [InlineData(new[] { "--open", "X" }, true)]
    [InlineData(new[] { "--size", "800x600" }, true)]
    public void ShowsExisting(string[] args, bool expected) => Assert.Equal(expected, StartupArgs.Parse(args).ShowsExisting);
}

/// <summary>Modo aislado (solo Debug): datos a otra carpeta.</summary>
public sealed class SandboxTests : IDisposable
{
    private readonly string _store = Store.Folder, _settings = AppSettings.FilePath, _log = AppLog.Folder;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rcm-sandbox-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Store.Folder = _store;
        AppSettings.FilePath = _settings;
        AppLog.Folder = _log;
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void SinVariable_NoHaceNada()
    {
        Sandbox.Apply(null);
        Sandbox.Apply("");
        Assert.Equal(_store, Store.Folder);
        Assert.Equal(_settings, AppSettings.FilePath);
    }

    [Fact]
    public void ConRutaAbsoluta_TodoVaAEsaCarpeta()
    {
        Sandbox.Apply("  " + _dir + "  ");
        Assert.True(Sandbox.IsOn);
        Assert.Equal(_dir, Sandbox.Folder);
        Assert.True(Directory.Exists(_dir));
        Assert.Equal(_dir, Store.Folder);
        Assert.Equal(Path.Combine(_dir, "settings.json"), AppSettings.FilePath);
        Assert.Equal(_dir, AppLog.Folder);
        Assert.Equal("SOC_SANDBOX", Sandbox.Variable);
    }

    [Fact]
    public void ConOtroValor_CarpetaTemporalFija()
    {
        Sandbox.Apply("1");
        Assert.Equal(Path.Combine(Path.GetTempPath(), "sOCRCManager-sandbox"), Sandbox.Folder);
        Assert.Equal(Sandbox.Folder, Store.Folder);
    }
}

/// <summary>Tema claro y oscuro.</summary>
public sealed class ThemeTests
{
    [Fact]
    public void LosDosTemas_TienenLosMismosColores_YValidos()
    {
        var dark = ThemeManager.Palette(dark: true);
        var light = ThemeManager.Palette(dark: false);
        Assert.Equal(dark.Keys.OrderBy(k => k), light.Keys.OrderBy(k => k));
        foreach (var hex in dark.Values.Concat(light.Values))
            Assert.Matches("^#[0-9A-F]{6}$", hex);
        Assert.NotEqual(dark["PageBackground"], light["PageBackground"]);
    }

    [Fact]
    public void Apply_PoneLosPinceles()
    {
        var resources = new System.Windows.ResourceDictionary();
        ThemeManager.Apply(resources, dark: true);
        var brush = Assert.IsType<System.Windows.Media.SolidColorBrush>(resources["PageBackground"]);
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x14, 0x13, 0x18), brush.Color);
        ThemeManager.Apply(resources, dark: false);
        Assert.Equal(System.Windows.Media.Colors.White, ((System.Windows.Media.SolidColorBrush)resources["CardBackground"]).Color);
        Assert.Equal(ThemeManager.Palette(false).Count, resources.Count);
    }

    [Fact]
    public void PrefiereOscuro_LeeElRegistroSinRomperse()
    {
        // Solo lee HKCU (el ajuste del usuario); da lo mismo que diga si o no.
        var dark = ThemeManager.PrefersDark();
        Assert.Equal(dark, ThemeManager.PrefersDark());
    }
}
