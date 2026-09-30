using SocRcManager.Models;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>Ficheros .rdm sinteticos (nunca el import.rdm de la raiz, que puede llevar datos reales).</summary>
public class RdmImportTests
{
    private static RdmImport.Result Import(string connectionsXml)
    {
        using var dir = new TempDir();
        var path = dir.File("prueba.rdm", $"<?xml version=\"1.0\"?><RDMExport><Connections>{connectionsXml}</Connections></RDMExport>");
        return RdmImport.Read(path);
    }

    [Fact]
    public void Carpetas_con_barra_invertida_pasan_a_rutas_con_barra()
    {
        var r = Import("""
            <Connection><ConnectionType>Group</ConnectionType><Group>Clientes\Acme</Group><Name>Acme</Name></Connection>
            <Connection><ConnectionType>Group</ConnectionType><Group> Clientes \ Vacia \ </Group></Connection>
            <Connection><ConnectionType>Group</ConnectionType></Connection>
            """);
        Assert.Equal(["Clientes/Acme", "Clientes/Vacia"], r.Folders);
        Assert.Empty(r.Connections);
        Assert.Equal(0, r.Skipped);
    }

    [Fact]
    public void RDP_con_puerto_en_la_url_usuario_dominio_y_notas()
    {
        var r = Import("""
            <Connection>
              <ConnectionType>RDPConfigured</ConnectionType><Group>Oficina/Servidores</Group><Name> DC01 </Name>
              <Url>dc01.lan:3390</Url><Description> Controlador </Description>
              <RDP><UserName> administrador </UserName><Domain>LAN</Domain></RDP>
            </Connection>
            """);
        var c = Assert.Single(r.Connections);
        Assert.Equal(ConnectionKind.Rdp, c.Kind);
        Assert.Equal("DC01", c.Name);
        Assert.Equal("Oficina/Servidores", c.Folder);
        Assert.Equal("dc01.lan", c.Host);
        Assert.Equal(3390, c.Port);
        Assert.Equal("administrador", c.UserName);
        Assert.Equal("LAN", c.Domain);
        Assert.Equal("Controlador", c.Notes);
        Assert.Empty(c.PasswordProtected);   // las contraseñas no se importan
    }

    [Fact]
    public void RDP_sin_nombre_usa_el_servidor_y_sin_puerto_el_3389()
    {
        var c = Assert.Single(Import("<Connection><ConnectionType>RDPConfigured</ConnectionType><Url>pc5</Url></Connection>").Connections);
        Assert.Equal("pc5", c.Name);
        Assert.Equal(3389, c.Port);
        Assert.Equal("", c.UserName);
        Assert.Equal("", c.Folder);
    }

    [Fact]
    public void RDP_con_IPv6_sin_corchetes_no_se_parte()
    {
        var c = Assert.Single(Import("<Connection><ConnectionType>RDPConfigured</ConnectionType><Url>fe80::1</Url></Connection>").Connections);
        Assert.Equal(("fe80::1", 3389), (c.Host, c.Port));
    }

    [Fact]
    public void SSH_con_puerto_aparte_clave_y_usuario()
    {
        var r = Import("""
            <Connection><ConnectionType>SSHShell</ConnectionType><Name>web</Name>
              <Terminal><Host>web.example.org</Host><HostPort>2222</HostPort><Username>deploy</Username><PrivateKeyFileName>C:\k\id</PrivateKeyFileName></Terminal>
            </Connection>
            <Connection><ConnectionType>SSHShell</ConnectionType><Name>otro</Name>
              <Terminal><Host>otro:2200</Host><HostPort>0</HostPort></Terminal>
            </Connection>
            <Connection><ConnectionType>SSHShell</ConnectionType><Terminal><Host>solo</Host></Terminal></Connection>
            """);
        Assert.Equal(3, r.Connections.Count);
        var web = r.Connections[0];
        Assert.Equal(ConnectionKind.Ssh, web.Kind);
        Assert.Equal(("web.example.org", 2222), (web.Host, web.Port));
        Assert.Equal("deploy", web.UserName);
        Assert.Equal(@"C:\k\id", web.PrivateKeyPath);
        Assert.Equal(("otro", 2200), (r.Connections[1].Host, r.Connections[1].Port));   // HostPort 0: vale el de la direccion
        Assert.Equal(("solo", 22, "solo"), (r.Connections[2].Host, r.Connections[2].Port, r.Connections[2].Name));
    }

