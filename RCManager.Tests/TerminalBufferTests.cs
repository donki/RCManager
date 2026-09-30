using System.Text;
using SocRcManager.Sessions;

namespace SocRcManager.Tests;

public class TerminalBufferTests
{
    private const string Esc = "\x1b";

    private static TerminalBuffer Term(int rows = 5, int cols = 10)
    {
        var t = new TerminalBuffer();
        t.Resize(rows, cols);
        return t;
    }

    private static string Row(TerminalBuffer t, int r, int viewOffset = 0) =>
        new string(Enumerable.Range(0, t.Cols).Select(c => t.CellAt(r, c, viewOffset).Ch).ToArray()).TrimEnd();

    private static string[] Screen(TerminalBuffer t) => Enumerable.Range(0, t.Rows).Select(r => Row(t, r)).ToArray();

    private static (int, int) Cursor(TerminalBuffer t) => (t.CursorRow, t.CursorCol);

    [Fact]
    public void Arranca_en_80x24_vacio_con_cursor_visible()
    {
        var t = new TerminalBuffer();
        Assert.Equal((24, 80), (t.Rows, t.Cols));
        Assert.Equal((0, 0), Cursor(t));
        Assert.True(t.CursorVisible);
        Assert.False(t.AppCursorKeys);
        Assert.False(t.AlternateScreen);
        Assert.Equal(0, t.ScrollbackCount);
        Assert.Equal("", t.Title);
        Assert.Equal(' ', t.CellAt(0, 0).Ch);
        Assert.Equal(-1, t.CellAt(0, 0).Fg);
    }

    [Fact]
    public void Texto_retorno_y_salto()
    {
        var t = Term();
        t.Feed("hola\r\nmundo");
        Assert.Equal(["hola", "mundo", "", "", ""], Screen(t));
        Assert.Equal((1, 5), Cursor(t));
    }

    [Fact]
    public void UTF8_partido_entre_dos_trozos()
    {
        var t = Term();
        var bytes = Encoding.UTF8.GetBytes("ñé€");
        t.Feed(bytes[..1], 1);
        t.Feed(bytes[1..], bytes.Length - 1);
        Assert.Equal("ñé€", Row(t, 0));
    }

    [Fact]
    public void Controles_retroceso_tabulador_campana_y_nulo()
    {
        var t = Term(3, 20);
        t.Feed("ab\bX");
        Assert.Equal("aX", Row(t, 0));
        t.Feed("\r\b");   // en la columna 0 el retroceso no sale
        Assert.Equal((0, 0), Cursor(t));
        t.Feed("\tT");
        Assert.Equal((0, 9), Cursor(t));
        Assert.Equal('T', t.CellAt(0, 8).Ch);
        t.Feed("\t\t\t");   // no pasa de la ultima columna
        Assert.Equal(19, t.CursorCol);
        t.Feed("\a\0\u0001");   // ni la campana ni el nulo ni otros controles pintan
        Assert.Equal(19, t.CursorCol);
        t.Feed("\v");
        Assert.Equal(1, t.CursorRow);
        t.Feed("\f");
        Assert.Equal(2, t.CursorRow);
    }

    [Fact]
    public void Ajuste_de_linea_al_llegar_al_final()
    {
        var t = Term(3, 4);
        t.Feed("abcd");
        Assert.Equal((0, 3), Cursor(t));   // se queda en la ultima columna, pendiente de saltar
        t.Feed("e");
        Assert.Equal(["abcd", "e", ""], Screen(t));
        t.Feed($"{Esc}[?7l");   // sin ajuste: se sobrescribe la ultima columna
        t.Feed("\r\nwxyz12");
        Assert.Equal("wxy2", Row(t, 2));
        t.Feed($"{Esc}[?7h");
    }

    [Fact]
    public void Scroll_guarda_las_lineas_en_el_historial_y_se_ven_con_desplazamiento()
    {
        var t = Term(3, 5);
        t.Feed("1\r\n2\r\n3\r\n4\r\n5");
        Assert.Equal(["3", "4", "5"], Screen(t));
        Assert.Equal(2, t.ScrollbackCount);
        Assert.Equal("1", Row(t, 0, viewOffset: 2));
        Assert.Equal("2", Row(t, 1, viewOffset: 2));
        Assert.Equal("3", Row(t, 2, viewOffset: 2));
        Assert.Equal("2", Row(t, 0, viewOffset: 1));
        Assert.Equal("4", Row(t, 2, viewOffset: 1));
    }

