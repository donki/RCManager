using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace SocRcManager.UITests;

/// <summary>
/// Una instancia de la aplicacion lanzada para una prueba: el exe Debug en modo aislado
/// (SOC_SANDBOX con una carpeta temporal nueva), con su ventana principal y utilidades para
/// esperar ventanas, buscar controles y guardar capturas.
/// </summary>
/// <remarks>
/// Reglas (constitucion general 8.4): nunca doble clic en el arbol (conecta), nunca el boton
/// Conectar. Ademas, en modo aislado la aplicacion no conecta aunque se le pida.
/// Todo se hace por patrones de UI Automation (Invoke, Value, SelectionItem), que no necesitan la
/// ventana en primer plano ni mueven el raton.
/// </remarks>
public sealed class RcApp : IDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly string _testName;
    private readonly string _foregroundBeforeName;
    private int _shot;

    public Application App { get; }
    public UIA3Automation Automation { get; } = new();
    public Window Main { get; private set; } = null!;
    public ConditionFactory Cf => Automation.ConditionFactory;

    /// <summary>Carpeta de datos del modo aislado de esta instancia (connections.json, settings.json...).</summary>
    public string DataFolder { get; }

    /// <summary>Si al arrancar la ventana de la aplicacion se quedo con el primer plano.</summary>
    public bool StoleFocusOnLaunch { get; }

    private RcApp(string testName, string[] args, string? connectionsJson, bool hidden)
    {
        _testName = testName;
        DataFolder = Path.Combine(Path.GetTempPath(), "sOCRCManager-uitests", $"{testName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DataFolder);
        if (connectionsJson is not null)
            File.WriteAllText(Path.Combine(DataFolder, "connections.json"), connectionsJson);

        var foregroundBefore = GetForegroundWindow();
        _foregroundBeforeName = ForegroundName();
        var psi = StartInfo(["--size", "1100x720", .. args]);
        App = Application.Launch(psi);
        if (hidden)
            return;
        WaitMainWindow();

        var foregroundAfter = GetForegroundWindow();
        GetWindowThreadProcessId(foregroundAfter, out var pid);
        StoleFocusOnLaunch = pid == App.ProcessId && foregroundAfter != foregroundBefore;
        File.AppendAllText(Path.Combine(ArtifactsFolder, "foco.log"),
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {testName}: {(StoleFocusOnLaunch ? "LA APP SE QUEDO CON EL PRIMER PLANO" : "sin robar el foco")}{Environment.NewLine}");
    }

    public static RcApp Launch([System.Runtime.CompilerServices.CallerMemberName] string testName = "") => new(testName, [], null, hidden: false);

    /// <summary>
    /// Con conexiones de prueba ya guardadas (JSON de connections.json) y argumentos (p. ej. --open).
    /// Con <paramref name="hidden"/> (--tray) no se espera la ventana: <see cref="WaitMainWindow"/>.
    /// </summary>
    public static RcApp LaunchWith(string? connectionsJson, string[] args, bool hidden = false,
        [System.Runtime.CompilerServices.CallerMemberName] string testName = "") => new(testName, args, connectionsJson, hidden);

    /// <summary>El exe con SOC_SANDBOX apuntando a la carpeta de esta instancia (para la segunda instancia).</summary>
    public ProcessStartInfo StartInfo(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(ExePath)!,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        psi.Environment["SOC_SANDBOX"] = DataFolder;
        return psi;
    }

    /// <summary>Espera la ventana principal (visible) de la aplicacion.</summary>
    public Window WaitMainWindow()
    {
        Main = Retry.WhileNull(() => App.GetMainWindow(Automation, TimeSpan.FromSeconds(1)), Timeout, throwOnTimeout: true).Result!;
        Main.WaitUntilClickable(Timeout);
        return Main;
    }

    /// <summary>Espera a que se cumpla la condicion (true) o se acabe el tiempo (false).</summary>
    public static bool WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var end = DateTime.UtcNow + (timeout ?? Timeout);
        while (true)
        {
            try { if (condition()) return true; } catch (Exception) { }
            if (DateTime.UtcNow > end) return false;
            Thread.Sleep(100);
        }
    }

    /// <summary>Si la ventana nativa esta visible (escondida en la bandeja no lo esta).</summary>
    public static bool IsVisible(IntPtr hwnd) => IsWindowVisible(hwnd);

    public static bool IsAlive(IntPtr hwnd) => IsWindow(hwnd);

    /// <summary>Ventanas de primer nivel de la aplicacion (la principal y las sueltas).</summary>
    /// <remarks>
    /// Por Win32 (EnumWindows con el pid) y no preguntando a UI Automation por todas las ventanas del
    /// escritorio: una ventana colgada de otro programa hace esperar segundos a cada consulta.
    /// </remarks>
    public Window[] TopWindows()
    {
        var found = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == App.ProcessId && IsWindowVisible(hwnd))
                found.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return found.Select(h => Automation.FromHandle(h).AsWindow()).ToArray();
    }

    /// <summary>JSON de conexiones de prueba: servidores .invalid (no existen), y en modo aislado tampoco se conecta.</summary>
    public static string Connections(params (string Name, string Kind, int Port)[] items) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            Connections = items.Select(i => new { Id = Guid.NewGuid(), i.Name, i.Kind, Host = "ejemplo.invalid", i.Port, UserName = "nadie" }).ToArray(),
            EmptyFolders = Array.Empty<string>(),
        });

    // =====================================================================
    //  Rutas
    // =====================================================================

    /// <summary>Carpeta del repo (la que tiene sOCRCManager.csproj), subiendo desde las pruebas.</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>El exe Debug de la aplicacion; RCMANAGER_EXE lo cambia por otro.</summary>
    public static string ExePath
    {
        get
        {
            var path = Environment.GetEnvironmentVariable("RCMANAGER_EXE") is { Length: > 0 } custom
                ? custom
                : Path.Combine(RepoRoot, "bin", "Debug", "net10.0-windows", "sOCRCManager.exe");
            if (!File.Exists(path))
                throw new FileNotFoundException($"No esta el exe de la aplicacion: compila antes en Debug (dotnet build sOCRCManager.csproj -c Debug). Buscado en {path}");
            return path;
        }
    }

    /// <summary>Capturas y registro de foco: RCManager.UITests/artifacts (ignorada en git).</summary>
    public static string ArtifactsFolder { get; } = Directory.CreateDirectory(
        Path.Combine(RepoRoot, "RCManager.UITests", "artifacts")).FullName;

    public static string TestData(string file) => Path.Combine(AppContext.BaseDirectory, "TestData", file);

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "sOCRCManager.csproj")))
                return dir.FullName;
        throw new DirectoryNotFoundException("No se encuentra sOCRCManager.csproj subiendo desde " + AppContext.BaseDirectory);
    }

    // =====================================================================
    //  Buscar y esperar
    // =====================================================================

    public AutomationElement ById(AutomationElement parent, string automationId) =>
        Retry.WhileNull(() => parent.FindFirstDescendant(Cf.ByAutomationId(automationId)), Timeout, throwOnTimeout: true,
            timeoutMessage: $"No aparece el control {automationId}").Result!;

    public Button Button(AutomationElement parent, string automationId) => ById(parent, automationId).AsButton();

    public TextBox TextBox(AutomationElement parent, string automationId) => ById(parent, automationId).AsTextBox();

    /// <summary>Espera a que la ventana principal tenga un dialogo modal abierto y lo devuelve.</summary>
    public Window WaitModal()
    {
        var modal = Retry.WhileNull(() => Main.ModalWindows.FirstOrDefault(), Timeout, throwOnTimeout: true,
            timeoutMessage: "No se abre el dialogo").Result!;
        modal.WaitUntilClickable(Timeout);
        return modal;
    }

    public void WaitNoModal() =>
        Retry.WhileTrue(() => Main.ModalWindows.Length > 0, Timeout, throwOnTimeout: true, timeoutMessage: "El dialogo no se cierra");

    /// <summary>Pulsar por el patron Invoke: no necesita primer plano ni mueve el raton.</summary>
    public static void Press(Button button) => button.Patterns.Invoke.Pattern.Invoke();

    /// <summary>Textos visibles del arbol de conexiones.</summary>
    public IReadOnlyList<string> TreeTexts()
    {
        var tree = ById(Main, "Tree");
        return tree.FindAllDescendants(Cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text))
            .Select(t => t.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();
    }

    public bool WaitTreeContains(string text, bool present = true) =>
        Retry.WhileFalse(() => TreeTexts().Contains(text) == present, Timeout).Result;

    /// <summary>Selecciona la fila del arbol que lleva ese texto (SelectionItem, sin clics).</summary>
    public void SelectTreeItem(string text)
    {
        var tree = ById(Main, "Tree");
        var item = Retry.WhileNull(() => tree.FindAllDescendants(Cf.ByControlType(FlaUI.Core.Definitions.ControlType.TreeItem))
            .FirstOrDefault(i => i.FindAllDescendants(Cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text)).Any(t => t.Name == text)),
            Timeout, throwOnTimeout: true, timeoutMessage: $"No esta «{text}» en el arbol").Result!;
        item.Patterns.SelectionItem.Pattern.Select();
        Retry.WhileFalse(() => item.Patterns.SelectionItem.Pattern.IsSelected.Value, Timeout, throwOnTimeout: true);
    }

    public string ConnectionsJson()
    {
        var path = Path.Combine(DataFolder, "connections.json");
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    /// <summary>Arbol de UI Automation de un elemento a un .txt de artefactos (para depurar).</summary>
    public void DumpTree(AutomationElement root, string step)
    {
        var lines = new List<string>();
        void Walk(AutomationElement e, int depth)
        {
            lines.Add($"{new string(' ', depth * 2)}{e.Properties.ControlType.ValueOrDefault} id={e.Properties.AutomationId.ValueOrDefault} class={e.Properties.ClassName.ValueOrDefault} name={e.Properties.Name.ValueOrDefault}");
            if (depth < 12)
                foreach (var child in e.FindAllChildren())
                    Walk(child, depth + 1);
        }
        Walk(root, 0);
        File.WriteAllLines(Path.Combine(ArtifactsFolder, $"{_testName}-{step}.uia.txt"), lines);
    }

    // =====================================================================
    //  Capturas
    // =====================================================================

    /// <summary>
    /// Captura de una ventana con PrintWindow (PW_RENDERFULLCONTENT): sale bien aunque este tapada
    /// por otras y sin traerla al frente.
    /// </summary>
    public string Capture(AutomationElement window, string step)
    {
        var hwnd = window.Properties.NativeWindowHandle.Value;
        GetWindowRect(hwnd, out var r);
        var (w, h) = (Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top));
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            try { PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT); }
            finally { g.ReleaseHdc(hdc); }
        }
        var file = Path.Combine(ArtifactsFolder, $"{_testName}-{++_shot:00}-{step}.png");
        bmp.Save(file, ImageFormat.Png);
        return file;
    }

    // =====================================================================

    public void Dispose()
    {
        // Y al acabar: si algun dialogo se quedo con el primer plano durante la prueba, se nota aqui.
        GetWindowThreadProcessId(GetForegroundWindow(), out var fgPid);
        File.AppendAllText(Path.Combine(ArtifactsFolder, "foco.log"),
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {_testName} (al acabar): {(fgPid == App.ProcessId ? "LA APP TIENE EL PRIMER PLANO" : "sin el primer plano")} (antes: {_foregroundBeforeName}; ahora: {ForegroundName()}){Environment.NewLine}");
        try
        {
            // Primero por las buenas (guarda el estado del arbol, como al cerrar a mano)...
            if (Main is not null)
                foreach (var modal in Main.ModalWindows)
                    modal.Close();
            if (!App.HasExited)
                App.Close();
            var process = Process.GetProcessById(App.ProcessId);
            if (!process.WaitForExit(5000))
                process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
            // Ya habia salido.
        }
        catch (Exception)
        {
            // ...y si no, por las malas: nunca se queda un sOCRCManager de pruebas vivo.
            try { Process.GetProcessById(App.ProcessId).Kill(entireProcessTree: true); } catch { }
        }
        // El registro de errores de la aplicacion, si dejo algo, a artefactos antes de borrar la carpeta.
        try
        {
            var log = Directory.GetFiles(DataFolder, "*.log").FirstOrDefault();
            if (log is not null)
                File.Copy(log, Path.Combine(ArtifactsFolder, $"{_testName}-{Path.GetFileName(log)}"), overwrite: true);
        }
        catch { }
        App.Dispose();
        Automation.Dispose();
        try { Directory.Delete(DataFolder, recursive: true); } catch { }
    }

    /// <summary>Proceso y titulo de la ventana en primer plano, para el registro de foco.</summary>
    private static string ForegroundName()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return "(ninguna)";
        GetWindowThreadProcessId(hwnd, out var pid);
        try { return Process.GetProcessById(pid).ProcessName; } catch { return pid.ToString(); }
    }

    private const uint PW_RENDERFULLCONTENT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
}
