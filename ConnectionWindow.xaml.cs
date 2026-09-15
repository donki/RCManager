using System.Windows;
using System.Windows.Controls;
using SocRcManager.Localization;
using SocRcManager.Models;
using SocRcManager.Services;
using Microsoft.Win32;

namespace SocRcManager;

/// <summary>
/// Alta y edicion de una conexion, con las mismas pestañas que el cliente de Escritorio remoto de
/// Windows. Escribe sobre el objeto que recibe solo al guardar.
/// </summary>
public partial class ConnectionWindow : Window
{
    private readonly Connection _connection;

    // Tamaños de la lista de Pantalla, en el mismo orden que los ComboBoxItem (0 = ajustar, ultimo = a medida).
    private static readonly (int W, int H)[] Sizes =
    [
        (0, 0), (1024, 768), (1280, 800), (1366, 768), (1600, 900), (1920, 1080), (1920, 1200), (2560, 1440),
    ];

    public ConnectionWindow(Connection connection, IReadOnlyList<string> folders)
    {
        InitializeComponent();
        _connection = connection;
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        CancelButton.ToolTip = Loc.Get("Cancel");
        SaveButton.ToolTip = Loc.Get("Save");
        BrowseButton.ToolTip = Loc.Get("BrowseTooltip");

        // --- General ---
        NameBox.Text = connection.Name;
        KindBox.SelectedIndex = connection.Kind == ConnectionKind.Ssh ? 1 : 0;
        FolderBox.ItemsSource = folders;
        FolderBox.Text = connection.Folder;
        HostBox.Text = connection.Host;
        PortBox.Text = connection.Port.ToString();
        UserBox.Text = connection.UserName;
        DomainBox.Text = connection.Domain;
        PasswordBox.Password = Secrets.Unprotect(connection.PasswordProtected);
        KeyBox.Text = connection.PrivateKeyPath;
        NotesBox.Text = connection.Notes;

        // --- Pantalla ---
        var size = Array.FindIndex(Sizes, s => s.W == connection.RdpWidth && s.H == connection.RdpHeight);
        SizeBox.SelectedIndex = connection.RdpSmartSizing && connection.RdpWidth == 0 ? 0 : size > 0 ? size : SizeBox.Items.Count - 1;
        WidthBox.Text = connection.RdpWidth > 0 ? connection.RdpWidth.ToString() : string.Empty;
        HeightBox.Text = connection.RdpHeight > 0 ? connection.RdpHeight.ToString() : string.Empty;
        ColorBox.SelectedIndex = connection.RdpColorDepth switch { 15 => 0, 16 => 1, 24 => 2, _ => 3 };
        MultiMonitorBox.IsChecked = connection.RdpMultiMonitor;
        ConnectionBarBox.IsChecked = connection.RdpConnectionBar;

        // --- Recursos locales ---
        AudioBox.SelectedIndex = Math.Clamp(connection.RdpAudioMode, 0, 2);
        AudioCaptureBox.IsChecked = connection.RdpAudioCapture;
        KeyboardBox.SelectedIndex = Math.Clamp(connection.RdpKeyboardMode, 0, 2);
        PrintersBox.IsChecked = connection.RdpPrinters;
        ClipboardBox.IsChecked = connection.RdpClipboard;
        DrivesBox.IsChecked = connection.RdpDrives;
        SmartCardsBox.IsChecked = connection.RdpSmartCards;
        PortsBox.IsChecked = connection.RdpPorts;
        DevicesBox.IsChecked = connection.RdpDevices;

        // --- Experiencia ---
        WallpaperBox.IsChecked = connection.RdpWallpaper;
        FontSmoothingBox.IsChecked = connection.RdpFontSmoothing;
        CompositionBox.IsChecked = connection.RdpDesktopComposition;
        WindowDragBox.IsChecked = connection.RdpWindowDrag;
        MenuAnimationBox.IsChecked = connection.RdpMenuAnimation;
        VisualStylesBox.IsChecked = connection.RdpVisualStyles;
        BitmapCacheBox.IsChecked = connection.RdpBitmapCache;
        AutoReconnectBox.IsChecked = connection.RdpAutoReconnect;

        // --- Avanzado ---
        AuthBox.SelectedIndex = Math.Clamp(connection.RdpAuthLevel, 0, 2);
        AdminBox.IsChecked = connection.RdpAdminSession;
        GatewayModeBox.SelectedIndex = Math.Clamp(connection.RdpGatewayMode, 0, 2);
        GatewayHostBox.Text = connection.RdpGatewayHost;
        GatewaySameBox.IsChecked = connection.RdpGatewaySameCredentials;
        GatewayUserBox.Text = connection.RdpGatewayUserName;
        GatewayDomainBox.Text = connection.RdpGatewayDomain;
        GatewayPasswordBox.Password = Secrets.Unprotect(connection.RdpGatewayPasswordProtected);

        ShowKindFields();
        OnSizeChanged(this, null!);
        OnGatewayChanged(this, null!);
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private ConnectionKind Kind => KindBox.SelectedIndex == 1 ? ConnectionKind.Ssh : ConnectionKind.Rdp;

    private void OnKindChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        // Al cambiar de tipo, el puerto por defecto sigue al tipo si el usuario no lo habia tocado.
        if (PortBox.Text == "3389" || PortBox.Text == "22" || PortBox.Text.Length == 0)
            PortBox.Text = Kind == ConnectionKind.Ssh ? "22" : "3389";
        ShowKindFields();
    }

