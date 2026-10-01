using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace SocRcManager.UITests;

/// <summary>
/// Pestañas sueltas (sacar una sesion a su ventana y devolverla) e instancia unica, sobre el exe Debug
/// en modo aislado.
/// </summary>
/// <remarks>
/// Las sesiones se abren con <c>--open</c> sobre conexiones guardadas a <c>ejemplo.invalid</c>: en
/// modo aislado la aplicacion abre la pestaña con el control de la sesion (el ActiveX de Escritorio
/// remoto, el terminal) pero <b>no conecta</b>, y ese dominio ademas no existe (constitucion general
/// 8.4). Nada de clics: todo por patrones de UI Automation.
/// </remarks>
public sealed class SessionWindowTests
{
    private static readonly string TwoSessions = RcApp.Connections(("Prueba RDP", "Rdp", 3389), ("Prueba SSH", "Ssh", 22));

    private static AutomationElement[] SessionTabs(RcApp app) =>
        app.Main.FindAllDescendants(app.Cf.ByAutomationId("SessionTab"));

    private static AutomationElement WaitTab(RcApp app, string name) =>
        Retry.WhileNull(() => SessionTabs(app).FirstOrDefault(t => t.Name == name), RcApp.Timeout, throwOnTimeout: true,
            timeoutMessage: $"No sale la pestaña «{name}»").Result!;

    /// <summary>Ventanas sueltas de la aplicacion.</summary>
    private static Window[] Detached(RcApp app) =>
        app.TopWindows().Where(w => w.Properties.AutomationId.ValueOrDefault == "SessionWindow").ToArray();

    /// <summary>
    /// La ventana nativa del control de Escritorio remoto (ActiveX de mstscax dentro del
    /// WindowsFormsHost): la que tiene que seguir siendo la misma al moverla, o la sesion se habria
    /// cortado.
    /// </summary>
    private static IntPtr RdpHandle(RcApp app, AutomationElement root)
    {
        var host = Retry.WhileNull(() => root.FindAllDescendants().FirstOrDefault(e =>
                (e.Properties.ClassName.ValueOrDefault ?? string.Empty).Contains("ATL:", StringComparison.OrdinalIgnoreCase)
                || (e.Properties.ClassName.ValueOrDefault ?? string.Empty).StartsWith("WindowsForms10", StringComparison.Ordinal)),
            RcApp.Timeout, throwOnTimeout: false).Result;
        if (host is null)
        {
            app.DumpTree(root, "sin-control-rdp");
            throw new Xunit.Sdk.XunitException("No se encuentra la ventana nativa del control RDP");
        }
        return host.Properties.NativeWindowHandle.Value;
    }

    [Fact]
    public void Pestaña_SaleAUnaVentanaYVuelve_SinRecrearElControl()
    {
        using var app = RcApp.LaunchWith(TwoSessions, ["--open", "Prueba RDP", "--open", "Prueba SSH"]);

        var rdpTab = WaitTab(app, "Prueba RDP");
        WaitTab(app, "Prueba SSH");
        rdpTab.Patterns.SelectionItem.Pattern.Select();
        var handle = RdpHandle(app, app.Main);
        app.Capture(app.Main, "dos-pestañas");

        // --- Sacar la pestaña RDP a su ventana (boton de la pestaña)
        RcApp.Press(app.Button(rdpTab, "DetachButton"));
        var window = Retry.WhileNull(() => Detached(app).FirstOrDefault(), RcApp.Timeout, throwOnTimeout: true,
            timeoutMessage: "No se abre la ventana suelta").Result!;
        window.WaitUntilClickable(RcApp.Timeout);
        Assert.Contains("Prueba RDP", window.Title);
        Assert.True(RcApp.WaitUntil(() => SessionTabs(app).Length == 1), "La pestaña sigue en la principal");
        Assert.Equal("Prueba SSH", SessionTabs(app)[0].Name);
        // El mismo control (la misma ventana nativa), ahora dentro de la ventana suelta.
        Assert.Equal(handle, RdpHandle(app, window));
        Assert.True(RcApp.IsAlive(handle));
        app.Capture(window, "ventana-suelta");
        app.Capture(app.Main, "principal-sin-rdp");

        // --- Volver a la principal (boton de la ventana suelta)
        RcApp.Press(app.Button(window, "AttachButton"));
        Assert.True(RcApp.WaitUntil(() => Detached(app).Length == 0), "La ventana suelta no se cierra");
        rdpTab = WaitTab(app, "Prueba RDP");
        Assert.Equal(2, SessionTabs(app).Length);
        rdpTab.Patterns.SelectionItem.Pattern.Select();
        Assert.Equal(handle, RdpHandle(app, app.Main));
        Assert.True(RcApp.IsAlive(handle));
        app.Capture(app.Main, "de-vuelta");

        // Se recuerda donde estaba la ventana suelta.
        var settings = File.ReadAllText(Path.Combine(app.DataFolder, "settings.json"));
        Assert.Contains("DetachedWindows", settings);
        Assert.Contains("\"Width\"", settings);
    }

