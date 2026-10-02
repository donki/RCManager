using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SocRcManager.Sessions;

namespace SocRcManager.Tests;

/// <summary>Teclas del terminal: secuencias xterm y codigos de control.</summary>
public sealed class TerminalKeysTests
{
    [Theory]
    [InlineData(Key.Enter, "\r")]
    [InlineData(Key.Back, "\x7f")]
    [InlineData(Key.Tab, "\t")]
    [InlineData(Key.Escape, "\x1b")]
    [InlineData(Key.Up, "\x1b[A")]
    [InlineData(Key.Down, "\x1b[B")]
    [InlineData(Key.Right, "\x1b[C")]
    [InlineData(Key.Left, "\x1b[D")]
    [InlineData(Key.Home, "\x1b[H")]
    [InlineData(Key.End, "\x1b[F")]
    [InlineData(Key.Insert, "\x1b[2~")]
    [InlineData(Key.Delete, "\x1b[3~")]
    [InlineData(Key.PageUp, "\x1b[5~")]
    [InlineData(Key.PageDown, "\x1b[6~")]
    [InlineData(Key.F1, "\x1bOP")]
    [InlineData(Key.F2, "\x1bOQ")]
    [InlineData(Key.F3, "\x1bOR")]
    [InlineData(Key.F4, "\x1bOS")]
    [InlineData(Key.F5, "\x1b[15~")]
    [InlineData(Key.F6, "\x1b[17~")]
    [InlineData(Key.F7, "\x1b[18~")]
    [InlineData(Key.F8, "\x1b[19~")]
    [InlineData(Key.F9, "\x1b[20~")]
    [InlineData(Key.F10, "\x1b[21~")]
    [InlineData(Key.F11, "\x1b[23~")]
    [InlineData(Key.F12, "\x1b[24~")]
    public void Teclas_especiales(Key key, string expected) =>
        Assert.Equal(expected, TerminalKeys.Sequence(key, false, false, false, false));

    [Theory]
    [InlineData(Key.Up, "\x1bOA")]
    [InlineData(Key.Down, "\x1bOB")]
    [InlineData(Key.Right, "\x1bOC")]
    [InlineData(Key.Left, "\x1bOD")]
    [InlineData(Key.Home, "\x1b[H")]
    public void Flechas_en_modo_cursor_de_aplicacion(Key key, string expected) =>
        Assert.Equal(expected, TerminalKeys.Sequence(key, false, false, false, true));

    [Theory]
    [InlineData(Key.A, "\x01")]
    [InlineData(Key.C, "\x03")]
    [InlineData(Key.D, "\x04")]
    [InlineData(Key.L, "\x0c")]
    [InlineData(Key.Z, "\x1a")]
    [InlineData(Key.Space, "\0")]
    [InlineData(Key.OemOpenBrackets, "\x1b")]
    public void Control_mas_tecla(Key key, string expected) =>
        Assert.Equal(expected, TerminalKeys.Sequence(key, true, false, false, false));

    [Fact]
    public void Lo_que_no_manda_nada()
    {
        Assert.Null(TerminalKeys.Sequence(Key.A, false, false, false, false));          // texto: va por la entrada de texto
        Assert.Null(TerminalKeys.Sequence(Key.A, true, false, true, false));            // AltGr (Ctrl+Alt) no es control
        Assert.Null(TerminalKeys.Sequence(Key.D1, true, false, false, false));
        Assert.Null(TerminalKeys.Sequence(Key.PageUp, false, true, false, false));      // Mayus+RePag desplaza el historial
        Assert.Null(TerminalKeys.Sequence(Key.PageDown, false, true, false, false));
        Assert.Null(TerminalKeys.Sequence(Key.LeftShift, false, true, false, false));
        Assert.Equal("\0", TerminalKeys.Sequence(Key.Space, true, false, true, false));
        Assert.Equal("\r", TerminalKeys.Sequence(Key.Enter, true, true, true, false));  // las especiales mandan igual
    }
}

/// <summary>La seleccion con el raton del terminal.</summary>
public sealed class TerminalSelectionTests
{
    [Fact]
    public void Sin_empezar_no_hay_nada()
    {
        var s = new TerminalSelection();
        Assert.False(s.IsActive);
        Assert.False(s.Covers(0, 0, 0, 80));
        s.Extend((1, 1));   // sin pulsar antes: nada
        Assert.False(s.IsActive);
    }

    [Fact]
    public void Pulsar_y_soltar_en_la_misma_celda_no_selecciona()
    {
        var s = new TerminalSelection();
        s.Begin((2, 3));
        Assert.True(s.IsActive);
        Assert.False(s.Finish());
        Assert.False(s.IsActive);
    }

