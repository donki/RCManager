using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using SocRcManager.Localization;
using SocRcManager.Models;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>Pestañas que salen a su propia ventana y vuelven (boton, menu, arrastre) y el cierre con ventanas sueltas.</summary>
public sealed class MainDetachTests : MainWindowTest
{
    private static Panel WindowHeader(SessionWindow w) => (Panel)Ui.Find<ContentControl>(w, c => c.Content is StackPanel)!.Content;

    [Fact]
    public void Sacar_con_el_boton_y_devolver_sin_reconectar()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Uno");
        var s = Sessions.Single();
        Ui.Run(() => Ui.Click(HeaderButton(Header(Tab(main)), "DetachButton")));
        Ui.Run(() =>
        {
            var w = Detached().Single();
            Assert.Equal("Uno", w.Title);
            Assert.Empty(main.Tabs.Items);
            Assert.Equal(Visibility.Visible, main.EmptyTabs.Visibility);
            Assert.Equal(Loc.Format("DetachedStatus", "Uno"), main.StatusText.Text);
            Assert.True(main.HasOpenSessions);
            var attach = HeaderButton(WindowHeader(w), "AttachButton");
            Assert.Equal(Loc.Get("AttachTooltip"), attach.ToolTip);

            // El titulo de la sesion llega tambien a su ventana.
            s.RaiseTitle("root@uno");
        });
        Ui.Run(() =>
        {
            var w = Detached().Single();
            Assert.Equal("Uno · root@uno", w.Title);
            w.Width = 900;
            w.Height = 600;
            Ui.Click(HeaderButton(WindowHeader(w), "AttachButton"));
        });
        Ui.Run(() =>
        {
            Assert.Empty(Detached());
            Assert.Equal(["Uno · root@uno"], TabTitles(main));
            Assert.Same(Tab(main), main.Tabs.SelectedItem);
            Assert.Equal(Visibility.Collapsed, main.EmptyTabs.Visibility);
            Assert.Equal(Loc.Format("AttachedStatus", "Uno"), main.StatusText.Text);
            Assert.Equal("DetachButton", System.Windows.Automation.AutomationProperties.GetAutomationId(Header(Tab(main)).Children.OfType<Button>().ElementAt(3)));
        });
        Assert.Equal(["connect"], s.Log.Where(l => l == "connect"));   // sin reconectar
        Assert.Equal(2, s.Log.Count(l => l == "leave"));
        Assert.Equal([main], Desk.Foreground);
        var saved = AppSettings.Load().DetachedWindows[SessionPlacement.Key(s.Connection.Id)];
        Assert.Equal((900, 600), (saved.Width, saved.Height));

