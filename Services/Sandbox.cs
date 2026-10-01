using System.IO;

namespace SocRcManager.Services;

/// <summary>
/// Modo aislado para las pruebas de interfaz (solo en compilaciones Debug): con la variable de
/// entorno <c>SOC_SANDBOX</c> definida, la aplicacion no toca nada de verdad del usuario.
/// </summary>
/// <remarks>
/// <para>Ajustes, conexiones, arbol y registro de errores van a otra carpeta: la ruta que traiga
/// <c>SOC_SANDBOX</c> si es absoluta, o <c>%TEMP%\sOCRCManager-sandbox</c> si trae otra cosa
/// (p. ej. <c>1</c>).</para>
///
/// <para>Y no sale nada del equipo: no se conecta a ningun servidor (constitucion general 8.4: un
/// clic perdido en una prueba abrio una vez una sesion RDP real de trabajo) ni se entra en Google
/// Drive u OneDrive. La ventana no se activa al abrir, para no quitarle el foco a quien este
/// trabajando mientras corren las pruebas.</para>
///
/// <para>En Release no existe: <see cref="IsOn"/> es siempre falso y la variable no hace nada.</para>
/// </remarks>
public static class Sandbox
{
    public const string Variable = "SOC_SANDBOX";

#if DEBUG
    /// <summary>Si esta en modo aislado. Lo decide <see cref="Apply()"/>, lo primero al arrancar.</summary>
    public static bool IsOn { get; private set; }
#else
    public static bool IsOn => false;
#endif

    /// <summary>Carpeta de datos del modo aislado (null fuera de el).</summary>
    public static string? Folder { get; private set; }

    /// <summary>Al arrancar, antes de leer nada: redirige los datos a la carpeta aislada.</summary>
    public static void Apply() => Apply(Environment.GetEnvironmentVariable(Variable));

    /// <summary>Con el valor de la variable (las pruebas lo pasan directamente). En Release no hace nada.</summary>
    internal static void Apply(string? value)
    {
#if DEBUG
        if (value is not { Length: > 0 })
            return;
        IsOn = true;
        value = value.Trim();
        Folder = Path.IsPathFullyQualified(value) ? value : Path.Combine(Path.GetTempPath(), "sOCRCManager-sandbox");
        Directory.CreateDirectory(Folder);

        Store.Folder = Folder;
        AppSettings.FilePath = Path.Combine(Folder, "settings.json");
        AppLog.Folder = Folder;

        // Ninguna ventana propia se activa al abrirse (tampoco los dialogos modales), para no
        // quitarle el primer plano a quien este trabajando mientras corren las pruebas.
        foreach (var type in typeof(Sandbox).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(System.Windows.Window)) && !t.IsAbstract))
            System.Windows.Window.ShowActivatedProperty.OverrideMetadata(type, new System.Windows.FrameworkPropertyMetadata(false));
#endif
    }
}
