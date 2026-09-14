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
}