    [Fact]
    public void SFTP_y_SCP_pasan_a_conexiones_de_ficheros_por_SSH()
    {
        var r = Import("""
            <Connection><ConnectionType>Sftp</ConnectionType><Name>s</Name>
              <Sftp><Host>files.lan</Host><Username>u</Username><PrivateKeyFileName>k.pem</PrivateKeyFileName><RemotePath>/srv</RemotePath><LocalPath>D:\x</LocalPath></Sftp>
            </Connection>
            <Connection><ConnectionType>SCP</ConnectionType><Name>c</Name><Host>scp.lan:2022</Host></Connection>
            """);
        var s = r.Connections[0];
        Assert.Equal(ConnectionKind.Sftp, s.Kind);
        Assert.Equal(("files.lan", 22), (s.Host, s.Port));
        Assert.Equal("u", s.UserName);
        Assert.Equal("k.pem", s.PrivateKeyPath);
        Assert.False(s.UseScp);
        Assert.Equal("/srv", s.RemotePath);
        Assert.Equal(@"D:\x", s.LocalPath);
        Assert.Equal(0, s.FtpsMode);

        var c = r.Connections[1];
        Assert.Equal(ConnectionKind.Sftp, c.Kind);
        Assert.True(c.UseScp);
        Assert.Equal(("scp.lan", 2022), (c.Host, c.Port));
        Assert.Equal("/", c.RemotePath);   // sin ruta remota: la raiz
    }

    [Fact]
    public void FTP_FTPS_explicito_e_implicito()
    {
        var r = Import("""
            <Connection><ConnectionType>Ftp</ConnectionType><Name>plano</Name><Ftp><Host>ftp.lan</Host><UserName>anon</UserName></Ftp></Connection>
            <Connection><ConnectionType>Ftps</ConnectionType><Name>explicito</Name><Ftp><HostName>ftps.lan</HostName></Ftp></Connection>
            <Connection><ConnectionType>FTP</ConnectionType><Name>implicito</Name><Ftp><Host>imp.lan</Host><Protocol>FTPS Implicit</Protocol></Ftp></Connection>
            <Connection><ConnectionType>FtpNative</ConnectionType><Name>tls990</Name><FtpNative><Host>t.lan</Host><Port>990</Port><FtpType>TLS</FtpType></FtpNative></Connection>
            <Connection><ConnectionType>FTPNative</ConnectionType><Name>sftp-por-protocolo</Name><Ftp><Host>p.lan</Host><Protocol>SFTP</Protocol><User>x</User><PrivateKeyPath>kk</PrivateKeyPath></Ftp></Connection>
            """);
        Assert.Equal(5, r.Connections.Count);
        var (plano, exp, imp, tls, sftp) = (r.Connections[0], r.Connections[1], r.Connections[2], r.Connections[3], r.Connections[4]);

        Assert.Equal((ConnectionKind.Ftp, 21, 0, "anon"), (plano.Kind, plano.Port, plano.FtpsMode, plano.UserName));
        Assert.Empty(plano.PrivateKeyPath);
        Assert.Equal(("ftps.lan", 21, 1), (exp.Host, exp.Port, exp.FtpsMode));
        Assert.Equal((990, 2), (imp.Port, imp.FtpsMode));
        Assert.Equal((990, 2), (tls.Port, tls.FtpsMode));   // TLS en el 990: implicito
        Assert.Equal((ConnectionKind.Sftp, 22, "x", "kk"), (sftp.Kind, sftp.Port, sftp.UserName, sftp.PrivateKeyPath));
    }

    [Fact]
    public void Campos_fuera_de_la_caja_del_tipo_y_url_con_puerto()
    {
        var c = Assert.Single(Import("""
            <Connection><ConnectionType>FTPS</ConnectionType><Name>n</Name><Url>ftp.lan:2121</Url><Username>u</Username><InitialRemoteDirectory>/pub</InitialRemoteDirectory></Connection>
            """).Connections);
        Assert.Equal(("ftp.lan", 2121, 1), (c.Host, c.Port, c.FtpsMode));
        Assert.Equal("u", c.UserName);
        Assert.Equal("/pub", c.RemotePath);
    }