    /// <summary>SSH solo tiene General; las otras pestañas son las de mstsc.</summary>
    private void ShowKindFields()
    {
        var ssh = Kind == ConnectionKind.Ssh;
        SshPanel.Visibility = ssh ? Visibility.Visible : Visibility.Collapsed;
        DomainPanel.Visibility = ssh ? Visibility.Collapsed : Visibility.Visible;
        foreach (var tab in new[] { DisplayTab, ResourcesTab, ExperienceTab, AdvancedTab })
            tab.Visibility = ssh ? Visibility.Collapsed : Visibility.Visible;
        if (ssh)
            Sections.SelectedItem = GeneralTab;
    }

    private void OnSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomSizePanel is null)
            return;
        CustomSizePanel.Visibility = SizeBox.SelectedIndex == SizeBox.Items.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnGatewayChanged(object sender, RoutedEventArgs e)
    {
        if (GatewayPanel is null || GatewayCredsPanel is null)
            return;
        GatewayPanel.Visibility = GatewayModeBox.SelectedIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
        GatewayCredsPanel.Visibility = GatewaySameBox.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "OpenSSH / PEM|*;*.pem;*.key;*.ppk|*.*|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
            KeyBox.Text = dialog.FileName;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var host = HostBox.Text.Trim();
        if (name.Length == 0 || host.Length == 0)
        {
            StatusText.Text = Loc.Get("NameRequired");
            Sections.SelectedItem = GeneralTab;
            return;
        }

        var c = _connection;

        // --- General ---
        c.Name = name;
        c.Kind = Kind;
        c.Folder = (FolderBox.Text ?? string.Empty).Trim().Trim('/');
        c.Host = host;
        c.Port = int.TryParse(PortBox.Text.Trim(), out var port) && port is > 0 and < 65536 ? port : c.DefaultPort;
        c.UserName = UserBox.Text.Trim();
        c.Domain = DomainBox.Text.Trim();
        c.PasswordProtected = Secrets.Protect(PasswordBox.Password);
        c.PrivateKeyPath = Kind == ConnectionKind.Ssh ? KeyBox.Text.Trim() : string.Empty;
        c.Notes = NotesBox.Text.Trim();

        // --- Pantalla ---
        if (SizeBox.SelectedIndex == 0)
        {
            c.RdpSmartSizing = true;
            c.RdpWidth = c.RdpHeight = 0;
        }
        else
        {
            c.RdpSmartSizing = false;
            if (SizeBox.SelectedIndex == SizeBox.Items.Count - 1)
            {
                if (!int.TryParse(WidthBox.Text.Trim(), out var w) || !int.TryParse(HeightBox.Text.Trim(), out var h) || w < 200 || h < 200)
                {
                    StatusText.Text = Loc.Get("RdpSizeInvalid");
                    Sections.SelectedItem = DisplayTab;
                    return;
                }
                (c.RdpWidth, c.RdpHeight) = (w, h);
            }
            else
            {
                (c.RdpWidth, c.RdpHeight) = Sizes[SizeBox.SelectedIndex];
            }
        }
        c.RdpColorDepth = ColorBox.SelectedIndex switch { 0 => 15, 1 => 16, 2 => 24, _ => 32 };
        c.RdpMultiMonitor = MultiMonitorBox.IsChecked == true;
        c.RdpConnectionBar = ConnectionBarBox.IsChecked == true;

        // --- Recursos locales ---
        c.RdpAudioMode = Math.Max(0, AudioBox.SelectedIndex);
        c.RdpAudioCapture = AudioCaptureBox.IsChecked == true;
        c.RdpKeyboardMode = Math.Max(0, KeyboardBox.SelectedIndex);
        c.RdpPrinters = PrintersBox.IsChecked == true;
        c.RdpClipboard = ClipboardBox.IsChecked == true;
        c.RdpDrives = DrivesBox.IsChecked == true;
        c.RdpSmartCards = SmartCardsBox.IsChecked == true;
        c.RdpPorts = PortsBox.IsChecked == true;
        c.RdpDevices = DevicesBox.IsChecked == true;

        // --- Experiencia ---
        c.RdpWallpaper = WallpaperBox.IsChecked == true;
        c.RdpFontSmoothing = FontSmoothingBox.IsChecked == true;
        c.RdpDesktopComposition = CompositionBox.IsChecked == true;
        c.RdpWindowDrag = WindowDragBox.IsChecked == true;
        c.RdpMenuAnimation = MenuAnimationBox.IsChecked == true;
        c.RdpVisualStyles = VisualStylesBox.IsChecked == true;
        c.RdpBitmapCache = BitmapCacheBox.IsChecked == true;
        c.RdpAutoReconnect = AutoReconnectBox.IsChecked == true;

        // --- Avanzado ---
        c.RdpAuthLevel = Math.Max(0, AuthBox.SelectedIndex);
        c.RdpAdminSession = AdminBox.IsChecked == true;
        c.RdpGatewayMode = Math.Max(0, GatewayModeBox.SelectedIndex);
        c.RdpGatewayHost = GatewayHostBox.Text.Trim();
        c.RdpGatewaySameCredentials = GatewaySameBox.IsChecked == true;
        c.RdpGatewayUserName = GatewayUserBox.Text.Trim();
        c.RdpGatewayDomain = GatewayDomainBox.Text.Trim();
        c.RdpGatewayPasswordProtected = Secrets.Protect(GatewayPasswordBox.Password);

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