    [Fact]
    public void VariasVentanasSueltas_AlCerrarLaPrincipal_PreguntaYCierraTodo()
    {
        using var app = RcApp.LaunchWith(TwoSessions, ["--open", "Prueba RDP", "--open", "Prueba SSH"]);
        var rdpTab = WaitTab(app, "Prueba RDP");
        var sshTab = WaitTab(app, "Prueba SSH");

        RcApp.Press(app.Button(rdpTab, "DetachButton"));
        Assert.True(RcApp.WaitUntil(() => Detached(app).Length == 1));
        RcApp.Press(app.Button(sshTab, "DetachButton"));
        Assert.True(RcApp.WaitUntil(() => Detached(app).Length == 2), "No salen dos ventanas sueltas");
        Assert.Empty(SessionTabs(app));
        Assert.False(app.ById(app.Main, "EmptyTabs").IsOffscreen);
        foreach (var w in Detached(app))
            app.Capture(w, "suelta");

        // Cerrar la principal pregunta; cancelar lo deja todo como estaba.
        app.Main.Patterns.Window.Pattern.Close();
        var confirm = app.WaitModal();
        app.Capture(confirm, "preguntar");
        RcApp.Press(app.Button(confirm, "CancelButton"));
        app.WaitNoModal();
        Assert.Equal(2, Detached(app).Length);
        Assert.False(app.App.HasExited);

        // Aceptar cierra la principal y las sueltas.
        app.Main.Patterns.Window.Pattern.Close();
        confirm = app.WaitModal();
        RcApp.Press(app.Button(confirm, "OkButton"));
        Assert.True(RcApp.WaitUntil(() => app.App.HasExited), "La aplicacion no se cierra");
    }

    [Fact]
    public void SegundaInstancia_TraeLaPrimeraDeLaBandeja_YAbreLaConexion()
    {
        using var app = RcApp.LaunchWith(TwoSessions, []);
        var hwnd = app.Main.Properties.NativeWindowHandle.Value;

        // Minimizar la esconde (en modo aislado, sin icono en el area de notificacion).
        app.Main.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Minimized);
        Assert.True(RcApp.WaitUntil(() => !RcApp.IsVisible(hwnd)), "La ventana no se esconde");

        // Se abre otra vez con --open: la segunda se va y la primera sale con la conexion abierta.
        var watch = Stopwatch.StartNew();
        using (var second = Process.Start(app.StartInfo(["--open", "Prueba SSH"]))!)
        {
            Assert.True(second.WaitForExit(15000), "La segunda instancia no se cierra");
            Assert.Equal(0, second.ExitCode);
        }
        Assert.True(RcApp.WaitUntil(() => RcApp.IsVisible(hwnd)), "La primera no vuelve de la bandeja");
        WaitTab(app, "Prueba SSH");
        Assert.Single(app.TopWindows());
        File.AppendAllText(Path.Combine(RcApp.ArtifactsFolder, "foco.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} segunda instancia atendida en {watch.ElapsedMilliseconds} ms{Environment.NewLine}");
        app.Capture(app.Main, "traida-al-frente");

        // Con --tray (arranque con Windows) no se enseña nada mas: la segunda se va igual.
        using (var tray = Process.Start(app.StartInfo(["--tray"]))!)
        {
            Assert.True(tray.WaitForExit(15000), "La instancia con --tray no se cierra");
        }
        Assert.Single(SessionTabs(app));
    }

    [Fact]
    public void ArrancaEnLaBandeja_YLaSegundaInstanciaLaEnseña()
    {
        using var app = RcApp.LaunchWith(null, ["--tray"], hidden: true);

        // Sin ventana visible mientras esta en la bandeja.
        Thread.Sleep(1500);
        Assert.False(app.App.HasExited);
        Assert.Equal(IntPtr.Zero, Process.GetProcessById(app.App.ProcessId).MainWindowHandle);

        using (var second = Process.Start(app.StartInfo([]))!)
        {
            Assert.True(second.WaitForExit(15000), "La segunda instancia no se cierra");
        }
        var main = app.WaitMainWindow();
        Assert.Contains("[SOC_SANDBOX]", main.Title);
        app.Capture(main, "desde-la-bandeja");
    }
}
