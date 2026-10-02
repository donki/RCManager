using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SocRcManager.Files;
using SocRcManager.Localization;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>Un lado del explorador de ficheros: navegar, ordenar, abrir, crear, renombrar, borrar, permisos, teclas y arrastrar.</summary>
public sealed class FilePaneTests : UiTest
{
    private readonly MemServer _server = new MemServer("/srv")
        .Folder("/srv/www", 0x1ED, "www-data", "www-data")
        .File("/srv/notas.txt", "hola", new DateTime(2024, 1, 2, 3, 4, 0), 0x1A4, "root", "adm")
        .File("/srv/imagen.png", "\0PNG", null, 0x1A4, "root")
        .File("/srv/.bashrc", "alias ll=ls", null, 0x1A4)
        .File("/srv/www/index.html", "<p>", null, 0x1A4);

    private FilePane _pane = null!;
    private Window _window = null!;

    private string Status => Ui.Run(() => _pane.PaneStatus.Text);

    private List<FileRow> Rows => Ui.Run(() => ((IEnumerable<FileRow>)_pane.List.ItemsSource).ToList());

    private void Host(IFileSide side, string title = "Lado")
    {
        Ui.Run(() =>
        {
            _pane = new FilePane();
            _window = Ui.Show(new Window { Width = 900, Height = 600, Content = _pane });
            _pane.Attach(side, title, "Llevar");
        });
        Ui.WaitUntil(() => _pane.List.ItemsSource is not null);
    }

    private void HostRemote() => Host(new RemoteSide(_server), "srv");

    private void HostLocal()
    {
        Dir.File("Zeta.txt", "z");
        Dir.File("alfa.log", "aa");
        Dir.File("Beta/dentro.txt", "d");
        Dir.File("carpeta/x.txt", "x");
        Dir.File(".oculto", "o");
        var hidden = Dir.File("marcado.txt", "m");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        Host(new LocalSide(Dir.Path), "Este equipo");
    }

    private void Select(params string[] names) => Ui.Run(() =>
    {
        _pane.List.SelectedItems.Clear();
        foreach (var row in ((IEnumerable<FileRow>)_pane.List.ItemsSource).Where(r => names.Contains(r.Name)))
            _pane.List.SelectedItems.Add(row);
    });

    private void Key(Key key) => Ui.Run(() => PaneInput.Key(_pane.List, key));

    private void WaitPath(string path) => Ui.WaitUntil(() => _pane.CurrentPath == path);

    [Fact]
    public void Local_CarpetasPrimero_OrdenSinMayusculas_YSinOcultos()
    {
        HostLocal();
        Assert.Equal(["Beta", "carpeta", "alfa.log", "Zeta.txt"], Rows.Select(r => r.Name));
        Assert.Equal(Loc.Format("FilesCount", 2, 2), Status);
        Assert.Equal(Dir.Path, Ui.Run(() => _pane.PathBox.Text));
        Assert.Equal("Este equipo", Ui.Run(() => _pane.SideTitle.Text));
        Assert.Equal(Visibility.Collapsed, Ui.Run(() => _pane.EditButton.Visibility));
        Assert.Equal(Visibility.Collapsed, Ui.Run(() => _pane.PermissionsButton.Visibility));
        Assert.Equal(0, Ui.Run(() => _pane.ModeColumn.Width));
        Assert.Equal("", Ui.Run(() => (string)_pane.TransferButton.Content));
        Assert.Same(Ui.Run(() => _pane.Side), Ui.Run(() => _pane.Side));
        Assert.True(Ui.Run(() => _pane.Side!.IsLocal));

        // Con los ocultos: el del punto y el marcado.
        Ui.Run(() => Ui.Click(_pane.HiddenButton));
        Ui.WaitUntil(() => _pane.List.Items.Count == 6);
        Assert.True(Ui.Run(() => _pane.ShowHidden));
        Assert.Contains(Rows, r => r.Name == ".oculto");
        Assert.Contains(Rows, r => r.Name == "marcado.txt");
    }

