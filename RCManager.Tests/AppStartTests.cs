using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using SocRcManager.Localization;
using SocRcManager.Models;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>
/// El arranque de la aplicacion (App.Start), la instancia unica y los errores no controlados. La
/// instancia unica va con un nombre propio de cada prueba (nunca el de la instancia de verdad) y el
/// apagado se sustituye: en las pruebas no se puede cerrar el hilo de interfaz.
/// </summary>
public sealed class AppStartTests : MainWindowTest
{
    private static App App => (App)Application.Current;

    private readonly string _name;
    private readonly Action _shutdown;
    private readonly Func<SingleInstance> _create;
    private int _shutdowns;

    public AppStartTests()
    {
        _name = SingleInstance.NameFor(Path.Combine(Dir.Path, "instancia"));
        (_shutdown, _create) = Ui.Run(() => (App.ShutdownApp, App.CreateSingleInstance));
        Ui.Run(() =>
        {
            App.ShutdownApp = () => _shutdowns++;
            App.CreateSingleInstance = () => new SingleInstance(_name);
        });
        // La principal se enseña (fuera de la pantalla): puede ser dueña de dialogos. Las sueltas, no.
        Dialogs.ShowWindow = w =>
        {
            Ui.Hide(w);
            if (w is MainWindow)
                w.Show();
            Ui.Shown.Add(w);
        };
    }

