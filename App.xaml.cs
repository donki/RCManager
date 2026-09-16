using System.Windows;
using SocRcManager.Services;

namespace SocRcManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeManager.Apply();
        // sOCRCManager.exe --open "Nombre de la conexion" [--open "Otra"]: abre esas sesiones al
        // arrancar (accesos directos a un servidor concreto).
        // --size AxAl fija el tamaño de la ventana; --edit "Nombre" abre el editor de esa conexion
        // (--edit-tab N en esa pestaña). Sirven para capturas de pantalla y para accesos directos.
        var open = new List<string>();
        string? edit = null;
        var editTab = 0;
        (int W, int H)? size = null;
        for (var i = 0; i < e.Args.Length - 1; i++)
        {
            var key = e.Args[i].ToLowerInvariant();
            if (key == "--open") open.Add(e.Args[++i]);
            else if (key == "--edit") edit = e.Args[++i];
            else if (key == "--edit-tab") int.TryParse(e.Args[++i], out editTab);
            else if (key == "--size" && e.Args[++i].Split('x') is [var w, var h] && int.TryParse(w, out var pw) && int.TryParse(h, out var ph))
                size = (pw, ph);
        }

        var window = new MainWindow();
        MainWindow = window;
        if (size is { } s)
        {
            window.Width = s.W;
            window.Height = s.H;
        }
        window.Show();
        foreach (var name in open)
            window.OpenByName(name);
        if (edit is not null)
            window.Dispatcher.BeginInvoke(() => window.EditByName(edit, editTab), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
