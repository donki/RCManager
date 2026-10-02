using System.Drawing;
using SocRcManager.Sessions;

namespace SocRcManager.Tests;

/// <summary>
/// El control RDP de mentira: apunta lo que la sesion le pide y deja lanzar sus eventos. Nunca
/// conecta con nada.
/// </summary>
internal sealed class FakeRdpControl : IRdpControl
{
    private bool _fullScreen;

    public System.Windows.Forms.Control Control { get; } = new System.Windows.Forms.Panel();
    public bool IsHandleCreated { get; set; } = true;

    /// <summary>Como el control de verdad sin ventana: leer o poner FullScreen lanza.</summary>
    public bool FullScreenThrows { get; set; }

    public bool FullScreen
    {
        get => FullScreenThrows ? throw new InvalidOperationException("sin ventana") : _fullScreen;
        set
        {
            if (FullScreenThrows)
                throw new InvalidOperationException("sin ventana");
            _fullScreen = value;
            Log.Add($"full {value}");
        }
    }

    public bool IsConnected { get; set; }

    /// <summary>Lo que se ha pedido, en orden («connect», «disconnect», «size 800x600»…).</summary>
    public List<string> Log { get; } = [];

    public RdpSettings? Applied { get; private set; }

    /// <summary>Peticiones de tamaño y escala (ancho, alto, escala).</summary>
    public List<(int Width, int Height, int Scale)> Updates { get; } = [];

    /// <summary>Lo que contesta UpdateDisplay (lanzar = el servidor no la acepta ahora).</summary>
    public Func<bool> UpdateResult { get; set; } = () => true;

    public Exception? ConnectError { get; set; }
    public Exception? DisconnectError { get; set; }
    public int Focused { get; private set; }

    public Rectangle ScreenBounds { get; set; } = new(0, 0, 1920, 1080);
    public IReadOnlyList<Rectangle> AllScreens { get; set; } = [new(0, 0, 1920, 1080)];

    public void SetDesktopSize(int width, int height) => Log.Add($"size {width}x{height}");
    public void SetUseMultimon(bool on) => Log.Add($"multimon {on}");
    public void Apply(RdpSettings settings) => Applied = settings;

    public bool UpdateDisplay(int width, int height, int scalePercent)
    {
        Updates.Add((width, height, scalePercent));
        return UpdateResult();
    }

    public void Connect()
    {
        Log.Add("connect");
        if (ConnectError is not null)
            throw ConnectError;
    }

    public void Disconnect()
    {
        Log.Add("disconnect");
        if (DisconnectError is not null)
            throw DisconnectError;
    }

    public void Focus() => Focused++;

    public event Action? Connected;
    public event Action? LoginComplete;
    public event Action? AutoReconnected;
    public event Action<int>? Disconnected;
    public event Action? RequestLeaveFullScreen;
    public event Action? ConfirmClose;
    public event Action? RequestContainerMinimize;
    public event Action? LeaveFullScreenMode;
    public event Action? HandleDestroyed;

    public void RaiseConnected() { IsConnected = true; Connected?.Invoke(); }
    public void RaiseLoginComplete() => LoginComplete?.Invoke();
    public void RaiseAutoReconnected() => AutoReconnected?.Invoke();
    public void RaiseDisconnected(int reason) { IsConnected = false; Disconnected?.Invoke(reason); }
    public void RaiseRequestLeaveFullScreen() => RequestLeaveFullScreen?.Invoke();
    public void RaiseConfirmClose() => ConfirmClose?.Invoke();
    public void RaiseRequestContainerMinimize() => RequestContainerMinimize?.Invoke();
    public void RaiseLeaveFullScreenMode() => LeaveFullScreenMode?.Invoke();
    public void RaiseHandleDestroyed() => HandleDestroyed?.Invoke();
}

/// <summary>
/// Un shell SSH de mentira: lo que la prueba «manda desde el servidor» se lee por el flujo, y lo que
/// escribe la sesion (las teclas) se apunta.
/// </summary>
internal sealed class FakeShell : ISshShell
{
    public FakeShellStream Shell { get; } = new();
    public Stream Stream => Shell;
    public int Closed { get; private set; }
    public Exception? CloseError { get; set; }

    public void Close()
    {
        Closed++;
        if (CloseError is not null)
            throw CloseError;
    }
}

/// <summary>El flujo del shell de mentira: una cola de trozos que «llegan del servidor».</summary>
internal sealed class FakeShellStream : Stream
{
    private readonly System.Threading.Channels.Channel<object> _incoming = System.Threading.Channels.Channel.CreateUnbounded<object>();

    /// <summary>Lo que ha escrito la sesion (teclas y pegados), en orden.</summary>
    public List<byte[]> Written { get; } = [];
    public int Flushes { get; private set; }
    public bool Disposed { get; private set; }
    public Exception? WriteError { get; set; }

    /// <summary>
    /// Campo con el nombre del de <c>ShellStream</c> de SSH.NET: el redimensionado lo busca por
    /// reflexion y le pide <c>SendWindowChangeRequest</c>.
    /// </summary>
    private readonly FakeChannel _channel = new();

    public List<(uint Cols, uint Rows)> WindowChanges => _channel.Requests;

    /// <summary>Llega un trozo del servidor.</summary>
    public void Receive(string text) => _incoming.Writer.TryWrite(System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>El servidor cierra el shell (la lectura devuelve 0).</summary>
    public void End() => _incoming.Writer.TryComplete();

    /// <summary>La lectura falla con este error.</summary>
    public void Fail(Exception error) => _incoming.Writer.TryWrite(error);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!await _incoming.Reader.WaitToReadAsync(cancellationToken))
            return 0;
        var item = await _incoming.Reader.ReadAsync(cancellationToken);
        if (item is Exception error)
            throw error;
        var bytes = (byte[])item;
        bytes.CopyTo(buffer);
        return bytes.Length;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (WriteError is not null)
            throw WriteError;
        Written.Add(buffer[offset..(offset + count)]);
    }

    public override void Flush() => Flushes++;

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    private sealed class FakeChannel
    {
        public List<(uint Cols, uint Rows)> Requests { get; } = [];

        public bool SendWindowChangeRequest(uint columns, uint rows, uint width, uint height)
        {
            Requests.Add((columns, rows));
            return true;
        }
    }
}

/// <summary>El portapapeles del terminal sin tocar el de Windows.</summary>
internal sealed class FakeClipboard : IDisposable
{
    private readonly Func<string?> _read = TerminalControl.ReadClipboard;
    private readonly Action<string> _write = TerminalControl.WriteClipboard;

    public string? Text { get; set; }

    public FakeClipboard()
    {
        TerminalControl.ReadClipboard = () => Text;
        TerminalControl.WriteClipboard = t => Text = t;
    }

    public void Dispose()
    {
        TerminalControl.ReadClipboard = _read;
        TerminalControl.WriteClipboard = _write;
    }
}
