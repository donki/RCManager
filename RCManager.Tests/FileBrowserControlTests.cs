using System.Windows;
using System.Windows.Controls;
using SocRcManager.Files;
using SocRcManager.Localization;
using SocRcManager.Models;

namespace SocRcManager.Tests;

/// <summary>El explorador de dos paneles: transferencias con progreso, conflictos, cancelar, errores y abrir ficheros remotos.</summary>
public sealed class FileBrowserControlTests : UiTest
{
    private readonly MemServer _server = new MemServer("/srv")
        .File("/srv/notas.txt", "hola\nmundo")
        .File("/srv/imagen.png", "\0PNG")
        .File("/srv/vacio.bin", "\0")
        .Folder("/srv/web")
        .File("/srv/web/index.html", "<p>");

    private readonly Connection _options = new() { TransferParallel = 2, TransferRetries = 0, TransferOnConflict = 0 };
    private FileBrowserControl _browser = null!;
    private readonly List<string> _failed = [];
    private readonly string _oldTempRoot = FileBrowserControl.TempRoot;

    public FileBrowserControlTests()
    {
        FileBrowserControl.TempRoot = Path.Combine(Dir.Path, "abiertos");
    }

    public override void Dispose()
    {
        FileBrowserControl.TempRoot = _oldTempRoot;
        base.Dispose();
    }

    private string Local => Path.Combine(Dir.Path, "pc");

    private void Host(bool attach = true)
    {
        Directory.CreateDirectory(Local);
        Ui.Run(() =>
        {
            _browser = new FileBrowserControl();
            _browser.Failed += _failed.Add;
            Ui.Show(new Window { Width = 1200, Height = 700, Content = _browser });
            if (attach)
                _browser.Attach(new LocalSide(Local), new RemoteSide(_server), "srv", _options, _ => Task.FromResult<IRemoteFileSystem>(_server.Another()));
        });
        if (attach)
        {
            Ui.WaitUntil(() => _browser.LocalPane.CurrentPath == Local && _browser.RemotePane.CurrentPath == "/srv");
            Ui.Run(() => _browser.Engine!.RetryDelay = TimeSpan.FromMilliseconds(1));
        }
    }

    private string TransferText => Ui.Run(() => _browser.TransferText.Text);

    private void WaitText(string text)
    {
        try { Ui.WaitUntil(() => _browser.TransferText.Text == text); }
        catch (TimeoutException) { Assert.Fail($"Se esperaba «{text}» y hay «{TransferText}»; registro: {string.Join(" | ", _server.Log)}"); }
    }

    private static void Select(FilePane pane, params string[] names) => Ui.Run(() =>
    {
        pane.List.SelectedItems.Clear();
        foreach (var row in ((IEnumerable<FileRow>)pane.List.ItemsSource).Where(r => names.Contains(r.Name)))
            pane.List.SelectedItems.Add(row);
    });

    private static void Key(FilePane pane, System.Windows.Input.Key key) => Ui.Run(() => PaneInput.Key(pane.List, key));

    [Fact]
    public void Subir_ConProgreso_YAlAcabarSeRefrescaElDestino()
    {
        Host();
        File.WriteAllText(Path.Combine(Local, "a.txt"), new string('a', 3000));
        Ui.RunAsync(() => _browser.LocalPane.RefreshAsync());
        Assert.Equal(Visibility.Collapsed, Ui.Run(() => _browser.Progress.Visibility));
        Assert.Equal(Loc.Get("FilesThisPc"), Ui.Run(() => _browser.LocalPane.SideTitle.Text));
        Assert.Equal("srv", Ui.Run(() => _browser.RemotePane.SideTitle.Text));
        Assert.Equal(Loc.Get("FilesUpload"), Ui.Run(() => _browser.LocalPane.TransferButton.ToolTip));

        _server.HoldTransfers();
        Select(_browser.LocalPane, "a.txt");
        Ui.Run(() => Ui.Click(_browser.LocalPane.TransferButton));
        Ui.WaitUntil(() => _browser.TransferText.Text.Length > 0 && _browser.Progress.Value > 0);
        Assert.Equal(Visibility.Visible, Ui.Run(() => _browser.Progress.Visibility));
        Assert.Equal(Visibility.Visible, Ui.Run(() => _browser.CancelButton.Visibility));
        Assert.Equal(Loc.Format("FilesUploading", "a.txt", FileRules.SizeText(1500), FileRules.SizeText(3000)), TransferText);
        Assert.Equal(50, Ui.Run(() => _browser.Progress.Value));

        _server.Release();
        WaitText(Loc.Get("FilesDone"));
        Assert.Equal(new string('a', 3000), _server.Text("/srv/a.txt"));
        Assert.Equal(Visibility.Collapsed, Ui.Run(() => _browser.Progress.Visibility));
        Assert.Equal(Visibility.Collapsed, Ui.Run(() => _browser.CancelButton.Visibility));
        Assert.Equal(0, Ui.Run(() => _browser.Progress.Value));
        Ui.WaitUntil(() => _browser.RemotePane.List.Items.Cast<FileRow>().Any(r => r.Name == "a.txt"));
        Assert.Empty(_failed);
    }

