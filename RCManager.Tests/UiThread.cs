using System.Windows;
using System.Windows.Threading;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>
/// El hilo de interfaz de las pruebas: un hilo STA con la <see cref="App"/> de la aplicacion (sus
/// recursos: estilos, colores) y su Dispatcher. Las ventanas y controles se crean y se manejan aqui
/// con <see cref="Run(Action)"/>; las modales se abren de verdad pero fuera de la pantalla, sin
/// activarse y sin boton en la barra de tareas, y las contesta la prueba (<see cref="Answer"/>).
/// </summary>
/// <remarks>
/// Hay un solo Application por proceso: este hilo vive lo que dura la tanda. Las pruebas van de una
/// en una (Support.cs), asi que no se pisan.
/// </remarks>
public static class Ui
{
    private static readonly Lazy<Dispatcher> _dispatcher = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Queue<Func<Window, bool>> _answers = new();

    /// <summary>Posicion fuera de cualquier pantalla donde se abren las ventanas de las pruebas.</summary>
    public const double Offscreen = -32000;

    public static Dispatcher Dispatcher => _dispatcher.Value;

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        Exception? error = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                App.SkipStartup = true;
                var app = new App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                dispatcher = Dispatcher.CurrentDispatcher;
                // Un error en el hilo de interfaz no tumba la tanda: lo recoge la prueba que espera.
                dispatcher.UnhandledException += (_, e) => { LastError = e.Exception; e.Handled = true; };
            }
            catch (Exception ex)
            {
                error = ex;
            }
            ready.Set();
            if (error is null)
                Dispatcher.Run();
        })
        { IsBackground = true, Name = "Ui de las pruebas" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (error is not null)
            throw new InvalidOperationException("No se pudo arrancar el hilo de interfaz de las pruebas", error);

        // Ninguna ventana de la aplicacion se activa al abrirse (como el modo aislado).
        dispatcher!.Invoke(Sandbox.QuietWindows);

        InstallHooks();
        return dispatcher;
    }

    /// <summary>Las puertas de Dialogs en modo prueba (tambien al acabar cada prueba, por si una las cambio).</summary>
    private static void InstallHooks()
    {
        Dialogs.ShowModal = w =>
        {
            Hide(w);
            // La respuesta se da cuando la ventana ya esta abierta (dentro de su bucle modal).
            w.Dispatcher.BeginInvoke(() => Respond(w), DispatcherPriority.ApplicationIdle);
            return w.ShowDialog();
        };
        Dialogs.ShowWindow = w =>
        {
            Hide(w);
            w.Show();
            Shown.Add(w);
        };
        Dialogs.PickFiles = (_, _, _) => PickedFiles;
        Dialogs.PickSaveFile = (_, _, name) => { SaveAsAsked = name; return SaveAs; };
        Dialogs.WriteFile = (path, text) => Written[path] = text;
        Dialogs.Start = psi => Started.Add(psi);
        Dialogs.SystemMessage = (_, text, title) => Messages.Add((title, text));
    }

    /// <summary>Lo ultimo que fallo en el hilo de interfaz sin que nadie lo recogiera.</summary>
    public static Exception? LastError { get; set; }

    /// <summary>Ventanas no modales que la aplicacion ha enseñado (Dialogs.ShowWindow).</summary>
    public static List<Window> Shown { get; } = [];

    /// <summary>Lo que la aplicacion ha pedido abrir con Windows (no se abre nada).</summary>
    public static List<System.Diagnostics.ProcessStartInfo> Started { get; } = [];

    /// <summary>Avisos del sistema que la aplicacion ha enseñado.</summary>
    public static List<(string Title, string Text)> Messages { get; } = [];

    /// <summary>Lo que «elige» el usuario en el dialogo de abrir ficheros (null = cancela).</summary>
    public static string[]? PickedFiles { get; set; }

    /// <summary>Lo que contesta el dialogo de guardar (null: cancelar) y el nombre que propuso la aplicacion.</summary>
    public static string? SaveAs { get; set; }
    public static string? SaveAsAsked { get; set; }

    /// <summary>Ficheros escritos por la aplicacion (ruta → contenido), sin tocar el disco.</summary>
    public static Dictionary<string, string> Written { get; } = [];

    /// <summary>Ventanas modales que se han abierto, en orden (para comprobar lo que se pregunto).</summary>
    public static List<Window> Modals { get; } = [];

    /// <summary>Saca la ventana de la pantalla antes de enseñarla.</summary>
    public static void Hide(Window w)
    {
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = Offscreen;
        w.Top = Offscreen;
        w.ShowInTaskbar = false;
        w.ShowActivated = false;
    }

    /// <summary>Enseña la ventana fuera de la pantalla y sin activarla (hace falta para ser Owner de una modal).</summary>
    public static T Show<T>(T w) where T : Window
    {
        Hide(w);
        w.Show();
        return w;
    }

    /// <summary>
    /// Encola la respuesta a la proxima ventana modal: la funcion la maneja (rellenar, pulsar) y
    /// devuelve true si ya la ha cerrado; si devuelve false, se cierra con DialogResult = false.
    /// Sin respuesta encolada, la modal se cancela.
    /// </summary>
    public static void Answer(Func<Window, bool> answer) => _answers.Enqueue(answer);

    /// <summary>Encola una respuesta para una modal de un tipo concreto.</summary>
    public static void Answer<T>(Action<T> answer) where T : Window =>
        Answer(w =>
        {
            Assert.IsType<T>(w);
            answer((T)w);
            return !w.IsVisible;
        });

    /// <summary>Cuantas respuestas quedan sin usar (una prueba deberia acabar con 0).</summary>
    public static int PendingAnswers => _answers.Count;

    public static void ClearAnswers() => _answers.Clear();

    private static void Respond(Window w)
    {
        Modals.Add(w);
        var closed = false;
        try
        {
            if (_answers.TryDequeue(out var answer))
                closed = answer(w);
        }
        catch (Exception ex)
        {
            LastError = ex;
        }
        if (!closed && w.IsVisible)
        {
            try { w.DialogResult = false; }
            catch (InvalidOperationException) { w.Close(); }
        }
    }

    /// <summary>Ejecuta en el hilo de interfaz y espera.</summary>
    public static void Run(Action action)
    {
        LastError = null;
        Dispatcher.Invoke(action);
        Flush();
    }

    /// <summary>Ejecuta en el hilo de interfaz y devuelve el resultado.</summary>
    public static T Run<T>(Func<T> func)
    {
        LastError = null;
        var r = Dispatcher.Invoke(func);
        Flush();
        return r;
    }

    /// <summary>Ejecuta algo asincrono en el hilo de interfaz y espera a que acabe.</summary>
    public static void RunAsync(Func<Task> func, int timeoutMs = 20000)
    {
        LastError = null;
        var task = Dispatcher.InvokeAsync(func).Task.Unwrap();
        if (!task.Wait(timeoutMs))
            throw new TimeoutException("La operacion en el hilo de interfaz no acabo a tiempo");
        Flush();
    }

    /// <summary>Deja que el hilo de interfaz acabe lo que tenga pendiente (BeginInvoke, enlaces, diseño).</summary>
    public static void Flush()
    {
        Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (LastError is { } e)
        {
            LastError = null;
            throw new InvalidOperationException("Error en el hilo de interfaz", e);
        }
    }

    /// <summary>Espera (sin bloquear el hilo de interfaz) hasta que se cumpla la condicion, comprobada en el hilo de interfaz.</summary>
    public static void WaitUntil(Func<bool> condition, int timeoutMs = 10000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!Dispatcher.Invoke(condition))
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException("La condicion no se cumplio a tiempo");
            Thread.Sleep(20);
        }
        Flush();
    }

    /// <summary>Pulsa un boton como un clic (Click y su comando, al momento). Si esta desactivado, no hace nada.</summary>
    public static void Click(System.Windows.Controls.Primitives.ButtonBase button)
    {
        if (!button.IsEnabled)
            return;
        typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(button, null);
    }

    /// <summary>Busca en el arbol visual y logico un elemento de ese tipo que cumpla la condicion.</summary>
    public static T? Find<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject =>
        FindAll(root, match).FirstOrDefault();

    /// <summary>Todos los elementos de ese tipo (arbol visual y logico).</summary>
    public static List<T> FindAll<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        var found = new List<T>();
        var seen = new HashSet<DependencyObject>();
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var o = queue.Dequeue();
            if (!seen.Add(o))
                continue;
            if (o is T t && (match is null || match(t)))
                found.Add(t);
            if (o is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D)
                for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(o); i++)
                    queue.Enqueue(System.Windows.Media.VisualTreeHelper.GetChild(o, i));
            foreach (var child in LogicalTreeHelper.GetChildren(o).OfType<DependencyObject>())
                queue.Enqueue(child);
        }
        return found;
    }

    /// <summary>Boton por su AutomationId (los botones de solo icono lo llevan).</summary>
    public static System.Windows.Controls.Button? ButtonById(DependencyObject root, string automationId) =>
        Find<System.Windows.Controls.Button>(root, b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == automationId);

    /// <summary>Cierra las ventanas que una prueba haya dejado abiertas y vacia los registros.</summary>
    public static void Reset()
    {
        Dispatcher.Invoke(() =>
        {
            foreach (var w in Application.Current?.Windows.OfType<Window>().ToList() ?? [])
            {
                // Las ventanas sueltas, al cerrarse con la X, piden volver a la principal en vez de cerrarse.
                if (w is SessionWindow session)
                    session.ForceClose = true;
                try { w.Close(); } catch (Exception) { }
            }
        });
        // Las ventanas se apuntan al cambio de idioma (estatico) y no se borran: que una prueba que
        // cambie el idioma desde otro hilo no llame a ventanas de pruebas anteriores.
        typeof(Localization.Loc).GetField("LanguageChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.SetValue(null, null);
        InstallHooks();
        ClearAnswers();
        Shown.Clear();
        Started.Clear();
        Messages.Clear();
        Modals.Clear();
        PickedFiles = null;
        SaveAs = null;
        SaveAsAsked = null;
        Written.Clear();
        LastError = null;
    }
}
