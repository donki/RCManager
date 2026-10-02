using System.Text;
using System.Windows;
using System.Windows.Input;
using SocRcManager.Files;
using SocRcManager.Localization;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>La busqueda del editor, sin ventanas.</summary>
public class TextSearchTests
{
    private const string Text = "uno dos UNO tres uno";

    [Theory]
    [InlineData(0, 0, 0)]     // sin seleccion al principio: la de la posicion 0
    [InlineData(0, 3, 8)]     // con «uno» seleccionado: la siguiente (sin mirar mayusculas)
    [InlineData(8, 3, 17)]
    [InlineData(17, 3, 0)]    // al final vuelve a empezar
    [InlineData(99, 0, 0)]    // una seleccion fuera del texto no rompe
    public void Hacia_delante(int start, int length, int expected) =>
        Assert.Equal(expected, TextSearch.Find(Text, "uno", start, length, backwards: false));

    [Theory]
    [InlineData(17, 3, 8)]    // desde la ultima, la anterior
    [InlineData(8, 3, 0)]
    [InlineData(0, 3, 17)]    // desde la primera, da la vuelta a la ultima
    [InlineData(99, 0, 17)]
    public void Hacia_atras(int start, int length, int expected) =>
        Assert.Equal(expected, TextSearch.Find(Text, "uno", start, length, backwards: true));

    [Fact]
    public void Hacia_atras_encuentra_la_que_empieza_en_cero_con_el_cursor_justo_detras()
    {
        // Fallo que habia: con el cursor en la posicion 1 se saltaba la «a» de la posicion 0 y daba la vuelta.
        Assert.Equal(0, TextSearch.Find("ab a", "a", 1, 0, backwards: true));
        Assert.Equal(3, TextSearch.Find("ab a", "a", 0, 1, backwards: true));
    }

    [Fact]
    public void Lo_que_no_esta_y_lo_vacio()
    {
        Assert.Equal(-1, TextSearch.Find(Text, "cuatro", 0, 0, backwards: false));
        Assert.Equal(-1, TextSearch.Find(Text, "cuatro", 5, 0, backwards: true));
        Assert.Equal(-1, TextSearch.Find(Text, "", 0, 0, backwards: false));
        Assert.Equal(-1, TextSearch.Find("", "x", 0, 0, backwards: true));
    }
}

/// <summary>
/// El editor de ficheros remotos con un servidor en memoria (<see cref="ContentRemote"/>): abre,
/// edita, busca, guarda (sube con la codificacion y los finales de linea con que llego) y pregunta al cerrar.
/// </summary>
public sealed class TextEditorTests : UiTest
{
    private readonly ContentRemote _fs = new();
    private Window _owner = null!;

    private TextEditorWindow Open(byte[] bytes, string path = "/etc/app.conf")
    {
        _fs.Contents[path] = bytes;
        TextEditorWindow? editor = null;
        Ui.Run(() => _owner = Ui.Show(new Window()));
        Ui.RunAsync(async () => editor = await TextEditorWindow.OpenRemoteAsync(_owner, _fs, ContentRemote.Entry(path)));
        Ui.Run(() => Ui.Show(editor!));
        return editor!;
    }

    private TextEditorWindow Open(string text, Encoding? encoding = null) => Open((encoding ?? new UTF8Encoding(false)).GetBytes(text));

    private static string EditorTemp => Path.Combine(Path.GetTempPath(), "sOCRCManager", "editor");

    private static string[] TempFiles() => Directory.Exists(EditorTemp) ? Directory.GetFiles(EditorTemp) : [];

    [Fact]
    public void Abre_UTF8_con_LF_y_lo_enseña_con_su_estado()
    {
        var before = TempFiles();
        var w = Open("linea 1\nlinea 2\n");
        Ui.Run(() =>
        {
            Assert.Equal("linea 1\r\nlinea 2\r\n", w._text.Text);
            Assert.Equal("app.conf", w.Title);
            Assert.False(w.IsDirty);
            Assert.Equal($"{Loc.Format("EditorPosition", 1, 1)}  ·  UTF-8  ·  LF", w._status.Text);
            Assert.Equal(Visibility.Collapsed, w._findBar.Visibility);
            Assert.NotNull(Ui.Find<System.Windows.Controls.TextBlock>(w, t => t.Text == "/etc/app.conf"));
        });
        Assert.Equal(before, TempFiles());   // lo bajado no se queda en el disco
    }

