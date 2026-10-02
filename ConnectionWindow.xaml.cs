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
        KindBox.SelectedIndex = ConnectionForm.KindIndex(connection.Kind);
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
        SizeBox.SelectedIndex = ConnectionForm.SizeIndex(connection.RdpWidth, connection.RdpHeight, connection.RdpSmartSizing);
        WidthBox.Text = connection.RdpWidth > 0 ? connection.RdpWidth.ToString() : string.Empty;
        HeightBox.Text = connection.RdpHeight > 0 ? connection.RdpHeight.ToString() : string.Empty;
        ColorBox.SelectedIndex = ConnectionForm.ColorIndex(connection.RdpColorDepth);
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

    private ConnectionKind Kind => ConnectionForm.KindAt(KindBox.SelectedIndex);

    private int DefaultPort() => ConnectionForm.DefaultPort(Kind, Math.Max(0, FtpsBox.SelectedIndex));

    private void OnKindChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        // Al cambiar de tipo, el puerto por defecto sigue al tipo si el usuario no lo habia tocado.
        FollowDefaultPort();
        ShowKindFields();
    }

    private void OnFtpsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        // FTPS implicito va por el 990; al volver a explicito o sin cifrar, al 21.
        FollowDefaultPort();
    }

    private void FollowDefaultPort()
    {
        if (ConnectionForm.PortFollowsKind(PortBox.Text))
            PortBox.Text = DefaultPort().ToString();
    }

    private static Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Lo que se ve segun el tipo (<see cref="ConnectionForm.FieldsFor"/>).</summary>
    private void ShowKindFields()
    {
        var kind = Kind;
        var fields = ConnectionForm.FieldsFor(kind);
        SshPanel.Visibility = Shown(fields.Ssh);
        DomainPanel.Visibility = Shown(fields.Domain);
        FtpPanel.Visibility = Shown(fields.Ftp);
        FilesPanel.Visibility = Shown(fields.Files);
        ScpBox.Visibility = Shown(fields.Scp);
        foreach (var tab in new[] { DisplayTab, ResourcesTab, ExperienceTab, AdvancedTab })
            tab.Visibility = Shown(fields.RdpTabs);
        TransfersTab.Visibility = Shown(fields.Transfers);
        FtpOptionsPanel.Visibility = Shown(fields.Ftp);

        var selected = ReferenceEquals(Sections.SelectedItem, GeneralTab) ? ConnectionForm.Tab.General
            : ReferenceEquals(Sections.SelectedItem, TransfersTab) ? ConnectionForm.Tab.Transfers
            : ConnectionForm.Tab.Rdp;
        if (ConnectionForm.BackToGeneral(kind, selected))
            Sections.SelectedItem = GeneralTab;
    }

    private void OnSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomSizePanel is null)
            return;
        CustomSizePanel.Visibility = Shown(SizeBox.SelectedIndex == ConnectionForm.CustomSizeIndex);
    }

    private void OnGatewayChanged(object sender, RoutedEventArgs e)
    {
        if (GatewayPanel is null || GatewayCredsPanel is null)
            return;
        GatewayPanel.Visibility = Shown(GatewayModeBox.SelectedIndex > 0);
        GatewayCredsPanel.Visibility = Shown(GatewaySameBox.IsChecked != true);
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (Dialogs.PickFiles(this, "SSH / PEM|*;*.pem;*.key;*.ppk|*.*|*.*", false) is [var file, ..])
            KeyBox.Text = file;
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

        // El tamaño a medida se comprueba antes de tocar nada: si esta mal, la conexion queda como estaba.
        (int W, int H)? customSize = null;
        if (SizeBox.SelectedIndex == ConnectionForm.CustomSizeIndex)
        {
            customSize = ConnectionForm.ParseCustomSize(WidthBox.Text, HeightBox.Text);
            if (customSize is null)
            {
                StatusText.Text = Loc.Get("RdpSizeInvalid");
                Sections.SelectedItem = DisplayTab;
                return;
            }
        }

        var c = _connection;
        var kind = Kind;

        // --- General ---
        c.Name = name;
        c.Kind = kind;
        c.Folder = (FolderBox.Text ?? string.Empty).Trim().Trim('/');
        c.Host = host;
        c.Port = ConnectionForm.ParsePort(PortBox.Text, DefaultPort());
        c.UserName = UserBox.Text.Trim();
        c.Domain = DomainBox.Text.Trim();
        c.PasswordProtected = Secrets.Protect(PasswordBox.Password);
        c.PrivateKeyPath = ConnectionForm.FieldsFor(kind).Ssh ? KeyBox.Text.Trim() : string.Empty;
        c.Notes = NotesBox.Text.Trim();

        // --- Pantalla completa / transferencias ---
        c.FullScreenScreen = Math.Max(0, ScreenBox.SelectedIndex);
        c.TransferParallel = ParallelBox.SelectedIndex + 1;
        c.TransferRetries = Math.Max(0, RetriesBox.SelectedIndex);
        c.TransferOnConflict = Math.Max(0, ConflictBox.SelectedIndex);
        c.TransferPreserveTimes = PreserveTimesBox.IsChecked == true;
        c.FilesShowHidden = ShowHiddenBox.IsChecked == true;
        c.FilesKeepAliveSeconds = ConnectionForm.ParseKeepAlive(KeepAliveBox.Text);
        c.FilesTimeoutSeconds = ConnectionForm.ParseTimeout(TimeoutBox.Text);
        c.FtpPassive = FtpModeBox.SelectedIndex != 1;
        c.FtpUtf8 = FtpEncodingBox.SelectedIndex != 1;

        // --- Ficheros ---
        c.FtpsMode = kind == ConnectionKind.Ftp ? Math.Max(0, FtpsBox.SelectedIndex) : 0;
        c.UseScp = kind == ConnectionKind.Sftp && ScpBox.IsChecked == true;
        c.RemotePath = RemotePathBox.Text.Trim();
        c.LocalPath = LocalPathBox.Text.Trim();

        // --- Pantalla ---
        // Ajustar a la ventana (0), uno de la lista o a medida.
        c.RdpSmartSizing = SizeBox.SelectedIndex <= 0;
        (c.RdpWidth, c.RdpHeight) = customSize ?? ConnectionForm.Sizes[Math.Max(0, SizeBox.SelectedIndex)];
        c.RdpColorDepth = ConnectionForm.ColorDepthAt(ColorBox.SelectedIndex);
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
