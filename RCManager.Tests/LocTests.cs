using System.Net.Sockets;
using System.Text.RegularExpressions;
using SocRcManager.Localization;
using SocRcManager.Models;
using SocRcManager.Services;

namespace SocRcManager.Tests;

public sealed class LocTests : IDisposable
{
    private readonly string _original = Loc.Language;

    public void Dispose() => Lang.Set(_original);

    private static Dictionary<string, string> Table(string name) => Priv.Static<Dictionary<string, string>>(typeof(Loc), name);

    [Fact]
    public void Las_claves_en_español_e_ingles_son_las_mismas()
    {
        var en = Table("English").Keys.ToHashSet();
        var es = Table("Spanish").Keys.ToHashSet();
        Assert.Empty(en.Except(es));   // en ingles y no en español
        Assert.Empty(es.Except(en));   // en español y no en ingles
        Assert.True(en.Count > 100);
    }

    [Fact]
    public void Ningun_texto_vacio_y_los_huecos_coinciden()
    {
        var en = Table("English");
        var es = Table("Spanish");
        foreach (var (key, text) in en)
        {
            Assert.False(string.IsNullOrWhiteSpace(text), $"en:{key} vacio");
            Assert.False(string.IsNullOrWhiteSpace(es[key]), $"es:{key} vacio");
            var holesEn = Regex.Matches(text, @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).Distinct().Order();
            var holesEs = Regex.Matches(es[key], @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).Distinct().Order();
            Assert.True(holesEn.SequenceEqual(holesEs), $"{key}: huecos distintos ({string.Join(",", holesEn)} / {string.Join(",", holesEs)})");
        }
    }

    [Fact]
    public void Todas_las_claves_con_huecos_se_pueden_formatear()
    {
        foreach (var lang in new[] { "es", "en" })
        {
            Lang.Set(lang);
            foreach (var key in Table("English").Keys)
                Assert.NotNull(Loc.Format(key, "a", "b", "c", "d"));
        }
    }

    [Fact]
    public void Toggle_cambia_de_idioma_y_avisa()
    {
        Lang.Set("es");
        var calls = 0;
        void OnChanged() => calls++;
        Loc.LanguageChanged += OnChanged;
        try
        {
            Loc.Toggle();
            Assert.Equal("en", Loc.Language);
            Assert.Equal("Close", Loc.Get("Close"));
            Loc.Toggle();
            Assert.Equal("es", Loc.Language);
            Assert.Equal(2, calls);
        }
        finally
        {
            Loc.LanguageChanged -= OnChanged;
        }
    }

    [Fact]
    public void Clave_desconocida_da_vacio()
    {
        Assert.Equal("", Loc.Get("NoExisteEstaClave"));
        Assert.Equal("", Loc.Format("NoExisteEstaClave", 1));
    }

    [Fact]
    public void Extension_de_marcado_devuelve_el_texto()
    {
        Lang.Set("en");
        Assert.Equal("Close", new TExtension("Close").ProvideValue(null!));
        Assert.Equal("", new TExtension().ProvideValue(null!));
    }
}

public sealed class ConnectionErrorsTests : IDisposable
{
    private readonly string _original = Loc.Language;
    private readonly Connection _c = new() { Host = "srv.lan", Port = 2222, UserName = "pepe", Kind = ConnectionKind.Ssh };

    public ConnectionErrorsTests() => Lang.Set("en");

    public void Dispose() => Lang.Set(_original);

    [Theory]
    [InlineData(SocketError.HostNotFound, "ErrHostNotFound")]
    [InlineData(SocketError.NoData, "ErrHostNotFound")]
    [InlineData(SocketError.TryAgain, "ErrHostNotFound")]
    [InlineData(SocketError.ConnectionRefused, "ErrRefused")]
    [InlineData(SocketError.TimedOut, "ErrTimeout")]
    [InlineData(SocketError.HostUnreachable, "ErrUnreachable")]
    [InlineData(SocketError.NetworkUnreachable, "ErrUnreachable")]
    [InlineData(SocketError.HostDown, "ErrUnreachable")]
    [InlineData(SocketError.ConnectionReset, "ErrReset")]
    [InlineData(SocketError.ConnectionAborted, "ErrReset")]
    public void Errores_de_socket(SocketError code, string key)
    {
        var expected = Loc.Format(key, _c.Host, _c.Port);
        Assert.Equal(expected, ConnectionErrors.Describe(new SocketException((int)code), _c));
    }

