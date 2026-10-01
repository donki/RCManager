using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace SocRcManager.Services;

/// <summary>
/// Tema claro u oscuro siguiendo al de Windows, con los mismos nombres de recurso que declara
/// App.xaml (la paleta indigo comun a las aplicaciones sOCratic).
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsDark { get; private set; }

    /// <summary>Colores del tema oscuro (nombre de recurso de App.xaml y color).</summary>
    private static readonly Dictionary<string, string> Dark = new()
    {
        ["PageBackground"] = "#141318",
        ["CardBackground"] = "#201F27",
        ["Separator"] = "#48454F",
        ["TextPrimary"] = "#E6E1E9",
        ["TextSecondary"] = "#C7C4D8",
        ["MirrorBackground"] = "#000000",
        ["WarningSurface"] = "#33291A",
    };

    /// <summary>Colores del tema claro.</summary>
    private static readonly Dictionary<string, string> Light = new()
    {
        ["PageBackground"] = "#F8F9FA",
        ["CardBackground"] = "#FFFFFF",
        ["Separator"] = "#C7C4D8",
        ["TextPrimary"] = "#191C1D",
        ["TextSecondary"] = "#464555",
        ["MirrorBackground"] = "#1B1B22",
        ["WarningSurface"] = "#FFF4E5",
    };

    /// <summary>Los colores que cambian entre el tema claro y el oscuro.</summary>
    public static IReadOnlyDictionary<string, string> Palette(bool dark) => dark ? Dark : Light;

    public static void Apply()
    {
        IsDark = PrefersDark();
        Apply(Application.Current.Resources, IsDark);
    }

    /// <summary>Pone en esos recursos los pinceles del tema pedido.</summary>
    public static void Apply(ResourceDictionary resources, bool dark)
    {
        foreach (var (key, hex) in Palette(dark))
            Set(resources, key, hex);
    }

    /// <summary>La barra de titulo la pinta Windows: se le pide que siga al tema (DWM).</summary>
    public static void ApplyToWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    /// <summary>Si Windows tiene puesto el modo oscuro para las aplicaciones.</summary>
    internal static bool PrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void Set(ResourceDictionary resources, string key, string hex) =>
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
