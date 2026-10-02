using System.Runtime.CompilerServices;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>
/// Al cargar las pruebas, antes de nada: los datos de la aplicacion (conexiones, ajustes, registro)
/// van a una carpeta temporal. Ninguna prueba puede tocar los de verdad aunque se olvide de redirigirlos.
/// </summary>
internal static class SafeData
{
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "rcm-tests", "datos-" + Environment.ProcessId);

    [ModuleInitializer]
    internal static void Redirect()
    {
        // Las XAML se cargan del ensamblado de la aplicacion, no del de testhost (antes de que nadie lo lea).
        // (Si testhost ya lo ha fijado, se cambia el campo: la propiedad no deja hacerlo dos veces.)
        typeof(System.Windows.Application).GetField("_resourceAssembly", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, typeof(App).Assembly);
        Directory.CreateDirectory(Root);
        Store.Folder = Root;
        AppSettings.FilePath = Path.Combine(Root, "settings.json");
        AppLog.Folder = Root;
        // Nada de abrir el navegador de verdad desde una prueba (OAuth).
        OAuthClient.OpenBrowser = _ => { };
    }
}

/// <summary>
/// Base de las pruebas que manejan ventanas: datos en una carpeta temporal propia, ajustes nuevos,
/// idioma español, y al acabar se cierran las ventanas que queden y se vacian los registros de
/// <see cref="Ui"/>.
/// </summary>
public abstract class UiTest : IDisposable
{
    protected TempDir Dir { get; } = new();

    protected UiTest()
    {
        Ui.Reset();
        Store.Folder = Path.Combine(Dir.Path, "datos");
        AppSettings.FilePath = Path.Combine(Dir.Path, "datos", "settings.json");
        AppLog.Folder = Path.Combine(Dir.Path, "datos");
        AppSettings.Current = null;
        Sandbox.IsOn = false;
        Ui.Run(() => Lang.Set("es"));
    }

    public virtual void Dispose()
    {
        try
        {
            Ui.Reset();
        }
        finally
        {
            Sandbox.IsOn = false;
            Store.Folder = SafeData.Root;
            AppSettings.FilePath = Path.Combine(SafeData.Root, "settings.json");
            AppLog.Folder = SafeData.Root;
            Dir.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