    [Fact]
    public void Arrastrar_hacia_atras_queda_en_orden_de_lectura()
    {
        var s = new TerminalSelection();
        s.Begin((3, 10));
        s.Extend((1, 5));
        Assert.True(s.Finish());
        Assert.Equal(((1, 5), (3, 10)), s.Ordered());
        s.Begin((2, 8));
        s.Extend((2, 4));
        Assert.Equal(((2, 4), (2, 8)), s.Ordered());
    }

    [Fact]
    public void Que_tramos_estan_dentro()
    {
        var s = new TerminalSelection();
        s.Begin((1, 5));
        s.Extend((3, 10));
        Assert.False(s.Covers(0, 0, 79, 80));   // fila de antes
        Assert.False(s.Covers(4, 0, 0, 80));    // fila de despues
        Assert.False(s.Covers(1, 4, 6, 80));    // empieza antes del principio
        Assert.True(s.Covers(1, 5, 79, 80));    // primera fila: de la columna 5 al final
        Assert.True(s.Covers(2, 0, 79, 80));    // fila de enmedio entera
        Assert.True(s.Covers(3, 0, 10, 80));    // ultima fila: hasta la 10
        Assert.False(s.Covers(3, 0, 11, 80));
        s.Clear();
        Assert.False(s.Covers(2, 0, 79, 80));
    }
}

/// <summary>El control del terminal en una ventana fuera de la pantalla: pintado, tamaño, teclado, raton y portapapeles.</summary>
public sealed class TerminalControlTests : UiTest
{
    private readonly FakeClipboard _clipboard = new();
    private readonly List<string> _sent = [];

    public override void Dispose()
    {
        _clipboard.Dispose();
        base.Dispose();
    }

    private TerminalControl Create()
    {
        var t = Ui.Run(() => new TerminalControl());
        t.Input += b => _sent.Add(Encoding.UTF8.GetString(b));
        return t;
    }

    /// <summary>El terminal en una ventana, con ese tamaño.</summary>
    private (TerminalControl Terminal, Border Holder) Shown(double width = 400, double height = 200)
    {
        var t = Create();
        var holder = Ui.Run(() =>
        {
            var b = new Border { Width = width, Height = height, Child = t };
            Ui.Show(new Window { Content = b, SizeToContent = SizeToContent.WidthAndHeight });
            return b;
        });
        return (t, holder);
    }