    [Fact]
    public void Otro_error_de_socket_lleva_el_mensaje()
    {
        var s = new SocketException((int)SocketError.AccessDenied);
        Assert.Equal(Loc.Format("ErrNetwork", s.Message), ConnectionErrors.Describe(s, _c));
    }

    [Fact]
    public void La_causa_se_busca_dentro_de_las_envolturas()
    {
        var ex = new InvalidOperationException("fuera", new AggregateException(new SocketException((int)SocketError.ConnectionRefused)));
        var text = ConnectionErrors.Describe(ex, _c);
        Assert.Equal(Loc.Format("ErrRefused", "srv.lan", 2222), text);
        Assert.Contains("srv.lan", text);
    }

    [Fact]
    public void Autenticacion_SSH_y_FTP()
    {
        Assert.Equal(Loc.Format("ErrAuth", "pepe"), ConnectionErrors.Describe(new Renci.SshNet.Common.SshAuthenticationException("no"), _c));
        Assert.Equal(Loc.Format("ErrAuth", "pepe"), ConnectionErrors.Describe(new FluentFTP.Exceptions.FtpAuthenticationException("530", "Login incorrect"), _c));
    }

    [Fact]
    public void Tiempo_agotado()
    {
        var expected = Loc.Format("ErrTimeout", "srv.lan", 2222);
        Assert.Equal(expected, ConnectionErrors.Describe(new Renci.SshNet.Common.SshOperationTimeoutException("t"), _c));
        Assert.Equal(expected, ConnectionErrors.Describe(new TimeoutException(), _c));
    }

    [Fact]
    public void SSH_TLS_y_FTP()
    {
        Assert.Equal(Loc.Format("ErrSsh", "cerrada"), ConnectionErrors.Describe(new Renci.SshNet.Common.SshConnectionException("cerrada"), _c));
        Assert.Equal(Loc.Format("ErrTls", "cert"), ConnectionErrors.Describe(new System.Security.Authentication.AuthenticationException("cert"), _c));
        Assert.Equal(Loc.Get("ErrTlsNotOffered"), ConnectionErrors.Describe(new FluentFTP.Exceptions.FtpSecurityNotAvailableException(), _c));
        var cmd = new FluentFTP.Exceptions.FtpCommandException("550", "No such file");
        Assert.Equal(Loc.Format("ErrServer", "550", cmd.Message), ConnectionErrors.Describe(cmd, _c));
        Assert.Equal(Loc.Get("ErrReset"), ConnectionErrors.Describe(new FluentFTP.Exceptions.FtpMissingSocketException(new IOException()), _c));
    }

    [Fact]
    public void Lo_desconocido_da_el_mensaje_en_una_linea()
    {
        Assert.Equal("uno dos", ConnectionErrors.Describe(new Exception("uno\r\ndos"), _c));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RDP_cierre_normal_no_dice_nada(int reason)
    {
        Assert.Null(ConnectionErrors.DescribeRdpDisconnect(reason));
    }

    [Theory]
    [InlineData(260, "RdpDnsError")]
    [InlineData(516, "RdpNoConnection")]
    [InlineData(2308, "RdpSocketClosed")]
    [InlineData(2825, "RdpAuthFailed")]
    public void RDP_motivos_conocidos(int reason, string key)
    {
        Assert.Equal(Loc.Get(key), ConnectionErrors.DescribeRdpDisconnect(reason));
    }

    [Fact]
    public void RDP_motivo_desconocido_lleva_el_codigo()
    {
        Assert.Contains("1234", ConnectionErrors.DescribeRdpDisconnect(1234));
    }
}