    [Fact]
    public void Filas_TextosDeCadaColumna()
    {
        var file = new FileRow(new FileEntry("a.txt", "/a.txt", false, 2048, new DateTime(2024, 1, 2, 3, 4, 0), 0x1A4, "root", "adm"));
        Assert.Equal("a.txt", file.Name);
        Assert.Equal(FileRules.SizeText(2048), file.SizeText);
        Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 0).ToString("g"), file.DateText);
        Assert.Equal("rw-r--r--", file.ModeText);
        Assert.Equal("root:adm", file.OwnerText);
        Assert.Equal("", file.Glyph);

        var dir = new FileRow(new FileEntry("d", "/d", true, 0, null, null, "root"));
        Assert.Equal("", dir.Glyph);
        Assert.Equal(string.Empty, dir.SizeText);
        Assert.Equal(string.Empty, dir.DateText);
        Assert.Equal(string.Empty, dir.ModeText);
        Assert.Equal("root", dir.OwnerText);
        Assert.Equal(string.Empty, new FileRow(new FileEntry("e", "/e", true, 0, null)).OwnerText);
    }

    [Fact]
    public void Navegar_EntrarSubirRutaEscritaYDobleClic()
    {
        HostLocal();
        Select("Beta");
        Key(System.Windows.Input.Key.Enter);
        WaitPath(Path.Combine(Dir.Path, "Beta"));
        Assert.Equal(["dentro.txt"], Rows.Select(r => r.Name));

        Key(System.Windows.Input.Key.Back);
        WaitPath(Dir.Path);

        Select("carpeta");
        Ui.Run(() => PaneInput.DoubleClick(_pane.List));
        WaitPath(Path.Combine(Dir.Path, "carpeta"));

        Ui.Run(() => Ui.Click(_pane.UpButton));
        WaitPath(Dir.Path);

        // Ruta escrita (con espacios alrededor) e Intro.
        Ui.Run(() =>
        {
            _pane.PathBox.Text = "  " + Path.Combine(Dir.Path, "Beta") + " ";
            PaneInput.Key(_pane.PathBox, System.Windows.Input.Key.Enter);
        });
        WaitPath(Path.Combine(Dir.Path, "Beta"));
        // Otra tecla en la ruta no navega.
        Ui.Run(() => { _pane.PathBox.Text = Dir.Path; Assert.False(PaneInput.Key(_pane.PathBox, System.Windows.Input.Key.A)); });

        // Abrir con el boton una carpeta tambien entra.
        Ui.RunAsync(() => _pane.NavigateAsync(Dir.Path));
        Select("Beta");
        Ui.Run(() => Ui.Click(_pane.OpenButton));
        WaitPath(Path.Combine(Dir.Path, "Beta"));

        // Refrescar ve lo nuevo.
        Dir.File("Beta/nuevo.txt", "n");
        Ui.Run(() => Ui.Click(_pane.RefreshButton));
        Ui.WaitUntil(() => _pane.List.Items.Count == 2);
    }

    [Fact]
    public void Navegar_DesdeLaRaizDeUnaUnidad_VaAEsteEquipo_YDeAhiNoSube()
    {
        HostLocal();
        var root = Path.GetPathRoot(Dir.Path)!;
        Ui.RunAsync(() => _pane.NavigateAsync(root));
        Ui.Run(() => Ui.Click(_pane.UpButton));
        WaitPath(string.Empty);
        Assert.Equal(Loc.Get("FilesThisPc"), Ui.Run(() => _pane.PathBox.Text));
        Assert.Contains(Rows, r => r.Entry.FullPath.Equals(root, StringComparison.OrdinalIgnoreCase));
        Assert.All(Rows, r => Assert.True(r.Entry.IsDirectory));

        // En «Este equipo» no se sube mas, ni se crea, renombra o borra nada.
        Key(System.Windows.Input.Key.Back);
        Assert.Equal(string.Empty, Ui.Run(() => _pane.CurrentPath));
        Select(Rows[0].Name);
        Key(System.Windows.Input.Key.F7);
        Key(System.Windows.Input.Key.F2);
        Key(System.Windows.Input.Key.Delete);
        Assert.Empty(Ui.Modals);

        // Copiar ruta sin seleccion ni ruta: nada.
        var copied = new List<string>();
        var old = FilePane.SetClipboard;
        FilePane.SetClipboard = copied.Add;
        try
        {
            Ui.Run(() => { _pane.List.SelectedItems.Clear(); Ui.Click(_pane.CopyPathButton); });
            Assert.Empty(copied);
        }
        finally { FilePane.SetClipboard = old; }
    }

    [Fact]
    public void RutaQueNoExiste_ErrorEnLaBarraYAviso()
    {
        HostRemote();
        var failed = new List<string>();
        Ui.Run(() => _pane.Failed += failed.Add);
        Ui.RunAsync(() => _pane.NavigateAsync("/no/existe"));
        Assert.Equal("No such directory: /no/existe", Status);
        Assert.Equal(["No such directory:\n/no/existe"], failed);
        Assert.Equal("/srv", Ui.Run(() => _pane.CurrentPath));   // se queda donde estaba
    }

    [Fact]
    public void ListadoCancelado_GanaLaUltimaNavegacion()
    {
        var slow = new SlowSide(new RemoteSide(_server));
        Host(slow);
        slow.Hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Ui.Run(() => { _ = _pane.NavigateAsync("/srv/www"); });
        slow.Hold = null;
        Ui.RunAsync(() => _pane.NavigateAsync("/srv"));
        Ui.Run(() => slow.Release(true));
        Assert.Equal("/srv", Ui.Run(() => _pane.CurrentPath));
        Assert.Contains(Rows, r => r.Name == "www");

        // Si el lado lanza OperationCanceledException, no pasa nada.
        slow.ThrowCancel = true;
        Ui.RunAsync(() => _pane.NavigateAsync("/srv/www"));
        Assert.Equal("/srv", Ui.Run(() => _pane.CurrentPath));
        Assert.Equal(Loc.Format("FilesCount", 1, 2), Status);
    }

    [Fact]
    public void SinLado_NoHaceNada()
    {
        Ui.Run(() =>
        {
            var pane = new FilePane();
            Ui.Show(new Window { Content = pane });
            Assert.Null(pane.Side);
            Assert.True(pane.NavigateAsync("/x").IsCompleted);
            Ui.Click(pane.UpButton);
            Ui.Click(pane.NewFolderButton);
            Ui.Click(pane.RenameButton);
            Ui.Click(pane.DeleteButton);
            Ui.Click(pane.PermissionsButton);
            Ui.Click(pane.TransferButton);
            Ui.Click(pane.EditButton);
            Ui.Click(pane.OpenButton);
            PaneInput.Key(pane.List, System.Windows.Input.Key.Enter);
            Assert.Equal(string.Empty, pane.CurrentPath);

            // Aunque tuviera filas, sin lado no abre nada.
            pane.List.ItemsSource = new List<FileRow> { new(new FileEntry("a.txt", "/a.txt", false, 1, null)) };
            pane.List.SelectedIndex = 0;
            PaneInput.Key(pane.List, System.Windows.Input.Key.Enter);
            Ui.Click(pane.OpenButton);
        });
        Assert.Empty(Ui.Modals);
        Assert.Empty(Ui.Started);
    }

    [Fact]
    public void Remoto_ColumnasUnix_YOcultosConPunto()
    {
        HostRemote();
        Assert.Equal(["www", "imagen.png", "notas.txt"], Rows.Select(r => r.Name));
        Assert.Equal(90, Ui.Run(() => _pane.ModeColumn.Width));
        Assert.Equal(110, Ui.Run(() => _pane.OwnerColumn.Width));
        Assert.Equal(Visibility.Visible, Ui.Run(() => _pane.PermissionsButton.Visibility));
        Assert.Equal(Visibility.Visible, Ui.Run(() => _pane.EditButton.Visibility));
        Assert.Equal("root:adm", Rows.Single(r => r.Name == "notas.txt").OwnerText);

        // Un directorio sin permisos (servidor Windows): fuera las columnas.
        _server.Folder("/srv/www/vacio");
        Ui.RunAsync(() => _pane.NavigateAsync("/srv/www/vacio"));
        Assert.Equal(0, Ui.Run(() => _pane.ModeColumn.Width));
        Assert.Equal(Visibility.Collapsed, Ui.Run(() => _pane.PermissionsButton.Visibility));

        // Un servidor que no admite permisos no los enseña aunque vengan.
        _server.SupportsPermissions = false;
        Ui.RunAsync(() => _pane.NavigateAsync("/srv"));
        Assert.Equal(0, Ui.Run(() => _pane.ModeColumn.Width));
        _server.SupportsPermissions = true;
        Ui.RunAsync(() => _pane.NavigateAsync("/srv"));
        Assert.Equal(90, Ui.Run(() => _pane.ModeColumn.Width));
        Ui.RunAsync(() => _pane.RefreshAsync());
        Assert.Equal(90, Ui.Run(() => _pane.ModeColumn.Width));

        // Con los ocultos, tambien el que empieza por punto.
        Ui.Run(() => Ui.Click(_pane.HiddenButton));
        Ui.WaitUntil(() => _pane.List.Items.Count == 4);
    }

    [Fact]
    public void Columnas_ElNombreOcupaLoQueSobra()
    {
        HostRemote();
        Ui.Run(() => { _window.Width = 1000; });
        Ui.WaitUntil(() => _pane.ActualWidth > 900);
        var expected = Ui.Run(() => _pane.ActualWidth - 90 - 130 - 90 - 110 - 40);
        Assert.Equal(expected, Ui.Run(() => _pane.NameColumn.Width), 3);
        Ui.Run(() => { _window.Width = 300; });
        Ui.WaitUntil(() => _pane.ActualWidth < 400);
        Assert.Equal(120, Ui.Run(() => _pane.NameColumn.Width));
    }

    [Fact]
    public void Abrir_LocalConWindows_YSiFallaLoDiceLaBarra()
    {
        HostLocal();
        Select("alfa.log");
        Key(System.Windows.Input.Key.Enter);
        Assert.Equal(Path.Combine(Dir.Path, "alfa.log"), Assert.Single(Ui.Started).FileName);
        Assert.True(Ui.Started[0].UseShellExecute);

        Ui.Run(() => Ui.Click(_pane.OpenButton));
        Assert.Equal(2, Ui.Started.Count);

        var old = Dialogs.Start;
        Dialogs.Start = _ => throw new System.ComponentModel.Win32Exception("No hay\r\nprograma");
        try
        {
            Ui.Run(() => PaneInput.DoubleClick(_pane.List));
            Assert.Equal("No hay programa", Status);
        }
        finally { Dialogs.Start = old; }

        // Editar no existe en local: el boton esta oculto y no pide nada.
        var edits = new List<FileEntry>();
        Ui.Run(() => _pane.EditRequested += edits.Add);
        Ui.Run(() => Ui.Click(_pane.EditButton));
        Assert.Single(edits);   // (el boton invoca igual; el panel lo oculta en local)
    }

    [Fact]
    public void Abrir_Remoto_TextoAlEditor_BinarioFuera()
    {
        HostRemote();
        var edits = new List<string>();
        var external = new List<string>();
        Ui.Run(() =>
        {
            _pane.EditRequested += e => edits.Add(e.Name);
            _pane.OpenExternalRequested += e => external.Add(e.Name);
        });

        Select("notas.txt");
        Key(System.Windows.Input.Key.Enter);
        Select("imagen.png");
        Ui.Run(() => PaneInput.DoubleClick(_pane.List));
        Assert.Equal(["notas.txt"], edits);
        Assert.Equal(["imagen.png"], external);

        // El boton de abrir siempre usa el programa de Windows; el de editar, el editor.
        Select("notas.txt");
        Ui.Run(() => Ui.Click(_pane.OpenButton));
        Ui.Run(() => Ui.Click(_pane.EditButton));
        Assert.Equal(["imagen.png", "notas.txt"], external);
        Assert.Equal(["notas.txt", "notas.txt"], edits);

        // Con una carpeta, editar no hace nada.
        Select("www");
        Ui.Run(() => Ui.Click(_pane.EditButton));
        Assert.Equal(2, edits.Count);
        Assert.Empty(Ui.Started);
    }

    [Fact]
    public void CopiarRuta_LoSeleccionadoOLaCarpeta()
    {
        HostRemote();
        var copied = new List<string>();
        var old = FilePane.SetClipboard;
        FilePane.SetClipboard = copied.Add;
        try
        {
            Select("notas.txt", "www");
            Ui.Run(() => Ui.Click(_pane.CopyPathButton));
            Assert.Equal(new[] { "/srv/www", "/srv/notas.txt" }.Order(), copied[0].Split(Environment.NewLine).Order());
            Assert.Equal(Loc.Format("FilesPathCopied", 2), Status);

            Select();
            Ui.Run(() => Ui.Click(_pane.CopyPathButton));
            Assert.Equal("/srv", copied[1]);
            Assert.Equal(Loc.Format("FilesPathCopied", 1), Status);
        }
        finally { FilePane.SetClipboard = old; }
    }

    [Fact]
    public void NuevaCarpeta_F7_ConNombreRecortado_YCancelarOVacioNoHaceNada()
    {
        HostRemote();
        Ui.Answer<PromptWindow>(w =>
        {
            Assert.Equal(Loc.Get("FilesNewFolder"), w.Title);
            Ui.Find<TextBox>(w)!.Text = "  datos ";
            Ui.Click(Ui.ButtonById(w, "OkButton")!);
        });
        Key(System.Windows.Input.Key.F7);
        Ui.WaitUntil(() => _pane.List.Items.Cast<FileRow>().Any(r => r.Name == "datos"));
        Assert.Contains("mkdir /srv/datos", _server.Log);

        // Cancelar y nombre en blanco.
        Key(System.Windows.Input.Key.F7);
        Ui.Answer<PromptWindow>(w => { Ui.Find<TextBox>(w)!.Text = "   "; Ui.Click(Ui.ButtonById(w, "OkButton")!); });
        Ui.Run(() => Ui.Click(_pane.NewFolderButton));
        Assert.Equal(1, _server.Log.Count(l => l.StartsWith("mkdir")));

        // Error del servidor: en la barra.
        Ui.Answer<PromptWindow>(w => { Ui.Find<TextBox>(w)!.Text = "datos"; Ui.Click(Ui.ButtonById(w, "OkButton")!); });
        Ui.Run(() => Ui.Click(_pane.NewFolderButton));
        Ui.WaitUntil(() => _pane.PaneStatus.Text == "File exists");
        Assert.Equal(0, Ui.PendingAnswers);
    }

    [Fact]
    public void Renombrar_F2_PropoeElNombre_YMismoNombreNoHaceNada()
    {
        HostRemote();
        Select("notas.txt");
        string? proposed = null;
        Ui.Answer<PromptWindow>(w =>
        {
            proposed = Ui.Find<TextBox>(w)!.Text;
            Ui.Find<TextBox>(w)!.Text = " leeme.txt ";
            Ui.Click(Ui.ButtonById(w, "OkButton")!);
        });
        Key(System.Windows.Input.Key.F2);
        Ui.WaitUntil(() => _pane.List.Items.Cast<FileRow>().Any(r => r.Name == "leeme.txt"));
        Assert.Equal("notas.txt", proposed);
        Assert.Contains("mv /srv/notas.txt /srv/leeme.txt", _server.Log);

        // El mismo nombre, en blanco o cancelar: nada.
        Select("leeme.txt");
        Ui.Answer<PromptWindow>(w => Ui.Click(Ui.ButtonById(w, "OkButton")!));
        Ui.Run(() => Ui.Click(_pane.RenameButton));
        Ui.Answer<PromptWindow>(w => { Ui.Find<TextBox>(w)!.Text = ""; Ui.Click(Ui.ButtonById(w, "OkButton")!); });
        Ui.Run(() => Ui.Click(_pane.RenameButton));
        Ui.Run(() => Ui.Click(_pane.RenameButton));
        Assert.Equal(1, _server.Log.Count(l => l.StartsWith("mv")));

        // Sin seleccion: ni pregunta.
        Select();
        var modals = Ui.Modals.Count;
        Ui.Run(() => Ui.Click(_pane.RenameButton));
        Assert.Equal(modals, Ui.Modals.Count);

        // Error del servidor.
        _server.Fail("mv", new IOException("Permission denied"));
        Select("leeme.txt");
        Ui.Answer<PromptWindow>(w => { Ui.Find<TextBox>(w)!.Text = "otro"; Ui.Click(Ui.ButtonById(w, "OkButton")!); });
        Key(System.Windows.Input.Key.F2);
        Ui.WaitUntil(() => _pane.PaneStatus.Text == "Permission denied");
    }

    [Fact]
    public void Borrar_Supr_ConfirmaYBorraCarpetasEnteras()
    {
        HostRemote();
        Select("www", "notas.txt");
        string? message = null;
        Ui.Answer<PromptWindow>(w =>
        {
            message = Ui.FindAll<TextBlock>(w).Select(t => t.Text).First(t => t.Contains("Borrar"));
            Ui.Click(Ui.ButtonById(w, "OkButton")!);
        });
        Key(System.Windows.Input.Key.Delete);
        Ui.WaitUntil(() => _pane.List.Items.Count == 1);
        Assert.Equal(Loc.Format("FilesDeleteConfirm", Loc.Format("FilesItems", 2)), message);
        Assert.False(_server.HasDir("/srv/www"));
        Assert.False(_server.HasFile("/srv/www/index.html"));
        Assert.False(_server.HasFile("/srv/notas.txt"));
        Assert.True(_server.Log.IndexOf("rm /srv/www/index.html") < _server.Log.IndexOf("rmdir /srv/www"));

        // Uno solo: su nombre. Cancelar no borra.
        Select("imagen.png");
        Ui.Answer<PromptWindow>(w => message = Ui.FindAll<TextBlock>(w).Select(t => t.Text).First(t => t.Contains("Borrar")));
        Ui.Run(() => Ui.Click(_pane.DeleteButton));
        Assert.Equal(Loc.Format("FilesDeleteConfirm", "imagen.png"), message);
        Assert.True(_server.HasFile("/srv/imagen.png"));

        // Sin seleccion: ni pregunta.
        Select();
        var modals = Ui.Modals.Count;
        Key(System.Windows.Input.Key.Delete);
        Assert.Equal(modals, Ui.Modals.Count);

        // Error: en la barra, y se vuelve a listar.
        _server.Fail("rm", new IOException("Read-only\nfile system"));
        Select("imagen.png");
        Ui.Answer<PromptWindow>(w => Ui.Click(Ui.ButtonById(w, "OkButton")!));
        var lists = _server.Log.Count(l => l == "list /srv");
        Ui.Run(() => Ui.Click(_pane.DeleteButton));
        Ui.WaitUntil(() => _server.Log.Count(l => l == "list /srv") > lists);
        Assert.Equal("Read-only file system", Status);
    }

    [Fact]
    public void Borrar_EnLocal_LaCarpetaConTodo()
    {
        HostLocal();
        Select("Beta");
        Ui.Answer<PromptWindow>(w => Ui.Click(Ui.ButtonById(w, "OkButton")!));
        Key(System.Windows.Input.Key.Delete);
        Ui.WaitUntil(() => !_pane.List.Items.Cast<FileRow>().Any(r => r.Name == "Beta"));
        Assert.False(Directory.Exists(Path.Combine(Dir.Path, "Beta")));
    }

    [Fact]
    public void Permisos_AbreLaVentanaYAplicaElCambio()
    {
        HostRemote();
        Select("www");
        Ui.Answer<PermissionsWindow>(w =>
        {
            Ui.Find<TextBox>(w, t => t.Width == 80)!.Text = "750";
            Ui.Find<CheckBox>(w, c => c.Visibility == Visibility.Visible && c.Content is string)!.IsChecked = true;   // recursivo
            Ui.Click(Ui.Find<Button>(w, b => b.IsDefault)!);
        });
        Ui.Run(() => Ui.Click(_pane.PermissionsButton));
        Ui.WaitUntil(() => _pane.PaneStatus.Text == Loc.Get("PermsApplied"));
        Assert.Contains("chmod 750 /srv/www", _server.Log);
        Assert.Contains("chmod 750 /srv/www/index.html", _server.Log);

        // Sin cambiar nada: no se aplica (al volver a listar se pierde la seleccion).
        Select("www");
        var count = _server.Log.Count;
        Ui.Answer<PermissionsWindow>(w => Ui.Click(Ui.Find<Button>(w, b => b.IsDefault)!));
        Ui.Run(() => Ui.Click(_pane.PermissionsButton));
        Ui.Run(() => Ui.Click(_pane.PermissionsButton));   // cancelada (sin respuesta)
        Assert.Equal(count, _server.Log.Count);

        // Error del servidor.
        _server.Fail("chown", new UnauthorizedAccessException("Operation not permitted"));
        Select("notas.txt");
        Ui.Answer<PermissionsWindow>(w =>
        {
            Ui.FindAll<TextBox>(w).First(t => t.Text == "root").Text = "josep";
            Ui.Click(Ui.Find<Button>(w, b => b.IsDefault)!);
        });
        Ui.Run(() => Ui.Click(_pane.PermissionsButton));
        Assert.Equal("Operation not permitted", Status);
        Assert.Equal(0, Ui.PendingAnswers);

        // Sin seleccion: nada.
        Select();
        var modals = Ui.Modals.Count;
        Ui.Run(() => Ui.Click(_pane.PermissionsButton));
        Assert.Equal(modals, Ui.Modals.Count);
    }

    [Fact]
    public void Permisos_EnLocal_NoHaceNada()
    {
        HostLocal();
        Select("alfa.log");
        Ui.Run(() => Ui.Click(_pane.PermissionsButton));
        Assert.Empty(Ui.Modals);
    }

    [Fact]
    public void Transferir_F5OBoton_ConLoSeleccionado()
    {
        HostRemote();
        var asked = new List<IReadOnlyList<FileEntry>>();
        Ui.Run(() => _pane.TransferRequested += asked.Add);
        Select();
        Key(System.Windows.Input.Key.F5);
        Assert.Empty(asked);
        Select("notas.txt", "imagen.png");
        Key(System.Windows.Input.Key.F5);
        Ui.Run(() => Ui.Click(_pane.TransferButton));
        Assert.Equal(2, asked.Count);
        Assert.Equal(["imagen.png", "notas.txt"], asked[0].Select(e => e.Name).Order());

        // Una tecla que no es de la lista no se maneja.
        Assert.False(Ui.Run(() => PaneInput.Key(_pane.List, System.Windows.Input.Key.F9)));
    }

    [Fact]
    public void Arrastrar_AlOtroPanel_YNoAlMismo()
    {
        HostRemote();
        var source = _pane;
        FilePane target = null!;
        Ui.Run(() =>
        {
            target = new FilePane();
            Ui.Show(new Window { Content = target });
            target.Attach(new RemoteSide(_server), "otro", "Llevar");
        });
        Ui.WaitUntil(() => target.CurrentPath == "/srv");

        var dropped = new List<(FilePane From, IReadOnlyList<FileEntry> Entries)>();
        Ui.Run(() => target.DroppedFrom += (f, e) => dropped.Add((f, e)));
        DependencyObject? started = null;
        IDataObject? data = null;
        var old = FilePane.StartDrag;
        FilePane.StartDrag = (s, d) => { started = s; data = d; };
        try
        {
            Select("notas.txt");
            Ui.Run(() =>
            {
                var item = (ListViewItem)source.List.ItemContainerGenerator.ContainerFromItem(source.List.SelectedItem);
                // Sin boton pulsado sobre un elemento no se arma.
                PaneInput.MouseDown(source.List);
                source.TryStartDrag(true, Mouse.GetPosition(source.List) + new Vector(50, 50));
                Assert.Null(started);

                // Pulsado sobre un elemento: poco movimiento no arrastra; sin boton tampoco; lejos, si.
                PaneInput.MouseDown(Ui.Find<TextBlock>(item, t => t.Text == "notas.txt")!);
                var start = Mouse.GetPosition(source.List);
                PaneInput.MouseMove(source.List);
                source.TryStartDrag(true, start + new Vector(1, 1));
                source.TryStartDrag(false, start + new Vector(50, 50));
                Assert.Null(started);
                source.TryStartDrag(true, start + new Vector(50, 0));
                Assert.Same(source.List, started);

                // Ya no esta armado: otro movimiento no vuelve a empezar.
                started = null;
                source.TryStartDrag(true, start + new Vector(80, 0));
                Assert.Null(started);
            });

            Ui.Run(() =>
            {
                // Encima del otro panel: copiar; encima del mismo: no.
                Assert.Equal(DragDropEffects.Copy, PaneInput.Drag(target.List, data!, drop: false).Effects);
                Assert.Equal(DragDropEffects.None, PaneInput.Drag(source.List, data!, drop: false).Effects);
                Assert.Equal(DragDropEffects.None, PaneInput.Drag(target.List, new DataObject("texto"), drop: false).Effects);

                Assert.False(PaneInput.Drag(source.List, data!, drop: true).Handled);
                Assert.False(PaneInput.Drag(target.List, new DataObject("texto"), drop: true).Handled);
                Assert.True(PaneInput.Drag(target.List, data!, drop: true).Handled);
            });
            var drop = Assert.Single(dropped);
            Assert.Same(source, drop.From);
            Assert.Equal("/srv/notas.txt", Assert.Single(drop.Entries).FullPath);

            // Sin seleccion no se arrastra.
            Select();
            Ui.Run(() =>
            {
                var item = (ListViewItem)source.List.ItemContainerGenerator.ContainerFromIndex(0);
                PaneInput.MouseDown(item);
                source.TryStartDrag(true, Mouse.GetPosition(source.List) + new Vector(50, 50));
            });
            Assert.Null(started);
        }
        finally { FilePane.StartDrag = old; }
    }

    [Fact]
    public void Arrastrar_SobreEsteEquipo_NoSeAdmite()
    {
        HostLocal();
        Ui.RunAsync(() => _pane.NavigateAsync(string.Empty));
        FilePane other = null!;
        Ui.Run(() => other = new FilePane());
        var data = new DataObject(typeof(FilePane.FileDrag), new FilePane.FileDrag(other, []));
        Ui.Run(() =>
        {
            Assert.Equal(DragDropEffects.None, PaneInput.Drag(_pane.List, data, drop: false).Effects);
            Assert.False(PaneInput.Drag(_pane.List, data, drop: true).Handled);
        });
    }

    /// <summary>Un lado que puede hacerse esperar al listar (para ver que gana la ultima navegacion).</summary>
    private sealed class SlowSide(IFileSide inner) : IFileSide
    {
        private readonly List<TaskCompletionSource<bool>> _waiting = [];
        public TaskCompletionSource<bool>? Hold { get; set; }
        public bool ThrowCancel { get; set; }
        public bool IsLocal => inner.IsLocal;
        public string InitialDirectory => inner.InitialDirectory;

        public async Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken cancellationToken)
        {
            if (ThrowCancel)
                throw new OperationCanceledException();
            if (Hold is { } hold)
            {
                _waiting.Add(hold);
                await hold.Task;
            }
            return await inner.ListAsync(path, cancellationToken);
        }

        public void Release(bool value)
        {
            foreach (var w in _waiting)
                w.TrySetResult(value);
        }

        public string Parent(string path) => inner.Parent(path);
        public string Combine(string directory, string name) => inner.Combine(directory, name);
        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken) => inner.CreateDirectoryAsync(path, cancellationToken);
        public Task DeleteFileAsync(string path, CancellationToken cancellationToken) => inner.DeleteFileAsync(path, cancellationToken);
        public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken) => inner.DeleteDirectoryAsync(path, cancellationToken);
        public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken) => inner.RenameAsync(path, newPath, cancellationToken);
    }
}
