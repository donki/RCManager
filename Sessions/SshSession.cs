using System.IO;
using System.Windows;
using System.Windows.Controls;
using SocRcManager.Models;
using SocRcManager.Services;
using Renci.SshNet;

namespace SocRcManager.Sessions;

/// <summary>
/// Una pestaña SSH: SSH.NET (MIT) abre un shell interactivo y <see cref="TerminalControl"/> lo
/// pinta y le manda las teclas.
/// </summary>
/// <remarks>
/// <para>Se entra con contraseña o con clave privada (fichero OpenSSH o PEM). La contraseña
/// guardada se descifra solo para abrir la sesion y no se conserva en memoria mas alla.</para>
///
/// <para>La conexion se hace fuera del hilo de la interfaz; lo que llega del servidor se vuelca
/// al terminal en el despachador, en trozos, para no bloquear el pintado con salidas grandes.</para>
/// </remarks>
public sealed class SshSession : ISession
{
    private readonly Connection _connection;
    private readonly TerminalControl _terminal = new();
    private SshClient? _client;
    private ShellStream? _shell;
    private CancellationTokenSource? _reader;

    public SshSession(Connection connection)
    {
        _connection = connection;
        View = _terminal;
        _terminal.FontSize = Math.Clamp(connection.FontSize, 9, 28);
        _terminal.Input += bytes =>
        {
            try { _shell?.Write(bytes); _shell?.Flush(); }
            catch (Exception) { /* la sesion se esta cerrando */ }
        };
        _terminal.TitleChanged += title => TitleChanged?.Invoke(title);
    }

    public FrameworkElement View { get; }

    public string Title => _connection.Name;

    public event Action<string>? TitleChanged;
    public event Action<string?>? Ended;

    public async Task ConnectAsync(string password)
    {
        var host = _connection.Host;
        var port = _connection.Port;
        var user = _connection.UserName;

        var methods = new List<AuthenticationMethod>();
        if (_connection.PrivateKeyPath.Length > 0 && File.Exists(_connection.PrivateKeyPath))
        {
            var key = password.Length > 0 ? new PrivateKeyFile(_connection.PrivateKeyPath, password) : new PrivateKeyFile(_connection.PrivateKeyPath);
            methods.Add(new PrivateKeyAuthenticationMethod(user, key));
        }
        if (password.Length > 0)
        {
            methods.Add(new PasswordAuthenticationMethod(user, password));
            // Servidores que piden la contraseña por teclado (keyboard-interactive) en vez de por
            // el metodo «password»: se contesta con la misma.
            var kbd = new KeyboardInteractiveAuthenticationMethod(user);
            kbd.AuthenticationPrompt += (_, e) =>
            {
                foreach (var prompt in e.Prompts)
                    prompt.Response = password;
            };
            methods.Add(kbd);
        }
        if (methods.Count == 0)
            throw new InvalidOperationException(Localization.Loc.Get("SshNoCredentials"));

        var info = new ConnectionInfo(host, port, user, methods.ToArray()) { Timeout = TimeSpan.FromSeconds(20) };
        var client = new SshClient(info);

        await Task.Run(() => client.Connect());
        _client = client;

        // El tamaño del terminal en este momento; si la pestaña cambia despues se avisa al servidor.
        var shell = client.CreateShellStream("xterm-256color", (uint)_terminal.Cols, (uint)_terminal.Rows, 0, 0, 64 * 1024);
        _shell = shell;
        _terminal.Resized += (cols, rows) => ResizeShell(shell, cols, rows);

        _reader = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(shell, _reader.Token));
        _terminal.Focus();
    }

    private async Task ReadLoopAsync(ShellStream shell, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!token.IsCancellationRequested)
            {
                var n = await shell.ReadAsync(buffer, 0, buffer.Length, token);
                if (n <= 0)
                    break;
                var chunk = new byte[n];
                Array.Copy(buffer, chunk, n);
                await _terminal.Dispatcher.InvokeAsync(() => _terminal.Feed(chunk, chunk.Length));
            }
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            await _terminal.Dispatcher.InvokeAsync(() => Ended?.Invoke(ex.Message));
            return;
        }

        await _terminal.Dispatcher.InvokeAsync(() => Ended?.Invoke(null));
    }

    /// <summary>
    /// Avisa al servidor de que el terminal ha cambiado de tamaño (para que vim, htop o less se
    /// adapten). SSH.NET no lo expone en <see cref="ShellStream"/>, pero el canal de debajo si
    /// sabe mandar la peticion <c>window-change</c>: se le llama por reflexion, y si algun dia
    /// cambia por dentro, simplemente no se redimensiona.
    /// </summary>
    private static void ResizeShell(ShellStream shell, int cols, int rows)
    {
        try
        {
            var channelField = typeof(ShellStream).GetField("_channel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var channel = channelField?.GetValue(shell);
            var method = channel?.GetType().GetMethod("SendWindowChangeRequest");
            method?.Invoke(channel, [(uint)cols, (uint)rows, 0u, 0u]);
        }
        catch (Exception)
        {
            // Sin redimensionado remoto: el shell sigue con el tamaño con el que se abrio.
        }
    }

    public void Focus() => _terminal.Focus();

    public bool HasNativeFullScreen => false;

    public void EnterFullScreen(int screen)
    {
    }

    public bool CanZoom => true;

    public string Zoom(int steps)
    {
        _terminal.FontSize = Math.Clamp(_terminal.FontSize + steps, 9, 28);
        _connection.FontSize = _terminal.FontSize;
        return $"{_terminal.FontSize:0} pt";
    }

    public event Action? LeftFullScreen
    {
        add { }
        remove { }
    }

    public void Disconnect()
    {
        _reader?.Cancel();
        try { _shell?.Dispose(); } catch (Exception) { }
        try { _client?.Disconnect(); _client?.Dispose(); } catch (Exception) { }
        _shell = null;
        _client = null;
    }
}
