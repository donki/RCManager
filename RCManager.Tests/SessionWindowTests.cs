using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SocRcManager.Services;
using Rect = System.Drawing.Rectangle;

namespace SocRcManager.Tests;

/// <summary>
/// La ventana de una pestaña suelta: montaje, soltar la vista y la cabecera, cerrar con la X,
/// arrastre de la barra, pantalla completa y monitores. Las que se enseñan van fuera de la pantalla;
/// las de pantalla completa o maximizadas no se enseñan (se verian).
/// </summary>
public sealed class SessionWindowTests : UiTest
{
    private sealed record Parts(SessionWindow Window, TextBlock Header, Button HeaderButton, Border View);

    private static Parts Create(bool show)
    {
        return Ui.Run(() =>
        {
            var button = new Button { Content = "x" };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            var title = new TextBlock { Text = "Oficina" };
            header.Children.Add(title);
            header.Children.Add(button);
            var view = new Border { Width = 300, Height = 200 };
            var w = new SessionWindow("Oficina · escritorio", header, view);
            if (show)
                Ui.Show(w);
            return new Parts(w, title, button, view);
        });
    }

    [Fact]
    public void Se_monta_con_la_cabecera_arriba_y_la_vista_debajo()
    {
        var p = Create(show: true);
        Ui.Run(() =>
        {
            var w = p.Window;
            Assert.Equal("Oficina · escritorio", w.Title);
            Assert.Equal(SessionPlacement.MinWidth, w.MinWidth);
            Assert.Equal(SessionPlacement.MinHeight, w.MinHeight);
            Assert.Equal("SessionWindow", System.Windows.Automation.AutomationProperties.GetAutomationId(w));
            Assert.Equal("SessionBar", System.Windows.Automation.AutomationProperties.GetAutomationId(w.Bar));
            Assert.Same(w, Window.GetWindow(p.View));
            Assert.Same(w, Window.GetWindow(p.Header));
            // La barra queda encima de la vista.
            var barBottom = w.Bar.TranslatePoint(new Point(0, w.Bar.ActualHeight), w).Y;
            Assert.True(p.View.TranslatePoint(new Point(0, 0), w).Y >= barBottom - 0.5);
            Assert.Equal(Cursors.SizeAll, w.Bar.Cursor);
        });
    }

    [Fact]
    public void Soltar_la_vista_y_la_cabecera_una_sola_vez()
    {
        var p = Create(show: true);
        Ui.Run(() =>
        {
            var view = p.Window.ReleaseView();
            Assert.Same(p.View, view);
            Assert.Null(VisualParentOrLogical(p.View));
            Assert.Null(p.Window.ReleaseView());

            var header = p.Window.ReleaseHeader();
            Assert.Same(p.Header.Parent, header);
            Assert.Null(p.Window.ReleaseHeader());
        });
    }

    private static DependencyObject? VisualParentOrLogical(FrameworkElement e) =>
        System.Windows.Media.VisualTreeHelper.GetParent(e) ?? e.Parent;

    [Fact]
    public void La_X_no_cierra_pide_volver_a_la_principal()
    {
        var p = Create(show: true);
        var attach = 0;
        p.Window.AttachRequested += () => attach++;
        Ui.Run(p.Window.Close);
        Assert.True(Ui.Run(() => p.Window.IsVisible));
        Assert.Equal(1, attach);
    }

    [Fact]
    public void Cerrar_a_la_fuerza_cierra_sin_pedir_nada()
    {
        var p = Create(show: true);
        var attach = 0;
        p.Window.AttachRequested += () => attach++;
        Ui.Run(() =>
        {
            p.Window.ForceClose = true;
            p.Window.Close();
        });
        Assert.False(Ui.Run(() => p.Window.IsVisible));
        Assert.Equal(0, attach);
    }

    [Fact]
    public void Colocar_donde_estuvo()
    {
        var p = Create(show: false);
        Ui.Run(() =>
        {
            var w = p.Window;
            w.Place(new WindowBounds(-30000, -30000, 800, 600));
            Assert.Equal(WindowStartupLocation.Manual, w.WindowStartupLocation);
            Assert.Equal(new WindowBounds(-30000, -30000, 800, 600), w.Bounds);
            w.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.Equal(WindowState.Normal, w.WindowState);   // no estaba maximizada
        });
    }