    private static void Feed(TerminalControl t, string text) => Ui.Run(() =>
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        t.Feed(bytes, bytes.Length);
    });

    /// <summary>El color del pixel en el centro de una celda tal como se pinta.</summary>
    private static Color Pixel(TerminalControl t, int row, int col) => Ui.Run(() =>
    {
        var (cw, ch) = t.CellSize;
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(t.ActualWidth), (int)Math.Ceiling(t.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(t);
        var px = new byte[4];
        bmp.CopyPixels(new Int32Rect((int)(col * cw + cw / 2), (int)(row * ch + 1), 1, 1), px, 4, 0);
        return Color.FromRgb(px[2], px[1], px[0]);
    });

    [Fact]
    public void Sin_tamaño_es_un_terminal_de_80_por_24()
    {
        var t = Create();
        Assert.Equal((80, 24), Ui.Run(() => (t.Cols, t.Rows)));
        Assert.Equal(14, Ui.Run(() => t.FontSize));
        Assert.True(Ui.Run(() => t.Focusable));
    }

    [Fact]
    public void La_rejilla_sigue_al_tamaño_del_control()
    {
        var t = Create();
        var sizes = new List<(int, int)>();
        t.Resized += (c, r) => sizes.Add((c, r));
        var holder = Ui.Run(() =>
        {
            var b = new Border { Width = 400, Height = 200, Child = t };
            Ui.Show(new Window { Content = b, SizeToContent = SizeToContent.WidthAndHeight });
            return b;
        });
        var (cw, ch) = Ui.Run(() => t.CellSize);
        var expected = (Math.Max(20, (int)(400 / cw)), Math.Max(5, (int)(200 / ch)));
        Assert.Equal(expected, Ui.Run(() => (t.Cols, t.Rows)));
        Assert.Equal([expected], sizes);

        // Muy pequeño: como poco 20 x 5.
        Ui.Run(() => { holder.Width = 30; holder.Height = 10; });
        Assert.Equal((20, 5), Ui.Run(() => (t.Cols, t.Rows)));

        // El mismo numero de celdas: no se avisa otra vez.
        Ui.Run(() => holder.Width = 31);
        Assert.Equal(2, sizes.Count);
    }

    [Fact]
    public void Cambiar_la_letra_recalcula_la_rejilla()
    {
        var (t, _) = Shown(600, 300);
        var sizes = new List<(int Cols, int Rows)>();
        t.Resized += (c, r) => sizes.Add((c, r));
        var before = Ui.Run(() => t.CellSize);
        Ui.Run(() => t.FontSize = 14.05);   // casi lo mismo: no cambia nada
        Assert.Empty(sizes);

        Ui.Run(() => t.FontSize = 20);
        var after = Ui.Run(() => t.CellSize);
        Assert.True(after.Width > before.Width && after.Height > before.Height);
        var (cols, rows) = Assert.Single(sizes);
        Assert.Equal((Math.Max(20, (int)(600 / after.Width)), Math.Max(5, (int)(300 / after.Height))), (cols, rows));
    }

    [Fact]
    public void Cambiar_la_letra_sin_tamaño_no_redimensiona()
    {
        var t = Create();
        var resized = 0;
        t.Resized += (_, _) => resized++;
        Ui.Run(() => t.FontSize = 18);
        Assert.Equal(0, resized);
        Assert.Equal(18, Ui.Run(() => t.FontSize));
    }

    [Fact]
    public void Lo_que_llega_se_ve_y_el_titulo_se_avisa()
    {
        var t = Create();
        var titles = new List<string>();
        t.TitleChanged += titles.Add;
        Feed(t, "uno\r\ndos\r\n\x1b]2;mi titulo\x07");
        Assert.Equal("uno", Ui.Run(() => t.RowText(0)));
        Assert.Equal("dos", Ui.Run(() => t.RowText(1)));
        Assert.Equal("mi titulo", Ui.Run(() => t.Title));
        Assert.Equal(["mi titulo"], titles);
    }

    [Fact]
    public void Pinta_el_fondo_los_colores_y_el_inverso()
    {
        var (t, _) = Shown(400, 200);
        Ui.Run(() => t.SetColors(Brushes.White, new SolidColorBrush(Color.FromRgb(10, 20, 30))));
        // Fondo rojo (paleta 1), color directo, inverso con los colores por defecto, y negrita subrayada.
        Feed(t, "\x1b[41mR\x1b[0m\x1b[48;2;1;200;3mG\x1b[0m\x1b[7mI\x1b[0m\x1b[1;4;32mB\x1b[0m");
        Assert.Equal(Color.FromRgb(10, 20, 30), Pixel(t, 3, 10));          // fondo
        Assert.Equal(Color.FromRgb(205, 49, 49), Pixel(t, 0, 0));          // rojo
        Assert.Equal(Color.FromRgb(1, 200, 3), Pixel(t, 0, 1));            // color directo
        Assert.Equal(Color.FromRgb(255, 255, 255), Pixel(t, 0, 2));        // inverso: el texto por defecto de fondo
    }

    [Fact]
    public void Inverso_con_colores_de_la_paleta()
    {
        var (t, _) = Shown(400, 200);
        Feed(t, "\x1b[7;31;44mX\x1b[0m");
        // Inverso: el fondo es el color del texto (rojo).
        Assert.Equal(Color.FromRgb(205, 49, 49), Pixel(t, 0, 0));
    }

    [Fact]
    public void La_seleccion_se_pinta_resaltada()
    {
        var (t, _) = Shown(400, 200);
        Feed(t, "hola mundo");
        var (cw, ch) = Ui.Run(() => t.CellSize);
        Ui.Run(() =>
        {
            t.PointerDown(MouseButton.Left, new Point(0, 1));
            t.PointerMove(new Point(3 * cw + 1, 1));
            t.PointerUp();
        });
        var steel = ((SolidColorBrush)Brushes.SteelBlue).Color;
        Assert.Equal(steel, Pixel(t, 0, 0));
        Assert.Equal(steel, Pixel(t, 0, 3));
        Assert.NotEqual(steel, Pixel(t, 0, 4));
    }

    [Fact]
    public void El_cursor_se_pinta_con_el_foco()
    {
        var (t, _) = Shown(400, 200);
        Feed(t, "ab");
        var without = Pixel(t, 0, 2);
        Ui.Run(() => t.Focus());
        Assert.True(Ui.Run(() => t.IsFocused));
        Assert.NotEqual(without, Pixel(t, 0, 2));
        Feed(t, "\x1b[?25l");   // cursor oculto
        Assert.Equal(without, Pixel(t, 0, 2));
    }

    [Fact]
    public void Teclas_que_se_mandan_y_las_que_no()
    {
        var t = Create();
        Ui.Run(() =>
        {
            Assert.True(t.HandleKey(Key.Enter, ModifierKeys.None));
            Assert.True(t.HandleKey(Key.C, ModifierKeys.Control));
            Assert.False(t.HandleKey(Key.A, ModifierKeys.None));
            Assert.False(t.HandleKey(Key.A, ModifierKeys.Control | ModifierKeys.Alt));
        });
        Assert.Equal(["\r", "\x03"], _sent);
    }

    [Fact]
    public void Modo_cursor_de_aplicacion_cambia_las_flechas()
    {
        var t = Create();
        Feed(t, "\x1b[?1h");
        Ui.Run(() => t.HandleKey(Key.Up, ModifierKeys.None));
        Feed(t, "\x1b[?1l");
        Ui.Run(() => t.HandleKey(Key.Up, ModifierKeys.None));
        Assert.Equal(["\x1bOA", "\x1b[A"], _sent);
    }

    [Fact]
    public void Pegar_con_control_mayus_v_convierte_los_saltos_de_linea()
    {
        var t = Create();
        _clipboard.Text = "ls -l\r\ncd /\nexit";
        Assert.True(Ui.Run(() => t.HandleKey(Key.V, ModifierKeys.Control | ModifierKeys.Shift)));
        Assert.Equal(["ls -l\rcd /\rexit"], _sent);

        _clipboard.Text = null;   // sin texto: nada
        Ui.Run(() => t.HandleKey(Key.V, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Single(_sent);
    }

    [Fact]
    public void Portapapeles_que_falla_no_rompe()
    {
        var t = Create();
        TerminalControl.ReadClipboard = () => throw new System.Runtime.InteropServices.COMException("ocupado");
        TerminalControl.WriteClipboard = _ => throw new System.Runtime.InteropServices.COMException("ocupado");
        Feed(t, "texto");
        Ui.Run(() =>
        {
            t.HandleKey(Key.V, ModifierKeys.Control | ModifierKeys.Shift);
            t.PointerDown(MouseButton.Left, new Point(0, 0));
            t.PointerMove(new Point(30, 0));
            t.PointerUp();
            t.HandleKey(Key.C, ModifierKeys.Control | ModifierKeys.Shift);
        });
        Assert.Empty(_sent);
        Assert.False(t.Selection.IsActive);   // la seleccion se quita igual
    }

    [Fact]
    public void Copiar_con_control_mayus_c_la_seleccion()
    {
        var t = Create();
        Feed(t, "primera linea\r\nsegunda linea");
        var (cw, ch) = Ui.Run(() => t.CellSize);
        Ui.Run(() =>
        {
            t.HandleKey(Key.C, ModifierKeys.Control | ModifierKeys.Shift);   // sin seleccion: nada
            t.PointerDown(MouseButton.Left, new Point(8 * cw + 1, 1));
            t.PointerMove(new Point(6 * cw + 1, ch + 1));
            t.PointerUp();
        });
        Assert.Null(_clipboard.Text);
        Assert.True(t.Selection.IsActive);
        Assert.True(Ui.Run(() => t.HandleKey(Key.C, ModifierKeys.Control | ModifierKeys.Shift)));
        Assert.Equal("linea\nsegunda", _clipboard.Text);
        Assert.False(t.Selection.IsActive);
        Assert.Empty(_sent);
    }

    [Fact]
    public void Boton_derecho_copia_si_hay_seleccion_y_si_no_pega()
    {
        var t = Create();
        Feed(t, "abcdef");
        var (cw, _) = Ui.Run(() => t.CellSize);
        _clipboard.Text = "pegado";
        Assert.True(Ui.Run(() => t.PointerDown(MouseButton.Right, new Point(0, 0))));
        Assert.Equal(["pegado"], _sent);

        Ui.Run(() =>
        {
            Assert.False(t.PointerDown(MouseButton.Left, new Point(1, 1)));
            t.PointerMove(new Point(2 * cw + 1, 1));
            t.PointerUp();
            t.PointerDown(MouseButton.Right, new Point(0, 0));
        });
        Assert.Equal("abc", _clipboard.Text);
        Assert.Single(_sent);
    }

    [Fact]
    public void Un_clic_sin_arrastrar_no_selecciona_y_el_central_no_hace_nada()
    {
        var t = Create();
        Feed(t, "abc");
        Ui.Run(() =>
        {
            t.PointerDown(MouseButton.Left, new Point(1, 1));
            t.PointerUp();
            t.PointerUp();   // soltar otra vez: nada
            t.PointerMove(new Point(50, 1));   // sin pulsar: nada
            Assert.False(t.PointerDown(MouseButton.Middle, new Point(1, 1)));
        });
        Assert.False(t.Selection.IsActive);
    }

    [Fact]
    public void Arrastrar_fuera_se_queda_en_el_borde()
    {
        var t = Create();
        Feed(t, "abc");
        Ui.Run(() =>
        {
            t.PointerDown(MouseButton.Left, new Point(-50, -50));
            t.PointerMove(new Point(99999, 99999));
            t.PointerUp();
        });
        Assert.Equal(((0, 0), (23, 79)), t.Selection.Ordered());
    }

    [Fact]
    public void Si_otro_se_queda_el_raton_la_seleccion_no_sigue()
    {
        var (t, _) = Shown();
        Feed(t, "abcdef");
        var (cw, _) = Ui.Run(() => t.CellSize);
        Ui.Run(() =>
        {
            t.PointerDown(MouseButton.Left, new Point(1, 1));
            t.PointerMove(new Point(2 * cw + 1, 1));
            t.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent });
            t.PointerMove(new Point(5 * cw + 1, 1));
            t.PointerUp();
        });
        Assert.Equal(((0, 0), (0, 2)), t.Selection.Ordered());
        Ui.Run(t.ClearSelection);
        Assert.False(t.Selection.IsActive);
    }

    [Fact]
    public void Historial_con_mayus_repag_y_la_rueda()
    {
        var t = Create();
        Feed(t, string.Concat(Enumerable.Range(1, 60).Select(i => $"linea {i}\r\n")));
        Assert.Equal("linea 38", Ui.Run(() => t.RowText(0)));

        Ui.Run(() => t.HandleKey(Key.PageUp, ModifierKeys.Shift));
        Assert.Equal(12, t.ViewOffset);
        Assert.Equal("linea 26", Ui.Run(() => t.RowText(0)));
        Ui.Run(() => t.Wheel(120));
        Assert.Equal(15, t.ViewOffset);
        Ui.Run(() => t.Wheel(-120));
        Ui.Run(() => t.HandleKey(Key.PageDown, ModifierKeys.Shift));
        Assert.Equal(0, t.ViewOffset);
        Ui.Run(() => t.HandleKey(Key.PageDown, ModifierKeys.Shift));   // no baja de la pantalla en vivo
        Assert.Equal(0, t.ViewOffset);

        // Arriba del todo, como mucho el historial que hay.
        for (var i = 0; i < 10; i++)
            Ui.Run(() => t.HandleKey(Key.PageUp, ModifierKeys.Shift));
        Assert.Equal(37, t.ViewOffset);
        Assert.Equal("linea 1", Ui.Run(() => t.RowText(0)));

        // Lo que llega vuelve a la pantalla en vivo.
        Feed(t, "x");
        Assert.Equal(0, t.ViewOffset);
        Assert.Empty(_sent);
    }

    // ------------------------------------------------------------------ Los eventos de WPF llegan a lo de arriba

    [Fact]
    public void Eventos_de_teclado_texto_y_foco()
    {
        var (t, _) = Shown();
        Ui.Run(() =>
        {
            var source = PresentationSource.FromVisual(t)!;
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Tab) { RoutedEvent = Keyboard.KeyDownEvent };
            t.RaiseEvent(key);
            Assert.True(key.Handled);
            var unknown = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.OemComma) { RoutedEvent = Keyboard.KeyDownEvent };
            t.RaiseEvent(unknown);
            Assert.False(unknown.Handled);

            foreach (var text in new[] { "ñ", "\r", "" })
            {
                var e = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, t, text)) { RoutedEvent = TextCompositionManager.TextInputEvent };
                t.RaiseEvent(e);
                Assert.Equal(text == "ñ", e.Handled);
            }

            t.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, t) { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
            t.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, t, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        });
        Assert.Equal(["\t", "ñ"], _sent);
    }

    [Fact]
    public void Eventos_del_raton()
    {
        var (t, _) = Shown();
        Feed(t, string.Concat(Enumerable.Range(1, 40).Select(i => $"{i}\r\n")));
        _clipboard.Text = "pegado";
        Ui.Run(() =>
        {
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = Mouse.MouseWheelEvent };
            t.RaiseEvent(wheel);
            Assert.True(wheel.Handled);

            var right = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = Mouse.MouseDownEvent };
            t.RaiseEvent(right);
            Assert.True(right.Handled);

            var left = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent };
            t.RaiseEvent(left);
            Assert.False(left.Handled);
            t.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent });
            t.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
        });
        Assert.Equal(3, t.ViewOffset);
        Assert.Equal(["pegado"], _sent);
        Assert.False(t.Selection.IsActive);   // pulsar y soltar en el sitio
    }
}