    [Fact]
    public void El_historial_tiene_tope()
    {
        var t = Term(2, 5);
        t.Feed(string.Concat(Enumerable.Range(0, TerminalBuffer.ScrollbackMax + 50).Select(i => "x\r\n")));
        Assert.Equal(TerminalBuffer.ScrollbackMax, t.ScrollbackCount);
    }

    [Fact]
    public void Linea_del_historial_mas_estrecha_que_la_pantalla()
    {
        var t = Term(2, 3);
        t.Feed("abc\r\n\r\n");
        t.Resize(2, 6);
        Assert.Equal("abc", Row(t, 0, viewOffset: 1));
        Assert.Equal(' ', t.CellAt(0, 5, viewOffset: 1).Ch);
    }

    [Fact]
    public void Movimientos_del_cursor_CSI()
    {
        var t = Term(10, 20);
        t.Feed($"{Esc}[5;10H");
        Assert.Equal((4, 9), Cursor(t));
        t.Feed($"{Esc}[2A");
        Assert.Equal((2, 9), Cursor(t));
        t.Feed($"{Esc}[B");
        Assert.Equal((3, 9), Cursor(t));
        t.Feed($"{Esc}[3C");
        Assert.Equal((3, 12), Cursor(t));
        t.Feed($"{Esc}[100D");
        Assert.Equal((3, 0), Cursor(t));
        t.Feed($"{Esc}[99C");
        Assert.Equal((3, 19), Cursor(t));
        t.Feed($"{Esc}[2E");
        Assert.Equal((5, 0), Cursor(t));
        t.Feed($"{Esc}[F");
        Assert.Equal((4, 0), Cursor(t));
        t.Feed($"{Esc}[7G");
        Assert.Equal((4, 6), Cursor(t));
        t.Feed($"{Esc}[3`");
        Assert.Equal((4, 2), Cursor(t));
        t.Feed($"{Esc}[8d");
        Assert.Equal((7, 2), Cursor(t));
        t.Feed($"{Esc}[H");
        Assert.Equal((0, 0), Cursor(t));
        t.Feed($"{Esc}[99;99f");
        Assert.Equal((9, 19), Cursor(t));
        t.Feed($"{Esc}[99A");
        Assert.Equal(0, t.CursorRow);
        t.Feed($"{Esc}[99B");
        Assert.Equal(9, t.CursorRow);
        t.Feed($"{Esc}[99F");
        Assert.Equal((0, 0), Cursor(t));
        t.Feed($"{Esc}[99E");
        Assert.Equal(9, t.CursorRow);
    }

    [Fact]
    public void Guardar_y_recuperar_el_cursor()
    {
        var t = Term();
        t.Feed($"{Esc}[2;3H{Esc}7{Esc}[H{Esc}8");
        Assert.Equal((1, 2), Cursor(t));
        t.Feed($"{Esc}[4;5H{Esc}[s{Esc}[H{Esc}[u");
        Assert.Equal((3, 4), Cursor(t));
    }

    [Fact]
    public void Borrar_pantalla_J()
    {
        var t = Term(3, 4);
        t.Feed("aaaa\r\nbbbb\r\ncccc");
        t.Feed($"{Esc}[2;3H{Esc}[J");
        Assert.Equal(["aaaa", "bb", ""], Screen(t));

        t = Term(3, 4);
        t.Feed("aaaa\r\nbbbb\r\ncccc");
        t.Feed($"{Esc}[2;3H{Esc}[1J");
        Assert.Equal(["", "   b", "cccc"], Screen(t));

        foreach (var n in new[] { 2, 3 })
        {
            t = Term(3, 4);
            t.Feed("aaaa\r\nbbbb");
            t.Feed($"{Esc}[{n}J");
            Assert.Equal(["", "", ""], Screen(t));
        }
    }

    [Fact]
    public void Borrar_linea_K_y_caracteres_X()
    {
        var t = Term(1, 6);
        t.Feed($"abcdef{Esc}[1;3H{Esc}[K");
        Assert.Equal("ab", Row(t, 0));
        t = Term(1, 6);
        t.Feed($"abcdef{Esc}[1;3H{Esc}[1K");
        Assert.Equal("   def", Row(t, 0));
        t = Term(1, 6);
        t.Feed($"abcdef{Esc}[2K");
        Assert.Equal("", Row(t, 0));
        t = Term(1, 6);
        t.Feed($"abcdef{Esc}[1;2H{Esc}[2X");
        Assert.Equal("a  def", Row(t, 0));
        t = Term(1, 6);
        t.Feed($"abcdef{Esc}[1;2H{Esc}[99X");
        Assert.Equal("a", Row(t, 0));
    }