    public override void Dispose()
    {
        Ui.Run(() =>
        {
            // Lo que deja Start: que cerrar la ventana no cierre la aplicacion, sus manejadores y la instancia.
            App.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            App.MainWindow = null;
            App.DispatcherUnhandledException -= App.OnDispatcherError;
            TaskScheduler.UnobservedTaskException -= App.OnTaskError;
            AppDomain.CurrentDomain.UnhandledException -= App.OnFatalError;
            App.Instance?.Dispose();
            typeof(App).GetField("_single", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(App, null);
            App.ShutdownApp = _shutdown;
            App.CreateSingleInstance = _create;
            App.OtherInstanceTimeout = TimeSpan.FromSeconds(2);
            ThemeManager.Apply(Application.Current.Resources, false);
        });
        base.Dispose();
    }

    /// <summary>Arranca como la aplicacion y deja enseguida el modo de cierre de las pruebas.</summary>
    private static MainWindow Start(params string[] args) => Ui.Run(() =>
    {
        App.Start(args);
        Assert.Equal(ShutdownMode.OnMainWindowClose, App.ShutdownMode);
        App.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var main = (MainWindow)App.MainWindow;
        // Si luego sale de la bandeja, fuera de la pantalla.
        Ui.Hide(main);
        return main;
    });

    private static SingleInstance.Request Request(string version, params string[] args) => new(version, args, null);

    private static string Log() => File.Exists(AppLog.FilePath) ? File.ReadAllText(AppLog.FilePath) : string.Empty;

    [Fact]
    public void Primera_instancia_en_la_bandeja_y_otra_que_le_pide_abrir_una_conexion()
    {
        Seed([Conn("Web", "", ConnectionKind.Ssh)]);
        var main = Start("--tray");
        Ui.Run(() => Assert.False(main.IsVisible));
        Assert.Equal(["add 8001 " + Loc.Get("AppTitle")], TrayShell.Calls);
        Assert.True(App.Instance!.IsOwner);

        // Otra que arranca con --open: la de aqui sale de la bandeja y abre la conexion; la otra se cierra.
        using var other = new SingleInstance(_name);
        var outcome = other.Start(Request(App.MyVersion.ToString(), "--open", "Web"), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        Assert.Equal(SingleInstance.Outcome.HandedOver, outcome);
        Ui.WaitUntil(() => Sessions.Count == 1);
        Ui.Run(() => Assert.True(main.IsVisible));
        Assert.Equal("Web", Sessions[0].Connection.Name);
    }

    [Fact]
    public void Arranque_con_tamaño_conexiones_y_editor()
    {
        Seed([Conn("Web", "", ConnectionKind.Ssh), Conn("Ficheros", "", ConnectionKind.Sftp)]);
        var main = Start("--size", "900x700", "--open", "Web", "--edit", "Web", "--edit-tab", "1", "--edit-file", "Ficheros", "/etc/hosts");
        Ui.Flush();
        Ui.Run(() =>
        {
            Assert.Equal((900, 700), (main.Width, main.Height));
            Assert.Contains(main, Ui.Shown);
        });
        Assert.Equal(["Web", "Ficheros"], Sessions.Select(s => s.Connection.Name));
        var editor = Assert.IsType<ConnectionWindow>(Assert.Single(Ui.Modals));
        Ui.Run(() => Assert.Equal(1, editor.Sections.SelectedIndex));
        Assert.True(App.Instance!.IsOwner);
    }

    [Fact]
    public void Bandeja_con_algo_que_hacer_se_enseña_para_poder_preguntar()
    {
        // Antes: --tray --edit dejaba la ventana escondida sin haberse enseñado nunca, y el editor
        // (que la necesita de dueña) lanzaba «Cannot set Owner property...».
        Seed([Conn("Web", "", ConnectionKind.Ssh, password: "")]);
        var main = Start("--tray", "--edit", "Web", "--open", "Web");
        Ui.Flush();
        Ui.Run(() => Assert.True(main.IsVisible));
        Assert.Empty(TrayShell.Calls);
        // La contraseña (cancelada: no abre) y el editor.
        Assert.Equal([typeof(PromptWindow), typeof(ConnectionWindow)], Ui.Modals.Select(m => m.GetType()));
        Assert.Empty(Sessions);
    }

    [Fact]
    public void Si_otra_lo_atiende_esta_se_cierra_sin_abrir_nada()
    {
        using var other = new SingleInstance(_name);
        Assert.True(other.TryClaim());
        other.Listen(_ => (SingleInstance.Reply.Shown, null));
        var before = Ui.Run(() => App.MainWindow);
        Ui.Run(() => App.Start(["--open", "X"]));
        Assert.Equal(1, _shutdowns);
        Assert.Null(App.Instance);
        Ui.Run(() =>
        {
            Assert.Same(before, App.MainWindow);
            Assert.Equal(ShutdownMode.OnExplicitShutdown, App.ShutdownMode);
        });
    }

    [Fact]
    public void Si_la_otra_no_contesta_arranca_igual_y_lo_apunta()
    {
        using var other = new SingleInstance(_name);
        Assert.True(other.TryClaim());
        other.Listen(_ => (null, null));
        var main = Start();
        Assert.NotNull(main);
        Assert.False(App.Instance!.IsOwner);
        Assert.Contains("hay otra abierta que no contesta", Log());
    }

    [Fact]
    public void Otra_instancia_sin_ventana_principal_no_se_contesta()
    {
        Ui.Run(() =>
        {
            App.MainWindow = Ui.Show(new Window());
            Assert.Equal((null, null), App.HandleOtherInstance(Request("9.9.9.9")));
        });
    }

    [Fact]
    public void Otra_instancia_mas_nueva_y_sin_sesiones_se_queda_el_sitio()
    {
        var main = NewMain();
        var (reply, after) = Ui.Run(() =>
        {
            App.MainWindow = main;
            return App.HandleOtherInstance(Request("9999.0.0.0"));
        });
        Assert.Equal(SingleInstance.Reply.Yield, reply);
        Ui.Run(() => after!());
        Ui.Flush();
        Assert.Equal(1, _shutdowns);
        Ui.Run(() => Assert.DoesNotContain(main, Application.Current.Windows.OfType<Window>()));
        Assert.Contains("se le deja el sitio", Log());
    }

    [Fact]
    public void Otra_instancia_igual_o_rara_atiende_lo_pedido()
    {
        Seed([Conn("Web", "", ConnectionKind.Ssh)]);
        var main = Ui.Run(() =>
        {
            var m = new MainWindow();
            Ui.Hide(m);
            m.StartInTray();
            App.MainWindow = m;
            return m;
        });

        // --tray (arranque con Windows con esta ya abierta): nada que enseñar.
        var (reply, after) = Ui.Run(() => App.HandleOtherInstance(Request("no es una version", "--tray")));
        Assert.Equal(SingleInstance.Reply.Shown, reply);
        Ui.Run(() => after!());
        Ui.Flush();
        Ui.Run(() => Assert.False(main.IsVisible));

        // Una version vieja con --open: se enseña y la abre.
        (reply, after) = Ui.Run(() => App.HandleOtherInstance(Request("1.0", "--open", "Web")));
        Assert.Equal(SingleInstance.Reply.Shown, reply);
        Ui.Run(() => Assert.True(main.IsVisible));
        Ui.Run(() => after!());
        Ui.Flush();
        Assert.Single(Sessions);
    }

    [Fact]
    public void Otra_instancia_mas_nueva_con_sesiones_abiertas_pregunta()
    {
        Seed([Conn("Web", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Web");
        var exe = Dir.File("sOCRCManager.exe", "x");
        var request = new SingleInstance.Request("9999.0.0.0", ["--open", "Otra cosa"], exe);

        var (reply, after) = Ui.Run(() =>
        {
            App.MainWindow = main;
            return App.HandleOtherInstance(request);
        });
        Assert.Equal(SingleInstance.Reply.Shown, reply);
        Assert.Contains(main, Desk.Foreground);

        // No se acepta: sigue todo como estaba.
        Ui.Run(() => after!());
        Ui.Flush();
        Assert.Equal(0, _shutdowns);
        Assert.Contains(Loc.Format("NewerInstanceText", new Version(9999, 0, 0, 0), App.MyVersion, 1),
            Ui.Run(() => Ui.FindAll<System.Windows.Controls.TextBlock>(Ui.Modals.Single()).Select(t => t.Text).ToList()));

        // Se acepta: esta se cierra y arranca la nueva con lo que se le pidio.
        Ui.Answer<PromptWindow>(w => Ui.Click(Ui.ButtonById(w, "OkButton")!));
        Ui.Run(() => App.OfferNewerVersion(main, request, new Version(9999, 0, 0, 0)));
        Assert.Equal(1, _shutdowns);
        var psi = Assert.Single(Ui.Started);
        Assert.Equal(exe, psi.FileName);
        Assert.Equal(["--open", "Otra cosa"], psi.ArgumentList);
        Assert.False(psi.UseShellExecute);
        Ui.Run(() => Assert.DoesNotContain(main, Application.Current.Windows.OfType<Window>()));
        Assert.Contains("disconnect", Sessions[0].Log);
    }

    [Fact]
    public void La_version_nueva_que_no_existe_o_no_arranca_se_apunta_y_esta_se_cierra_igual()
    {
        var start = Dialogs.Start;
        try
        {
            // Sin exe: no se lanza nada.
            var main = NewMain();
            Ui.Answer<PromptWindow>(w => Ui.Click(Ui.ButtonById(w, "OkButton")!));
            Ui.Run(() => App.OfferNewerVersion(main, new SingleInstance.Request("9.0", [], Path.Combine(Dir.Path, "no-existe.exe")), new Version(9, 0)));
            Assert.Empty(Ui.Started);
            Assert.Equal(1, _shutdowns);

            // Windows no puede abrirlo.
            main = NewMain();
            Dialogs.Start = _ => throw new InvalidOperationException("sin permiso");
            Ui.Answer<PromptWindow>(w => Ui.Click(Ui.ButtonById(w, "OkButton")!));
            Ui.Run(() => App.OfferNewerVersion(main, new SingleInstance.Request("9.0", [], Dir.File("nuevo.exe", "x")), new Version(9, 0)));
            Assert.Equal(2, _shutdowns);
            Assert.Contains("no se pudo abrir la version nueva", Log());
            Assert.Contains("sin permiso", Log());
        }
        finally
        {
            Dialogs.Start = start;
        }
    }

    [Fact]
    public void Peticion_de_otra_instancia_por_el_hilo_de_la_tuberia()
    {
        // Con la interfaz libre se contesta; colgada mas de lo que se espera, no (la otra arrancara igual).
        Ui.Run(() => App.MainWindow = null);
        Assert.Equal((null, null), App.OnOtherInstance(Request("1.0")));

        App.OtherInstanceTimeout = TimeSpan.FromMilliseconds(50);
        using var gate = new ManualResetEventSlim();
        Ui.Dispatcher.BeginInvoke(new Action(() => gate.Wait(5000)));
        try
        {
            Assert.Equal((null, null), App.OnOtherInstance(Request("1.0")));
        }
        finally
        {
            gate.Set();
        }
        Ui.Flush();
    }

    // ------------------------------------------------------------------ Errores no controlados

    private static DispatcherUnhandledExceptionEventArgs DispatcherError(Exception ex)
    {
        var args = (DispatcherUnhandledExceptionEventArgs)typeof(DispatcherUnhandledExceptionEventArgs)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single().Invoke([Ui.Dispatcher]);
        typeof(DispatcherUnhandledExceptionEventArgs).GetMethod("Initialize", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(args, [ex, true]);
        return args;
    }

    [Fact]
    public void Error_no_controlado_se_apunta_y_avisa_una_sola_vez()
    {
        var main = NewMain();
        Ui.Run(() => App.MainWindow = main);
        // Mientras el aviso esta abierto, otro error no apila otra ventana.
        Ui.Answer(_ =>
        {
            App.ShowUnexpectedError();
            return false;
        });
        var args = DispatcherError(new InvalidOperationException("fallo raro"));
        Ui.Run(() => App.OnDispatcherError(App, args));
        Assert.True(args.Handled);
        var alert = Assert.IsType<PromptWindow>(Assert.Single(Ui.Modals));
        Ui.Run(() =>
        {
            Assert.Equal(Loc.Get("UnexpectedErrorTitle"), alert.Title);
            Assert.Same(main, alert.Owner);
        });
        Assert.Contains("error no controlado: System.InvalidOperationException: fallo raro", Log());

        // Despues, otro error vuelve a avisar (sin dueño si la principal no se ve).
        Ui.Run(() =>
        {
            main.Hide();
            App.ShowUnexpectedError();
        });
        Assert.Equal(2, Ui.Modals.Count);
        Ui.Run(() => Assert.Null(Ui.Modals[1].Owner));
    }

    [Fact]
    public void Si_no_se_puede_avisar_del_error_se_apunta()
    {
        var modal = Dialogs.ShowModal;
        Dialogs.ShowModal = _ => throw new InvalidOperationException("sin ventanas");
        try
        {
            Ui.Run(() => App.MainWindow = null);
            Ui.Run(App.ShowUnexpectedError);
        }
        finally
        {
            Dialogs.ShowModal = modal;
        }
        Assert.Contains("no se pudo enseñar el aviso de error", Log());
        // Y el siguiente error si avisa (no se queda marcado como «enseñando»).
        Ui.Run(App.ShowUnexpectedError);
        Assert.Single(Ui.Modals);
    }

    [Fact]
    public void Errores_de_tareas_y_fatales_se_apuntan()
    {
        var task = new UnobservedTaskExceptionEventArgs(new AggregateException(new TimeoutException("tarea perdida")));
        App.OnTaskError(null, task);
        Assert.True(task.Observed);
        App.OnFatalError(this, new UnhandledExceptionEventArgs(new OutOfMemoryException("sin memoria"), true));
        var log = Log();
        Assert.Contains("error no controlado en tarea", log);
        Assert.Contains("tarea perdida", log);
        Assert.Contains("error fatal: System.OutOfMemoryException: sin memoria", log);
    }

    [Fact]
    public void Arrancar_instala_los_manejadores_de_errores()
    {
        var main = Start("--tray");
        Assert.NotNull(main);
        // Un error en el hilo de interfaz: no tumba nada, se apunta y se avisa.
        Ui.Dispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("en la interfaz")));
        // (Sin Ui.WaitUntil: el hilo de las pruebas tambien apunta el error y lo relanzaria.)
        var until = DateTime.UtcNow.AddSeconds(10);
        while (Ui.Dispatcher.Invoke(() => Ui.Modals.Count) == 0 && DateTime.UtcNow < until)
            Thread.Sleep(20);
        Ui.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Ui.LastError = null;
        Assert.Single(Ui.Modals);
        Assert.Contains("error no controlado: System.InvalidOperationException: en la interfaz", Log());
    }

    // ------------------------------------------------------------------ OnStartup y OnExit

    [Fact]
    public void OnStartup_respeta_SkipStartup_y_OnExit_suelta_la_instancia()
    {
        var startup = typeof(StartupEventArgs).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var onStartup = typeof(App).GetMethod("OnStartup", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var onExit = typeof(App).GetMethod("OnExit", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // Con SkipStartup (las pruebas) no arranca nada.
        var before = Ui.Run(() => App.MainWindow);
        Ui.Run(() => onStartup.Invoke(App, [startup.Invoke([])]));
        Ui.Run(() => Assert.Same(before, App.MainWindow));
        Assert.Null(App.Instance);

        // Sin el, arranca con los argumentos de la linea de comandos.
        var e = (StartupEventArgs)startup.Invoke([]);
        typeof(StartupEventArgs).GetField("_args", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(e, new[] { "--tray" });
        App.SkipStartup = false;
        try
        {
            Ui.Run(() =>
            {
                onStartup.Invoke(App, [e]);
                App.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Ui.Hide(App.MainWindow);
            });
        }
        finally
        {
            App.SkipStartup = true;
        }
        Ui.Run(() => Assert.IsType<MainWindow>(App.MainWindow));
        var instance = App.Instance!;
        Assert.True(instance.IsOwner);

        var exit = typeof(ExitEventArgs).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single().Invoke([0]);
        Ui.Run(() => onExit.Invoke(App, [exit]));
        Assert.False(instance.IsOwner);
    }
}
