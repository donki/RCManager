using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using SocRcManager.Localization;
using SocRcManager.Services;

namespace SocRcManager;

/// <summary>«Acerca de»: version, contacto, idioma, privacidad, licencia y aviso legal.</summary>
public partial class AboutWindow : Window
{
    private const string ContactAddress = "mailto:jsoladelarosa@gmail.com";

    public AboutWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        LogoImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
        VersionLabel.Text = $"v{Version()}";
        PaintLanguageButtons();
    }

    /// <summary>La version del csproj, leida del ejecutable y sin el hash del commit que .NET añade tras el «+».</summary>
    private static string Version()
    {
        var text = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
        var plus = text.IndexOf('+');
        return plus > 0 ? text[..plus] : text;
    }

    /// <summary>El idioma activo se ve relleno de marca; el otro, de contorno.</summary>
    private void PaintLanguageButtons()
    {
        var spanish = Loc.Language == "es";
        SpanishButton.Background = spanish ? (System.Windows.Media.Brush)FindResource("Primary") : System.Windows.Media.Brushes.Transparent;
        SpanishButton.Foreground = spanish ? (System.Windows.Media.Brush)FindResource("OnPrimary") : (System.Windows.Media.Brush)FindResource("Primary");
        EnglishButton.Background = spanish ? System.Windows.Media.Brushes.Transparent : (System.Windows.Media.Brush)FindResource("Primary");
        EnglishButton.Foreground = spanish ? (System.Windows.Media.Brush)FindResource("Primary") : (System.Windows.Media.Brush)FindResource("OnPrimary");
    }

    private void OnContactClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ContactAddress) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.Get("Contact"));
        }
    }

    private void OnSpanishClick(object sender, RoutedEventArgs e) => Use("es");

    private void OnEnglishClick(object sender, RoutedEventArgs e) => Use("en");

    /// <summary>
    /// Cambia el idioma. Los textos del XAML se fijan al construir la ventana, asi que esta se
    /// cierra y se vuelve a abrir ya traducida; la principal se retraduce sola.
    /// </summary>
    private void Use(string language)
    {
        if (Loc.Language == language)
            return;

        Loc.Toggle();
        var owner = Owner;
        Close();
        new AboutWindow { Owner = owner }.ShowDialog();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