    [Fact]
    public void Latin1_con_CRLF_se_guarda_igual_que_llego()
    {
        var latin = Encoding.Latin1;
        var w = Open("año=1\r\nñu=2\r\n", latin);
        Ui.Run(() => Assert.EndsWith("Latin-1  ·  CRLF", w._status.Text));
        var saved = 0;
        Ui.Run(() =>
        {
            w.Saved += () => saved++;
            w._text.Text += "más=3\r\n";
        });
        Ui.RunAsync(() => w.SaveAsync());
        var (path, bytes) = Assert.Single(_fs.Uploads);
        Assert.Equal("/etc/app.conf", path);
        Assert.Equal(latin.GetBytes("año=1\r\nñu=2\r\nmás=3\r\n"), bytes);
        Assert.Equal(1, saved);
        Ui.Run(() =>
        {
            Assert.False(w.IsDirty);
            Assert.Equal("app.conf", w.Title);
            Assert.Contains(Loc.Format("EditorSaved", ""), w._status.Text);
            Assert.True(w.IsVisible);   // guardar con Ctrl+S no cierra
        });
    }

    [Fact]
    public void UTF8_con_BOM_y_LF_lo_conserva_al_guardar()
    {
        var w = Open([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("é\n")]);
        Ui.Run(() => w._text.Text = "é\r\nö\r\n");
        Ui.RunAsync(() => w.SaveAsync());
        Assert.Equal([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("é\nö\n")], _fs.Uploads.Single().Bytes);
    }

    [Fact]
    public void Un_binario_no_se_abre()
    {
        _fs.Contents["/bin/ls"] = [0x7F, (byte)'E', (byte)'L', (byte)'F', 0, 0, 1];
        Ui.Run(() => _owner = Ui.Show(new Window()));
        Exception? error = null;
        Ui.RunAsync(async () =>
        {
            try { await TextEditorWindow.OpenRemoteAsync(_owner, _fs, ContentRemote.Entry("/bin/ls")); }
            catch (Exception ex) { error = ex; }
        });
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(Loc.Get("EditorNotText"), error!.Message);
    }

    [Fact]
    public void Si_no_se_puede_bajar_el_error_llega()
    {
        _fs.DownloadError = new IOException("sin conexion");
        Ui.Run(() => _owner = Ui.Show(new Window()));
        Exception? error = null;
        Ui.RunAsync(async () =>
        {
            try { await TextEditorWindow.OpenRemoteAsync(_owner, _fs, ContentRemote.Entry("/x")); }
            catch (Exception ex) { error = ex; }
        });
        Assert.Equal("sin conexion", Assert.IsType<IOException>(error).Message);
    }

    [Fact]
    public void Al_escribir_queda_marcado_y_la_posicion_sigue_al_cursor()
    {
        var w = Open("abc\ndef");
        Ui.Run(() =>
        {
            w._text.CaretIndex = w._text.Text.Length;   // final de la segunda linea
            Assert.StartsWith(Loc.Format("EditorPosition", 2, 4), w._status.Text);
            w._text.AppendText("!");
            Assert.True(w.IsDirty);
            Assert.Equal("● app.conf", w.Title);
            Assert.StartsWith("● ", w._status.Text);
        });
    }

    [Fact]
    public void Error_al_guardar_se_ve_en_una_linea_y_sigue_con_cambios()
    {
        var w = Open("x");
        var saved = false;
        Ui.Run(() => { w.Saved += () => saved = true; w._text.Text = "y"; });
        _fs.UploadError = new IOException("Permission denied\r\nno se puede escribir");
        Ui.RunAsync(() => w.SaveAsync());
        Ui.Run(() =>
        {
            Assert.Equal("Permission denied no se puede escribir", w._status.Text);
            Assert.True(w.IsDirty);
        });
        Assert.False(saved);
        Assert.Empty(_fs.Uploads);
    }

    [Fact]
    public void Mientras_sube_dice_guardando_y_el_boton_guarda()
    {
        var w = Open("x");
        _fs.UploadGate = new TaskCompletionSource();
        Ui.Run(() =>
        {
            w._text.Text = "nuevo";
            Ui.Click(Ui.ButtonById(w, "EditorSaveButton")!);
        });
        Ui.WaitUntil(() => w._status.Text == Loc.Get("EditorSaving"));
        _fs.UploadGate.SetResult();
        Ui.WaitUntil(() => !w.IsDirty);
        Assert.Equal("nuevo", Encoding.UTF8.GetString(_fs.Contents["/etc/app.conf"]));
    }

