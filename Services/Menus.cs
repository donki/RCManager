using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace SocRcManager.Services;

/// <summary>
/// Los menus contextuales y el de la bandeja, con el aspecto comun de las aplicaciones sOC
/// (constitucion general 6.14; los estilos <c>TrayMenu</c>, <c>TrayItem</c> y <c>TraySeparator</c>
/// de App.xaml). Como <c>TrayIcon.Item</c> de sOC ScreenShare: si se toca uno, mirar el otro.
/// Los estilos y colores van por referencia a los recursos, asi siguen al tema claro u oscuro.
/// </summary>
internal static class Menus
{
    /// <summary>Un menu vacio con el estilo comun.</summary>
    public static ContextMenu Create()
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenu");
        return menu;
    }

    /// <summary>
    /// Un elemento con su icono plano (glifo de Segoe Fluent Icons) en el color de acento, o en
    /// rojo (<c>Danger</c>) si la accion es destructiva.
    /// </summary>
    public static MenuItem Item(string text, string glyph, RoutedEventHandler click, bool danger = false, string? automationId = null)
    {
        var icon = new TextBlock { Text = glyph, FontSize = 14 };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, danger ? "Danger" : "PrimaryText");
        var item = new MenuItem { Header = text, Icon = icon };
        item.SetResourceReference(FrameworkElement.StyleProperty, "TrayItem");
        if (automationId is not null)
            AutomationProperties.SetAutomationId(item, automationId);
        item.Click += click;
        return item;
    }

    /// <summary>Lo mismo con una accion sin argumentos.</summary>
    public static MenuItem Item(string text, string glyph, Action action, bool danger = false, string? automationId = null) =>
        Item(text, glyph, (_, _) => action(), danger, automationId);

    public static Separator Separator()
    {
        var separator = new Separator();
        separator.SetResourceReference(FrameworkElement.StyleProperty, "TraySeparator");
        return separator;
    }
}
