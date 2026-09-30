using SocRcManager.Models;
using SocRcManager.Services;

namespace SocRcManager.Tests;

public class ConnectionTests
{
    [Theory]
    [InlineData(ConnectionKind.Rdp, 0, 3389)]
    [InlineData(ConnectionKind.Ssh, 0, 22)]
    [InlineData(ConnectionKind.Sftp, 0, 22)]
    [InlineData(ConnectionKind.Ftp, 0, 21)]
    [InlineData(ConnectionKind.Ftp, 1, 21)]
    [InlineData(ConnectionKind.Ftp, 2, 990)]
    public void DefaultPort_depende_de_la_clase_y_del_FTPS(ConnectionKind kind, int ftps, int expected)
    {
        Assert.Equal(expected, new Connection { Kind = kind, FtpsMode = ftps }.DefaultPort);
    }

    [Theory]
    [InlineData(ConnectionKind.Rdp, false, false)]
    [InlineData(ConnectionKind.Ssh, true, false)]
    [InlineData(ConnectionKind.Sftp, true, true)]
    [InlineData(ConnectionKind.Ftp, false, true)]
    public void IsSsh_e_IsFiles(ConnectionKind kind, bool ssh, bool files)
    {
        var c = new Connection { Kind = kind };
        Assert.Equal(ssh, c.IsSsh);
        Assert.Equal(files, c.IsFiles);
    }

    [Fact]
    public void Caption_omite_el_puerto_por_defecto()
    {
        Assert.Equal("srv", new Connection { Host = "srv", Port = 3389 }.Caption);
        Assert.Equal("srv:3390", new Connection { Host = "srv", Port = 3390 }.Caption);
        Assert.Equal("srv", new Connection { Host = "srv", Kind = ConnectionKind.Ssh, Port = 22 }.Caption);
        Assert.Equal("srv:3389", new Connection { Host = "srv", Kind = ConnectionKind.Ssh, Port = 3389 }.Caption);
    }

    [Fact]
    public void Valores_por_defecto_como_mstsc()
    {
        var c = new Connection();
        Assert.NotEqual(Guid.Empty, c.Id);
        Assert.Equal(ConnectionKind.Rdp, c.Kind);
        Assert.Equal(3389, c.Port);
        Assert.True(c.RdpSmartSizing);
        Assert.Equal(32, c.RdpColorDepth);
        Assert.Equal(2, c.RdpKeyboardMode);
        Assert.Equal(1, c.RdpAuthLevel);
        Assert.True(c.RdpDrives);
        Assert.True(c.RdpClipboard);
        Assert.Equal(2, c.TransferParallel);
        Assert.Equal(30, c.FilesKeepAliveSeconds);
        Assert.True(c.FtpPassive);
        Assert.True(c.FtpUtf8);
        Assert.Null(c.LastConnectedAt);
        Assert.NotEqual(new Connection().Id, c.Id);
    }

    [Fact]
    public void Clone_copia_todo_y_es_independiente()
    {
        var c = new Connection { Name = "A", Host = "h", Port = 2222, Kind = ConnectionKind.Ssh, Notes = "n" };
        var copy = c.Clone();
        Assert.NotSame(c, copy);
        Assert.Equal(c.Id, copy.Id);
        Assert.Equal("A", copy.Name);
        Assert.Equal(2222, copy.Port);
        copy.Name = "B";
        Assert.Equal("A", c.Name);
    }

    [Theory]
    [InlineData("web", true)]      // nombre
    [InlineData("EXAMPLE", true)]  // servidor, sin mayusculas
    [InlineData("admin", true)]    // usuario
    [InlineData("clientes/ac", true)] // carpeta
    [InlineData("backup", true)]   // notas
    [InlineData("nada", false)]
    public void Matches_busca_en_nombre_servidor_usuario_carpeta_y_notas(string filter, bool expected)
    {
        var c = new Connection { Name = "Web 1", Host = "web1.example.org", UserName = "admin", Folder = "Clientes/Acme", Notes = "Hace el backup" };
        Assert.Equal(expected, c.Matches(filter));
    }
}

public class HostAddressTests
{
    [Theory]
    [InlineData("srv", 3389, "srv", 3389)]
    [InlineData("srv:3390", 3389, "srv", 3390)]
    [InlineData("  srv:22  ", 3389, "srv", 22)]
    [InlineData("10.0.0.1:8022", 22, "10.0.0.1", 8022)]
    [InlineData("srv:0", 3389, "srv:0", 3389)]        // puerto 0: no es puerto
    [InlineData("srv:abc", 3389, "srv:abc", 3389)]
    [InlineData(":3390", 3389, ":3390", 3389)]        // sin servidor delante
    [InlineData("", 21, "", 21)]
    [InlineData(null, 21, "", 21)]
    [InlineData("fe80::1", 3389, "fe80::1", 3389)]    // IPv6 sin corchetes: todo es servidor
    [InlineData("2001:db8::10", 22, "2001:db8::10", 22)]
    [InlineData("[fe80::1]", 3389, "[fe80::1]", 3389)]
    [InlineData("[fe80::1]:3390", 3389, "[fe80::1]", 3390)]
    [InlineData("[fe80::1]:", 3389, "[fe80::1]:", 3389)]
    [InlineData("[fe80::1]x", 3389, "[fe80::1]x", 3389)]
    [InlineData("[fe80::1", 3389, "[fe80::1", 3389)]
    public void Split(string? address, int defaultPort, string host, int port)
    {
        Assert.Equal((host, port), HostAddress.Split(address, defaultPort));
    }
}
