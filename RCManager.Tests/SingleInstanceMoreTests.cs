using System.IO.Pipes;
using System.Text;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>
/// La instancia unica en los casos raros: mutex abandonado, nombre imposible, la otra que se va
/// mientras se espera, peticiones rotas y tuberia ocupada. Siempre con nombres propios de la prueba.
/// </summary>
public sealed class SingleInstanceMoreTests : UiTest
{
    private static string UniqueName() => "sOCRCManager-pruebas-d-" + Guid.NewGuid().ToString("N");

    private static SingleInstance.Request Req(params string[] args) => new("2026.10.2.0", args, null);

    [Fact]
    public void Mutex_abandonado_por_otra_que_murio_se_coge()
    {
        var name = UniqueName();
        Mutex? dead = null;
        // Un hilo coge el mutex y acaba sin soltarlo; el handle sigue abierto: queda abandonado.
        var t = new Thread(() => dead = new Mutex(true, "Local\\" + name));
        t.Start();
        t.Join();
        try
        {
            using var mine = new SingleInstance(name);
            Assert.True(mine.TryClaim());
            Assert.True(mine.IsOwner);
        }
        finally
        {
            dead!.Dispose();
        }
    }

    [Fact]
    public void Sin_mutex_posible_arranca_sin_ser_la_primera()
    {
        // Una barra en el nombre no vale para un objeto con nombre de Windows.
        using var odd = new SingleInstance(@"no\vale\" + Guid.NewGuid().ToString("N"));
        Assert.False(odd.TryClaim());
        Assert.False(odd.IsOwner);
    }

    [Fact]
    public void La_otra_se_cierra_mientras_se_espera_y_esta_es_la_primera()
    {
        var name = UniqueName();
        var claimed = new ManualResetEventSlim();
        var other = new Thread(() =>
        {
            using var first = new SingleInstance(name);
            first.TryClaim();   // tiene el mutex pero no escucha (arrancando)
            claimed.Set();
            Thread.Sleep(500);
        });   // al acabar, Dispose suelta el mutex
        other.Start();
        Assert.True(claimed.Wait(TimeSpan.FromSeconds(5)));

        using var second = new SingleInstance(name);
        Assert.Equal(SingleInstance.Outcome.First, second.Start(Req(), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1)));
        Assert.True(second.IsOwner);
        other.Join();
    }

    [Fact]
    public void Una_peticion_rota_no_se_atiende_y_se_sigue_escuchando()
    {
        var name = UniqueName();
        using var first = new SingleInstance(name);
        Assert.True(first.TryClaim());
        var handled = 0;
        first.Listen(_ => { Interlocked.Increment(ref handled); return (SingleInstance.Reply.Shown, null); });

        using (var pipe = new NamedPipeClientStream(".", first.PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly))
        {
            pipe.Connect(5000);
            var bytes = Encoding.UTF8.GetBytes("esto no es una peticion\n");
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
        }

        var outcome = 0;
        var t = new Thread(() =>
        {
            using var second = new SingleInstance(name);
            outcome = (int)second.Start(Req("--open", "x"), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3));
        });
        t.Start();
        t.Join();
        Assert.Equal((int)SingleInstance.Outcome.HandedOver, outcome);
        Assert.Equal(1, handled);
    }

    [Fact]
    public void Tuberia_ocupada_se_apunta_y_se_deja_de_escuchar_al_cerrar()
    {
        var name = UniqueName();
        // Otra (arrancada igual) tiene ya la unica tuberia con ese nombre.
        using var busy = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var first = new SingleInstance(name);
        Assert.True(first.TryClaim());
        first.Listen(_ => (SingleInstance.Reply.Shown, null));

        var until = DateTime.UtcNow.AddSeconds(10);
        while (!(File.Exists(AppLog.FilePath) && File.ReadAllText(AppLog.FilePath).Contains("instancia unica:")))
        {
            Assert.True(DateTime.UtcNow < until, "No se apunto el fallo de la tuberia");
            Thread.Sleep(20);
        }
        first.Dispose();   // corta la espera antes de reintentar
        Assert.False(first.IsOwner);
    }
}
