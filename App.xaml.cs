using System.Windows;
using SocRcManager.Localization;
using SocRcManager.Services;

namespace SocRcManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Debug con SOC_SANDBOX: datos en otra carpeta y sin conectar a nada (pruebas de interfaz).
        Sandbox.Apply();

        // Un error que no se esperaba no puede cerrar la aplicacion (constitucion general, 6.12):
        // se apunta en el registro con la traza y se avisa en el idioma del usuario. Las sesiones
        // abiertas siguen vivas.
        DispatcherUnhandledException += (_, ex) =>
        {
            AppLog.Write($"error no controlado: {ex.Exception}");
            ex.Handled = true;
            ShowUnexpectedError();
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            AppLog.Write($"error no controlado en tarea: {ex.Exception}");
            ex.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            AppLog.Write($"error fatal: {ex.ExceptionObject}");

        ThemeManager.Apply();
        // sOCRCManager.exe --open "Nombre de la conexion" [--open "Otra"]: abre esas sesiones al
        // arrancar (accesos directos a un servidor concreto).
        // --size AxAl fija el tamaño de la ventana; --edit "Nombre" abre el editor de esa conexion
        // (--edit-tab N en esa pestaña). Sirven para capturas de pantalla y para accesos directos.
        var open = new List<string>();
        string? edit = null;
        string? editFileConnection = null, editFilePath = null;
        var editTab = 0;
        (int W, int H)? size = null;
        for (var i = 0; i < e.Args.Length - 1; i++)
        {
            var key = e.Args[i].ToLowerInvariant();
            if (key == "--open") open.Add(e.Args[++i]);
            else if (key == "--edit") edit = e.Args[++i];
            else if (key == "--edit-file" && i + 2 < e.Args.Length) { editFileConnection = e.Args[++i]; editFilePath = e.Args[++i]; }
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
        // --edit-file "Conexion" "/ruta/fichero": abre esa conexion de ficheros y el fichero en el editor.
        if (editFileConnection is not null && editFilePath is not null)
            window.OpenFileInEditor(editFileConnection, editFilePath);
        if (edit is not null)
            window.Dispatcher.BeginInvoke(() => window.EditByName(edit, editTab), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private bool _showingError;

    /// <summary>
    /// Aviso de un error inesperado. Uno cada vez: si el mismo fallo se repite mientras el aviso
    /// esta abierto (un temporizador, un redibujado), no se apilan ventanas.
    /// </summary>
    private void ShowUnexpectedError()
    {
        if (_showingError) return;
        _showingError = true;
        try
        {
            var owner = MainWindow is { IsVisible: true } w ? w : null;
            PromptWindow.Alert(owner!, Loc.Get("UnexpectedErrorTitle"), Loc.Format("UnexpectedErrorText", AppLog.FilePath));
        }
        catch (Exception ex)
        {
            AppLog.Write($"no se pudo enseñar el aviso de error: {ex}");
        }
        finally
        {
            _showingError = false;
        }
    }
}
