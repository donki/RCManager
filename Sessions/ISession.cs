using System.Windows;

namespace SocRcManager.Sessions;

/// <summary>Lo que una pestaña sabe hacer, sea RDP o SSH.</summary>
public interface ISession
{
    /// <summary>Lo que se pone dentro de la pestaña.</summary>
    FrameworkElement View { get; }

    string Title { get; }

    /// <summary>El otro lado ha cambiado el titulo (el shell, o el escritorio al conectar).</summary>
    event Action<string>? TitleChanged;

    /// <summary>La sesion se ha acabado; con motivo si fue un fallo, null si fue a peticion.</summary>
    event Action<string?>? Ended;

    Task ConnectAsync(string password);

    void Focus();

    void Disconnect();

    /// <summary>
    /// La sesion sabe ponerse a pantalla completa por si misma (el control RDP de Windows, con su
    /// barra superior que se esconde sola). Si no, lo hace la ventana.
    /// </summary>
    bool HasNativeFullScreen { get; }

    /// <summary>Pantalla completa nativa; <paramref name="screen"/> es 0 (la de la ventana) o 1..n.</summary>
    void EnterFullScreen(int screen);

    /// <summary>Puede agrandar o achicar lo que enseña (letra del terminal, escala del escritorio…).</summary>
    bool CanZoom { get; }

    /// <summary>Zoom en pasos (+1 / -1). Devuelve el valor para enseñarlo («14 pt», «125 %»).</summary>
    string Zoom(int steps);

    /// <summary>Salir de la pantalla completa nativa a peticion (por ejemplo para pasar a otro monitor).</summary>
    void LeaveFullScreen();

    /// <summary>Ha salido de su pantalla completa nativa (por la barra o por el servidor).</summary>
    event Action? LeftFullScreen;
}
