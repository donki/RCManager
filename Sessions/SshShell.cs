using System.IO;
using SocRcManager.Models;
using Renci.SshNet;

namespace SocRcManager.Sessions;

/// <summary>
/// Un shell interactivo abierto: el flujo de bytes en los dos sentidos y el cliente que lo sostiene.
/// El de verdad es SSH.NET (<see cref="SshShell.ConnectAsync"/>); las pruebas ponen uno de mentira.
/// </summary>
internal interface ISshShell
{
    /// <summary>Lo que llega del servidor (lectura) y las teclas (escritura).</summary>
    Stream Stream { get; }

    /// <summary>Cierra la conexion (el flujo ya se ha cerrado antes).</summary>
    void Close();
}

/// <summary>El shell de SSH.NET (MIT): autenticacion, conexion y redimensionado del terminal.</summary>
internal sealed class SshShell(SshClient client, ShellStream shell) : ISshShell
{
    public Stream Stream => shell;

    public void Close()
    {
        client.Disconnect();
        client.Dispose();
    }

    /// <summary>Conecta y abre un shell xterm de <paramref name="cols"/> x <paramref name="rows"/>.</summary>
    public static async Task<ISshShell> ConnectAsync(Connection connection, string password, int cols, int rows)
    {
        // Con la clave privada y/o la contraseña, igual que el explorador de ficheros (SshAuth).
        var info = Files.SshAuth.Build(connection, password);
        info.Timeout = TimeSpan.FromSeconds(20);
        var client = new SshClient(info);
        try
        {
            await Task.Run(() => client.Connect());
            return new SshShell(client, client.CreateShellStream("xterm-256color", (uint)cols, (uint)rows, 0, 0, 64 * 1024));
        }
        catch (Exception)
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Avisa al servidor de que el terminal ha cambiado de tamaño (para que vim, htop o less se
    /// adapten). SSH.NET no lo expone en <see cref="ShellStream"/>, pero el canal de debajo si
    /// sabe mandar la peticion <c>window-change</c>: se le llama por reflexion, y si algun dia
    /// cambia por dentro, simplemente no se redimensiona.
    /// </summary>
    public static void Resize(Stream shell, int cols, int rows)
    {
        try
        {
            var channelField = shell.GetType().GetField("_channel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var channel = channelField?.GetValue(shell);
            var method = channel?.GetType().GetMethod("SendWindowChangeRequest");
            method?.Invoke(channel, [(uint)cols, (uint)rows, 0u, 0u]);
        }
        catch (Exception)
        {
            // Sin redimensionado remoto: el shell sigue con el tamaño con el que se abrio.
        }
    }
}
