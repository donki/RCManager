using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>Lo que se le puede preguntar al escritorio de verdad sin tocar nada (monitores y raton).</summary>
public sealed class DesktopTests
{
    [Fact]
    public void Monitores_de_verdad_uno_principal_y_el_area_de_trabajo_dentro()
    {
        var monitors = new WindowsDesktop().Monitors();
        Assert.NotEmpty(monitors);
        Assert.Single(monitors, m => m.Primary);
        Assert.All(monitors, m =>
        {
            Assert.True(m.Bounds.Width > 0 && m.Bounds.Height > 0);
            Assert.InRange(m.WorkingArea.Left, m.Bounds.Left, m.Bounds.Right);
            Assert.InRange(m.WorkingArea.Bottom, m.Bounds.Top, m.Bounds.Bottom);
        });
    }

    [Fact]
    public void El_raton_esta_en_algun_monitor_y_fuera_de_ellos_no_hay_ventana()
    {
        var desktop = new WindowsDesktop();
        var (x, y) = desktop.CursorPosition();
        Assert.Contains(desktop.Monitors(), m => x >= m.Bounds.Left && x <= m.Bounds.Right && y >= m.Bounds.Top && y <= m.Bounds.Bottom);
        desktop.RootWindowAt(x, y);
        // Fuera de cualquier monitor no hay ventana.
        Assert.Equal(IntPtr.Zero, desktop.RootWindowAt(-100000, -100000));
    }

    [Fact]
    public void Con_una_ventana_sin_enseñar_ni_se_activa_ni_se_enseña_ni_captura_el_raton()
    {
        var desktop = new WindowsDesktop();
        Ui.Run(() =>
        {
            var w = new System.Windows.Window();
            desktop.ForceForeground(w);
            Assert.False(w.IsVisible);
            Assert.False(w.IsActive);
            Assert.False(w.Topmost);

            // Un elemento fuera de cualquier ventana no se puede quedar el raton.
            var loose = new System.Windows.Controls.Border();
            desktop.CaptureMouse(loose);
            Assert.False(loose.IsMouseCaptured);
            w.Close();
        });
    }

    [Fact]
    public void Rectangulos_en_pixeles()
    {
        var r = new PixelRect(-10, 20, 100, 50);
        Assert.Equal((90, 70), (r.Right, r.Bottom));
    }
}