    [Fact]
    public void Bajar_ConF5_YArrastrando_UnaCarpetaEntera()
    {
        Host();
        Select(_browser.RemotePane, "notas.txt");
        Key(_browser.RemotePane, System.Windows.Input.Key.F5);
        WaitText(Loc.Get("FilesDone"));
        Assert.Equal("hola\nmundo", File.ReadAllText(Path.Combine(Local, "notas.txt")));

        // Soltar la carpeta del servidor en este PC.
        var web = Ui.Run(() => ((IEnumerable<FileRow>)_browser.RemotePane.List.ItemsSource).Single(r => r.Name == "web").Entry);
        Ui.Run(() =>
        {
            var data = new DataObject(typeof(FilePane.FileDrag), new FilePane.FileDrag(_browser.RemotePane, [web]));
            Assert.True(PaneInput.Drag(_browser.LocalPane.List, data, drop: true).Handled);
        });
        Ui.WaitUntil(() => File.Exists(Path.Combine(Local, "web", "index.html")));
        WaitText(Loc.Get("FilesDone"));
        Ui.WaitUntil(() => _browser.LocalPane.List.Items.Cast<FileRow>().Any(r => r.Name == "web"));

        // Y de este PC al servidor, soltando en el panel remoto.
        File.WriteAllText(Path.Combine(Local, "subir.txt"), "s");
        var up = new FileEntry("subir.txt", Path.Combine(Local, "subir.txt"), false, 1, null);
        Ui.Run(() => PaneInput.Drag(_browser.RemotePane.List, new DataObject(typeof(FilePane.FileDrag), new FilePane.FileDrag(_browser.LocalPane, [up])), drop: true));
        Ui.WaitUntil(() => _server.HasFile("/srv/subir.txt"));
    }

    [Fact]
    public void Conflicto_LaVentanaPregunta_YParaTodos()
    {
        _options.TransferOnConflict = 0;
        Host();
        File.WriteAllText(Path.Combine(Local, "notas.txt"), "local");
        File.WriteAllText(Path.Combine(Local, "imagen.png"), "local");
        Ui.RunAsync(() => _browser.LocalPane.RefreshAsync());

        // Sobrescribir y «para todos»: una sola pregunta para los dos.
        string? message = null;
        Ui.Answer<ConflictWindow>(w =>
        {
            Assert.Equal(Loc.Get("ConflictTitle"), w.Title);
            message = Ui.FindAll<TextBlock>(w).Select(t => t.Text).First(t => t.Contains('«') || t.Contains('"'));
            Ui.Find<CheckBox>(w, c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "ApplyAllCheck")!.IsChecked = true;
            Ui.Click(Ui.ButtonById(w, "OverwriteButton")!);
        });
        Select(_browser.LocalPane, "notas.txt", "imagen.png");
        Key(_browser.LocalPane, System.Windows.Input.Key.F5);
        WaitText(Loc.Get("FilesDone"));
        Assert.Single(Ui.Modals);
        Assert.Contains(message, new[] { Loc.Format("ConflictMessage", "notas.txt"), Loc.Format("ConflictMessage", "imagen.png") });
        Assert.Equal("local", _server.Text("/srv/notas.txt"));
        Assert.Equal("local", _server.Text("/srv/imagen.png"));

        // Saltar: no se toca.
        _server.File("/srv/notas.txt", "remoto");
        Ui.Answer<ConflictWindow>(w => Ui.Click(Ui.ButtonById(w, "SkipButton")!));
        Select(_browser.LocalPane, "notas.txt");
        Key(_browser.LocalPane, System.Windows.Input.Key.F5);
        WaitText(Loc.Get("FilesDone"));
        Assert.Equal("remoto", _server.Text("/srv/notas.txt"));

        // Cerrar la ventana (o la X): se cancela la transferencia.
        Ui.Answer<ConflictWindow>(w => Ui.Click(Ui.ButtonById(w, "CancelButton")!));
        Select(_browser.LocalPane, "notas.txt");
        Ui.Run(() => _browser.TransferText.Text = string.Empty);
        Key(_browser.LocalPane, System.Windows.Input.Key.F5);
        WaitText(Loc.Get("FilesCancelled"));
        Assert.Equal("remoto", _server.Text("/srv/notas.txt"));
        Assert.Equal(0, Ui.PendingAnswers);
    }