    [Fact]
    public void Tipos_desconocidos_se_cuentan_y_sin_servidor_se_descartan()
    {
        var r = Import("""
            <Connection><ConnectionType>VNC</ConnectionType><Name>v</Name></Connection>
            <Connection><ConnectionType>WebBrowser</ConnectionType></Connection>
            <Connection><Name>sin tipo</Name></Connection>
            <Connection><ConnectionType>RDPConfigured</ConnectionType><Name>sin servidor</Name></Connection>
            """);
        Assert.Equal(3, r.Skipped);
        Assert.Empty(r.Connections);
    }

    [Fact]
    public void Fichero_sin_conexiones_o_con_otra_raiz()
    {
        using var dir = new TempDir();
        Assert.Empty(RdmImport.Read(dir.File("a.rdm", "<RDMExport/>")).Connections);
        Assert.Empty(RdmImport.Read(dir.File("b.rdm", "<Otra><Connections/></Otra>")).Folders);
    }

    [Fact]
    public void XML_roto_lanza()
    {
        using var dir = new TempDir();
        Assert.ThrowsAny<System.Xml.XmlException>(() => RdmImport.Read(dir.File("roto.rdm", "<RDMExport><Connections>")));
    }
}

public class RdpFileImportTests
{
    private static Connection Import(string content, string name = "Oficina.rdp")
    {
        using var dir = new TempDir();
        return RdpFileImport.Read(dir.File(name, content));
    }

    [Fact]
    public void Nombre_del_fichero_servidor_puerto_y_usuario()
    {
        var c = Import("""
            full address:s:srv.lan:3390
            username:s:LAN\pepe
            screen mode id:i:2
            """);
        Assert.Equal("Oficina", c.Name);
        Assert.Equal(ConnectionKind.Rdp, c.Kind);
        Assert.Equal(("srv.lan", 3390), (c.Host, c.Port));
        Assert.Equal(("LAN", "pepe"), (c.Domain, c.UserName));   // DOMINIO\usuario se separa
    }

    [Fact]
    public void Dominio_explicito_no_se_toca_y_server_port_como_respaldo()
    {
        var c = Import("""
            full address:s:srv
            server port:i:4000
            username:s:a\b
            domain:s:OTRO
            """);
        Assert.Equal(("srv", 4000), (c.Host, c.Port));
        Assert.Equal(("OTRO", @"a\b"), (c.Domain, c.UserName));
    }

    [Fact]
    public void IPv6_con_y_sin_puerto()
    {
        var withPort = Import("full address:s:[fe80::1]:3390");
        Assert.Equal(("[fe80::1]", 3390), (withPort.Host, withPort.Port));
        var bare = Import("full address:s:fe80::1");
        Assert.Equal(("fe80::1", 3389), (bare.Host, bare.Port));
    }

    [Fact]
    public void Fichero_vacio_da_los_valores_de_mstsc()
    {
        var c = Import("");
        Assert.Equal("", c.Host);
        Assert.Equal(3389, c.Port);
        Assert.False(c.RdpSmartSizing);   // sin nada: tamaño fijo, como mstsc
        Assert.Equal(32, c.RdpColorDepth);
        Assert.True(c.RdpConnectionBar);
        Assert.Equal(0, c.RdpAudioMode);
        Assert.Equal(2, c.RdpKeyboardMode);
        Assert.True(c.RdpPrinters);
        Assert.True(c.RdpClipboard);
        Assert.True(c.RdpSmartCards);
        Assert.False(c.RdpPorts);
        Assert.False(c.RdpDevices);
        Assert.False(c.RdpDrives);
        Assert.True(c.RdpWallpaper);
        Assert.True(c.RdpFontSmoothing);
        Assert.True(c.RdpWindowDrag);
        Assert.True(c.RdpBitmapCache);
        Assert.True(c.RdpAutoReconnect);
        Assert.Equal(1, c.RdpAuthLevel);
        Assert.False(c.RdpAdminSession);
        Assert.Equal(0, c.RdpGatewayMode);
        Assert.True(c.RdpGatewaySameCredentials);
        Assert.Equal("", c.Notes);
    }