    [Fact]
    public void Colocar_maximizada_se_maximiza_al_cargar()
    {
        var p = Create(show: false);
        Ui.Run(() =>
        {
            var w = p.Window;
            w.Place(new WindowBounds(10, 20, 800, 600, Maximized: true));
            Assert.Equal(WindowState.Normal, w.WindowState);
            w.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.Equal(WindowState.Maximized, w.WindowState);
            Assert.True(w.Bounds.Maximized);
        });
    }

    [Fact]
    public void Pantalla_completa_y_vuelta()
    {
        var p = Create(show: false);
        Ui.Run(() =>
        {
            var w = p.Window;
            w.Place(new WindowBounds(100, 50, 900, 700));
            w.SetFullScreen(false);   // ya estaba asi: nada
            Assert.False(w.IsFullScreen);

            w.SetFullScreen(true);
            Assert.True(w.IsFullScreen);
            Assert.Equal(Visibility.Collapsed, w.Bar.Visibility);
            Assert.Equal(WindowStyle.None, w.WindowStyle);
            Assert.Equal(ResizeMode.NoResize, w.ResizeMode);
            Assert.Equal(WindowState.Maximized, w.WindowState);
            // Se recuerda donde estaba antes, no la pantalla entera.
            Assert.Equal(new WindowBounds(100, 50, 900, 700), w.Bounds);
            w.SetFullScreen(true);   // otra vez: nada
            Assert.Equal(new WindowBounds(100, 50, 900, 700), w.Bounds);

            w.SetFullScreen(false);
            Assert.False(w.IsFullScreen);
            Assert.Equal(Visibility.Visible, w.Bar.Visibility);
            Assert.Equal(WindowStyle.SingleBorderWindow, w.WindowStyle);
            Assert.Equal(ResizeMode.CanResize, w.ResizeMode);
            Assert.Equal(WindowState.Normal, w.WindowState);
        });
    }

    [Fact]
    public void Pantalla_completa_desde_maximizada_vuelve_maximizada()
    {
        var p = Create(show: false);
        Ui.Run(() =>
        {
            var w = p.Window;
            w.WindowState = WindowState.Maximized;
            w.SetFullScreen(true);
            Assert.False(w.Bounds.Maximized);   // en pantalla completa no cuenta como maximizada
            w.SetFullScreen(false);
            Assert.Equal(WindowState.Maximized, w.WindowState);
        });
    }

    [Theory]
    [InlineData(Key.F11, ModifierKeys.None, false, true)]
    [InlineData(Key.F11, ModifierKeys.None, true, true)]
    [InlineData(Key.Escape, ModifierKeys.Control, true, true)]
    [InlineData(Key.Escape, ModifierKeys.Control, false, false)]   // Ctrl+Esc solo sale de la pantalla completa
    [InlineData(Key.Escape, ModifierKeys.None, true, false)]
    [InlineData(Key.F10, ModifierKeys.None, true, false)]
    public void Teclas_de_pantalla_completa(Key key, ModifierKeys modifiers, bool fullScreen, bool toggles)
    {
        var p = Create(show: false);
        var requests = 0;
        p.Window.FullScreenToggleRequested += () => requests++;
        var handled = Ui.Run(() =>
        {
            if (fullScreen)
                p.Window.SetFullScreen(true);
            return p.Window.HandleKey(key, modifiers);
        });
        Assert.Equal(toggles, handled);
        Assert.Equal(toggles ? 1 : 0, requests);
    }