    [Fact]
    public void Insertar_y_borrar_caracteres()
    {
        var t = Term(1, 6);
        t.Feed($"abcdef{Esc}[1;2H{Esc}[2P");
        Assert.Equal("adef", Row(t, 0));
        t = Term(1, 6);
        t.Feed($"abcdef{Esc}[1;2H{Esc}[2@");
        Assert.Equal("a  bcd", Row(t, 0));
        t = Term(1, 6);
        t.Feed($"abcdef{Esc}[1;2H{Esc}[99@");
        Assert.Equal("a", Row(t, 0));
    }

    [Fact]
    public void Insertar_y_borrar_lineas_dentro_de_la_region()
    {
        var t = Term(4, 3);
        t.Feed("1\r\n2\r\n3\r\n4");
        t.Feed($"{Esc}[2;1H{Esc}[L");
        Assert.Equal(["1", "", "2", "3"], Screen(t));
        t.Feed($"{Esc}[2M");
        Assert.Equal(["1", "3", "", ""], Screen(t));
    }

    [Fact]
    public void Insertar_o_borrar_lineas_fuera_de_la_region_no_hace_nada()
    {
        var t = Term(4, 3);
        t.Feed("1\r\n2\r\n3\r\n4");
        t.Feed($"{Esc}[2;3r");   // region filas 2..3; el cursor va a (0,0), fuera
        t.Feed($"{Esc}[L{Esc}[M");
        Assert.Equal(["1", "2", "3", "4"], Screen(t));
    }

    [Fact]
    public void Region_de_scroll_no_toca_lo_de_fuera_ni_el_historial()
    {
        var t = Term(4, 3);
        t.Feed("A\r\nb\r\nc\r\nZ");
        t.Feed($"{Esc}[2;3r");
        Assert.Equal((0, 0), Cursor(t));
        t.Feed($"{Esc}[3;1H\nd");   // salto en el fondo de la region: sube solo la region
        Assert.Equal(["A", "c", "d", "Z"], Screen(t));
        Assert.Equal(0, t.ScrollbackCount);
        t.Feed($"{Esc}[2;1H{Esc}M");   // indice inverso en lo alto de la region: baja la region
        Assert.Equal(["A", "", "c", "Z"], Screen(t));
        t.Feed($"{Esc}[S");
        Assert.Equal(["A", "c", "", "Z"], Screen(t));
        t.Feed($"{Esc}[T");
        Assert.Equal(["A", "", "c", "Z"], Screen(t));
    }

    [Fact]
    public void Cursor_arriba_y_abajo_se_paran_en_la_region()
    {
        var t = Term(6, 3);
        t.Feed($"{Esc}[2;5r{Esc}[4;1H{Esc}[9A");
        Assert.Equal(1, t.CursorRow);   // tope de la region
        t.Feed($"{Esc}[9B");
        Assert.Equal(4, t.CursorRow);   // fondo de la region
        t.Feed($"{Esc}[6;1H{Esc}[B");
        Assert.Equal(5, t.CursorRow);   // por debajo de la region: hasta la ultima fila
        t.Feed($"{Esc}[1;1H{Esc}[A");
        Assert.Equal(0, t.CursorRow);
    }

    [Fact]
    public void Region_invalida_se_recorta()
    {
        var t = Term(4, 3);
        t.Feed($"{Esc}[3;2r");   // el fondo por encima del tope: se queda en el tope
        t.Feed($"{Esc}[3;1H\n");
        Assert.Equal(2, t.CursorRow);
        t.Feed($"{Esc}[r");     // sin argumentos: la pantalla entera
        t.Feed($"{Esc}[4;1H\n");
        Assert.Equal(3, t.CursorRow);
    }

    [Fact]
    public void Indices_ESC_D_E_y_M()
    {
        var t = Term(3, 5);
        t.Feed($"ab{Esc}D");
        Assert.Equal((1, 2), Cursor(t));
        t.Feed($"{Esc}E");
        Assert.Equal((2, 0), Cursor(t));
        t.Feed($"{Esc}M{Esc}M");
        Assert.Equal((0, 0), Cursor(t));
        t.Feed($"{Esc}M");   // en lo alto: baja la pantalla
        Assert.Equal(["", "ab", ""], Screen(t));
        t.Feed($"{Esc}[3;1H{Esc}[2;3r{Esc}[3;1H{Esc}[1;1H\n\n\n");
        Assert.Equal(2, t.CursorRow);
    }

