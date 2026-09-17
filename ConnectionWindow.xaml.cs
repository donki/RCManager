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
        KindBox.SelectedIndex = connection.Kind switch { ConnectionKind.Ssh => 1, ConnectionKind.Sftp => 2, ConnectionKind.Ftp => 3, _ => 0 };
        FolderBox.ItemsSource = folders;
        FolderBox.Text = connection.Folder;
        HostBox.Text = connection.Host;
        PortBox.Text = connection.Port.ToString();
        UserBox.Text = connection.UserName;
        DomainBox.Text = connection.Domain;
        PasswordBox.Password = Secrets.Unprotect(connection.PasswordProtected);
        KeyBox.Text = connection.PrivateKeyPath;
        NotesBox.Text = connection.Notes;

        // --- Pantalla completa: en que monitor ---
        ScreenBox.Items.Add(new ComboBoxItem { Content = Loc.Get("ScreenCurrent") });
        var screens = System.Windows.Forms.Screen.AllScreens;
        for (var i = 0; i < screens.Length; i++)
            ScreenBox.Items.Add(new ComboBoxItem { Content = Loc.Format("ScreenN", i + 1, screens[i].Bounds.Width, screens[i].Bounds.Height, screens[i].Primary ? " · " + Loc.Get("ScreenPrimary") : string.Empty) });
        ScreenBox.SelectedIndex = Math.Clamp(connection.FullScreenScreen, 0, screens.Length);

        // --- Transferencias ---
        ParallelBox.SelectedIndex = Math.Clamp(connection.TransferParallel, 1, 8) - 1;
        RetriesBox.SelectedIndex = Math.Clamp(connection.TransferRetries, 0, 5);
        ConflictBox.SelectedIndex = Math.Clamp(connection.TransferOnConflict, 0, 2);
        PreserveTimesBox.IsChecked = connection.TransferPreserveTimes;
        ShowHiddenBox.IsChecked = connection.FilesShowHidden;
        KeepAliveBox.Text = connection.FilesKeepAliveSeconds.ToString();
        TimeoutBox.Text = connection.FilesTimeoutSeconds.ToString();
        FtpModeBox.SelectedIndex = connection.FtpPassive ? 0 : 1;
        FtpEncodingBox.SelectedIndex = connection.FtpUtf8 ? 0 : 1;

        // --- Ficheros ---
        FtpsBox.SelectedIndex = Math.Clamp(connection.FtpsMode, 0, 2);
        ScpBox.IsChecked = connection.UseScp;
        RemotePathBox.Text = connection.RemotePath;
        LocalPathBox.Text = connection.LocalPath;

        // --- Pantalla ---
        var size = Array.FindIndex(Sizes, s => s.W == connection.RdpWidth && s.H == connection.RdpHeight);
        SizeBox.SelectedIndex = connection.RdpSmartSizing && connection.RdpWidth == 0 ? 0 : size > 0 ? size : SizeBox.Items.Count - 1;
        WidthBox.Text = connection.RdpWidth > 0 ? connection.RdpWidth.ToString() : string.Empty;
        HeightBox.Text = connection.RdpHeight > 0 ? connection.RdpHeight.ToString() : string.Empty;
        ColorBox.SelectedIndex = connection.RdpColorDepth switch { 15 => 0, 16 => 1, 24 => 2, _ => 3 };
        StartFullScreenBox.IsChecked = connection.RdpStartFullScreen;
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

    /// <summary>Pestaña con la que se abre (0 = General).</summary>
    public int InitialTab
    {
        set
        {
            if (value > 0 && value < Sections.Items.Count)
                Sections.SelectedIndex = value;
        }
    }

    private ConnectionKind Kind => KindBox.SelectedIndex switch { 1 => ConnectionKind.Ssh, 2 => ConnectionKind.Sftp, 3 => ConnectionKind.Ftp, _ => ConnectionKind.Rdp };

    private static readonly string[] DefaultPorts = ["3389", "22", "21", "990"];

    private void OnKindChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        // Al cambiar de tipo, el puerto por defecto sigue al tipo si el usuario no lo habia tocado.
        if (DefaultPorts.Contains(PortBox.Text) || PortBox.Text.Length == 0)
            PortBox.Text = DefaultPort().ToString();
        ShowKindFields();
    }

    private int DefaultPort() => Kind switch
    {
        ConnectionKind.Ssh or ConnectionKind.Sftp => 22,
        ConnectionKind.Ftp => FtpsBox.SelectedIndex == 2 ? 990 : 21,
        _ => 3389,
    };

    private void OnFtpsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        // FTPS implicito va por el 990; al volver a explicito o sin cifrar, al 21.
        if (DefaultPorts.Contains(PortBox.Text) || PortBox.Text.Length == 0)
            PortBox.Text = DefaultPort().ToString();
    }

    /// <summary>Solo RDP tiene las pestañas de mstsc; SSH y SFTP llevan clave privada; FTP el modo FTPS.</summary>
    private void ShowKindFields()
    {
        var kind = Kind;
        var rdp = kind == ConnectionKind.Rdp;
        var ssh = kind is ConnectionKind.Ssh or ConnectionKind.Sftp;
        var files = kind is ConnectionKind.Sftp or ConnectionKind.Ftp;
        SshPanel.Visibility = ssh ? Visibility.Visible : Visibility.Collapsed;
        DomainPanel.Visibility = rdp ? Visibility.Visible : Visibility.Collapsed;
        FtpPanel.Visibility = kind == ConnectionKind.Ftp ? Visibility.Visible : Visibility.Collapsed;
        FilesPanel.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
        ScpBox.Visibility = kind == ConnectionKind.Sftp ? Visibility.Visible : Visibility.Collapsed;
        foreach (var tab in new[] { DisplayTab, ResourcesTab, ExperienceTab, AdvancedTab })
            tab.Visibility = rdp ? Visibility.Visible : Visibility.Collapsed;
        TransfersTab.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
        FtpOptionsPanel.Visibility = kind == ConnectionKind.Ftp ? Visibility.Visible : Visibility.Collapsed;
        if (!rdp && !files)
            Sections.SelectedItem = GeneralTab;
        else if (rdp && ReferenceEquals(Sections.SelectedItem, TransfersTab) || files && !ReferenceEquals(Sections.SelectedItem, GeneralTab) && !ReferenceEquals(Sections.SelectedItem, TransfersTab))
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
        c.PrivateKeyPath = Kind is ConnectionKind.Ssh or ConnectionKind.Sftp ? KeyBox.Text.Trim() : string.Empty;
        c.Notes = NotesBox.Text.Trim();

        // --- Pantalla completa / transferencias ---
        c.FullScreenScreen = Math.Max(0, ScreenBox.SelectedIndex);
        c.TransferParallel = ParallelBox.SelectedIndex + 1;
        c.TransferRetries = Math.Max(0, RetriesBox.SelectedIndex);
        c.TransferOnConflict = Math.Max(0, ConflictBox.SelectedIndex);
        c.TransferPreserveTimes = PreserveTimesBox.IsChecked == true;
        c.FilesShowHidden = ShowHiddenBox.IsChecked == true;
        c.FilesKeepAliveSeconds = int.TryParse(KeepAliveBox.Text.Trim(), out var ka) && ka >= 0 ? ka : 30;
        c.FilesTimeoutSeconds = int.TryParse(TimeoutBox.Text.Trim(), out var to) && to >= 5 ? to : 20;
        c.FtpPassive = FtpModeBox.SelectedIndex != 1;
        c.FtpUtf8 = FtpEncodingBox.SelectedIndex != 1;

        // --- Ficheros ---
        c.FtpsMode = Kind == ConnectionKind.Ftp ? Math.Max(0, FtpsBox.SelectedIndex) : 0;
        c.UseScp = Kind == ConnectionKind.Sftp && ScpBox.IsChecked == true;
        c.RemotePath = RemotePathBox.Text.Trim();
        c.LocalPath = LocalPathBox.Text.Trim();

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
        c.RdpStartFullScreen = StartFullScreenBox.IsChecked == true;
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
