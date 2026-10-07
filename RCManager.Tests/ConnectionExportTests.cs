using System.Security.Cryptography;
using SocRcManager.Models;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>El fichero .rcm para pasar conexiones a otro RC Manager: que rama sale, con o sin contraseñas, y leerlo.</summary>
public sealed class ConnectionExportTests
{
    private static Connection Conn(string name, string folder) => new()
    {
        Name = name,
        Folder = folder,
        Host = name.ToLowerInvariant() + ".lan",
        UserName = "ana",
        PasswordProtected = Secrets.Protect("pw-" + name),
        RdpGatewayPasswordProtected = Secrets.Protect("gw-" + name),
        LastConnectedAt = DateTime.Now,
    };

    private static readonly Connection[] All =
    [
        Conn("Dc", "Clientes/Acme"),
        Conn("Fs", "Clientes/Acme/Sub"),
        Conn("Web", "Clientes"),
        Conn("Suelta", ""),
        Conn("Otra", "Clientes/AcmeNo"),
    ];

    private static readonly string[] Empty = ["Clientes/Acme/Vacia", "Casa/Vacia"];

    [Fact]
    public void Una_rama_sale_con_su_nombre_arriba_y_sin_lo_de_fuera()
    {
        var content = ConnectionExport.Select(All, Empty, "Clientes/Acme");
        Assert.Equal([("Dc", "Acme"), ("Fs", "Acme/Sub")], content.Connections.Select(c => (c.Name, c.Folder)));
        Assert.Equal(["Acme/Vacia"], content.Folders);
        // Son copias: el almacen no cambia.
        Assert.Equal("Clientes/Acme", All[0].Folder);

        // De primer nivel, igual pero sin padre que quitar; con barras sobrantes tambien.
        Assert.Equal(["Clientes/Acme", "Clientes/Acme/Sub", "Clientes", "Clientes/AcmeNo"],
            ConnectionExport.Select(All, Empty, "/Clientes/").Connections.Select(c => c.Folder));
    }

    [Fact]
    public void Todo_o_una_rama_vacia()
    {
        var all = ConnectionExport.Select(All, Empty, "");
        Assert.Equal(5, all.Connections.Count);
        Assert.Equal(Empty, all.Folders);

        // Una carpeta sin conexiones dentro sale como carpeta vacia, una sola vez.
        Assert.Equal(["Vacia"], ConnectionExport.Select(All, Empty, "Casa/Vacia").Folders);
        Assert.Equal(["Nueva"], ConnectionExport.Select(All, [], "Nueva").Folders);
    }

    [Fact]
    public void Sin_frase_va_en_claro_y_sin_contraseñas()
    {
        var text = ConnectionExport.Write(ConnectionExport.Select(All, Empty, "Clientes/Acme"), null);
        Assert.False(ConnectionExport.IsEncrypted(text));
        Assert.Contains("dc.lan", text);
        Assert.DoesNotContain("pw-", text);
        Assert.DoesNotContain("dpapi", text);
        Assert.Equal(ConnectionExport.Write(ConnectionExport.Select(All, Empty, "Clientes/Acme"), ""), text);

        var read = ConnectionExport.Read(text, null);
        Assert.Equal(["Dc", "Fs"], read.Connections.Select(c => c.Name));
        Assert.All(read.Connections, c => Assert.Equal((string.Empty, string.Empty), (c.PasswordProtected, c.RdpGatewayPasswordProtected)));
        Assert.All(read.Connections, c => Assert.Null(c.LastConnectedAt));
        Assert.DoesNotContain(read.Connections, c => All.Any(a => a.Id == c.Id));
        Assert.Equal(["Acme/Vacia"], read.Folders);
    }

    [Fact]
    public void Con_frase_va_cifrado_y_con_las_contraseñas()
    {
        var text = ConnectionExport.Write(ConnectionExport.Select(All, Empty, ""), "frase larga");
        Assert.True(ConnectionExport.IsEncrypted(text));
        Assert.DoesNotContain("dc.lan", text);
        Assert.DoesNotContain("pw-", text);

        var read = ConnectionExport.Read(text, "frase larga");
        var dc = read.Connections.Single(c => c.Name == "Dc");
        Assert.Equal(("Clientes/Acme", "dc.lan", "ana"), (dc.Folder, dc.Host, dc.UserName));
        // Protegidas otra vez con DPAPI para quien importa.
        Assert.StartsWith("dpapi", dc.PasswordProtected);
        Assert.Equal(("pw-Dc", "gw-Dc"), (Secrets.Unprotect(dc.PasswordProtected), Secrets.Unprotect(dc.RdpGatewayPasswordProtected)));

        Assert.ThrowsAny<CryptographicException>(() => ConnectionExport.Read(text, "otra frase"));
        Assert.ThrowsAny<CryptographicException>(() => ConnectionExport.Read(text, null));
    }

    [Theory]
    [InlineData("esto no es json")]
    [InlineData("{\"Connections\":[]}")]
    [InlineData("{\"Format\":\"socrcm\",\"Version\":0}")]
    [InlineData("null")]
    public void Lo_que_no_es_un_rcm_se_dice(string text)
    {
        Assert.Throws<InvalidDataException>(() => ConnectionExport.IsEncrypted(text));
        Assert.Throws<InvalidDataException>(() => ConnectionExport.Read(text, null));
    }

    [Fact]
    public void Las_carpetas_se_limpian_al_leer()
    {
        var text = "{\"Format\":\"socrcm\",\"Version\":1,\"Encrypted\":false,\"Data\":{\"Connections\":[{\"Name\":\"A\",\"Host\":\"a\",\"Folder\":\"/X/Y/\"}],\"Folders\":[\"/Z/\",\"\",\"/\"]}}";
        var read = ConnectionExport.Read(text, null);
        Assert.Equal("X/Y", read.Connections.Single().Folder);
        Assert.Equal(["Z"], read.Folders);
    }
}
