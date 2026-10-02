using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SocRcManager.Files;
using SocRcManager.Localization;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>La ventana de permisos abierta como modal (fuera de la pantalla) y contestada por la prueba.</summary>
public sealed class PermissionsWindowTests : UiTest
{
    private static readonly FileEntry Conf = new("app.conf", "/etc/app.conf", false, 10, null, 0b110_100_100, "pepe", "users");
    private static readonly FileEntry Www = new("www", "/srv/www", true, 0, null, 0b111_101_101, "www-data", "www-data");

    /// <summary>Abre la ventana con esas entradas, deja que la prueba la maneje y devuelve lo que quedo.</summary>
    private (bool? Result, PermissionsWindow Window) Ask(IReadOnlyList<FileEntry> entries, Action<PermissionsWindow>? answer = null)
    {
        Ui.Answer<PermissionsWindow>(w => answer?.Invoke(w));
        return Ui.Run(() =>
        {
            var owner = Ui.Show(new Window());
            var w = new PermissionsWindow(owner, entries);
            return (Dialogs.ShowModal(w), w);
        });
    }

    private static bool[] Boxes(PermissionsWindow w) => w._bits.Select(b => b.IsChecked == true).ToArray();

    private static void Ok(PermissionsWindow w) => Ui.Click(Ui.ButtonById(w, "OkButton")!);

    [Fact]
    public void Un_fichero_enseña_sus_permisos_y_aceptar_sin_tocar_no_pide_nada()
    {
        var (result, w) = Ask([Conf], w =>
        {
            Assert.Equal(Loc.Get("PermsTitle"), w.Title);
            Assert.NotNull(Ui.Find<TextBlock>(w, t => t.Text == "app.conf"));
            Assert.Equal(UnixMode.ToBits(0b110_100_100), Boxes(w));
            Assert.Equal("644", w._octal.Text);
            Assert.Equal(("pepe", "users"), (w._owner.Text, w._group.Text));
            Assert.Equal(Visibility.Collapsed, w._recursive.Visibility);   // un fichero no tiene «dentro»
            Ok(w);
        });
        Assert.True(result);
        Assert.Equal(new PermissionChange(0b110_100_100, false, "pepe", "users", false, false), w.Change);
    }

    [Fact]
    public void Una_casilla_cambia_el_octal_y_el_modo()
    {
        var (result, w) = Ask([Conf], w =>
        {
            w._bits[2].IsChecked = true;   // ejecucion del propietario
            Assert.Equal("744", w._octal.Text);
            w._bits[5].IsChecked = true;   // ejecucion del grupo
            w._bits[3].IsChecked = false;  // sin lectura del grupo
            Assert.Equal("714", w._octal.Text);
            Ok(w);
        });
        Assert.True(result);
        Assert.Equal(0b111_001_100, w.Mode);
        Assert.True(w.Change!.ChangeMode);
        Assert.Equal(0b111_001_100, w.Change.Mode);
        Assert.False(w.Change.ChangeOwner);
    }

    [Fact]
    public void El_octal_cambia_las_casillas_y_lo_incompleto_no_toca_nada()
    {
        Ask([Conf], w =>
        {
            w._octal.Text = "0755";
            Assert.Equal(UnixMode.ToBits(0b111_101_101), Boxes(w));
            Assert.Equal(0b111_101_101, w.Mode);
            Assert.Equal("0755", w._octal.Text);   // lo escrito no se reescribe mientras se teclea

            w._octal.Text = "75";       // a medio escribir
            Assert.Equal(UnixMode.ToBits(0b111_101_101), Boxes(w));
            w._octal.Text = "789";      // 8 y 9 no son octales
            Assert.Equal(0b111_101_101, w.Mode);
            w._octal.Text = " 600 ";
            Assert.Equal(UnixMode.ToBits(0b110_000_000), Boxes(w));
            Assert.Equal(0b110_000_000, w.Mode);
            Ok(w);
        });
    }

    [Fact]
    public void Volver_al_modo_de_partida_no_es_un_cambio()
    {
        var (_, w) = Ask([Conf], w =>
        {
            w._octal.Text = "777";
            w._octal.Text = "644";
            Ok(w);
        });
        Assert.False(w.Change!.ChangeMode);
    }

    [Fact]
    public void Propietario_y_grupo_sin_espacios()
    {
        var (_, w) = Ask([Conf], w =>
        {
            w._owner.Text = "  root ";
            w._group.Text = " wheel";
            Ok(w);
        });
        Assert.Equal(("root", "wheel"), Ui.Run(() => (w.OwnerName, w.GroupName)));
        Assert.Equal(new PermissionChange(0b110_100_100, false, "root", "wheel", true, false), w.Change);
    }

    [Fact]
    public void Varias_entradas_con_una_carpeta_ofrecen_recursivo_y_parten_de_la_primera()
    {
        var (result, w) = Ask([Conf, Www, Conf with { Name = "b.conf", FullPath = "/etc/b.conf" }], w =>
        {
            Assert.NotNull(Ui.Find<TextBlock>(w, t => t.Text == Loc.Format("FilesItems", 3)));
            Assert.Equal(Visibility.Visible, w._recursive.Visibility);
            Assert.Equal("644", w._octal.Text);   // los de la primera
            Assert.Equal("pepe", w._owner.Text);
            w._recursive.IsChecked = true;
            Ok(w);
        });
        Assert.True(result);
        Assert.True(Ui.Run(() => w.Recursive));
        Assert.True(w.Change!.Recursive);
        Assert.False(w.Change.ChangeMode);
    }

    [Fact]
    public void Sin_permisos_conocidos_empieza_vacio_y_solo_cambia_si_se_marca_algo()
    {
        var bare = new FileEntry("x", "/x", false, 0, null);
        var (_, untouched) = Ask([bare], w =>
        {
            Assert.All(Boxes(w), Assert.False);
            Assert.Equal(string.Empty, w._octal.Text);
            Assert.Equal((string.Empty, string.Empty), (w._owner.Text, w._group.Text));
            Assert.Null(w.Mode);
            Ok(w);
        });
        Assert.Equal(new PermissionChange(null, false, "", "", false, false), untouched.Change);

        var (_, marked) = Ask([bare], w =>
        {
            w._bits[0].IsChecked = true;
            Assert.Equal("400", w._octal.Text);
            Ok(w);
        });
        Assert.Equal(0b100_000_000, marked.Change!.Mode);
        Assert.True(marked.Change.ChangeMode);
    }

    [Fact]
    public void Cancelar_no_deja_cambio()
    {
        var (result, w) = Ask([Conf], w => w._octal.Text = "777");   // se cierra sin aceptar
        Assert.False(result);
        Assert.Null(w.Change);
    }

    [Fact]
    public void Escape_cierra_como_cancelar()
    {
        var (result, w) = Ask([Conf], w =>
        {
            Assert.False(C2Keys.Press(w, Key.A, Keyboard.KeyDownEvent).Handled);
            Assert.True(w.IsVisible);   // otra tecla no cierra
            C2Keys.Press(w, Key.Escape, Keyboard.KeyDownEvent);
        });
        Assert.False(result);
        Assert.Null(w.Change);
        Assert.False(Ui.Run(() => w.IsVisible));
    }
}
