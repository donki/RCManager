using System.IO;
using System.Windows;
using SocRcManager.Localization;
using SocRcManager.Services;

namespace SocRcManager;

public partial class App : Application
{
    /// <summary>Las pruebas crean la App solo por sus recursos (estilos, colores): sin arrancar nada.</summary>
    internal static bool SkipStartup { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!SkipStartup)
            Start(e.Args);
    }

    /// <summary>El arranque: modo aislado, errores, instancia unica, tema y la ventana principal.</summary>
    internal void Start(string[] commandLine)
    {
        // Debug con SOC_SANDBOX: datos en otra carpeta y sin conectar a nada (pruebas de interfaz).
        Sandbox.Apply();

        // Un error que no se esperaba no puede cerrar la aplicacion (constitucion general, 6.12):
        // se apunta en el registro con la traza y se avisa en el idioma del usuario. Las sesiones
        // abiertas siguen vivas.
        DispatcherUnhandledException += (_, ex) =>
        {
            AppLog.Write($"error no controlado: {ex.Exception}");
            ex.Handled = true;
            ShowUnexpectedError();
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            AppLog.Write($"error no controlado en tarea: {ex.Exception}");
            ex.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            AppLog.Write($"error fatal: {ex.ExceptionObject}");

        // Una sola instancia (constitucion general 8.3): si ya hay una abierta, aunque este en el area
        // de notificacion, se le pasa lo pedido (ponerse delante, --open...) y esta se cierra. Si no
        // contesta en un par de segundos, esta arranca igual.
        _single = new SingleInstance(SingleInstance.NameFor(Sandbox.Folder));
        var outcome = _single.Start(new SingleInstance.Request(MyVersion.ToString(), commandLine, Environment.ProcessPath),
            patience: TimeSpan.FromSeconds(5), ackTimeout: TimeSpan.FromSeconds(2.5));
        if (outcome == SingleInstance.Outcome.HandedOver)
        {
            _single.Dispose();
            _single = null;
            Shutdown();
            return;
        }
        if (outcome == SingleInstance.Outcome.StartAnyway)
            AppLog.Write("instancia unica: hay otra abierta que no contesta; se arranca igual");

        // Las ventanas sueltas (pestañas sacadas) no mantienen viva la aplicacion: manda la principal.
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        ThemeManager.Apply();

        // sOCRCManager.exe --open "Nombre de la conexion" [--open "Otra"]: abre esas sesiones al
        // arrancar (accesos directos a un servidor concreto). --tray: escondida en el area de
        // notificacion. --size AxAl, --edit, --edit-tab, --edit-file: capturas y accesos directos
        // (StartupArgs).
        var args = StartupArgs.Parse(commandLine);
        var window = new MainWindow();
        MainWindow = window;
        if (args.Size is { } s)
        {
            window.Width = s.Width;
            window.Height = s.Height;
        }
        if (args.Tray)
            window.StartInTray();
        else
            window.Show();
        Run(window, args);

        if (outcome == SingleInstance.Outcome.First)
            _single.Listen(OnOtherInstance);
    }

    private SingleInstance? _single;

    private static Version MyVersion => typeof(App).Assembly.GetName().Version ?? new Version(0, 0);

    /// <summary>Lo que piden los argumentos (al arrancar, o lo que manda otra instancia).</summary>
    private static void Run(MainWindow window, StartupArgs args)
    {
        foreach (var name in args.Open)
            window.OpenByName(name);
        // --edit-file "Conexion" "/ruta/fichero": abre esa conexion de ficheros y el fichero en el editor.
        if (args.EditFileConnection is { } connection && args.EditFilePath is { } path)
            window.OpenFileInEditor(connection, path);
        if (args.Edit is { } edit)
            window.Dispatcher.BeginInvoke(() => window.EditByName(edit, args.EditTab), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// Otra instancia acaba de arrancar (hilo de la tuberia). Se atiende en el hilo de la interfaz; si
    /// esta no responde en dos segundos (un dialogo no la bloquea, un cuelgue si), no se contesta y la
    /// otra arranca igual.
    /// </summary>
    private (SingleInstance.Reply? Reply, Action? After) OnOtherInstance(SingleInstance.Request request)
    {
        var op = Dispatcher.InvokeAsync(() => HandleOtherInstance(request));
        if (!op.Task.Wait(TimeSpan.FromSeconds(2)))
        {
            op.Abort();
            return (null, null);
        }
        return op.Result;
    }

    private (SingleInstance.Reply? Reply, Action? After) HandleOtherInstance(SingleInstance.Request request)
    {
        if (MainWindow is not MainWindow window)
            return (null, null);
        var theirs = Version.TryParse(request.Version, out var v) ? v : null;
        var args = StartupArgs.Parse(request.Args);
        switch (SingleInstance.Decide(MyVersion, theirs, window.HasOpenSessions))
        {
            case SingleInstance.Answer.Yield:
                // Manda la version nueva y aqui no hay nada abierto: esta se cierra y la otra sigue.
                AppLog.Write($"instancia unica: se abre la {theirs} y esta es la {MyVersion}; se le deja el sitio");
                return (SingleInstance.Reply.Yield, () => Dispatcher.BeginInvoke(() => { window.CloseForced(); Shutdown(); }));

            case SingleInstance.Answer.ShowAndOfferUpdate:
                window.BringToFront();
                return (SingleInstance.Reply.Shown, () => Dispatcher.BeginInvoke(() => OfferNewerVersion(window, request, theirs!)));

            default:
                // --tray de otra (arranque con Windows con esta ya abierta): nada que enseñar.
                if (args.ShowsExisting)
                    window.BringToFront();
                // Despues de contestar: abrir una conexion puede pedir la contraseña (un dialogo).
                return (SingleInstance.Reply.Shown, () => Dispatcher.BeginInvoke(() => Run(window, args)));
        }
    }

    /// <summary>
    /// Se ha abierto una version mas nueva y esta tiene sesiones abiertas: no se cortan sin preguntar.
    /// Si se acepta, esta se cierra y arranca la nueva con lo que se le pidio.
    /// </summary>
    private void OfferNewerVersion(MainWindow window, SingleInstance.Request request, Version theirs)
    {
        if (!PromptWindow.Confirm(window, Loc.Get("NewerInstanceTitle"), Loc.Format("NewerInstanceText", theirs, MyVersion, window.OpenSessionCount),
                Loc.Get("NewerInstanceOk"), ""))
            return;
        window.CloseForced();
        _single?.Dispose();
        _single = null;
        try
        {
            if (request.Exe is { Length: > 0 } exe && File.Exists(exe))
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
                foreach (var a in request.Args)
                    psi.ArgumentList.Add(a);
                Dialogs.Start(psi);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"no se pudo abrir la version nueva: {ex}");
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }

    private bool _showingError;

    /// <summary>
    /// Aviso de un error inesperado. Uno cada vez: si el mismo fallo se repite mientras el aviso
    /// esta abierto (un temporizador, un redibujado), no se apilan ventanas.
    /// </summary>
    private void ShowUnexpectedError()
    {
        if (_showingError) return;
        _showingError = true;
        try
        {
            var owner = MainWindow is { IsVisible: true } w ? w : null;
            PromptWindow.Alert(owner!, Loc.Get("UnexpectedErrorTitle"), Loc.Format("UnexpectedErrorText", AppLog.FilePath));
        }
        catch (Exception ex)
        {
            AppLog.Write($"no se pudo enseñar el aviso de error: {ex}");
        }
        finally
        {
            _showingError = false;
        }
    }
}
