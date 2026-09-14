using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Connections.Services;

/// <summary>
/// Tema claro u oscuro siguiendo al de Windows, con los mismos nombres de recurso que declara
/// App.xaml (la paleta indigo comun a las aplicaciones sOCratic).
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsDark { get; private set; }

    public static void Apply()
    {
        IsDark = PrefersDark();
        var resources = Application.Current.Resources;

        if (IsDark)
        {
            Set(resources, "PageBackground", "#141318");
            Set(resources, "CardBackground", "#201F27");
            Set(resources, "Separator", "#48454F");
            Set(resources, "TextPrimary", "#E6E1E9");
            Set(resources, "TextSecondary", "#C7C4D8");
            Set(resources, "MirrorBackground", "#000000");
            Set(resources, "WarningSurface", "#33291A");
        }
        else
        {
            Set(resources, "PageBackground", "#F8F9FA");
            Set(resources, "CardBackground", "#FFFFFF");
            Set(resources, "Separator", "#C7C4D8");
            Set(resources, "TextPrimary", "#191C1D");
            Set(resources, "TextSecondary", "#464555");
            Set(resources, "MirrorBackground", "#1B1B22");
            Set(resources, "WarningSurface", "#FFF4E5");
        }
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

    private static bool PrefersDark()
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