    [Fact]
    public void Colores_y_atributos_SGR()
    {
        var t = Term(1, 20);
        t.Feed($"{Esc}[1;4;7;31;42ma{Esc}[22;24;27;39;49mb{Esc}[93;104mc{Esc}[0md{Esc}[mE");
        var a = t.CellAt(0, 0);
        Assert.True(a.Bold && a.Underline && a.Inverse);
        Assert.Equal((1, 2), (a.Fg, a.Bg));
        var b = t.CellAt(0, 1);
        Assert.False(b.Bold || b.Underline || b.Inverse);
        Assert.Equal((-1, -1), (b.Fg, b.Bg));
        Assert.Equal((11, 12), (t.CellAt(0, 2).Fg, t.CellAt(0, 2).Bg));
        Assert.Equal((-1, -1), (t.CellAt(0, 3).Fg, t.CellAt(0, 3).Bg));
        Assert.True(TerminalCell.SameStyle(t.CellAt(0, 3), t.CellAt(0, 4)));
        Assert.False(TerminalCell.SameStyle(t.CellAt(0, 0), t.CellAt(0, 1)));
    }

    [Fact]
    public void Colores_de_256_y_directos()
    {
        var t = Term(1, 10);
        t.Feed($"{Esc}[38;5;208;48;5;17ma{Esc}[38;2;255;128;0;48;2;1;2;3mb{Esc}[38;5mc{Esc}[48;2;1mdX");
        Assert.Equal((208, 17), (t.CellAt(0, 0).Fg, t.CellAt(0, 0).Bg));
        Assert.Equal(256 + 0xFF8000, t.CellAt(0, 1).Fg);
        Assert.Equal(256 + 0x010203, t.CellAt(0, 1).Bg);
        Assert.Equal(256 + 0xFF8000, t.CellAt(0, 2).Fg);   // 38;5 sin indice: no cambia
        Assert.Equal(256 + 0x010203, t.CurrentAttributes.Bg);
    }

    [Fact]
    public void Borrar_usa_el_fondo_actual()
    {
        var t = Term(1, 4);
        t.Feed($"abcd{Esc}[44m{Esc}[2K");
        Assert.Equal(4, t.CellAt(0, 0).Bg);
        Assert.Equal(-1, t.CellAt(0, 0).Fg);
    }

    [Fact]
    public void Modos_privados_cursor_teclas_y_pantalla_alternativa()
    {
        var t = Term(3, 5);
        t.Feed($"{Esc}[?25l{Esc}[?1h");
        Assert.False(t.CursorVisible);
        Assert.True(t.AppCursorKeys);
        t.Feed($"{Esc}[?25h{Esc}[?1l");
        Assert.True(t.CursorVisible);
        Assert.False(t.AppCursorKeys);

        t.Feed($"shell{Esc}[2;2H");
        t.Feed($"{Esc}[?1049h");
        Assert.True(t.AlternateScreen);
        Assert.Equal(["", "", ""], Screen(t));
        Assert.Equal((0, 0), Cursor(t));
        t.Feed("vim\r\n\r\n\r\n\r\n");   // el scroll de la alternativa no va al historial
        Assert.Equal(0, t.ScrollbackCount);
        t.Feed($"{Esc}[?1049h");   // dos veces seguidas: no se pierde la principal
        t.Feed($"{Esc}[?1049l");
        Assert.False(t.AlternateScreen);
        Assert.Equal("shell", Row(t, 0));
        Assert.Equal((1, 1), Cursor(t));
        t.Feed($"{Esc}[?47l");   // salir sin haber entrado: nada
        Assert.Equal("shell", Row(t, 0));
        t.Feed($"{Esc}[?47h{Esc}[?1047l");
        Assert.False(t.AlternateScreen);
    }

    [Fact]
    public void h_y_l_sin_interrogacion_se_ignoran()
    {
        var t = Term();
        t.Feed($"{Esc}[25l{Esc}[4h");
        Assert.True(t.CursorVisible);
    }

    [Fact]
    public void Titulo_por_OSC_con_campana_o_ST()
    {
        var t = Term();
        var titles = new List<string>();
        t.TitleChanged += titles.Add;
        t.Feed($"{Esc}]0;pepe@srv: ~\a");
        t.Feed($"{Esc}]2;otro{Esc}\\");
        t.Feed($"{Esc}]1;icono\a{Esc}]8;;http://x\a{Esc}]sin punto y coma\a");
        Assert.Equal(["pepe@srv: ~", "otro"], titles);
        Assert.Equal("otro", t.Title);
        Assert.Equal("", Row(t, 0));   // nada de eso se pinta
    }