    [Fact]
    public void Buscar_con_la_seleccion_siguiente_anterior_y_lo_que_no_esta()
    {
        var w = Open("Hola mundo\nhola otra vez\nadios");
        Ui.Run(() =>
        {
            w._text.Select(0, 4);
            Ui.Click(Ui.ButtonById(w, "EditorFindButton")!);
            Assert.Equal(Visibility.Visible, w._findBar.Visibility);
            Assert.Equal("Hola", w._find.Text);   // lo seleccionado pasa a la casilla

            Ui.Click(Ui.ButtonById(w, "EditorFindNextButton")!);
            Assert.Equal((12, 4), (w._text.SelectionStart, w._text.SelectionLength));   // «hola» de la segunda linea (\r\n en el TextBox)
            Ui.Click(Ui.ButtonById(w, "EditorFindNextButton")!);
            Assert.Equal(0, w._text.SelectionStart);   // da la vuelta
            Ui.Click(Ui.ButtonById(w, "EditorFindPrevButton")!);
            Assert.Equal(12, w._text.SelectionStart);

            w._find.Text = "nada";
            Ui.Click(Ui.ButtonById(w, "EditorFindNextButton")!);
            Assert.Equal(Loc.Format("EditorNotFound", "nada"), w._status.Text);
            Assert.Equal(12, w._text.SelectionStart);   // la seleccion no se mueve

            w._find.Text = "";
            var status = w._status.Text;
            w.FindNext(backwards: false);   // vacio: no hace nada
            Assert.Equal(status, w._status.Text);

            Ui.Click(Ui.ButtonById(w, "EditorFindCloseButton")!);
            Assert.Equal(Visibility.Collapsed, w._findBar.Visibility);
        });
    }

    [Fact]
    public void Una_seleccion_de_varias_lineas_no_pasa_a_la_casilla()
    {
        var w = Open("uno\ndos");
        Ui.Run(() =>
        {
            w._find.Text = "previo";
            w._text.SelectAll();
            w.ShowFind();
            Assert.Equal("previo", w._find.Text);
        });
    }

    [Fact]
    public void Teclas_de_la_casilla_de_buscar()
    {
        var w = Open("a b a b");
        Ui.Run(() =>
        {
            w.ShowFind();
            w._find.Text = "b";
            Assert.True(w.OnFindKey(Key.Enter, ModifierKeys.None));
            Assert.Equal(2, w._text.SelectionStart);
            Assert.True(w.OnFindKey(Key.Enter, ModifierKeys.None));
            Assert.Equal(6, w._text.SelectionStart);
            Assert.True(w.OnFindKey(Key.Enter, ModifierKeys.Shift));   // Mayus+Intro: hacia atras
            Assert.Equal(2, w._text.SelectionStart);
            Assert.False(w.OnFindKey(Key.A, ModifierKeys.None));
            Assert.True(w.OnFindKey(Key.Escape, ModifierKeys.None));
            Assert.Equal(Visibility.Collapsed, w._findBar.Visibility);

            // Por el evento de teclado de verdad (sin modificadores pulsados): Intro busca, Esc cierra.
            w.ShowFind();
            var enter = C2Keys.Press(w._find, Key.Enter, Keyboard.KeyDownEvent);
            Assert.True(enter.Handled);
            Assert.Equal(6, w._text.SelectionStart);
            Assert.False(C2Keys.Press(w._find, Key.B, Keyboard.KeyDownEvent).Handled);
            Assert.True(C2Keys.Press(w._find, Key.Escape, Keyboard.KeyDownEvent).Handled);
            Assert.Equal(Visibility.Collapsed, w._findBar.Visibility);
        });
    }

    [Fact]
    public void Atajos_con_Ctrl_y_la_letra_se_recuerda()
    {
        AppSettings.Current = new AppSettings { EditorFontSize = 27 };
        var w = Open("x");
        Ui.Run(() =>
        {
            Assert.Equal(27, w._text.FontSize);
            Assert.False(w.OnShortcut(Key.S, ModifierKeys.None));   // sin Ctrl no es un atajo
            Assert.False(w.OnShortcut(Key.Q, ModifierKeys.Control));
            Assert.True(w.OnShortcut(Key.Add, ModifierKeys.Control));
            Assert.True(w.OnShortcut(Key.OemPlus, ModifierKeys.Control));
            Assert.Equal(28, w._text.FontSize);   // nunca mas de 28
            Assert.True(w.OnShortcut(Key.Subtract, ModifierKeys.Control));
            Assert.True(w.OnShortcut(Key.OemMinus, ModifierKeys.Control));
            Assert.Equal(26, w._text.FontSize);
            for (var i = 0; i < 30; i++)
                w.OnShortcut(Key.Subtract, ModifierKeys.Control);
            Assert.Equal(9, w._text.FontSize);   // ni menos de 9
            Assert.True(w.OnShortcut(Key.F, ModifierKeys.Control | ModifierKeys.Shift));
            Assert.Equal(Visibility.Visible, w._findBar.Visibility);
        });
        Assert.Equal(9, AppSettings.Load().EditorFontSize);   // guardado en los ajustes

        Ui.Run(() => w._text.Text = "y");
        Ui.Run(() => Assert.True(w.OnShortcut(Key.S, ModifierKeys.Control)));
        Ui.WaitUntil(() => _fs.Uploads.Count == 1);
        // El evento de verdad sin Ctrl no hace nada.
        Ui.Run(() => Assert.False(C2Keys.Press(w, Key.S, Keyboard.PreviewKeyDownEvent).Handled));
    }

