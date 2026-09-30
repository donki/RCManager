using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace SocRcManager.UITests;

/// <summary>
/// Recorridos de la interfaz sobre el exe Debug en modo aislado. Cada prueba arranca su propia
/// instancia con una carpeta de datos vacia y la cierra al acabar.
/// </summary>
/// <remarks>
/// Prohibido en estas pruebas: doble clic en el arbol (conecta), el boton Conectar, arrastrar al
/// area de pestañas, Intro sobre una conexion. En modo aislado la aplicacion tampoco conectaria,
/// pero la primera barrera es no pedirlo.
/// </remarks>
public sealed class MainWindowTests
{
    private const string FakeHost = "ejemplo.invalid";

    [Fact]
    public void MainWindow_Arranca_EnModoAislado()
    {
        using var app = RcApp.Launch();

        Assert.Contains("[SOC_SANDBOX]", app.Main.Title);
        // Carpeta de datos vacia: el arbol arranca sin conexiones y con el aviso de «no hay».
        Assert.Empty(app.ById(app.Main, "Tree").FindAllChildren());
        Assert.False(app.ById(app.Main, "EmptyTree").IsOffscreen);
        Assert.True(app.Button(app.Main, "ConnectButton").IsAvailable);
        app.Capture(app.Main, "principal");
    }

    [Fact]
    public void Ajustes_SeAbrenYSeCierran()
    {
        using var app = RcApp.Launch();

        RcApp.Press(app.Button(app.Main, "SettingsButton"));
        var settings = app.WaitModal();
        Assert.True(app.Button(settings, "LocalButton").IsAvailable);
        Assert.True(app.Button(settings, "ImportButton").IsAvailable);
        app.Capture(settings, "ajustes");

        RcApp.Press(app.Button(settings, "CloseButton"));
        app.WaitNoModal();
        app.Capture(app.Main, "cerrados");
    }

    [Fact]
    public void Conexion_CrearEditarYBorrar_SinConectar()
    {
        using var app = RcApp.Launch();

        // --- Crear
        RcApp.Press(app.Button(app.Main, "NewConnectionButton"));
        var editor = app.WaitModal();
        app.TextBox(editor, "NameBox").Text = "Prueba UI";
        app.TextBox(editor, "HostBox").Text = FakeHost;
        app.TextBox(editor, "UserBox").Text = "nadie";
        app.Capture(editor, "nueva");
        RcApp.Press(app.Button(editor, "SaveButton"));
        app.WaitNoModal();

        Assert.True(app.WaitTreeContains("Prueba UI"), "La conexion nueva no sale en el arbol");
        Assert.Contains(FakeHost, app.ConnectionsJson());
        app.Capture(app.Main, "creada");

        // --- Editar (con el boton Editar: el doble clic conectaria)
        app.SelectTreeItem("Prueba UI");
        RcApp.Press(app.Button(app.Main, "EditButton"));
        editor = app.WaitModal();
        Assert.Equal("Prueba UI", app.TextBox(editor, "NameBox").Text);
        Assert.Equal(FakeHost, app.TextBox(editor, "HostBox").Text);
        app.TextBox(editor, "NameBox").Text = "Prueba UI editada";
        app.Capture(editor, "editar");
        RcApp.Press(app.Button(editor, "SaveButton"));
        app.WaitNoModal();

        Assert.True(app.WaitTreeContains("Prueba UI editada"), "El nombre editado no sale en el arbol");
        Assert.True(app.WaitTreeContains("Prueba UI", present: false), "Sigue el nombre viejo en el arbol");
        Assert.Contains("Prueba UI editada", app.ConnectionsJson());
        app.Capture(app.Main, "editada");

        // --- Borrar (pide confirmacion)
        app.SelectTreeItem("Prueba UI editada");
        RcApp.Press(app.Button(app.Main, "DeleteButton"));
        var confirm = app.WaitModal();
        app.Capture(confirm, "confirmar-borrado");
        RcApp.Press(app.Button(confirm, "OkButton"));
        app.WaitNoModal();

        Assert.True(app.WaitTreeContains("Prueba UI editada", present: false), "La conexion borrada sigue en el arbol");
        Assert.DoesNotContain(FakeHost, app.ConnectionsJson());
        app.Capture(app.Main, "borrada");
    }