    [Fact]
    public void Respuestas_de_posicion_e_identificacion()
    {
        var t = Term();
        var replies = new List<string>();
        t.Reply += replies.Add;
        t.Feed($"{Esc}[3;4H{Esc}[6n{Esc}[5n{Esc}[c");
        Assert.Equal(["\x1b[3;4R", "\x1b[?1;2c"], replies);
    }

    [Fact]
    public void Secuencias_ignoradas_no_rompen_nada()
    {
        var t = Term();
        t.Feed($"{Esc}(B{Esc})0{Esc}*x{Esc}+y{Esc}={Esc}>{Esc}Z{Esc}[8;24;80t{Esc}[?2004h{Esc}[5z");
        t.Feed("ok");
        Assert.Equal("ok", Row(t, 0));
    }

    [Fact]
    public void ESC_dentro_de_una_CSI_empieza_otra_secuencia()
    {
        var t = Term();
        t.Feed($"{Esc}[12{Esc}[2;2Hx");
        Assert.Equal('x', t.CellAt(1, 1).Ch);
    }

    [Fact]
    public void Reset_ESC_c_limpia_la_pantalla()
    {
        var t = Term(2, 4);
        t.Feed($"abc\r\ndef{Esc}c");
        Assert.Equal(["", ""], Screen(t));
        Assert.Equal((0, 0), Cursor(t));
    }

    [Fact]
    public void Redimensionar_conserva_lo_que_cabe_y_quita_la_region()
    {
        var t = Term(3, 4);
        t.Feed($"abcd\r\nefgh\r\nij{Esc}[2;3r{Esc}[?1049h");
        t.Resize(2, 2);
        Assert.False(t.AlternateScreen);   // al cambiar de tamaño se vuelve a la principal
        Assert.Equal((2, 2), (t.Rows, t.Cols));
        t.Resize(4, 6);
        Assert.Equal(["", "", "", ""], Screen(t));   // la alternativa (vacia) fue la que se conservo
        t = Term(3, 4);
        t.Feed("abcd\r\nefgh\r\nij");
        t.Resize(2, 2);
        Assert.Equal(["ab", "ef"], Screen(t));
        Assert.Equal((1, 1), Cursor(t));   // el cursor se mete dentro
        t.Resize(3, 5);
        Assert.Equal(["ab", "ef", ""], Screen(t));
        t.Feed("\r\n\r\n\r\nx");   // la region vuelve a ser la pantalla entera
        Assert.Equal("x", Row(t, 2));
    }

    [Fact]
    public void Texto_de_una_seleccion()
    {
        var t = Term(3, 6);
        t.Feed("uno   \r\ndos x\r\ntres");
        Assert.Equal("no\ndos x\ntr", t.TextBetween((0, 1), (2, 1)));
        Assert.Equal("dos", t.TextBetween((1, 0), (1, 2)));
        Assert.Equal("", t.TextBetween((0, 4), (0, 5)));
    }

    [Fact]
    public void Texto_de_una_seleccion_en_el_historial()
    {
        var t = Term(2, 4);
        t.Feed("old\r\nnew\r\nnow");
        Assert.Equal("old\nnew", t.TextBetween((0, 0), (1, 3), viewOffset: 1));
    }

    [Theory]
    [InlineData(1, true, false, 1)]       // paleta normal
    [InlineData(1, true, true, 9)]        // negrita: el brillante
    [InlineData(1, false, true, 1)]       // el fondo no se abrillanta
    [InlineData(9, true, true, 9)]        // ya brillante
    public void Rgb_de_la_paleta(int index, bool fg, bool bold, int paletteIndex)
    {
        var palette = Enumerable.Range(0, 16).Select(i => ((byte)i, (byte)i, (byte)i)).ToArray();
        Assert.Equal(palette[paletteIndex], TerminalBuffer.Rgb(index, fg, bold, palette));
    }

    [Theory]
    [InlineData(16, 0, 0, 0)]                 // cubo: esquina negra
    [InlineData(231, 255, 255, 255)]          // cubo: esquina blanca
    [InlineData(16 + 36 * 1 + 6 * 2 + 3, 95, 135, 175)]
    [InlineData(232, 8, 8, 8)]                // grises
    [InlineData(255, 238, 238, 238)]
    [InlineData(256 + 0x123456, 0x12, 0x34, 0x56)]   // color directo
    public void Rgb_del_cubo_grises_y_directo(int index, int r, int g, int b)
    {
        var palette = new (byte, byte, byte)[16];
        Assert.Equal(((byte)r, (byte)g, (byte)b), TerminalBuffer.Rgb(index, true, false, palette));
    }
}