    [Fact]
    public void F11_en_la_ventana_pide_la_pantalla_completa()
    {
        var p = Create(show: true);
        var requests = 0;
        p.Window.FullScreenToggleRequested += () => requests++;
        var (f11, other) = Ui.Run(() =>
        {
            var source = PresentationSource.FromVisual(p.Window)!;
            var a = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.F11) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            p.Window.RaiseEvent(a);
            var b = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.A) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            p.Window.RaiseEvent(b);
            return (a.Handled, b.Handled);
        });
        Assert.True(f11);
        Assert.False(other);
        Assert.Equal(1, requests);
    }

    [Fact]
    public void Arrastrar_la_barra_pasado_el_umbral()
    {
        var p = Create(show: true);
        var drags = 0;
        p.Window.BarDragStarted += () => drags++;
        var dx = SystemParameters.MinimumHorizontalDragDistance;
        var dy = SystemParameters.MinimumVerticalDragDistance;
        Ui.Run(() =>
        {
            var w = p.Window;
            w.BarMoved(new Point(500, 500), true);    // sin pulsar antes: nada
            w.BarPressed(w.Bar, new Point(10, 10));
            w.BarMoved(new Point(10 + dx / 2, 10 + dy / 2), true);   // dentro del umbral
            w.BarMoved(new Point(200, 10), false);    // sin el boton pulsado
            Assert.Equal(0, drags);
            w.BarMoved(new Point(10, 10 + dy + 1), true);
            Assert.Equal(1, drags);
            w.BarMoved(new Point(300, 300), true);    // ya empezado: una vez
            Assert.Equal(1, drags);

            w.BarPressed(null, new Point(10, 10));
            w.BarMoved(new Point(10 + dx + 1, 10), true);
            Assert.Equal(2, drags);
        });
    }

    [Fact]
    public void Pulsar_en_un_boton_de_la_cabecera_no_arrastra_y_soltar_cancela()
    {
        var p = Create(show: true);
        var drags = 0;
        p.Window.BarDragStarted += () => drags++;
        Ui.Run(() =>
        {
            var w = p.Window;
            var inside = (DependencyObject)System.Windows.Media.VisualTreeHelper.GetChild(p.HeaderButton, 0);
            w.BarPressed(inside, new Point(10, 10));
            w.BarMoved(new Point(300, 300), true);
            w.BarPressed(p.Header, new Point(10, 10));
            w.BarReleased();
            w.BarMoved(new Point(300, 300), true);
        });
        Assert.Equal(0, drags);
    }

    [Fact]
    public void Los_eventos_del_raton_de_la_barra()
    {
        var p = Create(show: true);
        var drags = 0;
        p.Window.BarDragStarted += () => drags++;
        Ui.Run(() =>
        {
            var bar = p.Window.Bar;
            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
            bar.RaiseEvent(down);
            // El raton de verdad no se mueve: la misma posicion no pasa del umbral.
            bar.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent });
            bar.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
            Assert.False(bar.IsMouseCaptured);
        });
        Assert.Equal(0, drags);
    }

    [Fact]
    public void Traer_al_frente_la_enseña_y_la_restaura()
    {
        var p = Create(show: false);
        Ui.Run(() =>
        {
            var w = p.Window;
            Ui.Hide(w);
            w.BringToFront(activate: false);
            Assert.True(w.IsVisible);
            Assert.Equal(WindowState.Normal, w.WindowState);
        });
    }

    [Fact]
    public void Traer_al_frente_una_minimizada()
    {
        var p = Create(show: false);
        Ui.Run(() =>
        {
            var w = p.Window;
            Ui.Hide(w);
            w.WindowState = WindowState.Minimized;
            w.BringToFront(activate: false);
            Assert.Equal(WindowState.Normal, w.WindowState);
        });
    }

    [Fact]
    public void Llevar_a_otro_monitor()
    {
        IReadOnlyList<Rect> screens = [new Rect(0, 0, 1920, 1080), new Rect(-2560, -300, 2560, 1440)];
        var w = Ui.Run(() => new Window { Left = 5, Top = 6 });
        Ui.Run(() =>
        {
            SessionWindow.MoveToScreen(w, 0, screens);   // 0 = donde esta
            SessionWindow.MoveToScreen(w, 3, screens);   // no existe
            SessionWindow.MoveToScreen(w, 0);            // con los monitores de verdad: tampoco
            Assert.Equal((5.0, 6.0), (w.Left, w.Top));

            w.WindowState = WindowState.Maximized;
            SessionWindow.MoveToScreen(w, 2, screens);
            Assert.Equal(WindowState.Normal, w.WindowState);
            Assert.Equal((-2520.0, -260.0), (w.Left, w.Top));   // 40 dentro de su esquina
        });
    }

    [Fact]
    public void Llevar_a_otro_monitor_en_unidades_de_WPF()
    {
        // Una ventana enseñada (fuera de la pantalla) tiene la escala de su monitor; el «monitor» de
        // destino tambien esta fuera de la pantalla para no enseñar nada.
        var w = Ui.Run(() => Ui.Show(new Window { Width = 200, Height = 100 }));
        var (left, top, scale) = Ui.Run(() =>
        {
            SessionWindow.MoveToScreen(w, 1, [new Rect(-30000, -30000, 1000, 1000)]);
            var m = PresentationSource.FromVisual(w)!.CompositionTarget!.TransformFromDevice;
            return (w.Left, w.Top, m.M11);
        });
        Assert.Equal(-30000 * scale + 40, left, 3);
        Assert.Equal(-30000 * scale + 40, top, 3);
    }
}