    [Fact]
    public void Sin_ajustes_cargados_la_letra_cambia_igual()
    {
        AppSettings.Current = null;
        var w = Open("x");
        Ui.Run(() =>
        {
            Assert.Equal(13, w._text.FontSize);
            w.SetFontSize(20);
            Assert.Equal(20, w._text.FontSize);
        });
        Assert.False(File.Exists(AppSettings.FilePath));
    }

    [Fact]
    public void Cerrar_sin_cambios_no_pregunta()
    {
        var w = Open("x");
        Ui.Run(w.Close);
        Assert.Empty(Ui.Modals);
        Assert.False(Ui.Run(() => w.IsVisible));
    }

    [Fact]
    public void Cerrar_con_cambios_y_descartar_cierra_sin_subir()
    {
        var w = Open("x");
        Ui.Run(() => w._text.Text = "cambio");
        Ui.Answer<SaveQuestionWindow>(q =>
        {
            Assert.Contains("app.conf", Ui.Find<System.Windows.Controls.TextBlock>(q, t => t.Text.Contains("app.conf"))!.Text);
            Ui.Click(Ui.ButtonById(q, "DiscardButton")!);
        });
        Ui.Run(w.Close);
        Assert.IsType<SaveQuestionWindow>(Assert.Single(Ui.Modals));
        Assert.False(Ui.Run(() => w.IsVisible));
        Assert.Empty(_fs.Uploads);
    }

    [Fact]
    public void Cerrar_con_cambios_y_cancelar_sigue_abierto()
    {
        var w = Open("x");
        Ui.Run(() => w._text.Text = "cambio");
        Ui.Run(w.Close);   // sin respuesta: la pregunta se cancela
        Assert.Single(Ui.Modals);
        Ui.Run(() =>
        {
            Assert.True(w.IsVisible);
            Assert.True(w.IsDirty);
        });
        Assert.Empty(_fs.Uploads);
    }

    [Fact]
    public void Cerrar_con_cambios_y_guardar_sube_y_cierra()
    {
        var w = Open("x");
        var saved = 0;
        Ui.Run(() => { w._text.Text = "guardado al cerrar"; w.Saved += () => saved++; });
        Ui.Answer<SaveQuestionWindow>(q => Ui.Click(Ui.ButtonById(q, "SaveButton")!));
        Ui.Run(w.Close);
        Ui.WaitUntil(() => !w.IsVisible);
        Assert.Equal("guardado al cerrar", Encoding.UTF8.GetString(_fs.Uploads.Single().Bytes));
        Assert.Equal(1, saved);
        Assert.Single(Ui.Modals);   // al cerrarse tras guardar ya no pregunta otra vez
    }

    [Fact]
    public void Cerrar_y_guardar_con_error_no_cierra()
    {
        var w = Open("x");
        _fs.UploadError = new IOException("disco lleno");
        Ui.Run(() => w._text.Text = "y");
        Ui.Answer<SaveQuestionWindow>(q => Ui.Click(Ui.ButtonById(q, "SaveButton")!));
        Ui.Run(w.Close);
        Ui.WaitUntil(() => w._status.Text == "disco lleno");
        Ui.Run(() => Assert.True(w.IsVisible));
    }

    [Fact]
    public void Linea_de_estado()
    {
        Assert.Equal($"● {Loc.Format("EditorPosition", 3, 5)}  ·  UTF-8  ·  CRLF  ·  nota",
            TextEditorWindow.StatusText(true, 2, 4, new UTF8Encoding(true), true, "nota"));
        Assert.Equal($"{Loc.Format("EditorPosition", 1, 1)}  ·  Latin-1  ·  LF",
            TextEditorWindow.StatusText(false, 0, 0, Encoding.Latin1, false, null));
    }
}