    [Fact]
    public void Todas_las_opciones_de_mstsc()
    {
        var c = Import("""
            full address:s:srv
            screen mode id:i:1
            desktopwidth:i:1920
            desktopheight:i:1080
            session bpp:i:16
            use multimon:i:1
            displayconnectionbar:i:0
            audiomode:i:2
            audiocapturemode:i:1
            keyboardhook:i:0
            redirectprinters:i:0
            redirectclipboard:i:0
            redirectsmartcards:i:0
            redirectcomports:i:1
            devicestoredirect:s:*
            drivestoredirect:s:C:;
            disable wallpaper:i:1
            allow font smoothing:i:0
            allow desktop composition:i:0
            disable full window drag:i:1
            disable menu anims:i:1
            disable themes:i:1
            bitmapcachepersistenable:i:0
            autoreconnection enabled:i:0
            authentication level:i:2
            administrative session:i:1
            gatewayhostname:s:gw.example.org
            gatewayusagemethod:i:2
            promptcredentialonce:i:0
            alternate full address:s:srv-alt
            password 51:b:01000000D08C9DDF
            """);
        Assert.False(c.RdpSmartSizing);
        Assert.Equal((1920, 1080), (c.RdpWidth, c.RdpHeight));
        Assert.Equal(16, c.RdpColorDepth);
        Assert.True(c.RdpMultiMonitor);
        Assert.False(c.RdpConnectionBar);
        Assert.Equal(2, c.RdpAudioMode);
        Assert.True(c.RdpAudioCapture);
        Assert.Equal(0, c.RdpKeyboardMode);
        Assert.False(c.RdpPrinters);
        Assert.False(c.RdpClipboard);
        Assert.False(c.RdpSmartCards);
        Assert.True(c.RdpPorts);
        Assert.True(c.RdpDevices);
        Assert.True(c.RdpDrives);
        Assert.False(c.RdpWallpaper);
        Assert.False(c.RdpFontSmoothing);
        Assert.False(c.RdpDesktopComposition);
        Assert.False(c.RdpWindowDrag);
        Assert.False(c.RdpMenuAnimation);
        Assert.False(c.RdpVisualStyles);
        Assert.False(c.RdpBitmapCache);
        Assert.False(c.RdpAutoReconnect);
        Assert.Equal(2, c.RdpAuthLevel);
        Assert.True(c.RdpAdminSession);
        Assert.Equal("gw.example.org", c.RdpGatewayHost);
        Assert.Equal(2, c.RdpGatewayMode);
        Assert.False(c.RdpGatewaySameCredentials);
        Assert.Equal("alternate full address: srv-alt", c.Notes);
        Assert.Empty(c.PasswordProtected);   // la contraseña DPAPI de mstsc no se lee
    }

    [Theory]
    [InlineData("smart sizing:i:1", true)]
    [InlineData("dynamic resolution:i:1", true)]
    [InlineData("screen mode id:i:2", true)]
    [InlineData("screen mode id:i:1", false)]
    public void Ajustar_a_la_pestaña(string line, bool smart)
    {
        var c = Import("desktopwidth:i:800\n" + line);
        Assert.Equal(smart, c.RdpSmartSizing);
        Assert.Equal(smart ? 0 : 800, c.RdpWidth);   // con ajuste no se guarda un tamaño fijo
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 1)]   // detectar: como «siempre»
    public void Modo_de_la_puerta_de_enlace(int usage, int mode)
    {
        Assert.Equal(mode, Import($"gatewayhostname:s:gw\ngatewayusagemethod:i:{usage}").RdpGatewayMode);
    }

    [Fact]
    public void Sin_puerta_de_enlace_el_modo_es_no_usar()
    {
        Assert.Equal(0, Import("gatewayusagemethod:i:1").RdpGatewayMode);
    }

    [Fact]
    public void Valores_fuera_de_rango_se_recortan_y_basura_se_ignora()
    {
        var c = Import("""
            session bpp:i:8
            audiomode:i:9
            keyboardhook:i:-3
            authentication level:i:7
            sin dos puntos
            :s:sin clave
            clave:sin segundo
            redirectprinters:i:abc
            alternate full address:s:SRV
            full address:s:srv
            """);
        Assert.Equal(32, c.RdpColorDepth);
        Assert.Equal(2, c.RdpAudioMode);
        Assert.Equal(0, c.RdpKeyboardMode);
        Assert.Equal(2, c.RdpAuthLevel);
        Assert.True(c.RdpPrinters);   // entero ilegible: el valor por defecto
        Assert.Equal("", c.Notes);    // la alternativa es la misma direccion (sin mayusculas)
    }

    [Fact]
    public void Claves_sin_distinguir_mayusculas()
    {
        var c = Import("Full Address:s:SRV\nUserName:s:Ana");
        Assert.Equal(("SRV", "Ana"), (c.Host, c.UserName));
    }
}
