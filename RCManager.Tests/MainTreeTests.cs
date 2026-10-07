using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SocRcManager.Localization;
using SocRcManager.Models;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>El arbol de la ventana principal: carpetas y conexiones, alta, edicion, borrado, busqueda, importar y arrastrar.</summary>
public sealed class MainTreeTests : MainWindowTest
{
    [Fact]
    public void Arbol_con_carpetas_anidadas_detalle_del_servidor_y_estado_guardado()
    {
        var web = Conn("Web", "Clientes/Acme", ConnectionKind.Ssh, "web.acme");
        var same = Conn("srv1", "Clientes", host: "srv1");
        var noName = Conn("", "", ConnectionKind.Sftp, "x.lan");
        var ftp = Conn("Ftp", "", ConnectionKind.Ftp, "ftp.lan");
        Seed([web, same, noName, ftp], "Vacia", "Clientes/Otra");
        new AppSettings { ExpandedFolders = ["Clientes"], SelectedConnectionId = same.Id, TreeWidth = 260 }.Save();

        var main = NewMain();
        Ui.Run(() =>
        {
            Assert.Equal(["Clientes", "Acme", "Web", "Otra", "srv1", "Vacia", Loc.Get("Unnamed"), "Ftp"], Items(main).Select(Text).ToList());
            // El servidor sale al lado si no es el nombre.
            Assert.Equal(["Web", "web.acme"], Texts(Item(main, "Web")));
            Assert.Equal(["srv1"], Texts(Item(main, "srv1")));
            // Como se dejo: «Clientes» abierta, «Acme» no, la seleccion y el ancho.
            Assert.True(Item(main, "Clientes").IsExpanded);
            Assert.False(Item(main, "Acme").IsExpanded);
            Assert.True(Item(main, "srv1").IsSelected);
            Assert.Equal(260, main.TreeColumn.Width.Value);
            Assert.Equal(Visibility.Collapsed, main.EmptyTree.Visibility);
            Assert.True(main.ConnectButton.IsEnabled);
            Assert.True(main.DuplicateButton.IsEnabled);
            Assert.Equal(Loc.Get("EditTooltip"), main.EditButton.ToolTip);

            // Una carpeta seleccionada: ni conectar ni duplicar; editar es renombrar.
            Select(main, "Vacia");
            Assert.False(main.ConnectButton.IsEnabled);
            Assert.False(main.DuplicateButton.IsEnabled);
            Assert.True(main.DeleteButton.IsEnabled);
            Assert.Equal(Loc.Get("RenameFolderTooltip"), main.EditButton.ToolTip);
            Item(main, "Acme").IsExpanded = true;
            Select(main, "Web");
            main.Close();
        });

        // Al cerrar se guarda lo abierto, la seleccion y el ancho.
        var s = AppSettings.Load();
        Assert.Equal(["Clientes", "Clientes/Acme"], s.ExpandedFolders);
        Assert.Equal(web.Id, s.SelectedConnectionId);
        Assert.Equal(260, s.TreeWidth);
    }

    [Fact]
    public void Arbol_vacio_sin_estado_guardado()
    {
        var main = NewMain();
        Ui.Run(() =>
        {
            Assert.Empty(main.Tree.Items);
            Assert.Equal(Visibility.Visible, main.EmptyTree.Visibility);
            Assert.False(main.EditButton.IsEnabled);
            Assert.False(main.DeleteButton.IsEnabled);
            Assert.False(main.ConnectButton.IsEnabled);
            Assert.Equal(320, main.TreeColumn.Width.Value);   // sin ancho guardado, el del diseño
            Assert.Equal(Loc.Get("Ready"), main.StatusText.Text);
            Assert.Equal(Loc.Get("AppTitle"), main.Title);
        });
    }

    [Fact]
    public void Sin_estado_guardado_todo_abierto_y_lo_cerrado_se_respeta_al_repintar()
    {
        Seed([Conn("A", "Uno/Dos"), Conn("B", "Tres")]);
        var main = NewMain();
        Ui.Run(() =>
        {
            Assert.All(Items(main).Where(i => Texts(i).Count == 1 && i.Items.Count > 0), i => Assert.True(i.IsExpanded));
            Item(main, "Tres").IsExpanded = false;
            // Repintar (cambiar de idioma lo hace) respeta lo que se ha cerrado a mano.
            Ui.Click(main.LanguageButton);
            Ui.Click(main.LanguageButton);
            Assert.False(Item(main, "Tres").IsExpanded);
            Assert.True(Item(main, "Uno").IsExpanded);
        });
    }

