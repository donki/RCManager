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
        var open = new List<string>();
        for (var i = 0; i < e.Args.Length - 1; i++)
            if (string.Equals(e.Args[i], "--open", StringComparison.OrdinalIgnoreCase))
                open.Add(e.Args[++i]);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        foreach (var name in open)
            window.OpenByName(name);
    }
}