    [Fact]
    public void Importar_Rdp_DesdeAjustes()
    {
        using var app = RcApp.Launch();
        var rdp = RcApp.TestData("servidor-de-prueba.rdp");
        Assert.True(File.Exists(rdp));

        RcApp.Press(app.Button(app.Main, "SettingsButton"));
        var settings = app.WaitModal();
        RcApp.Press(app.Button(settings, "ImportButton"));

        // El dialogo comun de Windows para abrir ficheros: se escribe la ruta en «Nombre» (1148) y
        // se pulsa «Abrir» (1), todo por UI Automation.
        // Se buscan juntos y desde cero en cada vuelta: el dialogo aparece antes de tener dentro
        // la casilla, y un elemento pedido demasiado pronto no se actualiza solo.
        var found = Retry.WhileNull(() =>
            {
                var d = app.Main.ModalWindows.Concat(settings.ModalWindows)
                    .FirstOrDefault(w => w.Properties.ClassName.ValueOrDefault == "#32770");
                var edit = d?.FindFirstDescendant(app.Cf.ByAutomationId("1148").And(app.Cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit)));
                return edit is null ? null : Tuple.Create(d!, edit);
            },
            TimeSpan.FromSeconds(30), throwOnTimeout: true, ignoreException: true,
            timeoutMessage: "No se abre el dialogo de abrir fichero o no tiene la casilla del nombre (1148)").Result!;
        var (dialog, fileName) = (found.Item1, found.Item2);
        fileName.Patterns.Value.Pattern.SetValue(rdp);
        app.Capture(dialog, "dialogo-abrir");
        dialog.FindFirstChild(app.Cf.ByAutomationId("1"))!.AsButton().Invoke();
        Retry.WhileTrue(() => app.Main.ModalWindows.Any(w => w.Properties.ClassName.ValueOrDefault == "#32770"), RcApp.Timeout,
            throwOnTimeout: true, timeoutMessage: "El dialogo de abrir fichero no se cierra");

        Assert.True(app.WaitTreeContains("servidor-de-prueba"), "La conexion importada no sale en el arbol");
        var json = app.ConnectionsJson();
        Assert.Contains("importado.invalid", json);
        Assert.Contains("3390", json);

        // Ajustes sigue abierto detras del dialogo: se cierra.
        settings = app.WaitModal();
        RcApp.Press(app.Button(settings, "CloseButton"));
        app.WaitNoModal();
        app.Capture(app.Main, "importada");
    }

    [Fact]
    public void Idioma_CambiaEntreEspañolEIngles()
    {
        using var app = RcApp.Launch();
        var settingsButton = app.Button(app.Main, "SettingsButton");
        var emptyTree = app.ById(app.Main, "EmptyTree");

        const string SettingsEs = "Ajustes: dónde se guardan las conexiones";
        const string SettingsEn = "Settings: where connections are stored";
        const string EmptyEs = "Todavía no hay conexiones. Añade una con + y aparecerá aquí.";
        const string EmptyEn = "No connections yet. Add one with + and it will show up here.";

        // Empieza en el idioma del Windows; se comprueba que cada pulsacion pasa al otro y vuelve.
        var startsSpanish = settingsButton.HelpText == SettingsEs;
        Assert.Equal(startsSpanish ? SettingsEs : SettingsEn, settingsButton.HelpText);
        Assert.Equal(startsSpanish ? EmptyEs : EmptyEn, emptyTree.Name);
        app.Capture(app.Main, startsSpanish ? "es" : "en");

        RcApp.Press(app.Button(app.Main, "LanguageButton"));
        Assert.True(Retry.WhileFalse(() => settingsButton.HelpText == (startsSpanish ? SettingsEn : SettingsEs), RcApp.Timeout).Result,
            "El tooltip de Ajustes no cambia de idioma");
        Assert.Equal(startsSpanish ? EmptyEn : EmptyEs, emptyTree.Name);
        app.Capture(app.Main, startsSpanish ? "en" : "es");

        RcApp.Press(app.Button(app.Main, "LanguageButton"));
        Assert.True(Retry.WhileFalse(() => settingsButton.HelpText == (startsSpanish ? SettingsEs : SettingsEn), RcApp.Timeout).Result,
            "No vuelve al idioma de partida");
    }

    [Fact]
    public void AcercaDe_EnseñaLaVersionYSeCierra()
    {
        using var app = RcApp.Launch();

        RcApp.Press(app.Button(app.Main, "AboutButton"));
        var about = app.WaitModal();
        var version = app.ById(about, "VersionLabel").Name;
        Assert.Matches(@"^v\d{4}\.\d+\.\d+\.\d+", version);
        Assert.True(app.Button(about, "SpanishButton").IsAvailable);
        Assert.True(app.Button(about, "EnglishButton").IsAvailable);
        app.Capture(about, "acerca-de");

        RcApp.Press(app.Button(about, "CloseButton"));
        app.WaitNoModal();
    }
}
