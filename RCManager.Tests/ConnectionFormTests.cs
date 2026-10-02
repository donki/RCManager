using SocRcManager.Models;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>Las reglas del editor de conexiones sin la ventana (<see cref="ConnectionForm"/>).</summary>
public sealed class ConnectionFormTests
{
    [Theory]
    [InlineData(ConnectionKind.Rdp, 0)]
    [InlineData(ConnectionKind.Ssh, 1)]
    [InlineData(ConnectionKind.Sftp, 2)]
    [InlineData(ConnectionKind.Ftp, 3)]
    public void Tipo_y_posicion_en_la_lista_ida_y_vuelta(ConnectionKind kind, int index)
    {
        Assert.Equal(index, ConnectionForm.KindIndex(kind));
        Assert.Equal(kind, ConnectionForm.KindAt(index));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void Posicion_fuera_de_la_lista_es_RDP(int index) => Assert.Equal(ConnectionKind.Rdp, ConnectionForm.KindAt(index));

    [Theory]
    [InlineData(ConnectionKind.Rdp, 0, 3389)]
    [InlineData(ConnectionKind.Rdp, 2, 3389)]
    [InlineData(ConnectionKind.Ssh, 0, 22)]
    [InlineData(ConnectionKind.Sftp, 2, 22)]
    [InlineData(ConnectionKind.Ftp, 0, 21)]
    [InlineData(ConnectionKind.Ftp, 1, 21)]
    [InlineData(ConnectionKind.Ftp, 2, 990)]
    public void Puerto_por_defecto(ConnectionKind kind, int ftps, int port) => Assert.Equal(port, ConnectionForm.DefaultPort(kind, ftps));

    [Theory]
    [InlineData("", true)]
    [InlineData("3389", true)]
    [InlineData("22", true)]
    [InlineData("21", true)]
    [InlineData("990", true)]
    [InlineData("2222", false)]
    [InlineData(" 22", false)]
    [InlineData("abc", false)]
    public void El_puerto_sigue_al_tipo_si_no_es_uno_propio(string text, bool follows) =>
        Assert.Equal(follows, ConnectionForm.PortFollowsKind(text));

    [Theory]
    [InlineData("2222", 2222)]
    [InlineData(" 8022 ", 8022)]
    [InlineData("1", 1)]
    [InlineData("65535", 65535)]
    [InlineData("0", 22)]
    [InlineData("65536", 22)]
    [InlineData("-5", 22)]
    [InlineData("", 22)]
    [InlineData("veintidos", 22)]
    public void Puerto_escrito_o_el_por_defecto(string text, int expected) => Assert.Equal(expected, ConnectionForm.ParsePort(text, 22));

    [Theory]
    [InlineData("0", 0)]
    [InlineData(" 45 ", 45)]
    [InlineData("-1", 30)]
    [InlineData("x", 30)]
    [InlineData("", 30)]
    public void Mantener_viva_cero_es_nunca_y_lo_raro_30(string text, int expected) => Assert.Equal(expected, ConnectionForm.ParseKeepAlive(text));

    [Theory]
    [InlineData("5", 5)]
    [InlineData("60", 60)]
    [InlineData("4", 20)]
    [InlineData("", 20)]
    [InlineData("1e3", 20)]
    public void Espera_al_menos_5_y_lo_raro_20(string text, int expected) => Assert.Equal(expected, ConnectionForm.ParseTimeout(text));

    [Theory]
    [InlineData(0, 0, true, 0)]
    [InlineData(1024, 768, false, 1)]
    [InlineData(1920, 1080, false, 5)]
    [InlineData(2560, 1440, false, 7)]
    [InlineData(1920, 1080, true, 5)]     // ajustar con tamaño fijo: manda el tamaño
    [InlineData(0, 0, false, 8)]          // ni ajustar ni tamaño: a medida (vacio)
    [InlineData(1700, 950, false, 8)]     // no esta en la lista: a medida
    [InlineData(1024, 800, false, 8)]     // ancho de uno y alto de otro
    public void Posicion_del_tamaño(int w, int h, bool smart, int index) => Assert.Equal(index, ConnectionForm.SizeIndex(w, h, smart));

    [Fact]
    public void A_medida_es_la_ultima_detras_de_los_tamaños()
    {
        Assert.Equal(ConnectionForm.Sizes.Count, ConnectionForm.CustomSizeIndex);
        Assert.Equal((0, 0), ConnectionForm.Sizes[0]);
        Assert.Equal(8, ConnectionForm.Sizes.Count);
    }

    [Theory]
    [InlineData("1700", "950", 1700, 950)]
    [InlineData(" 200 ", "200", 200, 200)]
    public void Tamaño_a_medida_valido(string w, string h, int ew, int eh) => Assert.Equal((ew, eh), ConnectionForm.ParseCustomSize(w, h));

    [Theory]
    [InlineData("199", "800")]
    [InlineData("800", "199")]
    [InlineData("", "800")]
    [InlineData("800", "")]
    [InlineData("ancho", "alto")]
    public void Tamaño_a_medida_malo_es_null(string w, string h) => Assert.Null(ConnectionForm.ParseCustomSize(w, h));

    [Theory]
    [InlineData(15, 0)]
    [InlineData(16, 1)]
    [InlineData(24, 2)]
    [InlineData(32, 3)]
    public void Colores_ida_y_vuelta(int depth, int index)
    {
        Assert.Equal(index, ConnectionForm.ColorIndex(depth));
        Assert.Equal(depth, ConnectionForm.ColorDepthAt(index));
    }

    [Fact]
    public void Colores_raros_son_32()
    {
        Assert.Equal(3, ConnectionForm.ColorIndex(8));
        Assert.Equal(32, ConnectionForm.ColorDepthAt(-1));
    }

    [Fact]
    public void Lo_que_se_ve_con_cada_tipo()
    {
        Assert.Equal(new ConnectionForm.Fields(Ssh: false, Domain: true, Ftp: false, Files: false, Scp: false, RdpTabs: true, Transfers: false),
            ConnectionForm.FieldsFor(ConnectionKind.Rdp));
        Assert.Equal(new ConnectionForm.Fields(Ssh: true, Domain: false, Ftp: false, Files: false, Scp: false, RdpTabs: false, Transfers: false),
            ConnectionForm.FieldsFor(ConnectionKind.Ssh));
        Assert.Equal(new ConnectionForm.Fields(Ssh: true, Domain: false, Ftp: false, Files: true, Scp: true, RdpTabs: false, Transfers: true),
            ConnectionForm.FieldsFor(ConnectionKind.Sftp));
        Assert.Equal(new ConnectionForm.Fields(Ssh: false, Domain: false, Ftp: true, Files: true, Scp: false, RdpTabs: false, Transfers: true),
            ConnectionForm.FieldsFor(ConnectionKind.Ftp));
    }

    [Theory]
    [InlineData(ConnectionKind.Rdp, ConnectionForm.Tab.General, false)]
    [InlineData(ConnectionKind.Rdp, ConnectionForm.Tab.Rdp, false)]
    [InlineData(ConnectionKind.Rdp, ConnectionForm.Tab.Transfers, true)]
    [InlineData(ConnectionKind.Ssh, ConnectionForm.Tab.General, false)]
    [InlineData(ConnectionKind.Ssh, ConnectionForm.Tab.Rdp, true)]
    [InlineData(ConnectionKind.Ssh, ConnectionForm.Tab.Transfers, true)]
    [InlineData(ConnectionKind.Sftp, ConnectionForm.Tab.Transfers, false)]
    [InlineData(ConnectionKind.Sftp, ConnectionForm.Tab.Rdp, true)]
    [InlineData(ConnectionKind.Ftp, ConnectionForm.Tab.Transfers, false)]
    [InlineData(ConnectionKind.Ftp, ConnectionForm.Tab.Rdp, true)]
    public void Vuelve_a_General_si_la_pestaña_ya_no_se_ve(ConnectionKind kind, ConnectionForm.Tab tab, bool back) =>
        Assert.Equal(back, ConnectionForm.BackToGeneral(kind, tab));
}