        // La siguiente vez sale con el tamaño que tuvo.
        Ui.Run(() => Ui.Click(HeaderButton(Header(Tab(main)), "DetachButton")));
        Ui.Run(() =>
        {
            var w = Detached().Single();
            Assert.Equal((900, 600), (w.Width, w.Height));
        });
    }

    [Fact]
    public void Menu_de_la_pestaña_sacar_devolver_y_desconectar()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Uno");
        var menu = Ui.Run(() => Tab(main).ContextMenu);
        var (move, disconnect) = Ui.Run(() => (menu.Items.OfType<MenuItem>().First(), menu.Items.OfType<MenuItem>().Last()));
        Ui.Run(() =>
        {
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
            Assert.Equal(Loc.Get("DetachMenu"), move.Header);
            Assert.Equal(Loc.Get("DisconnectMenu"), disconnect.Header);
            Input.Click(move);
            Assert.Single(Detached());
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
            Assert.Equal(Loc.Get("AttachMenu"), move.Header);
            Input.Click(move);
            Assert.Empty(Detached());
            Assert.Single(main.Tabs.Items);

            Input.Click(disconnect);
            Assert.Empty(main.Tabs.Items);
            // Ya cerrada, sacarla no hace nada.
            Input.Click(move);
            Assert.Empty(Detached());
        });
        Assert.Contains("disconnect", Sessions[0].Log);
    }

    [Fact]
    public void Cerrar_la_ventana_suelta_con_la_X_la_devuelve_y_dos_veces_no_pasa_nada()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Uno");
        Ui.Run(() => Ui.Click(HeaderButton(Header(Tab(main)), "DetachButton")));
        var w = Ui.Run(() => Detached().Single());
        Ui.Run(() =>
        {
            Peek.Raise(w, "AttachRequested");
            Peek.Raise(w, "AttachRequested");   // la segunda ya no tiene ventana que cerrar
        });
        Ui.Run(() =>
        {
            Assert.Empty(Detached());
            Assert.Single(main.Tabs.Items);
        });
    }

    [Fact]
    public void Sesion_que_se_acaba_en_su_ventana_la_cierra_y_recuerda_donde_estaba()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Uno");
        Ui.Run(() => Ui.Click(HeaderButton(Header(Tab(main)), "DetachButton")));
        Sessions[0].RaiseEnded("se corto");
        Ui.Flush();
        Ui.Run(() =>
        {
            Assert.Empty(Detached());
            Assert.Empty(main.Tabs.Items);
            Assert.False(main.HasOpenSessions);
            Assert.Equal(Visibility.Collapsed, main.SessionsButton.Visibility);
        });
        Assert.True(AppSettings.Load().DetachedWindows.ContainsKey(SessionPlacement.Key(Sessions[0].Connection.Id)));
    }

    [Fact]
    public void Sacar_en_pantalla_completa_sale_antes_de_ella()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain(show: false);
        Ui.Run(() => main.OpenByName("Uno"));
        Ui.Flush();
        Ui.Run(() =>
        {
            main.Shortcut(Key.F11, ModifierKeys.None);
            Ui.Click(HeaderButton(Header(Tab(main)), "DetachButton"));
            Assert.Equal(WindowStyle.SingleBorderWindow, main.WindowStyle);
            Assert.Single(Detached());
        });
    }

    [Fact]
    public void Pantalla_completa_de_la_ventana_suelta()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh), Conn("Dos")]);
        Setup = s => s.HasNativeFullScreen = s.Connection.Kind == ConnectionKind.Rdp;
        var main = NewMain();
        Connect(main, "Uno");
        Connect(main, "Dos");
        Ui.Run(() =>
        {
            Ui.Click(HeaderButton(Header(Tab(main, 0)), "DetachButton"));
            Ui.Click(HeaderButton(Header(Tab(main, 0)), "DetachButton"));
        });
        Ui.Run(() =>
        {
            var (uno, dos) = (Detached().Single(w => w.Title == "Uno"), Detached().Single(w => w.Title == "Dos"));

            // F11 en la ventana: la de SSH se pone ella a pantalla completa; la de RDP lo hace el control.
            Input.Key(uno, Key.F11, Keyboard.PreviewKeyDownEvent);
            Assert.True(uno.IsFullScreen);
            Input.Key(uno, Key.F11, Keyboard.PreviewKeyDownEvent);
            Assert.False(uno.IsFullScreen);
            Input.Key(dos, Key.F11, Keyboard.PreviewKeyDownEvent);
            Assert.False(dos.IsFullScreen);

            // El boton de la cabecera.
            Ui.Click(HeaderButton(WindowHeader(uno), "TabFullScreenButton"));
            Assert.True(uno.IsFullScreen);
            Ui.Click(HeaderButton(WindowHeader(dos), "TabFullScreenButton"));
            Assert.False(dos.IsFullScreen);

            // Devolverla estando a pantalla completa: primero sale de ella.
            Ui.Click(HeaderButton(WindowHeader(uno), "AttachButton"));
            Assert.Equal(["Uno"], TabTitles(main));
        });
        Assert.Contains("fullscreen 0", Sessions[1].Log);
    }

    [Fact]
    public void Menu_de_sesiones_con_una_ventana_suelta_la_trae_al_frente()
    {
        Sandbox.IsOn = true;   // sin activarla
        Seed([Conn("Uno", "", ConnectionKind.Ssh), Conn("Dos", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Uno");
        Connect(main, "Dos");
        Ui.Run(() => Ui.Click(HeaderButton(Header(Tab(main, 0)), "DetachButton")));
        Ui.Run(() =>
        {
            var w = Detached().Single();
            Ui.Click(main.SessionsButton);
            var items = Desk.Menus.Single().Items.OfType<MenuItem>().ToList();
            Assert.False(items[0].IsChecked);   // la suelta no esta activa
            Assert.True(items[1].IsChecked);
            Input.Click(items[0]);
            Assert.True(w.IsVisible);
            Assert.False(w.IsActive);

            // Ctrl+Tab solo pasa por las pestañas de la principal (aqui queda una).
            Assert.True(main.Shortcut(Key.Tab, ModifierKeys.Control));
            Assert.Same(Tab(main), main.Tabs.SelectedItem);
        });
    }

    // ------------------------------------------------------------------ Cerrar la principal con ventanas sueltas

    [Fact]
    public void Cerrar_con_ventanas_sueltas_pregunta_y_se_puede_cancelar()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh), Conn("Dos", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Uno");
        Connect(main, "Dos");
        Ui.Run(() => Ui.Click(HeaderButton(Header(Tab(main, 0)), "DetachButton")));

        Ui.Run(main.Close);   // cancelada
        Ui.Run(() =>
        {
            Assert.Contains(main, Application.Current.Windows.OfType<Window>());
            Assert.Single(Detached());
        });
        Assert.Contains(Loc.Format("CloseDetachedConfirm", 1), Ui.Run(() => Ui.FindAll<TextBlock>(Ui.Modals.Single()).Select(t => t.Text).ToList()));

        Ui.Answer<PromptWindow>(w => Ui.Click(Ui.ButtonById(w, "OkButton")!));
        Ui.Run(main.Close);
        Ui.Run(() =>
        {
            Assert.DoesNotContain(main, Application.Current.Windows.OfType<Window>());
            Assert.Empty(Detached());
        });
        Assert.All(Sessions, s => Assert.Contains("disconnect", s.Log));
    }

    [Fact]
    public void Cerrar_escondida_con_ventanas_sueltas_la_saca_antes_de_preguntar()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain(show: false);
        Ui.Run(() => main.OpenByName("Uno"));
        Ui.Flush();
        Ui.Run(() => Ui.Click(HeaderButton(Header(Tab(main)), "DetachButton")));
        Ui.Answer<PromptWindow>(w => Ui.Click(Ui.ButtonById(w, "OkButton")!));
        Ui.Run(main.Close);
        Ui.Flush();
        Assert.Same(main, Ui.Run(() => Ui.Modals.Single().Owner));
        Ui.Run(() =>
        {
            Assert.Empty(Detached());
            Assert.DoesNotContain(main, Application.Current.Windows.OfType<Window>());
        });
    }

    [Fact]
    public void Salir_desde_el_icono_con_ventanas_sueltas_saca_la_ventana_y_pregunta()
    {
        // Antes: escondida en la bandeja, «Salir» intentaba enseñarla mientras se cerraba y WPF lanzaba.
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Uno");
        Ui.Run(() =>
        {
            Ui.Click(HeaderButton(Header(Tab(main)), "DetachButton"));
            main.StartInTray();
            Assert.False(main.IsVisible);
        });
        var tray = Peek.Field<TrayIcon>(main, "_tray");
        var handled = false;
        Ui.Run(() => tray.Hook(IntPtr.Zero, TrayIcon.WmCommand, new IntPtr(TrayIcon.IdExit), IntPtr.Zero, ref handled));   // cancelada
        Ui.Flush();
        Ui.Run(() =>
        {
            Assert.True(main.IsVisible);
            Assert.Contains(main, Application.Current.Windows.OfType<Window>());
            Assert.Single(Detached());
        });
        Assert.Single(Ui.Modals);
        Assert.Equal(["add 8001 " + Loc.Get("AppTitle"), "delete", "activate"], TrayShell.Calls);
    }

    [Fact]
    public void Cerrar_sin_preguntar_si_el_ajuste_lo_dice()
    {
        new AppSettings { AskBeforeClosingDetached = false }.Save();
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Uno");
        Ui.Run(() => Ui.Click(HeaderButton(Header(Tab(main)), "DetachButton")));
        Ui.Run(main.Close);
        Assert.Empty(Ui.Modals);
        Ui.Run(() => Assert.Empty(Detached()));
    }

    // ------------------------------------------------------------------ Arrastrar pestañas

    /// <summary>Pulsa en el titulo de la pestaña y arrastra lo bastante.</summary>
    private static void DragTab(MainWindow main, int index = 0)
    {
        var title = (UIElement)Header(Tab(main, index)).Children[0];
        Input.Mouse(title, Mouse.PreviewMouseDownEvent);
        main.TabsMouseMove(new Point(-5000, -5000), leftPressed: true);
    }

    private static Point Dip(MainWindow main, int x, int y) =>
        (PresentationSource.FromVisual(main)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity).Transform(new Point(x, y));

    [Fact]
    public void Arrastrar_una_pestaña_fuera_la_saca_donde_se_suelta()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Uno");
        Ui.Run(() => DragTab(main));
        Ui.Flush();
        Assert.Equal([Ui.Run(() => (UIElement)main.Tabs)], Desk.Captured);
        var drag = Desk.Drags.Single();
        Assert.Same(main, drag.Source);
        Assert.Equal(Sessions[0].Connection.Id.ToString(), ((IDataObject)drag.Data).GetData("SocRcManager.Session"));
        Ui.Run(() =>
        {
            Assert.Single(Detached());
            Assert.Empty(main.Tabs.Items);
        });
    }

    [Fact]
    public void Arrastre_cancelado_o_soltado_en_la_principal_no_hace_nada()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Uno");

        // Esc durante el arrastre.
        Desk.DuringDrag = (source, _) =>
        {
            Input.QueryContinue((UIElement)source, escape: false);
            Input.QueryContinue((UIElement)source, escape: true);
        };
        Ui.Run(() => DragTab(main));
        Ui.Flush();
        Ui.Run(() => Assert.Single(main.Tabs.Items));

        // Soltada sobre las pestañas de la principal.
        Desk.DuringDrag = (_, data) => Assert.True(Input.Drag(main.TabsArea, DragDrop.DropEvent, data).Handled);
        Ui.Run(() =>
        {
            DragTab(main);
            Assert.Equal(DragDropEffects.Move | DragDropEffects.Copy, Input.Drag(main.TabsArea, DragDrop.DragOverEvent, (IDataObject)Desk.Drags[^1].Data).Effects);
        });
        Ui.Flush();
        Ui.Run(() => Assert.Single(main.Tabs.Items));

        // Con el raton sobre la principal (fuera del area de pestañas).
        Desk.DuringDrag = null;
        Desk.Root = Ui.Run(() => new WindowInteropHelper(main).Handle);
        Ui.Run(() => DragTab(main));
        Ui.Flush();
        Ui.Run(() =>
        {
            Assert.Single(main.Tabs.Items);
            Assert.Empty(Detached());
        });
        Assert.Equal(3, Desk.Drags.Count);
    }

    [Fact]
    public void Sin_arrastrar_lo_bastante_o_desde_un_boton_no_empieza()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Uno");
        Ui.Run(() =>
        {
            Input.Move(main.Tabs, Mouse.PreviewMouseMoveEvent);   // el raton de verdad no esta pulsado
            Input.Mouse((UIElement)Header(Tab(main)).Children[0], Mouse.PreviewMouseDownEvent);
            var start = Peek.Field<Point?>(main, "_tabDragStart")!.Value;
            main.TabsMouseMove(start, leftPressed: true);   // pulsado pero sin moverse
            main.TabsMouseMove(new Point(-5000, -5000), leftPressed: false);
            Input.Mouse((UIElement)Header(Tab(main)).Children[0], Mouse.PreviewMouseUpEvent);
            main.TabsMouseMove(new Point(-5000, -5000), leftPressed: true);   // ya soltado

            // Desde un boton de la cabecera, o fuera de las cabeceras (el contenido): no.
            Input.Mouse(HeaderButton(Header(Tab(main)), "DisconnectButton"), Mouse.PreviewMouseDownEvent);
            main.TabsMouseMove(new Point(-5000, -5000), leftPressed: true);
            Input.Mouse(main.Tabs, Mouse.PreviewMouseDownEvent);
            main.TabsMouseMove(new Point(-5000, -5000), leftPressed: true);
        });
        Assert.Empty(Desk.Drags);
        Assert.Single(Desk.Captured);
    }

    [Fact]
    public void Arrastrar_la_barra_de_una_ventana_suelta()
    {
        Seed([Conn("Uno", "", ConnectionKind.Ssh)]);
        var main = NewMain();
        Connect(main, "Uno");
        Ui.Run(() => Ui.Click(HeaderButton(Header(Tab(main)), "DetachButton")));
        var w = Ui.Run(() => Detached().Single());

        // Soltada en otro sitio: la ventana se lleva alli (el raton sobre su barra).
        Ui.Run(() => Peek.Raise(w, "BarDragStarted"));
        Ui.Flush();
        var cursor = Ui.Run(() => Dip(main, Desk.Cursor.X, Desk.Cursor.Y));
        Ui.Run(() => Assert.Equal((cursor.X - 60, cursor.Y - 14), (w.Left, w.Top)));
        Assert.Same(w, Desk.Drags.Single().Source);

        // Soltada sobre si misma (u otra suelta): nada.
        Desk.Root = Ui.Run(() => new WindowInteropHelper(w).EnsureHandle());
        Ui.Run(() =>
        {
            w.Left = 1;
            Peek.Raise(w, "BarDragStarted");
        });
        Ui.Flush();
        Ui.Run(() => Assert.Equal(1, w.Left));

        // Soltada sobre la principal: vuelve como pestaña.
        Desk.Root = Ui.Run(() => new WindowInteropHelper(main).Handle);
        Ui.Run(() => Peek.Raise(w, "BarDragStarted"));
        Ui.Flush();
        Ui.Run(() =>
        {
            Assert.Empty(Detached());
            Assert.Single(main.Tabs.Items);
        });
    }
}