    [Fact]
    public void ConflictWindow_RespuestasDeCadaBoton()
    {
        Host(attach: false);
        var owner = Ui.Run(() => Window.GetWindow(_browser)!);
        Ui.Answer<ConflictWindow>(w => Ui.Click(Ui.ButtonById(w, "SkipButton")!));
        Assert.Equal(new ConflictWindow.Answer(false, false, false), Ui.Run(() => ConflictWindow.Ask(owner, "a")));
        Ui.Answer<ConflictWindow>(w =>
        {
            Ui.Find<CheckBox>(w)!.IsChecked = true;
            Ui.Click(Ui.ButtonById(w, "SkipButton")!);
        });
        Assert.Equal(new ConflictWindow.Answer(false, true, false), Ui.Run(() => ConflictWindow.Ask(owner, "a")));
        Ui.Answer<ConflictWindow>(w => Ui.Click(Ui.ButtonById(w, "OverwriteButton")!));
        Assert.Equal(new ConflictWindow.Answer(true, false, false), Ui.Run(() => ConflictWindow.Ask(owner, "a")));
        // Sin contestar (Escape, la X): cancelar.
        Assert.Equal(new ConflictWindow.Answer(false, false, true), Ui.Run(() => ConflictWindow.Ask(owner, "a")));
        var w = (ConflictWindow)Ui.Modals[^1];
        Assert.Same(owner, Ui.Run(() => w.Owner));
        Assert.True(Ui.Run(() => Ui.ButtonById(w, "OverwriteButton")!.IsDefault));
        Assert.True(Ui.Run(() => Ui.ButtonById(w, "CancelButton")!.IsCancel));
    }

    [Fact]
    public void Error_SeVeEnLaBarra_SeAvisa_YSeRefrescanLosDosPaneles()
    {
        Host();
        File.WriteAllText(Path.Combine(Local, "a.txt"), "a");
        Ui.RunAsync(() => _browser.LocalPane.RefreshAsync());
        _server.Fail("put", new IOException("Disk\nfull"));
        var lists = _server.Log.Count(l => l == "list /srv");
        Select(_browser.LocalPane, "a.txt");
        Key(_browser.LocalPane, System.Windows.Input.Key.F5);
        WaitText("Disk full");
        Assert.Equal(["Disk\nfull"], _failed);
        Ui.WaitUntil(() => _server.Log.Count(l => l == "list /srv") > lists);
    }

    [Fact]
    public void Reintentos_DeLaConexion_AcabanBien()
    {
        _options.TransferRetries = 2;
        Host();
        _server.FailTimes("/srv/notas.txt", 2);
        Select(_browser.RemotePane, "notas.txt");
        Key(_browser.RemotePane, System.Windows.Input.Key.F5);
        WaitText(Loc.Get("FilesDone"));
        Assert.Equal(3, _server.Log.Count(l => l == "get /srv/notas.txt"));
        Assert.True(File.Exists(Path.Combine(Local, "notas.txt")));
    }

