using System.IO;
using System.Windows;
using SocRcManager.Models;

namespace SocRcManager.Sessions;

/// <summary>
/// Una pestaña SSH: SSH.NET (MIT) abre un shell interactivo y <see cref="TerminalControl"/> lo
/// pinta y le manda las teclas.
/// </summary>
/// <remarks>
/// <para>Se entra con contraseña o con clave privada (fichero OpenSSH o PEM). La contraseña
/// guardada se descifra solo para abrir la sesion y no se conserva en memoria mas alla.</para>
///
/// <para>La conexion se hace fuera del hilo de la interfaz (<see cref="SshShell"/>); lo que llega
/// del servidor se vuelca al terminal en el despachador, en trozos, para no bloquear el pintado con
/// salidas grandes.</para>
/// </remarks>
public sealed class SshSession : ISession
{
    private readonly Connection _connection;
    private readonly Func<Connection, string, int, int, Task<ISshShell>> _open;
    private readonly TerminalControl _terminal = new();
    private ISshShell? _client;
    private Stream? _shell;
    private CancellationTokenSource? _reader;

    public SshSession(Connection connection) : this(connection, SshShell.ConnectAsync)
    {
    }

    /// <param name="open">Abre el shell (conexion, usuario y contraseña, columnas, filas).</param>
    internal SshSession(Connection connection, Func<Connection, string, int, int, Task<ISshShell>> open)
    {
        _connection = connection;
        _open = open;
        View = _terminal;
        _terminal.FontSize = Math.Clamp(connection.FontSize, 9, 28);
        _terminal.Input += Send;
        _terminal.TitleChanged += title => TitleChanged?.Invoke(title);
    }

    /// <summary>El terminal de la pestaña.</summary>
    internal TerminalControl Terminal => _terminal;

    /// <summary>La lectura del shell en marcha (las pruebas esperan a que acabe).</summary>
    internal Task? ReadLoop { get; private set; }

    public FrameworkElement View { get; }

    public string Title => _connection.Name;

    public event Action<string>? TitleChanged;
    public event Action<string?>? Ended;

    private void Send(byte[] bytes)
    {
        try { _shell?.Write(bytes); _shell?.Flush(); }
        catch (Exception) { /* la sesion se esta cerrando */ }
    }

    public async Task ConnectAsync(string password)
    {
        // El tamaño del terminal en este momento; si la pestaña cambia despues se avisa al servidor.
        var client = await _open(_connection, password, _terminal.Cols, _terminal.Rows);
        _client = client;
        var shell = client.Stream;
        _shell = shell;
        _terminal.Resized += (cols, rows) => SshShell.Resize(shell, cols, rows);

        _reader = new CancellationTokenSource();
        var token = _reader.Token;
        ReadLoop = Task.Run(() => ReadLoopAsync(shell, token));
        _terminal.Focus();
    }

    private async Task ReadLoopAsync(Stream shell, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!token.IsCancellationRequested)
            {
                var n = await shell.ReadAsync(buffer, token);
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

    public void Focus() => _terminal.Focus();

    public bool HasNativeFullScreen => false;

    public void LeaveFullScreen()
    {
    }

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
        try { _client?.Close(); } catch (Exception) { }
        _shell = null;
        _client = null;
    }
}