    [Fact]
    public void Buscar_filtra_por_nombre_servidor_y_abre_las_carpetas()
    {
        Seed([Conn("Correo", "Oficina/Servidores", host: "mail.lan"), Conn("Web", "Clientes"), Conn("Base", "")], "Vacia");
        var main = NewMain();
        Ui.Run(() =>
        {
            Item(main, "Oficina").IsExpanded = false;
            main.SearchBox.Text = "mail";
            Assert.Equal(Visibility.Collapsed, main.SearchHint.Visibility);
            Assert.Equal(["Oficina", "Servidores", "Correo"], Items(main).Select(Text).ToList());
            Assert.All(Items(main).Take(2), i => Assert.True(i.IsExpanded));

            main.SearchBox.Text = "nada de nada";
            Assert.Empty(main.Tree.Items);
            Assert.Equal(Visibility.Collapsed, main.EmptyTree.Visibility);   // hay conexiones, solo que no encajan

            main.SearchBox.Text = "";
            Assert.Equal(Visibility.Visible, main.SearchHint.Visibility);
            Assert.Contains("Vacia", Items(main).Select(Text));
        });
    }

    [Fact]
    public void Carpeta_nueva_dentro_de_la_seleccionada_y_sin_barras()
    {
        Seed([Conn("Web", "Clientes")]);
        var main = NewMain();
        Ui.Answer<PromptWindow>(w =>
        {
            Ui.Find<TextBox>(w)!.Text = " Acme/Sur ";
            Ui.Click(Ui.ButtonById(w, "OkButton")!);
        });
        Ui.Run(() =>
        {
            Select(main, "Web");
            Ui.Click(main.NewFolderButton);
        });
        Assert.Contains("Clientes/Acme-Sur", Saved().EmptyFolders);
        Ui.Run(() => Assert.Contains("Acme-Sur", Items(main).Select(Text)));

        // En la raiz (sin seleccion) y cancelando o en blanco: nada.
        Ui.Answer<PromptWindow>(w => { Ui.Find<TextBox>(w)!.Text = "Raiz"; Ui.Click(Ui.ButtonById(w, "OkButton")!); });
        Ui.Run(() =>
        {
            Item(main, "Web").IsSelected = false;
            Ui.Click(main.NewFolderButton);
        });
        Ui.Answer<PromptWindow>(w => { Ui.Find<TextBox>(w)!.Text = "  "; Ui.Click(Ui.ButtonById(w, "OkButton")!); });
        Ui.Run(() => Ui.Click(main.NewFolderButton));
        Ui.Run(() => Ui.Click(main.NewFolderButton));   // cancelada
        Assert.Equal(["Clientes/Acme-Sur", "Raiz"], Saved().EmptyFolders);
        Assert.Equal(4, Ui.Modals.Count);
    }

    private static void FillConnection(ConnectionWindow w, string name, string host)
    {
        w.NameBox.Text = name;
        w.HostBox.Text = host;
        Ui.Click(w.SaveButton);
    }

    [Fact]
    public void Conexion_nueva_en_la_carpeta_seleccionada()
    {
        Seed([], "Clientes");
        var main = NewMain();
        Ui.Answer<ConnectionWindow>(w => FillConnection(w, "Nueva", "nueva.lan"));
        Ui.Run(() =>
        {
            Select(main, "Clientes");
            Ui.Click(main.NewConnectionButton);
            Assert.True(Item(main, "Nueva").IsSelected);
        });
        var c = Assert.Single(Saved().Connections);
        Assert.Equal(("Nueva", "nueva.lan", "Clientes"), (c.Name, c.Host, c.Folder));

        // Cancelada: nada.
        Ui.Run(() => Ui.Click(main.NewConnectionButton));
        Assert.Single(Saved().Connections);
    }

    [Fact]
    public void Editar_conexion_la_sustituye_y_cancelar_no_toca_nada()
    {
        var c = Conn("Vieja", "Clientes");
        Seed([c]);
        var main = NewMain();
        Ui.Run(() => Select(main, "Vieja"));
        Ui.Run(() => Ui.Click(main.EditButton));   // cancelada
        Assert.Equal("Vieja", Saved().Connections[0].Name);

        Ui.Answer<ConnectionWindow>(w => FillConnection(w, "Renombrada", "otra.lan"));
        Ui.Run(() =>
        {
            Ui.Click(main.EditButton);
            Assert.True(Item(main, "Renombrada").IsSelected);
        });
        var saved = Assert.Single(Saved().Connections);
        Assert.Equal((c.Id, "Renombrada", "otra.lan"), (saved.Id, saved.Name, saved.Host));
    }

    [Fact]
    public void Renombrar_carpeta_mueve_lo_de_dentro()
    {
        Seed([Conn("Web", "Clientes/Acme")]);
        var main = NewMain();
        Ui.Answer<PromptWindow>(w =>
        {
            var box = Ui.Find<TextBox>(w)!;
            Assert.Equal("Acme", box.Text);   // se propone el nombre de ahora
            box.Text = "Acme/Norte";
            Ui.Click(Ui.ButtonById(w, "OkButton")!);
        });
        Ui.Run(() =>
        {
            Select(main, "Acme");
            Ui.Click(main.EditButton);
        });
        Assert.Equal("Clientes/Acme-Norte", Saved().Connections[0].Folder);

        // La de arriba del todo, y el mismo nombre (o cancelar) no cambia nada.
        Ui.Answer<PromptWindow>(w => { Ui.Find<TextBox>(w)!.Text = "Gente"; Ui.Click(Ui.ButtonById(w, "OkButton")!); });
        Ui.Run(() =>
        {
            Select(main, "Clientes");
            Ui.Click(main.EditButton);
        });
        Assert.Equal("Gente/Acme-Norte", Saved().Connections[0].Folder);
        Ui.Answer<PromptWindow>(w => Ui.Click(Ui.ButtonById(w, "OkButton")!));
        Ui.Run(() =>
        {
            Select(main, "Gente");
            Ui.Click(main.EditButton);
            Ui.Click(main.EditButton);
        });
        Assert.Equal("Gente/Acme-Norte", Saved().Connections[0].Folder);
    }