    [Fact]
    public void Cancelar_ConElBoton_YLaColaSeVacia()
    {
        Host();
        File.WriteAllText(Path.Combine(Local, "a.txt"), "a");
        File.WriteAllText(Path.Combine(Local, "b.txt"), "b");
        Ui.RunAsync(() => _browser.LocalPane.RefreshAsync());
        _server.HoldTransfers();
        Select(_browser.LocalPane, "a.txt");
        Key(_browser.LocalPane, System.Windows.Input.Key.F5);
        Ui.WaitUntil(() => _server.MaxRunning == 1);
        // Mientras va, otra peticion espera en la cola.
        Select(_browser.LocalPane, "b.txt");
        Key(_browser.LocalPane, System.Windows.Input.Key.F5);
        Ui.Run(() => Ui.Click(_browser.CancelButton));
        WaitText(Loc.Get("FilesCancelled"));
        Assert.False(_server.HasFile("/srv/a.txt"));
        Assert.False(_server.HasFile("/srv/b.txt"));
        Assert.Equal(Visibility.Collapsed, Ui.Run(() => _browser.CancelButton.Visibility));

        // Lo siguiente vuelve a ir.
        _server.Release();
        Select(_browser.LocalPane, "b.txt");
        Key(_browser.LocalPane, System.Windows.Input.Key.F5);
        Ui.WaitUntil(() => _server.HasFile("/srv/b.txt"));
    }

    [Fact]
    public void Cola_DosPeticionesSeguidas_VanUnaTrasOtra()
    {
        Host();
        File.WriteAllText(Path.Combine(Local, "a.txt"), "a");
        File.WriteAllText(Path.Combine(Local, "b.txt"), "b");
        Ui.RunAsync(() => _browser.LocalPane.RefreshAsync());
        _server.HoldTransfers();
        Select(_browser.LocalPane, "a.txt");
        Key(_browser.LocalPane, System.Windows.Input.Key.F5);
        Ui.WaitUntil(() => _server.MaxRunning == 1);
        Select(_browser.LocalPane, "b.txt");
        Key(_browser.LocalPane, System.Windows.Input.Key.F5);
        _server.Release();
        WaitText(Loc.Get("FilesDone"));
        Ui.WaitUntil(() => _server.HasFile("/srv/a.txt") && _server.HasFile("/srv/b.txt"));
        Assert.Equal(1, _server.MaxRunning);   // una peticion tras otra
    }

    [Fact]
    public void NadaQueLlevar_OUnDestinoSinCarpeta_NoEmpieza()
    {
        Host();
        Ui.Run(() =>
        {
            var data = new DataObject(typeof(FilePane.FileDrag), new FilePane.FileDrag(_browser.LocalPane, []));
            PaneInput.Drag(_browser.RemotePane.List, data, drop: true);
        });
        Assert.Equal(Visibility.Collapsed, Ui.Run(() => _browser.Progress.Visibility));

        // Con el panel local en «Este equipo» no se puede bajar ahi.
        Ui.RunAsync(() => _browser.LocalPane.NavigateAsync(string.Empty));
        Select(_browser.RemotePane, "notas.txt");
        Key(_browser.RemotePane, System.Windows.Input.Key.F5);
        Assert.Equal(string.Empty, TransferText);
        Assert.DoesNotContain(_server.Log, l => l.StartsWith("get"));
    }

