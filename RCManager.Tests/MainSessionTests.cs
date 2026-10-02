using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using SocRcManager.Localization;
using SocRcManager.Models;
using SocRcManager.Services;
using SocRcManager.Sessions;

namespace SocRcManager.Tests;

/// <summary>Las sesiones de la ventana principal con sesiones de mentira: abrir, conectar, cerrar, zoom, menu y atajos.</summary>
public sealed class MainSessionTests : MainWindowTest
{
    [Fact]
    public void Conectar_con_la_contraseña_guardada()
    {
        Seed([Conn("Web", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Web");
        var s = Assert.Single(Sessions);
        Assert.Equal("pw", s.Password);
        Assert.Equal("connect", s.Log[0]);
        Assert.Contains("focus", s.Log);
        Assert.NotNull(Saved().Connections[0].LastConnectedAt);
        Ui.Run(() =>
        {
            Assert.Equal(Loc.Format("Connected", "Web"), main.StatusText.Text);
            Assert.Equal(["Web"], TabTitles(main));
            Assert.Same(s.View, ((Border)Tab(main).Content).Child);
            Assert.Equal(Visibility.Collapsed, main.EmptyTabs.Visibility);
            Assert.Equal(Visibility.Visible, main.SessionsButton.Visibility);
            Assert.True(main.HasOpenSessions);
            Assert.Equal(1, main.OpenSessionCount);
            Assert.Equal("Web", System.Windows.Automation.AutomationProperties.GetName(Tab(main)));
        });
    }

    [Fact]
    public void Sin_contraseña_se_pide_y_se_guarda_si_se_marca()
    {
        Seed([Conn("Escritorio", password: "")]);
        var main = NewMain();
        Ui.Answer<PromptWindow>(w =>
        {
            Ui.Find<Controls.RevealPasswordBox>(w)!.Password = "secreta";
            Ui.Find<CheckBox>(w)!.IsChecked = true;
            Ui.Click(Ui.ButtonById(w, "OkButton")!);
        });
        Connect(main, "Escritorio");
        Assert.Equal("secreta", Sessions.Single().Password);
        Assert.Equal("secreta", Secrets.Unprotect(Saved().Connections[0].PasswordProtected));
        Assert.Contains(Loc.Format("PasswordPrompt", "pepe", "escritorio.lan"), Ui.Run(() => Ui.FindAll<TextBlock>(Ui.Modals[0]).Select(t => t.Text).ToList()));
    }

    [Fact]
    public void Sin_contraseña_y_sin_guardarla()
    {
        Seed([Conn("Escritorio", password: "")]);
        var main = NewMain();
        Ui.Answer<PromptWindow>(w =>
        {
            Ui.Find<Controls.RevealPasswordBox>(w)!.Password = "una vez";
            Ui.Click(Ui.ButtonById(w, "OkButton")!);
        });
        Connect(main, "Escritorio");
        Assert.Equal("una vez", Sessions.Single().Password);
        Assert.Empty(Saved().Connections[0].PasswordProtected);
    }

    [Fact]
    public void Cancelar_la_contraseña_SSH_no_abre_y_RDP_abre_sin_ella()
    {
        Seed([Conn("Shell", "", ConnectionKind.Ssh, password: ""), Conn("Escritorio", password: "")]);
        var main = NewMain();
        Connect(main, "Shell");
        Assert.Empty(Sessions);
        Connect(main, "Escritorio");
        Assert.Equal("", Sessions.Single().Password);   // el escritorio remoto la pide el
        Assert.Equal(2, Ui.Modals.Count);
    }

    [Fact]
    public void Con_clave_privada_no_se_pide_contraseña()
    {
        var c = Conn("Shell", "", ConnectionKind.Ssh, password: "");
        c.PrivateKeyPath = Dir.File("id_ed25519", "x");
        Seed([c]);
        var main = NewMain();
        Connect(main, "Shell");
        Assert.Equal("", Sessions.Single().Password);
        Assert.Empty(Ui.Modals);
    }

    [Fact]
    public void Si_no_conecta_se_cierra_la_pestaña_y_avisa_con_la_razon()
    {
        Seed([Conn("Caido")]);
        Setup = s => s.Connect = _ => Task.FromException(new TimeoutException("sin respuesta"));
        var main = NewMain();
        Connect(main, "Caido");
        var s = Sessions.Single();
        Assert.Contains("disconnect", s.Log);
        var alert = Assert.IsType<PromptWindow>(Assert.Single(Ui.Modals));
        Ui.Run(() =>
        {
            Assert.Empty(main.Tabs.Items);
            Assert.Equal(Visibility.Visible, main.EmptyTabs.Visibility);
            Assert.Equal(Visibility.Collapsed, main.SessionsButton.Visibility);
            Assert.StartsWith(Loc.Format("ConnectFailed", "Caido", "").Trim(), main.StatusText.Text);
            Assert.Equal(Loc.Get("ConnectFailedTitle"), alert.Title);
        });
        Assert.Null(Saved().Connections[0].LastConnectedAt);
    }

    [Fact]
    public void En_modo_aislado_abre_la_pestaña_sin_conectar_ni_pedir_contraseña()
    {
        Sandbox.IsOn = true;
        Seed([Conn("Real", password: "")]);
        var main = NewMain();
        Ui.Run(() => Assert.Equal(Loc.Get("AppTitle") + " [SOC_SANDBOX]", main.Title));
        Connect(main, "Real");
        var s = Sessions.Single();
        Assert.DoesNotContain("connect", s.Log);
        Assert.Empty(Ui.Modals);
        Ui.Run(() => Assert.Equal(Loc.Format("SandboxNotConnected", "Real"), main.StatusText.Text));
    }

    [Fact]
    public void Titulo_fin_de_sesion_y_cerrar()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh), Conn("Dos", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Uno");
        Connect(main, "Dos");
        var (uno, dos) = (Sessions[0], Sessions[1]);

        uno.RaiseTitle("root@uno: ~");
        Ui.Flush();
        Ui.Run(() => Assert.Equal(["Uno · root@uno: ~", "Dos"], TabTitles(main)));
        uno.RaiseTitle("");
        Ui.Flush();
        Ui.Run(() => Assert.Equal("Uno", TabTitles(main)[0]));

        // Se acaba por un fallo: se dice el motivo y se quita la pestaña.
        uno.RaiseEnded("se corto");
        Ui.Flush();
        Ui.Run(() =>
        {
            Assert.Equal(Loc.Format("SessionEnded", "Uno", "se corto"), main.StatusText.Text);
            Assert.Equal(["Dos"], TabTitles(main));
        });
        // Un segundo aviso de la misma sesion ya no hace nada.
        uno.RaiseEnded(null);
        Ui.Flush();
        Assert.Equal(1, uno.Log.Count(l => l == "disconnect"));

        // El boton de desconectar.
        Ui.Run(() => Ui.Click(HeaderButton(Header(Tab(main)), "DisconnectButton")));
        Assert.Contains("disconnect", dos.Log);
        dos.RaiseEnded(null);
        Ui.Flush();
        Ui.Run(() =>
        {
            Assert.Empty(main.Tabs.Items);
            Assert.Equal(Visibility.Visible, main.EmptyTabs.Visibility);
            Assert.False(main.HasOpenSessions);
        });
    }

    [Fact]
    public void Fin_de_sesion_a_peticion()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Uno");
        Sessions[0].RaiseEnded(null);
        Ui.Flush();
        Ui.Run(() => Assert.Equal(Loc.Format("SessionClosed", "Uno"), main.StatusText.Text));
    }

    [Fact]
    public void Zoom_desde_la_cabecera_y_sin_zoom_no_hay_botones()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh), Conn("Dos", "", ConnectionKind.Sftp)]);
        Setup = s => s.CanZoom = s.Connection.Name == "Uno";
        var main = NewMain();
        Connect(main, "Uno");
        Connect(main, "Dos");
        Ui.Run(() =>
        {
            var buttons = Header(Tab(main)).Children.OfType<Button>().ToList();
            Assert.Equal(5, buttons.Count);   // alejar, acercar, pantalla completa, sacar y desconectar
            Ui.Click(buttons[1]);
            Assert.Equal(Loc.Format("ZoomStatus", "Uno", "15 pt"), main.StatusText.Text);
            Ui.Click(buttons[0]);
            Ui.Click(buttons[0]);
            Assert.Equal(Loc.Format("ZoomStatus", "Uno", "13 pt"), main.StatusText.Text);
            Assert.Equal(3, Header(Tab(main, 1)).Children.OfType<Button>().Count());
        });
    }

    [Fact]
    public void Menu_de_sesiones_y_control_tab()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh), Conn("Dos", "", ConnectionKind.Sftp), Conn("Tres")]);
        var main = NewMain();

        // Sin sesiones, el menu no se abre.
        Ui.Run(() => Peek.Call(main, "OnSessionsClick", main.SessionsButton, new RoutedEventArgs()));
        Assert.Empty(Desk.Menus);

        Connect(main, "Uno");
        Connect(main, "Dos");
        Connect(main, "Tres");
        Ui.Run(() =>
        {
            Ui.Click(main.SessionsButton);
            var menu = Assert.Single(Desk.Menus);
            Assert.Same(main.SessionsButton, menu.PlacementTarget);
            var items = menu.Items.OfType<MenuItem>().ToList();
            Assert.Equal(["Uno", "Dos", "Tres"], items.Select(i => (string)i.Header));
            Assert.Equal([false, false, true], items.Select(i => i.IsChecked));
            // Un icono por tipo: terminal, ficheros y escritorio.
            Assert.Equal(3, items.Select(i => ((TextBlock)i.Icon).Text).Distinct().Count());

            Input.Click(items[0]);
            Assert.Same(Tab(main, 0), main.Tabs.SelectedItem);

            // Ctrl+Tab adelante (y da la vuelta), Ctrl+Mayus+Tab atras.
            Assert.True(main.Shortcut(Key.Tab, ModifierKeys.Control));
            Assert.Same(Tab(main, 1), main.Tabs.SelectedItem);
            Assert.True(main.Shortcut(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift));
            Assert.True(main.Shortcut(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift));
            Assert.Same(Tab(main, 2), main.Tabs.SelectedItem);
            Assert.True(main.Shortcut(Key.Tab, ModifierKeys.Control));
            Assert.Same(Tab(main, 0), main.Tabs.SelectedItem);

            // Sin pestaña seleccionada empieza por la primera; Tab a secas no es un atajo.
            main.Tabs.SelectedItem = null;
            main.Shortcut(Key.Tab, ModifierKeys.Control);
            Assert.Same(Tab(main, 1), main.Tabs.SelectedItem);
            Assert.False(main.Shortcut(Key.Tab, ModifierKeys.None));
            Assert.False(main.Shortcut(Key.Escape, ModifierKeys.Control));   // sin pantalla completa
            Assert.False(Input.Key(main, Key.A, Keyboard.PreviewKeyDownEvent).Handled);
        });
        Assert.Contains("focus", Sessions[0].Log);
    }

    [Fact]
    public void Control_tab_con_una_sola_pestaña_no_hace_nada()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Uno");
        Ui.Run(() =>
        {
            Assert.True(main.Shortcut(Key.Tab, ModifierKeys.Control));
            Assert.Same(Tab(main), main.Tabs.SelectedItem);
        });
    }

    [Fact]
    public void Abrir_por_nombre_editar_por_nombre_y_fichero_en_el_editor()
    {
        Seed([Conn("Web", "", ConnectionKind.Ssh), Conn("Ficheros", "", ConnectionKind.Sftp)]);
        var main = NewMain();
        Ui.Run(() => main.OpenByName("no existe\r\nde verdad"));
        Ui.Run(() => Assert.Equal(Loc.Format("OpenNotFound", "no existe de verdad"), main.StatusText.Text));
        Ui.Run(() => main.OpenByName("WEB"));
        Ui.Flush();
        Assert.Equal("Web", Assert.Single(Sessions).Connection.Name);
        Ui.Run(() => Assert.True(Item(main, "Web").IsSelected));

        // --edit-file: solo conexiones de ficheros (y que existan).
        Ui.Run(() =>
        {
            main.OpenFileInEditor("Web", "/etc/hosts");
            main.OpenFileInEditor("Nada", "/etc/hosts");
        });
        Assert.Single(Sessions);
        Ui.Run(() => main.OpenFileInEditor("Ficheros", "/etc/hosts"));
        Ui.Flush();
        Assert.Equal(2, Sessions.Count);

        // --edit: el editor en la pestaña pedida; si no existe, nada.
        Ui.Run(() => main.EditByName("Nada", 1));
        Assert.Empty(Ui.Modals);
        Ui.Answer<ConnectionWindow>(w =>
        {
            Assert.Equal(2, w.Sections.SelectedIndex);
            w.NameBox.Text = "Web2";
            Ui.Click(w.SaveButton);
        });
        Ui.Run(() => main.EditByName("web", 2));
        Assert.Equal("Web2", Saved().Connections.Single(c => c.Kind == ConnectionKind.Ssh).Name);
        Ui.Run(() => main.EditByName("Web2", 0));   // cancelada
        Assert.Equal("Web2", Saved().Connections.Single(c => c.Kind == ConnectionKind.Ssh).Name);
    }

    [Fact]
    public void Fichero_en_el_editor_con_la_sesion_de_ficheros_de_verdad()
    {
        // Modo aislado: la sesion de ficheros de verdad no conecta, y el editor sin conexion no abre nada.
        Sandbox.IsOn = true;
        SessionFactory.Create = SessionFactory.ForKind;
        Seed([Conn("Ficheros", "", ConnectionKind.Sftp)]);
        var main = NewMain();
        Ui.Run(() => main.OpenFileInEditor("Ficheros", "/etc/hosts"));
        Ui.Flush();
        Ui.Run(() => Assert.IsType<ContentControl>(((Border)Tab(main).Content).Child));
        Assert.DoesNotContain(Ui.Run(() => Application.Current.Windows.OfType<Window>().Select(w => w.GetType().Name).ToList()), n => n.Contains("Editor"));
    }

    [Fact]
    public void Las_sesiones_de_verdad_segun_el_tipo()
    {
        Ui.Run(() =>
        {
            Assert.IsType<SshSession>(SessionFactory.ForKind(new Connection { Kind = ConnectionKind.Ssh }));
            Assert.IsType<FileSession>(SessionFactory.ForKind(new Connection { Kind = ConnectionKind.Sftp }));
            Assert.IsType<FileSession>(SessionFactory.ForKind(new Connection { Kind = ConnectionKind.Ftp }));
            Assert.IsType<RdpSession>(SessionFactory.ForKind(new Connection { Kind = ConnectionKind.Rdp }));
        });
    }

    [Fact]
    public void El_escritorio_remoto_pide_minimizar_la_ventana_donde_este()
    {
        // RDP de verdad en modo aislado (nunca conecta) y en una ventana sin enseñar.
        Sandbox.IsOn = true;
        SessionFactory.Create = SessionFactory.ForKind;
        Seed([Conn("Escritorio")]);
        var main = NewMain(show: false);
        Ui.Run(() => main.OpenByName("Escritorio"));
        Ui.Flush();
        var rdp = Ui.Run(() => (RdpSession)Peek.Field<System.Collections.IList>(main, "_open").Cast<object>().Select(o => o.GetType().GetProperty("Session")!.GetValue(o)).Single()!);
        Ui.Run(() => Peek.Raise(rdp, "MinimizeRequested"));
        Ui.Run(() => Assert.Equal(WindowState.Minimized, main.WindowState));

        Ui.Run(() =>
        {
            main.WindowState = WindowState.Normal;
            Ui.Click(HeaderButton(Header(Tab(main)), "DetachButton"));
        });
        Ui.Run(() => Peek.Raise(rdp, "MinimizeRequested"));
        Ui.Run(() =>
        {
            Assert.Equal(WindowState.Minimized, Detached().Single().WindowState);
            Assert.Equal(WindowState.Normal, main.WindowState);
        });
    }

    // ------------------------------------------------------------------ Pantalla completa (ventana sin enseñar)

    [Fact]
    public void Pantalla_completa_de_la_ventana_con_F11_y_vuelta_con_control_esc()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh), Conn("Dos", "", ConnectionKind.Ssh)]);
        var main = NewMain(show: false);
        Ui.Run(() =>
        {
            main.OpenByName("Uno");
            main.OpenByName("Dos");
            main.WindowState = WindowState.Maximized;
        });
        Ui.Flush();
        Ui.Run(() =>
        {
            Assert.True(Input.Key(main, Key.F11, Keyboard.PreviewKeyDownEvent).Handled);
            Assert.Equal(WindowStyle.None, main.WindowStyle);
            Assert.Equal(WindowState.Maximized, main.WindowState);
            Assert.Equal(Visibility.Collapsed, main.TreePane.Visibility);
            Assert.Equal(Visibility.Collapsed, main.StatusBar.Visibility);
            Assert.Equal(0, main.TreeColumn.Width.Value);
            Assert.Equal(Visibility.Visible, main.FullScreenBar.Visibility);
            Assert.Equal("Dos", main.FullScreenTitle.Text);
            Assert.Equal(Visibility.Collapsed, Tab(main, 0).Visibility);   // solo se ve la activa
            Assert.Equal(0, Tab(main, 1).Height);

            // Cambiar de sesion en pantalla completa: la otra pasa a verse.
            Assert.True(main.Shortcut(Key.Tab, ModifierKeys.Control));
            Assert.Equal("Uno", main.FullScreenTitle.Text);
            Assert.Equal(Visibility.Visible, Tab(main, 0).Visibility);
            Assert.Equal(Visibility.Collapsed, Tab(main, 1).Visibility);

            // Esc a secas no sale (es del remoto); Ctrl+Esc si.
            Assert.False(main.Shortcut(Key.Escape, ModifierKeys.None));
            Assert.True(main.Shortcut(Key.Escape, ModifierKeys.Control));
            Assert.Equal(WindowStyle.SingleBorderWindow, main.WindowStyle);
            Assert.Equal(WindowState.Maximized, main.WindowState);   // como estaba
            Assert.Equal(Visibility.Visible, main.TreePane.Visibility);
            Assert.Equal(320, main.TreeColumn.Width.Value);
            Assert.Equal(Visibility.Collapsed, main.FullScreenBar.Visibility);
            Assert.All(main.Tabs.Items.OfType<TabItem>(), t => Assert.Equal(Visibility.Visible, t.Visibility));
            // Todas las cabeceras a su altura, tambien la que estuvo activa antes de cambiar de sesion.
            Assert.All(main.Tabs.Items.OfType<TabItem>(), t => Assert.True(double.IsNaN(t.Height)));
        });
    }

    [Fact]
    public void Barra_de_pantalla_completa_se_esconde_sola_salvo_con_el_raton_encima()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain(show: false);
        Ui.Run(() => main.OpenByName("Uno"));
        Ui.Flush();
        Ui.Run(() =>
        {
            Ui.Click(main.ExitFullScreenButton);   // el mismo boton entra y sale
            var timer = Peek.Field<System.Windows.Threading.DispatcherTimer>(main, "_barTimer");
            Assert.True(timer.IsEnabled);
            Assert.Equal(TimeSpan.FromSeconds(2), timer.Interval);

            // Con el raton encima no se esconde.
            Input.Move(main.FullScreenBar, Mouse.MouseEnterEvent);
            Assert.False(timer.IsEnabled);
            Peek.Call(main, "HideFullScreenBar", null, EventArgs.Empty);
            Assert.Equal(Visibility.Visible, main.FullScreenBar.Visibility);

            // Al irse, vuelve a contar y luego se esconde.
            Input.Move(main.FullScreenBar, Mouse.MouseLeaveEvent);
            Assert.True(timer.IsEnabled);
            Peek.Call(main, "HideFullScreenBar", null, EventArgs.Empty);
            Assert.Equal(Visibility.Collapsed, main.FullScreenBar.Visibility);
            Assert.False(timer.IsEnabled);

            // El raton en el borde de arriba la saca otra vez.
            Input.Move(main, Mouse.PreviewMouseMoveEvent);
            Assert.Equal(Visibility.Visible, main.FullScreenBar.Visibility);
            Input.Move(main, Mouse.PreviewMouseMoveEvent);   // ya a la vista: nada

            Ui.Click(main.ExitFullScreenButton);
            Assert.Equal(Visibility.Collapsed, main.FullScreenBar.Visibility);
            Input.Move(main, Mouse.PreviewMouseMoveEvent);   // fuera de la pantalla completa: nada
            Assert.Equal(Visibility.Collapsed, main.FullScreenBar.Visibility);
        });
    }

    [Fact]
    public void Cerrar_la_ultima_en_pantalla_completa_sale_de_ella()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain(show: false);
        Ui.Run(() => main.OpenByName("Uno"));
        Ui.Flush();
        Ui.Run(() =>
        {
            Ui.Click(main.FullScreenCloseButton);   // sin nada a pantalla completa tambien cierra la activa
        });
        Assert.Contains("disconnect", Sessions[0].Log);
        Ui.Run(() =>
        {
            Ui.Click(main.FullScreenCloseButton);   // sin sesiones: nada
            main.OpenByName("Uno");
        });
        Ui.Flush();
        Ui.Run(() =>
        {
            main.Shortcut(Key.F11, ModifierKeys.None);
            Assert.Equal(WindowStyle.None, main.WindowStyle);
            Ui.Click(main.FullScreenCloseButton);
            Assert.Equal(WindowStyle.SingleBorderWindow, main.WindowStyle);
            Assert.Equal(WindowState.Normal, main.WindowState);
            Assert.Empty(main.Tabs.Items);
        });
    }

    [Fact]
    public void Boton_de_pantalla_completa_de_la_pestaña_en_el_monitor_elegido()
    {
        var uno = Conn("Uno", "", ConnectionKind.Ssh);
        uno.FullScreenScreen = 2;
        var dos = Conn("Dos");
        dos.FullScreenScreen = 1;
        Seed([uno, dos]);
        Setup = s => s.HasNativeFullScreen = s.Connection.Kind == ConnectionKind.Rdp;
        var main = NewMain(show: false);
        Ui.Run(() =>
        {
            main.OpenByName("Uno");
            main.OpenByName("Dos");
        });
        Ui.Flush();
        Ui.Run(() =>
        {
            main.WindowState = WindowState.Maximized;
            // La de SSH: la ventana va al segundo monitor y se pone ella a pantalla completa.
            Ui.Click(HeaderButton(Header(Tab(main, 0)), "TabFullScreenButton"));
            Assert.Same(Tab(main, 0), main.Tabs.SelectedItem);
            Ui.Click(HeaderButton(Header(Tab(main, 0)), "TabFullScreenButton"));   // ya lo esta: nada
            Assert.Equal(-38080 + 40, main.Left);
            Assert.Equal(-40000 + 40, main.Top);
            Assert.Equal(WindowStyle.None, main.WindowStyle);
            main.Shortcut(Key.F11, ModifierKeys.None);
            Assert.Equal(WindowState.Normal, main.WindowState);   // se paso a Normal para moverla

            // La de RDP lo hace el control (en el primer monitor).
            Ui.Click(HeaderButton(Header(Tab(main, 1)), "TabFullScreenButton"));
            Assert.Equal(-40000 + 40, main.Left);
            Assert.Equal(WindowStyle.SingleBorderWindow, main.WindowStyle);
        });
        Assert.Contains("fullscreen 1", Sessions[1].Log);

        // Un monitor que ya no existe: la ventana se queda donde esta.
        Desk.Screens.RemoveAt(1);
        Ui.Run(() =>
        {
            main.Left = 5;
            Ui.Click(HeaderButton(Header(Tab(main, 0)), "TabFullScreenButton"));
            Assert.Equal(5, main.Left);
            main.Shortcut(Key.F11, ModifierKeys.None);
            main.Close();
        });
        Assert.Equal(320, AppSettings.Load().TreeWidth);
    }

    [Fact]
    public void Al_cerrar_en_pantalla_completa_se_guarda_el_ancho_del_arbol_de_antes()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain(show: false);
        Ui.Run(() => main.OpenByName("Uno"));
        Ui.Flush();
        Ui.Run(() =>
        {
            main.TreeColumn.Width = new GridLength(280);
            main.Shortcut(Key.F11, ModifierKeys.None);
            main.Close();
        });
        Assert.Equal(280, AppSettings.Load().TreeWidth);
        Assert.Contains("disconnect", Sessions[0].Log);
    }

    // ------------------------------------------------------------------ Instancia unica y bandeja

    [Fact]
    public void A_la_bandeja_y_de_vuelta_al_frente()
    {
        var main = Ui.Run(() =>
        {
            var m = new MainWindow();
            Ui.Hide(m);
            m.StartInTray();
            return m;
        });
        Ui.Run(() => Assert.False(main.IsVisible));
        Assert.Equal(["add 8001 " + Loc.Get("AppTitle")], TrayShell.Calls);

        Ui.Run(main.BringToFront);
        Ui.Run(() => Assert.True(main.IsVisible));
        Assert.Equal(["add 8001 " + Loc.Get("AppTitle"), "activate", "delete"], TrayShell.Calls);
        Assert.Equal([main], Desk.Foreground);

        // En modo aislado vuelve sin activarse (no se le quita el foco a nadie).
        Sandbox.IsOn = true;
        Ui.Run(() =>
        {
            main.StartInTray();
            main.BringToFront();
            Assert.True(main.IsVisible);
        });
        Assert.Single(Desk.Foreground);
        Assert.Single(TrayShell.Calls, "activate");

        // «Salir» del menu del icono cierra la ventana.
        var tray = Peek.Field<TrayIcon>(main, "_tray");
        var handled = false;
        Ui.Run(() => tray.Hook(IntPtr.Zero, TrayIcon.WmCommand, new IntPtr(TrayIcon.IdExit), IntPtr.Zero, ref handled));
        Ui.Run(() => Assert.DoesNotContain(main, Application.Current.Windows.OfType<Window>()));
    }

    [Fact]
    public void Al_frente_sin_icono_se_enseña()
    {
        var main = NewMain(show: false);
        Ui.Run(main.BringToFront);
        Ui.Run(() => Assert.True(main.IsVisible));
        Assert.Equal([main], Desk.Foreground);
        Assert.Empty(TrayShell.Calls);
    }

    [Fact]
    public void Cerrar_forzado_no_pregunta_por_las_ventanas_sueltas()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Uno");
        Ui.Run(() => Ui.Click(HeaderButton(Header(Tab(main)), "DetachButton")));
        Ui.Run(main.CloseForced);
        Assert.Empty(Ui.Modals);
        Ui.Run(() => Assert.Empty(Detached()));
        Assert.Contains("disconnect", Sessions[0].Log);
    }
}