    [Fact]
    public void Duplicar_hace_una_copia_nueva_sin_fecha_de_conexion()
    {
        var c = Conn("Web", "Clientes");
        c.LastConnectedAt = DateTime.Now;
        Seed([c]);
        var main = NewMain();
        Ui.Run(() =>
        {
            Select(main, "Web");
            Ui.Click(main.DuplicateButton);
            Assert.True(Item(main, "Web (2)").IsSelected);
            // Sin conexion seleccionada no hace nada.
            Item(main, "Web (2)").IsSelected = false;
            Peek.Call(main, "OnDuplicateClick", main, new RoutedEventArgs());
        });
        var all = Saved().Connections;
        Assert.Equal(2, all.Count);
        var copy = all.Single(x => x.Name == "Web (2)");
        Assert.NotEqual(c.Id, copy.Id);
        Assert.Null(copy.LastConnectedAt);
        Assert.Equal(("Clientes", "web.lan"), (copy.Folder, copy.Host));
    }

    [Fact]
    public void Boton_derecho_en_una_conexion_copia_o_edita()
    {
        Seed([Conn("Web", "Clientes"), Conn("Otra", "Clientes")]);
        var main = NewMain();
        var edited = false;
        Ui.Answer<ConnectionWindow>(w =>
        {
            edited = w.NameBox.Text == "Otra";
            Ui.Click(w.CancelButton);
        });
        Ui.Run(() =>
        {
            // Las carpetas llevan solo Exportar; cada conexion, el suyo con Copiar y Editar.
            Assert.Single(Item(main, "Clientes").ContextMenu!.Items);
            Select(main, "Otra");
            var menu = Item(main, "Web").ContextMenu!;
            var entries = menu.Items.Cast<MenuItem>().ToList();
            Assert.Equal(["CopyMenuItem", "EditMenuItem"], entries.Select(System.Windows.Automation.AutomationProperties.GetAutomationId));
            Assert.Equal([Loc.Get("CopyMenu"), Loc.Get("EditMenu")], entries.Select(e => (string)e.Header));
            Assert.All(entries, e => Assert.IsType<TextBlock>(e.Icon));

            // Abrir el menu selecciona la fila sobre la que se pulso (el boton derecho no lo hace solo).
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
            Assert.True(Item(main, "Web").IsSelected);
            entries[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, entries[0]));
            Assert.True(Item(main, "Web (2)").IsSelected);

            // Editar, sobre la fila del menu: abre el editor de esa conexion.
            var other = Item(main, "Otra").ContextMenu!;
            other.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, other));
            var edit = other.Items.Cast<MenuItem>().Last();
            edit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, edit));
        });
        Assert.Equal(0, Ui.PendingAnswers);
        Assert.True(edited);
        Assert.Equal(3, Saved().Connections.Count);
    }

    private static void AnswerPassphrase(string? passphrase) =>
        Ui.Answer<PromptWindow>(w =>
        {
            if (passphrase is null) { Ui.Click(Ui.ButtonById(w, "CancelButton")!); return; }
            Ui.Find<Controls.RevealPasswordBox>(w)!.Password = passphrase;
            Ui.Click(Ui.ButtonById(w, "OkButton")!);
        });

    [Fact]
    public void Exportar_una_carpeta_con_frase_e_importarla_en_otro_sitio()
    {
        Seed([Conn("Dc", "Clientes/Acme"), Conn("Fs", "Clientes/Acme/Sub"), Conn("Web", "Clientes")], "Clientes/Acme/Vacia");
        var main = NewMain();
        var menu = Ui.Run(() => Item(main, "Acme").ContextMenu!);
        MenuItem Export() => (MenuItem)menu.Items[0];
        void Click() => Ui.Run(() =>
        {
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
            Export().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, Export()));
        });

        Ui.Run(() =>
        {
            Assert.Equal("ExportFolderMenuItem", System.Windows.Automation.AutomationProperties.GetAutomationId(Export()));
            Assert.Equal(Loc.Get("ExportFolderMenu"), Export().Header);
        });

        // Cancelar la frase o el dialogo de guardar: no se escribe nada.
        AnswerPassphrase(null);
        Click();
        AnswerPassphrase("frase");
        Click();
        Assert.Equal("Acme.rcm", Ui.SaveAsAsked);
        Assert.Empty(Ui.Written);

        Ui.SaveAs = "C:/fuera/Acme.rcm";
        AnswerPassphrase("frase");
        Click();
        Assert.Equal(0, Ui.PendingAnswers);
        var text = Assert.Single(Ui.Written).Value;
        Ui.Run(() =>
        {
            Assert.True(Item(main, "Acme").IsSelected);
            Assert.Equal(Loc.Format("ExportedEncrypted", 2, "Acme.rcm"), main.StatusText.Text);
            Assert.Contains(Loc.Format("ExportPassphrase", 2), Ui.FindAll<TextBlock>(Ui.Modals.Last()).Select(t => t.Text));
        });

        // Importarlo: pide la frase; mala, lo dice; buena, entra «Acme» en la raiz con las contraseñas.
        var file = Dir.File("Acme.rcm", text);
        Ui.PickedFiles = [file];
        AnswerPassphrase(null);
        Ui.Run(main.ImportRdm);
        AnswerPassphrase("mala");
        Ui.Run(main.ImportRdm);
        Ui.Run(() => Assert.Equal(Loc.Get("ImportWrongPassphrase"), main.StatusText.Text));
        Assert.Equal(3, Saved().Connections.Count);

        AnswerPassphrase("frase");
        Ui.Run(main.ImportRdm);
        var store = Saved();
        Assert.Equal(5, store.Connections.Count);
        var dc = store.Connections.Single(c => c.Folder == "Acme");
        Assert.Equal("pw", Secrets.Unprotect(dc.PasswordProtected));
        Assert.Contains("Acme/Vacia", store.EmptyFolders);
        Ui.Run(() => Assert.Equal(Loc.Format("ImportedWithPasswords", 2, 0, 0), main.StatusText.Text));

        // Otra vez el mismo: ya estan, no se duplican ni las carpetas vacias.
        AnswerPassphrase("frase");
        Ui.Run(main.ImportRdm);
        Assert.Equal(5, Saved().Connections.Count);
        Assert.Single(Saved().EmptyFolders, f => f == "Acme/Vacia");
        Ui.Run(() => Assert.Equal(Loc.Format("ImportedWithPasswords", 0, 2, 0), main.StatusText.Text));
    }

    [Fact]
    public void Exportar_todo_sin_frase_y_sin_nada_que_exportar()
    {
        Seed([]);
        var main = NewMain();
        Ui.Run(() => main.ExportConnections(""));
        Ui.Run(() => Assert.Equal(Loc.Get("ExportNothing"), main.StatusText.Text));
        Assert.Equal(0, Ui.PendingAnswers);

        Seed([Conn("Dc", "Clientes"), Conn("Suelta")]);
        main = NewMain();
        Ui.SaveAs = "C:/fuera/todo.rcm";
        AnswerPassphrase("");
        Ui.Run(() => main.ExportConnections(""));
        Assert.Equal("conexiones.rcm", Ui.SaveAsAsked);
        var text = Ui.Written["C:/fuera/todo.rcm"];
        Assert.False(ConnectionExport.IsEncrypted(text));
        Assert.DoesNotContain("dpapi", text);
        Ui.Run(() => Assert.Equal(Loc.Format("ExportedPlain", 2, "todo.rcm"), main.StatusText.Text));

        // Sin contraseñas: al importar no se pide frase.
        Ui.PickedFiles = [Dir.File("todo.rcm", text)];
        Ui.Run(main.ImportRdm);
        Ui.Run(() => Assert.Equal(Loc.Format("Imported", 0, 2, 0), main.StatusText.Text));

        // Un fallo al escribir se dice.
        Dialogs.WriteFile = (_, _) => throw new IOException("disco lleno");
        AnswerPassphrase("");
        Ui.Run(() => main.ExportConnections("Clientes"));
        Ui.Run(() => Assert.Equal(Loc.Format("ExportFailed", "disco lleno"), main.StatusText.Text));
    }

    [Fact]
    public void Borrar_conexion_o_carpeta_pregunta_antes()
    {
        Seed([Conn("Web", "Clientes/Acme"), Conn("Otra", "Clientes"), Conn("Suelta")], "Clientes/Vacia");
        var main = NewMain();

        // Cancelar: sigue ahi.
        Ui.Run(() =>
        {
            Select(main, "Suelta");
            Ui.Click(main.DeleteButton);
        });
        Assert.Equal(3, Saved().Connections.Count);
        Assert.Contains(Loc.Format("DeleteConnectionConfirm", "Suelta"), Ui.Run(() => Ui.FindAll<TextBlock>(Ui.Modals[0]).Select(t => t.Text).ToList()));

        Ui.Answer<PromptWindow>(w => Ui.Click(Ui.ButtonById(w, "OkButton")!));
        Ui.Run(() => Ui.Click(main.DeleteButton));
        Assert.Equal(2, Saved().Connections.Count);

        // Una carpeta, con todo lo que tiene (la pregunta dice cuantas conexiones).
        Ui.Run(() => Select(main, "Clientes"));
        Ui.Run(() => Ui.Click(main.DeleteButton));   // cancelada
        Ui.Answer<PromptWindow>(w => Ui.Click(Ui.ButtonById(w, "OkButton")!));
        Ui.Run(() => Ui.Click(main.DeleteButton));
        var store = Saved();
        Assert.Empty(store.Connections);
        Assert.Empty(store.EmptyFolders);
        Ui.Run(() => Assert.Equal(Visibility.Visible, main.EmptyTree.Visibility));
        Assert.Contains(Loc.Format("DeleteFolderConfirm", "Clientes", 2), Ui.Run(() => Ui.FindAll<TextBlock>(Ui.Modals[^1]).Select(t => t.Text).ToList()));

        // Sin nada seleccionado no hace nada (ni pregunta).
        var modals = Ui.Modals.Count;
        Ui.Run(() => Peek.Call(main, "OnDeleteClick", main, new RoutedEventArgs()));
        Assert.Equal(modals, Ui.Modals.Count);
    }

    [Fact]
    public void Teclas_del_arbol_intro_conecta_y_suprimir_borra()
    {
        Seed([Conn("Web", "Clientes")]);
        var main = NewMain();
        Ui.Run(() =>
        {
            // Sobre una carpeta, Intro no hace nada.
            Select(main, "Clientes");
            Assert.False(Input.Key(main.Tree, Key.Enter, Keyboard.KeyDownEvent).Handled);
            Select(main, "Web");
            Assert.True(Input.Key(main.Tree, Key.Enter, Keyboard.KeyDownEvent).Handled);
        });
        Ui.Flush();
        Assert.Single(Sessions);
        Ui.Answer<PromptWindow>(w => Ui.Click(Ui.ButtonById(w, "OkButton")!));
        Ui.Run(() =>
        {
            Assert.True(Input.Key(main.Tree, Key.Delete, Keyboard.KeyDownEvent).Handled);
            Assert.False(Input.Key(main.Tree, Key.Delete, Keyboard.KeyDownEvent).Handled);   // ya no hay seleccion
            Assert.False(Input.Key(main.Tree, Key.A, Keyboard.KeyDownEvent).Handled);
        });
        Assert.Empty(Saved().Connections);
    }

    [Fact]
    public void Doble_clic_conecta_y_con_control_edita()
    {
        Seed([Conn("Web", "Clientes")]);
        var main = NewMain();
        Ui.Run(() =>
        {
            // Fuera de un elemento (el propio arbol) o sin seleccion: nada.
            Input.Mouse(main.Tree, Control.MouseDoubleClickEvent);
            Assert.False(main.TreeDoubleClick(Item(main, "Web"), ModifierKeys.None));
            // Sobre una carpeta, el doble clic la despliega (lo hace el arbol): aqui nada.
            Select(main, "Clientes");
            Assert.False(main.TreeDoubleClick(Item(main, "Clientes"), ModifierKeys.None));
            Select(main, "Web");
            Assert.True(main.TreeDoubleClick(Label(main, "Web"), ModifierKeys.None));
        });
        Ui.Flush();
        Assert.Single(Sessions);

        // El doble clic de verdad sobre el texto de la conexion: atendido (el arbol no la despliega).
        Ui.Run(() =>
        {
            var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = Label(main, "Web") };
            main.Tree.RaiseEvent(e);
            Assert.True(e.Handled);
        });
        Ui.Flush();
        Assert.Equal(2, Sessions.Count);

        Ui.Answer<ConnectionWindow>(w => FillConnection(w, "Web2", "web.lan"));
        Ui.Run(() => Assert.True(main.TreeDoubleClick(Item(main, "Web"), ModifierKeys.Control | ModifierKeys.Shift)));
        Assert.Equal("Web2", Saved().Connections[0].Name);
        Assert.Equal(2, Sessions.Count);
    }

    [Fact]
    public void Importar_rdp_y_rdm_sin_repetir_y_con_carpetas()
    {
        Seed([Conn("DC01", "Oficina/Servidores", host: "dc01.lan")]);
        var rdm = Dir.File("export.rdm", """
            <?xml version="1.0"?><RDMExport><Connections>
              <Connection><ConnectionType>Group</ConnectionType><Group>Oficina\Vacia</Group></Connection>
              <Connection><ConnectionType>RDPConfigured</ConnectionType><Group>Oficina\Servidores</Group><Name>DC01</Name><Url>dc01.lan</Url></Connection>
              <Connection><ConnectionType>RDPConfigured</ConnectionType><Group>Oficina\Servidores</Group><Name>FS01</Name><Url>fs01.lan</Url></Connection>
              <Connection><ConnectionType>Raro</ConnectionType><Name>X</Name></Connection>
            </Connections></RDMExport>
            """);
        var rdp = Dir.File("Casa.rdp", "full address:s:casa.lan:3390\r\nusername:s:yo\r\n");
        var empty = Dir.File("Vacio.rdp", "screen mode id:i:2\r\n");
        var main = NewMain();

        // Cancelar el dialogo: nada.
        Ui.Run(main.ImportRdm);
        Assert.Single(Saved().Connections);

        Ui.PickedFiles = [rdm, rdp, empty];
        Ui.Run(main.ImportRdm);
        var store = Saved();
        Assert.Equal(["Casa", "DC01", "FS01"], store.Connections.Select(c => c.Name).Order().ToList());
        Assert.Contains("Oficina/Vacia", store.EmptyFolders);
        Ui.Run(() =>
        {
            Assert.Equal(Loc.Format("Imported", 2, 1, 2), main.StatusText.Text);
            Assert.Contains("Vacia", Items(main).Select(Text));
        });

        // Un fichero roto: se dice en la barra de estado y no se toca nada.
        Ui.PickedFiles = [Dir.File("roto.rdm", "<RDMExport><Connections>")];
        Ui.Run(main.ImportRdm);
        Assert.Equal(3, Saved().Connections.Count);
        Ui.Run(() => Assert.StartsWith(Loc.Format("ImportFailed", "").Trim(), main.StatusText.Text));
    }

    [Fact]
    public void Abrir_la_carpeta_del_fichero_de_conexiones()
    {
        var main = NewMain();
        Ui.Run(() => Ui.Click(main.OpenFileButton));
        var psi = Assert.Single(Ui.Started);
        Assert.Equal("explorer.exe", psi.FileName);
        Assert.Equal($"/select,\"{Store.Location}\"", psi.Arguments);

        // Si Windows no puede, se dice en la barra de estado (en una linea).
        var start = Dialogs.Start;
        Dialogs.Start = _ => throw new InvalidOperationException("sin\r\nexplorador");
        try
        {
            Ui.Run(() => Ui.Click(main.OpenFileButton));
            Ui.Run(() => Assert.Equal("sin explorador", main.StatusText.Text));
        }
        finally
        {
            Dialogs.Start = start;
        }
    }

    [Fact]
    public void Idioma_acerca_de_y_ajustes()
    {
        Seed([Conn("", "")]);
        var main = NewMain();
        Ui.Run(() =>
        {
            Ui.Click(main.LanguageButton);
            Assert.Equal("en", Loc.Language);
            Assert.Equal(Loc.Get("AppTitle"), main.Title);
            Assert.Equal(Loc.Get("SearchHint"), main.SearchHint.Text);
            Assert.Equal(Loc.Get("Unnamed"), Text(Items(main).Single()));
            Ui.Click(main.LanguageButton);
        });
        Assert.Equal("es", Loc.Language);

        Ui.Run(() => Ui.Click(main.AboutButton));
        Assert.IsType<AboutWindow>(Assert.Single(Ui.Modals));

        // Ajustes: quitar la bandeja al minimizar llega al icono; importar desde alli repinta el arbol.
        Ui.PickedFiles = [Dir.File("Nueva.rdp", "full address:s:nueva.lan\r\n")];
        Ui.Answer<SettingsWindow>(w =>
        {
            Ui.Click(w.TrayBox);
            Ui.Click(w.ImportButton);
        });
        Ui.Run(() => Ui.Click(main.SettingsButton));
        Assert.IsType<SettingsWindow>(Ui.Modals[^1]);
        Assert.False(Peek.Field<TrayIcon>(main, "_tray").MinimizeToTray);
        Ui.Run(() => Assert.Contains("Nueva", Items(main).Select(Text)));

        // Sin cambios en Ajustes: no se repinta (sigue la misma seleccion de antes).
        Ui.Run(() => Ui.Click(main.SettingsButton));
        Assert.False(Peek.Field<TrayIcon>(main, "_tray").MinimizeToTray);
    }

    // ------------------------------------------------------------------ Arrastrar y soltar en el arbol

    /// <summary>Pulsa sobre el texto del elemento y arrastra lo bastante; el escritorio de mentira suelta donde se le diga.</summary>
    private void DragAndDrop(MainWindow main, string from, Func<UIElement?> to)
    {
        Desk.DuringDrag = (_, data) =>
        {
            var target = to() ?? main.Tree;
            var over = Input.Drag(target, DragDrop.DragOverEvent, data);
            Assert.True(over.Handled);
            Input.Drag(target, DragDrop.DropEvent, data);
        };
        Ui.Run(() =>
        {
            var source = (UIElement)((Panel)Item(main, from).Header).Children[1];
            Input.Mouse(source, Mouse.PreviewMouseDownEvent);
            main.TreeMouseMove(new Point(500, 500), leftPressed: true);
        });
        Desk.DuringDrag = null;
    }

    private static UIElement Label(MainWindow main, string text) => (UIElement)((Panel)Item(main, text).Header).Children[1];

    [Fact]
    public void Arrastrar_una_conexion_a_otra_carpeta_conserva_la_de_origen()
    {
        Seed([Conn("Web", "Clientes/Acme"), Conn("Correo", "Oficina")]);
        var main = NewMain();
        // Soltada sobre otra conexion: va a la carpeta de esa conexion.
        DragAndDrop(main, "Web", () => Label(main, "Correo"));
        var store = Saved();
        Assert.Equal("Oficina", store.Connections.Single(c => c.Name == "Web").Folder);
        Assert.Contains("Clientes/Acme", store.EmptyFolders);   // la carpeta que se queda vacia no desaparece
        Ui.Run(() => Assert.True(Item(main, "Web").IsSelected));
        Assert.IsType<DataObject>(Desk.Drags.Single().Data);

        // Al hueco de abajo (el arbol): a la raiz. Y la carpeta de origen no se vuelve a apuntar.
        DragAndDrop(main, "Web", () => null);
        Assert.Equal("", Saved().Connections.Single(c => c.Name == "Web").Folder);
        Assert.Single(Saved().EmptyFolders, "Clientes/Acme");
    }

    [Fact]
    public void Arrastrar_una_carpeta_dentro_de_otra()
    {
        Seed([Conn("Web", "Clientes/Acme")], "Oficina");
        var main = NewMain();
        DragAndDrop(main, "Acme", () => Label(main, "Oficina"));
        var store = Saved();
        Assert.Equal("Oficina/Acme", store.Connections[0].Folder);
        Assert.Equal(["Oficina", "Oficina/Acme"], store.AllFolders());
        Ui.Run(() => Assert.True(Item(main, "Acme").IsSelected));

        // A donde ya esta o dentro de si misma: nada.
        DragAndDrop(main, "Acme", () => Label(main, "Oficina"));
        DragAndDrop(main, "Oficina", () => Label(main, "Acme"));
        Assert.Equal("Oficina/Acme", Saved().Connections[0].Folder);

        // A la raiz; si ya estaba apuntada como vacia no se repite.
        DragAndDrop(main, "Acme", () => null);
        store = Saved();
        Assert.Equal("Acme", store.Connections[0].Folder);
        Assert.Equal(["Acme", "Oficina"], store.AllFolders());
    }

    [Fact]
    public void Arrastrar_sin_boton_o_poco_no_empieza()
    {
        Seed([Conn("Web")]);
        var main = NewMain();
        Ui.Run(() =>
        {
            Input.Move(main.Tree, UIElement.PreviewMouseMoveEvent);   // el raton de verdad no esta pulsado
            Input.Mouse(Label(main, "Web"), Mouse.PreviewMouseDownEvent);
            main.TreeMouseMove(new Point(500, 500), leftPressed: false);
            main.TreeMouseMove(Mouse.GetPosition(main.Tree), leftPressed: true);
            // Pulsado fuera de cualquier elemento: no hay nada que arrastrar.
            Input.Mouse(main.Tree, Mouse.PreviewMouseDownEvent);
            main.TreeMouseMove(new Point(500, 500), leftPressed: true);
        });
        Assert.Empty(Desk.Drags);
    }

    [Fact]
    public void Sobre_el_arbol_se_tiñe_el_destino_y_solo_si_se_puede_soltar()
    {
        Seed([Conn("Web", "Clientes"), Conn("Correo", "Oficina")]);
        var main = NewMain();
        Ui.Run(() =>
        {
            var dragged = DataFor(main, "Web");
            var oficina = Item(main, "Oficina");
            var bd = (Border)oficina.Template.FindName("Bd", oficina);

            // Sobre la conexion de otra carpeta: se resalta la carpeta.
            Assert.Equal(DragDropEffects.Move, Input.Drag(Label(main, "Correo"), DragDrop.DragOverEvent, dragged).Effects);
            Assert.Equal(main.FindResource("PrimaryLight"), bd.Background);
            Input.Drag(Label(main, "Correo"), DragDrop.DragOverEvent, dragged);   // el mismo: nada que repintar

            // Sobre su propia carpeta: no se puede, y se quita el tinte.
            Assert.Equal(DragDropEffects.None, Input.Drag(Label(main, "Clientes"), DragDrop.DragOverEvent, dragged).Effects);
            Assert.NotEqual(main.FindResource("PrimaryLight"), bd.Background);

            // Al salir del arbol se quita; datos que no son del arbol no se aceptan.
            Input.Drag(Label(main, "Correo"), DragDrop.DragOverEvent, dragged);
            Input.Drag(main.Tree, DragDrop.DragLeaveEvent, dragged);
            Assert.NotEqual(main.FindResource("PrimaryLight"), bd.Background);
            var other = new DataObject("Texto", "hola");
            Assert.Equal(DragDropEffects.None, Input.Drag(Label(main, "Correo"), DragDrop.DragOverEvent, other).Effects);
            Assert.False(Input.Drag(Label(main, "Correo"), DragDrop.DropEvent, other).Handled);

            // Un nodo que no es ni carpeta ni conexion no se puede soltar en ningun sitio.
            var nodeType = typeof(MainWindow).GetNestedType("Node", System.Reflection.BindingFlags.NonPublic)!;
            var nothing = new DataObject(nodeType, Activator.CreateInstance(nodeType)!);
            Assert.Equal(DragDropEffects.None, Input.Drag(Label(main, "Correo"), DragDrop.DragOverEvent, nothing).Effects);
            Assert.True(Input.Drag(Label(main, "Correo"), DragDrop.DropEvent, nothing).Handled);
        });
        Assert.Equal("Clientes", Saved().Connections.Single(c => c.Name == "Web").Folder);
    }

    /// <summary>Los datos que el arbol pone en el arrastre de ese elemento (los coge del escritorio de mentira).</summary>
    private IDataObject DataFor(MainWindow main, string text)
    {
        Input.Mouse(Label(main, text), Mouse.PreviewMouseDownEvent);
        main.TreeMouseMove(new Point(500, 500), leftPressed: true);
        return (IDataObject)Desk.Drags[^1].Data;
    }

    [Fact]
    public void Soltar_una_conexion_en_las_pestañas_la_abre()
    {
        Seed([Conn("Web", "Clientes")]);
        var main = NewMain();
        Ui.Run(() =>
        {
            var web = DataFor(main, "Web");
            var folder = DataFor(main, "Clientes");
            Assert.Equal(DragDropEffects.Move | DragDropEffects.Copy, Input.Drag(main.TabsArea, DragDrop.DragOverEvent, web).Effects);
            Assert.Equal(DragDropEffects.None, Input.Drag(main.TabsArea, DragDrop.DragOverEvent, folder).Effects);
            Assert.False(Input.Drag(main.TabsArea, DragDrop.DropEvent, folder).Handled);
            Assert.True(Input.Drag(main.TabsArea, DragDrop.DropEvent, web).Handled);
        });
        Ui.Flush();
        Assert.Equal("Web", Assert.Single(Sessions).Connection.Name);
        Ui.Run(() => Assert.Equal(["Web"], TabTitles(main)));
    }

    // ------------------------------------------------------------------ Nube

    private static (FakeHandler Handler, Func<AppSettings, Store, CloudSync> Create) Cloud(Func<System.Net.Http.HttpRequestMessage, string?, System.Net.Http.HttpResponseMessage> respond)
    {
        var h = new FakeHandler(respond);
        return (h, (s, st) => new CloudSync(s, st, new System.Net.Http.HttpClient(h)));
    }

    private static void CloudSettings(string passphrase)
    {
        var s = new AppSettings
        {
            Storage = StorageMode.GoogleDrive,
            Tokens = new OAuthTokens { AccessToken = "at", RefreshToken = "rt", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) },
        };
        if (passphrase.Length > 0)
            s.Passphrase = passphrase;
        s.Save();
    }

    [Fact]
    public void Nube_sin_frase_lo_dice_al_abrir()
    {
        CloudSettings("");
        var (h, create) = Cloud((_, _) => FakeHandler.Json("{}"));
        MainWindow.CreateSync = create;
        var main = NewMain();
        Ui.WaitUntil(() => main.StatusText.Text == Loc.Get("CloudNoPassphrase"));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public void Nube_que_falla_lo_dice_en_la_barra_de_estado_y_no_lo_tapa_el_sincronizando()
    {
        CloudSettings("frase larga");
        var (h, create) = Cloud((_, _) => FakeHandler.Json("{\"error\":{\"message\":\"caida\"}}", System.Net.HttpStatusCode.InternalServerError));
        MainWindow.CreateSync = create;
        var main = NewMain();
        // El fallo llega enseguida (aqui, sin esperar a la red): no lo puede tapar el «Sincronizando…» de antes.
        Ui.WaitUntil(() => h.Requests.Count > 0);
        Ui.Run(() => Assert.Equal(Loc.Format("CloudFailed", "Google Drive: 500 caida"), main.StatusText.Text));
    }

    [Fact]
    public void Nube_mas_reciente_repinta_el_arbol()
    {
        CloudSettings("frase larga");
        var other = new Store();
        other.Connections.Add(new Connection { Name = "DeLaNube", Host = "nube.lan" });
        var json = other.ExportPortable().Replace("\"ModifiedAt\": \"0001-01-01T00:00:00+00:00\"", "\"ModifiedAt\": \"2099-01-01T00:00:00+00:00\"");
        var content = Vault.Encrypt(json, "frase larga");
        var (_, create) = Cloud((r, _) => r.RequestUri!.ToString().Contains("alt=media")
            ? FakeHandler.Text(content)
            : FakeHandler.Json("{\"files\":[{\"id\":\"F\",\"modifiedTime\":\"2099-01-01T00:00:00Z\"}]}"));
        MainWindow.CreateSync = create;
        Seed([Conn("Local")]);
        var main = NewMain();
        Ui.WaitUntil(() => Items(main).Any(i => Text(i) == "DeLaNube"));
        Ui.Run(() => Assert.Equal(Loc.Format("CloudDownloaded", 1), main.StatusText.Text));
    }

    [Fact]
    public void Lo_que_cuenta_la_nube_desde_otro_hilo_llega_a_la_barra_de_estado()
    {
        // Las subidas tras guardar avisan desde un hilo del sistema: se pasa al de la interfaz.
        var main = NewMain();
        var sync = Ui.Run(() => Peek.Field<CloudSync>(main, "_sync"));
        Task.Run(() => Peek.Raise(sync, "Status", "Nube: subido\r\nya")).Wait();
        Ui.WaitUntil(() => main.StatusText.Text == "Nube: subido ya");
    }
}