    [Fact]
    public void EditarRemoto_AbreElEditor_YAlGuardarRefresca()
    {
        Host();
        Select(_browser.RemotePane, "notas.txt");
        Key(_browser.RemotePane, System.Windows.Input.Key.Enter);
        Ui.WaitUntil(() => Ui.Shown.Count == 1);
        var editor = Assert.IsType<TextEditorWindow>(Ui.Shown[0]);
        Assert.Same(Ui.Run(() => Window.GetWindow(_browser)), Ui.Run(() => editor.Owner));

        var lists = _server.Log.Count(l => l == "list /srv");
        Ui.Run(() => ((Action)typeof(TextEditorWindow).GetField("Saved", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(editor)!)());
        Ui.WaitUntil(() => _server.Log.Count(l => l == "list /srv") > lists);
    }

    [Fact]
    public void EditarRemoto_SiNoEsTexto_LoDiceLaBarra()
    {
        Host();
        Ui.RunAsync(() => _browser.EditRemotePathAsync("/srv/vacio.bin"));
        Assert.Equal(Loc.Get("EditorNotText"), TransferText);
        Assert.Empty(Ui.Shown);
    }

    [Fact]
    public void EditarRutaRemota_LaQueExiste_YLaQueNo()
    {
        Host();
        Ui.RunAsync(() => _browser.EditRemotePathAsync("/srv/no-existe.txt"));
        Assert.Empty(Ui.Shown);
        Ui.RunAsync(() => _browser.EditRemotePathAsync("/srv/web/index.html"));
        Assert.IsType<TextEditorWindow>(Assert.Single(Ui.Shown));
    }

    [Fact]
    public void AbrirRemotoFuera_BajaUnaCopiaYLaAbreWindows()
    {
        Host();
        Select(_browser.RemotePane, "imagen.png");
        Key(_browser.RemotePane, System.Windows.Input.Key.Enter);
        Ui.WaitUntil(() => Ui.Started.Count == 1);
        var copy = Path.Combine(FileBrowserControl.TempRoot, "imagen.png");
        Assert.Equal(copy, Ui.Started[0].FileName);
        Assert.True(Ui.Started[0].UseShellExecute);
        Assert.Equal("\0PNG", File.ReadAllText(copy));
        Assert.Equal(Loc.Get("FilesDone"), TransferText);

        // Si falla la bajada: en la barra, y no se abre nada.
        _server.Fail("get", new IOException("No\r\nroute"));
        Ui.Run(() => Ui.Click(_browser.RemotePane.OpenButton));
        WaitText("No route");
        Assert.Single(Ui.Started);
    }

    [Fact]
    public void SinAttach_NoHaceNada()
    {
        Host(attach: false);
        Ui.RunAsync(() => _browser.EditRemotePathAsync("/srv/notas.txt"));
        Assert.Null(Ui.Run(() => _browser.Engine));

        // Solo el panel remoto conectado: editar y abrir fuera no tienen a donde ir.
        Ui.Run(() => _browser.RemotePane.Attach(new RemoteSide(_server), "srv", "x"));
        Ui.WaitUntil(() => _browser.RemotePane.CurrentPath == "/srv");
        Select(_browser.RemotePane, "notas.txt");
        Key(_browser.RemotePane, System.Windows.Input.Key.Enter);
        Select(_browser.RemotePane, "imagen.png");
        Key(_browser.RemotePane, System.Windows.Input.Key.Enter);
        Assert.Empty(Ui.Shown);
        Assert.Empty(Ui.Started);
        Assert.DoesNotContain(_server.Log, l => l.StartsWith("get"));
        Ui.Run(() => _browser.Shutdown());
        Assert.False(_server.Disposed);
    }

    [Fact]
    public void Varios_FocoZoomNavegarAvisosYCerrar()
    {
        _options.FilesShowHidden = true;
        _server.File("/srv/.profile", "p");
        Host();
        Assert.True(Ui.Run(() => _browser.LocalPane.ShowHidden && _browser.RemotePane.ShowHidden));
        Assert.Contains(Ui.Run(() => _browser.RemotePane.List.Items.Cast<FileRow>().Select(r => r.Name).ToList()), n => n == ".profile");

        Ui.Run(() => _browser.PaneFontSize = 17);
        Assert.Equal(17, Ui.Run(() => _browser.PaneFontSize));
        Assert.Equal(17, Ui.Run(() => _browser.RemotePane.FontSize));

        Ui.RunAsync(() => _browser.RemotePaneNavigateAsync("/srv/web"));
        Assert.Equal("/srv/web", Ui.Run(() => _browser.RemotePane.CurrentPath));
        Ui.Run(() => _browser.FocusRemote());

        // Los errores de cualquiera de los dos paneles llegan al explorador.
        Ui.RunAsync(() => _browser.RemotePaneNavigateAsync("/nada"));
        Ui.RunAsync(() => _browser.LocalPane.NavigateAsync(Path.Combine(Local, "nada")));
        Assert.Equal(2, _failed.Count);
        Assert.StartsWith("No such directory", _failed[0]);

        Ui.Run(() => _browser.Shutdown());
        Assert.True(_server.Disposed);
    }
}
