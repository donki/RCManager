using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SocRcManager.Services;

/// <summary>
/// Una sola instancia por usuario y sesion de Windows (constitucion general 8.3): si la aplicacion ya
/// esta abierta —aunque este escondida en el area de notificacion—, abrirla otra vez la trae al frente
/// y le pasa lo que se pidio (<c>--open "Servidor"</c> abre esa conexion alli); la nueva se cierra.
/// </summary>
/// <remarks>
/// <para><b>Como se encuentran.</b> Un mutex con nombre (<c>Local\</c>: por sesion de Windows) dice si
/// hay otra; con ella se habla por una tuberia con nombre (solo del usuario actual) que lleva el
/// usuario y la sesion en el nombre. Los dos nombres dependen solo del usuario, no de la ruta del exe:
/// el exe suelto de OneDrive, el del MSIX (aplicacion de escritorio empaquetada: no aisla los objetos
/// con nombre) y un acceso directo se encuentran entre si.</para>
///
/// <para><b>Acuse.</b> La nueva espera la respuesta de la otra. Si la otra tiene el mutex pero no
/// contesta en unos segundos (colgada, o arrancando sin terminar), la nueva arranca igual: mejor dos
/// ventanas que ninguna. Si la otra se muere mientras tanto, el mutex queda libre y la nueva lo coge.</para>
///
/// <para><b>Manda la version nueva.</b> La que contesta decide (<see cref="Decide"/>): si es mas vieja
/// y no tiene sesiones abiertas, cede (se cierra y la nueva sigue); si tiene sesiones, se pone delante
/// y pregunta antes de cortarlas.</para>
///
/// <para>El modo aislado de pruebas (<c>SOC_SANDBOX</c>) usa otros nombres, que dependen de su carpeta
/// de datos: nunca choca con la instancia de verdad (constitucion general 8.4).</para>
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    /// <summary>Lo que la nueva le manda a la que ya estaba.</summary>
    public sealed record Request(string Version, string[] Args, string? Exe);

    /// <summary>Lo que contesta la que ya estaba.</summary>
    public enum Reply
    {
        /// <summary>Atendido (se ha puesto delante, ha abierto lo pedido): la nueva se cierra.</summary>
        Shown,

        /// <summary>La que estaba es mas vieja y se cierra: la nueva espera el mutex y sigue.</summary>
        Yield,
    }

    /// <summary>Que decide la que ya estaba al recibir una peticion.</summary>
    public enum Answer
    {
        /// <summary>Ponerse delante y atender lo pedido.</summary>
        Show,

        /// <summary>Ceder: la otra es mas nueva y aqui no hay sesiones que cortar.</summary>
        Yield,

        /// <summary>La otra es mas nueva pero aqui hay sesiones abiertas: ponerse delante y preguntar.</summary>
        ShowAndOfferUpdate,
    }

    /// <summary>Como acaba el intento de arrancar.</summary>
    public enum Outcome
    {
        /// <summary>Esta es la unica: sigue arrancando.</summary>
        First,

        /// <summary>La otra lo ha atendido: esta se cierra.</summary>
        HandedOver,

        /// <summary>Hay otra pero no contesta: esta arranca igual (sin ser la «primera»).</summary>
        StartAnyway,
    }

    public static Answer Decide(Version mine, Version? theirs, bool hasOpenSessions)
    {
        if (theirs is null || theirs <= mine)
            return Answer.Show;
        return hasOpenSessions ? Answer.ShowAndOfferUpdate : Answer.Yield;
    }

    /// <summary>
    /// Nombre base de mutex y tuberia: el mismo para cualquier copia del programa del mismo usuario y
    /// sesion; en modo aislado, otro distinto por carpeta de datos.
    /// </summary>
    public static string NameFor(string? sandboxFolder, string? user = null, int? session = null)
    {
        user ??= System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        session ??= System.Diagnostics.Process.GetCurrentProcess().SessionId;
        var name = $"sOCRCManager-{Hash(user)}-{session}";
        if (sandboxFolder is { Length: > 0 })
            name += "-sandbox-" + Hash(Path.GetFullPath(sandboxFolder).TrimEnd('\\').ToLowerInvariant());
        return name;
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();

    public static string Encode(Request request) => JsonSerializer.Serialize(request);

    public static Request? DecodeRequest(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        try
        {
            var r = JsonSerializer.Deserialize<Request>(line);
            return r is null ? null : r with { Args = r.Args ?? [], Version = r.Version ?? string.Empty };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static Reply? DecodeReply(string? line) => Enum.TryParse<Reply>(line?.Trim(), out var r) && Enum.IsDefined(r) ? r : null;

    // =====================================================================

    private readonly string _name;
    private Mutex? _mutex;
    private bool _owned;
    private CancellationTokenSource? _listening;
    private Thread? _listener;

    public SingleInstance(string name)
    {
        _name = name;
    }

    public string MutexName => "Local\\" + _name;

    public string PipeName => _name;

    /// <summary>Si esta instancia tiene el mutex.</summary>
    public bool IsOwner => _owned;

    /// <summary>Intenta quedarse el mutex sin esperar. Uno abandonado (proceso muerto) tambien vale.</summary>
    public bool TryClaim(TimeSpan wait = default)
    {
        if (_owned)
            return true;
        try
        {
            _mutex ??= new Mutex(false, MutexName);
            _owned = _mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            _owned = true;
        }
        catch (Exception)
        {
            // Sin mutex (raro): se arranca igual, mejor dos ventanas que ninguna.
            _owned = false;
        }
        return _owned;
    }

    /// <summary>
    /// Al arrancar: o esta es la unica, o se le pasa la peticion a la que ya estaba y se espera su
    /// acuse. <paramref name="patience"/> es cuanto se insiste en encontrar su tuberia (la otra puede
    /// estar arrancando a la vez) y <paramref name="ackTimeout"/> cuanto se espera la respuesta una
    /// vez entregada la peticion.
    /// </summary>
    public Outcome Start(Request request, TimeSpan patience, TimeSpan ackTimeout)
    {
        if (TryClaim())
            return Outcome.First;

        // Esta es la que acaba de abrir el usuario: puede ceder el primer plano a la otra.
        try { AllowSetForegroundWindow(-1 /* ASFW_ANY */); } catch (Exception) { }

        var deadline = DateTime.UtcNow + patience;
        do
        {
            // La otra se ha ido mientras tanto: el mutex queda libre.
            if (TryClaim())
                return Outcome.First;

            var reply = Send(request, TimeSpan.FromMilliseconds(400), ackTimeout, out var delivered);
            if (reply == Reply.Shown)
                return Outcome.HandedOver;
            if (reply == Reply.Yield)
                return TryClaim(TimeSpan.FromSeconds(15)) ? Outcome.First : Outcome.StartAnyway;
            if (delivered)
                return Outcome.StartAnyway;   // la recibio y no contesta: colgada
        }
        while (DateTime.UtcNow < deadline);

        return TryClaim() ? Outcome.First : Outcome.StartAnyway;
    }

    /// <summary>Una peticion por la tuberia. <paramref name="delivered"/> dice si llego a entregarse.</summary>
    private Reply? Send(Request request, TimeSpan connectTimeout, TimeSpan ackTimeout, out bool delivered)
    {
        delivered = false;
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.Connect((int)connectTimeout.TotalMilliseconds);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            writer.WriteLine(Encode(request));
            delivered = true;
            using var cts = new CancellationTokenSource(ackTimeout);
            var reader = new StreamReader(pipe, Encoding.UTF8);
            var line = reader.ReadLineAsync(cts.Token).AsTask().GetAwaiter().GetResult();
            return DecodeReply(line);
        }
        catch (Exception)
        {
            // Sin tuberia todavia (TimeoutException), cortada o sin respuesta a tiempo.
            return null;
        }
    }

    /// <summary>
    /// La primera instancia atiende a las que vengan despues, en un hilo aparte. <paramref name="handle"/>
    /// devuelve la respuesta (null = no contestar: la otra arrancara igual) y, si hace falta, algo que
    /// hacer despues de haber contestado (cerrarse, por ejemplo).
    /// </summary>
    public void Listen(Func<Request, (Reply? Reply, Action? After)> handle)
    {
        if (_listener is not null)
            return;
        _listening = new CancellationTokenSource();
        var token = _listening.Token;
        _listener = new Thread(() => ListenLoop(handle, token)) { IsBackground = true, Name = "SingleInstance" };
        _listener.Start();
    }

    private void ListenLoop(Func<Request, (Reply? Reply, Action? After)> handle, CancellationToken token)
    {
        var failures = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                pipe.WaitForConnectionAsync(token).GetAwaiter().GetResult();
                failures = 0;
                var reader = new StreamReader(pipe, Encoding.UTF8);
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                readTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                var request = DecodeRequest(reader.ReadLineAsync(readTimeout.Token).AsTask().GetAwaiter().GetResult());
                if (request is null)
                    continue;
                var (reply, after) = handle(request);
                if (reply is { } r)
                {
                    var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
                    writer.WriteLine(r.ToString());
                    try { pipe.WaitForPipeDrain(); } catch (Exception) { }
                }
                after?.Invoke();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Tuberia ocupada por otra instancia que arranco igual, o un cliente que se fue a
                // medias: se reintenta con calma (y si no hay manera, se deja de escuchar).
                AppLog.Write($"instancia unica: {ex.Message}");
                if (++failures > 20 || token.WaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Min(30, failures))))
                    return;
            }
        }
    }

    /// <summary>Deja de escuchar y suelta el mutex (desde el hilo que lo cogio).</summary>
    public void Dispose()
    {
        _listening?.Cancel();
        _listener = null;
        if (_owned)
        {
            try { _mutex?.ReleaseMutex(); } catch (Exception) { }
            _owned = false;
        }
        _mutex?.Dispose();
        _mutex = null;
    }

    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
}
