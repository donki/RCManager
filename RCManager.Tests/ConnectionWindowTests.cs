using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SocRcManager.Localization;
using SocRcManager.Models;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>
/// El editor de conexiones de verdad (fuera de la pantalla): lo que carga, lo que enseña segun el
/// tipo, el puerto que sigue al tipo, las comprobaciones al guardar y lo que escribe en la conexion.
/// </summary>
public sealed class ConnectionWindowTests : UiTest
{
    private static readonly string[] Folders = ["Clientes", "Clientes/Acme", "Casa"];

    private static string Snapshot(Connection c) => JsonSerializer.Serialize(c);

    /// <summary>Abre el editor como modal, deja que <paramref name="fill"/> lo maneje y pulsa guardar (o cancelar).</summary>
    private static (bool? Result, string Status, int Tab) Edit(Connection c, Action<ConnectionWindow> fill, bool save = true, int initialTab = 0)
    {
        var status = string.Empty;
        var tab = -1;
        Ui.Answer<ConnectionWindow>(w =>
        {
            fill(w);
            Ui.Click(save ? w.SaveButton : w.CancelButton);
            status = w.StatusText.Text;
            tab = w.Sections.SelectedIndex;
        });
        var result = Ui.Run(() => Dialogs.ShowModal(new ConnectionWindow(c, Folders) { InitialTab = initialTab }));
        Assert.Equal(0, Ui.PendingAnswers);
        return (result, status, tab);
    }

    private static ConnectionWindow Open(Connection c) => Ui.Run(() => Ui.Show(new ConnectionWindow(c, Folders)));

    private static Connection Rdp() => new()
    {
        Name = "Servidor",
        Kind = ConnectionKind.Rdp,
        Folder = "Clientes/Acme",
        Host = "srv.acme.lan",
        Port = 3390,
        UserName = "ana",
        Domain = "ACME",
        PasswordProtected = Secrets.Protect("secreta"),
        Notes = "notas",
        FullScreenScreen = 1,
        RdpSmartSizing = false,
        RdpWidth = 1700,
        RdpHeight = 950,
        RdpColorDepth = 16,
        RdpMultiMonitor = true,
        RdpConnectionBar = false,
        RdpAudioMode = 2,
        RdpAudioCapture = true,
        RdpKeyboardMode = 1,
        RdpPrinters = false,
        RdpClipboard = false,
        RdpDrives = false,
        RdpSmartCards = false,
        RdpPorts = true,
        RdpDevices = true,
        RdpWallpaper = false,
        RdpFontSmoothing = false,
        RdpDesktopComposition = false,
        RdpWindowDrag = false,
        RdpMenuAnimation = false,
        RdpVisualStyles = false,
        RdpBitmapCache = false,
        RdpAutoReconnect = false,
        RdpAuthLevel = 2,
        RdpAdminSession = true,
        RdpGatewayMode = 1,
        RdpGatewayHost = "gw.acme.lan",
        RdpGatewaySameCredentials = false,
        RdpGatewayUserName = "gwuser",
        RdpGatewayDomain = "GW",
        RdpGatewayPasswordProtected = Secrets.Protect("gwpass"),
    };

    [Fact]
    public void Carga_todo_lo_de_una_conexion_RDP()
    {
        var w = Open(Rdp());
        Ui.Run(() =>
        {
            Assert.Equal("Servidor", w.NameBox.Text);
            Assert.Equal(0, w.KindBox.SelectedIndex);
            Assert.Equal("Clientes/Acme", w.FolderBox.Text);
            Assert.Equal(Folders, w.FolderBox.ItemsSource);
            Assert.Equal(("srv.acme.lan", "3390", "ana", "ACME"), (w.HostBox.Text, w.PortBox.Text, w.UserBox.Text, w.DomainBox.Text));
            Assert.Equal("secreta", w.PasswordBox.Password);
            Assert.Equal("notas", w.NotesBox.Text);

            // Tamaño a medida: se ve el panel con los numeros.
            Assert.Equal(ConnectionForm.CustomSizeIndex, w.SizeBox.SelectedIndex);
            Assert.Equal(("1700", "950"), (w.WidthBox.Text, w.HeightBox.Text));
            Assert.Equal(Visibility.Visible, w.CustomSizePanel.Visibility);
            Assert.Equal(1, w.ColorBox.SelectedIndex);
            Assert.True(w.MultiMonitorBox.IsChecked);
            Assert.False(w.ConnectionBarBox.IsChecked);

            Assert.Equal(2, w.AudioBox.SelectedIndex);
            Assert.Equal(1, w.KeyboardBox.SelectedIndex);
            Assert.True(w.AudioCaptureBox.IsChecked);
            Assert.False(w.PrintersBox.IsChecked);
            Assert.True(w.PortsBox.IsChecked);
            Assert.True(w.DevicesBox.IsChecked);
            Assert.False(w.WallpaperBox.IsChecked);
            Assert.False(w.AutoReconnectBox.IsChecked);

            Assert.Equal(2, w.AuthBox.SelectedIndex);
            Assert.True(w.AdminBox.IsChecked);
            Assert.Equal(1, w.GatewayModeBox.SelectedIndex);
            Assert.Equal(("gw.acme.lan", "gwuser", "GW"), (w.GatewayHostBox.Text, w.GatewayUserBox.Text, w.GatewayDomainBox.Text));
            Assert.Equal("gwpass", w.GatewayPasswordBox.Password);
            Assert.Equal(Visibility.Visible, w.GatewayPanel.Visibility);
            Assert.Equal(Visibility.Visible, w.GatewayCredsPanel.Visibility);

            // Lo de RDP se ve; lo de SSH y ficheros no.
            Assert.Equal(Visibility.Visible, w.DomainPanel.Visibility);
            Assert.Equal(Visibility.Visible, w.DisplayTab.Visibility);
            Assert.Equal(Visibility.Visible, w.AdvancedTab.Visibility);
            Assert.Equal(Visibility.Collapsed, w.SshPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, w.FilesPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, w.TransfersTab.Visibility);

            Assert.Equal(Loc.Get("Save"), w.SaveButton.ToolTip);
            Assert.Equal(0, w.Sections.SelectedIndex);
        });
    }

    [Fact]
    public void Las_pantallas_se_listan_detras_de_la_actual()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var w = Open(new Connection { FullScreenScreen = 99 });
        Ui.Run(() =>
        {
            Assert.Equal(screens.Length + 1, w.ScreenBox.Items.Count);
            Assert.Equal(Loc.Get("ScreenCurrent"), ((System.Windows.Controls.ComboBoxItem)w.ScreenBox.Items[0]).Content);
            Assert.StartsWith(Loc.Format("ScreenN", 1, screens[0].Bounds.Width, screens[0].Bounds.Height, string.Empty),
                (string)((System.Windows.Controls.ComboBoxItem)w.ScreenBox.Items[1]).Content);
            // Una pantalla que ya no esta: la ultima que hay.
            Assert.Equal(screens.Length, w.ScreenBox.SelectedIndex);
        });
    }

    [Fact]
    public void Una_conexion_nueva_abre_con_lo_de_siempre()
    {
        var w = Open(new Connection());
        Ui.Run(() =>
        {
            Assert.Equal("3389", w.PortBox.Text);
            Assert.Equal(0, w.SizeBox.SelectedIndex);   // ajustar a la ventana
            Assert.Equal(Visibility.Collapsed, w.CustomSizePanel.Visibility);
            Assert.Equal((string.Empty, string.Empty), (w.WidthBox.Text, w.HeightBox.Text));
            Assert.Equal(3, w.ColorBox.SelectedIndex);
            Assert.Equal(1, w.ParallelBox.SelectedIndex);   // 2 a la vez
            Assert.Equal(Visibility.Collapsed, w.GatewayPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, w.GatewayCredsPanel.Visibility);
            Assert.Equal(string.Empty, w.PasswordBox.Password);
        });
    }

    [Fact]
    public void Cambiar_de_tipo_cambia_el_puerto_y_lo_que_se_ve()
    {
        var w = Open(new Connection { Name = "x", Host = "h" });
        Ui.Run(() =>
        {
            w.Sections.SelectedItem = w.DisplayTab;
            w.KindBox.SelectedIndex = 1;   // SSH
            Assert.Equal("22", w.PortBox.Text);
            Assert.Same(w.GeneralTab, w.Sections.SelectedItem);
            Assert.Equal(Visibility.Visible, w.SshPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, w.DomainPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, w.DisplayTab.Visibility);
            Assert.Equal(Visibility.Collapsed, w.TransfersTab.Visibility);
            Assert.Equal(Visibility.Collapsed, w.FilesPanel.Visibility);

            w.KindBox.SelectedIndex = 2;   // SFTP
            Assert.Equal("22", w.PortBox.Text);
            Assert.Equal(Visibility.Visible, w.ScpBox.Visibility);
            Assert.Equal(Visibility.Visible, w.FilesPanel.Visibility);
            Assert.Equal(Visibility.Visible, w.TransfersTab.Visibility);
            Assert.Equal(Visibility.Collapsed, w.FtpPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, w.FtpOptionsPanel.Visibility);

            w.Sections.SelectedItem = w.TransfersTab;
            w.KindBox.SelectedIndex = 3;   // FTP: la pestaña de transferencias sigue
            Assert.Equal("21", w.PortBox.Text);
            Assert.Same(w.TransfersTab, w.Sections.SelectedItem);
            Assert.Equal(Visibility.Visible, w.FtpPanel.Visibility);
            Assert.Equal(Visibility.Visible, w.FtpOptionsPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, w.ScpBox.Visibility);
            Assert.Equal(Visibility.Collapsed, w.SshPanel.Visibility);

            // FTPS implicito: el 990; explicito, otra vez el 21.
            w.FtpsBox.SelectedIndex = 2;
            Assert.Equal("990", w.PortBox.Text);
            w.FtpsBox.SelectedIndex = 1;
            Assert.Equal("21", w.PortBox.Text);

            w.KindBox.SelectedIndex = 0;   // RDP: sin pestaña de transferencias
            Assert.Equal("3389", w.PortBox.Text);
            Assert.Same(w.GeneralTab, w.Sections.SelectedItem);
            Assert.Equal(Visibility.Collapsed, w.TransfersTab.Visibility);
            Assert.Equal(Visibility.Visible, w.ResourcesTab.Visibility);
        });
    }

    [Fact]
    public void Un_puerto_propio_no_se_toca_al_cambiar_de_tipo()
    {
        var w = Open(new Connection { Kind = ConnectionKind.Ftp, Port = 2121 });
        Ui.Run(() =>
        {
            w.FtpsBox.SelectedIndex = 2;
            Assert.Equal("2121", w.PortBox.Text);
            w.KindBox.SelectedIndex = 1;
            Assert.Equal("2121", w.PortBox.Text);
            // Vacio vuelve a seguir al tipo.
            w.PortBox.Text = string.Empty;
            w.KindBox.SelectedIndex = 2;
            Assert.Equal("22", w.PortBox.Text);
        });
    }

    [Fact]
    public void Con_una_pestaña_de_ficheros_elegida_pasar_a_RDP_y_a_SFTP()
    {
        var w = Open(new Connection { Kind = ConnectionKind.Sftp, Port = 22 });
        Ui.Run(() =>
        {
            Assert.Equal("22", w.PortBox.Text);
            w.KindBox.SelectedIndex = 0;
            w.Sections.SelectedItem = w.ExperienceTab;
            w.KindBox.SelectedIndex = 2;   // una pestaña de RDP con SFTP: a General
            Assert.Same(w.GeneralTab, w.Sections.SelectedItem);
        });
    }

    [Fact]
    public void Tamaño_y_puerta_de_enlace_enseñan_sus_campos()
    {
        var w = Open(new Connection());
        Ui.Run(() =>
        {
            w.SizeBox.SelectedIndex = ConnectionForm.CustomSizeIndex;
            Assert.Equal(Visibility.Visible, w.CustomSizePanel.Visibility);
            w.SizeBox.SelectedIndex = 3;
            Assert.Equal(Visibility.Collapsed, w.CustomSizePanel.Visibility);

            w.GatewayModeBox.SelectedIndex = 2;
            Assert.Equal(Visibility.Visible, w.GatewayPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, w.GatewayCredsPanel.Visibility);
            w.GatewaySameBox.IsChecked = false;
            Assert.Equal(Visibility.Visible, w.GatewayCredsPanel.Visibility);
            w.GatewaySameBox.IsChecked = true;
            Assert.Equal(Visibility.Collapsed, w.GatewayCredsPanel.Visibility);
            w.GatewayModeBox.SelectedIndex = 0;
            Assert.Equal(Visibility.Collapsed, w.GatewayPanel.Visibility);
        });
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(5, 5)]
    [InlineData(6, 0)]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(50, 0)]
    public void Pestaña_con_la_que_se_abre(int initial, int expected)
    {
        var tab = Ui.Run(() => Ui.Show(new ConnectionWindow(new Connection(), Folders) { InitialTab = initial }).Sections.SelectedIndex);
        Assert.Equal(expected, tab);
    }

    [Fact]
    public void Guardar_RDP_escribe_lo_editado_en_la_conexion()
    {
        var c = new Connection();
        var (result, status, _) = Edit(c, w =>
        {
            w.NameBox.Text = "  Oficina  ";
            w.FolderBox.Text = " /Clientes/Acme/ ";
            w.HostBox.Text = " 10.0.0.5 ";
            w.PortBox.Text = "3391";
            w.UserBox.Text = " jefe ";
            w.DomainBox.Text = " CASA ";
            w.PasswordBox.Password = "contraseña";
            w.KeyBox.Text = @"C:\claves\id_rsa";   // en RDP no se guarda
            w.NotesBox.Text = " apuntes \n";
            w.ScreenBox.SelectedIndex = 1;

            w.SizeBox.SelectedIndex = 5;
            w.ColorBox.SelectedIndex = 2;
            w.MultiMonitorBox.IsChecked = true;
            w.ConnectionBarBox.IsChecked = false;

            w.AudioBox.SelectedIndex = 1;
            w.AudioCaptureBox.IsChecked = true;
            w.KeyboardBox.SelectedIndex = 0;
            w.PrintersBox.IsChecked = false;
            w.ClipboardBox.IsChecked = false;
            w.DrivesBox.IsChecked = false;
            w.SmartCardsBox.IsChecked = false;
            w.PortsBox.IsChecked = true;
            w.DevicesBox.IsChecked = true;

            w.WallpaperBox.IsChecked = false;
            w.FontSmoothingBox.IsChecked = false;
            w.CompositionBox.IsChecked = false;
            w.WindowDragBox.IsChecked = false;
            w.MenuAnimationBox.IsChecked = false;
            w.VisualStylesBox.IsChecked = false;
            w.BitmapCacheBox.IsChecked = false;
            w.AutoReconnectBox.IsChecked = false;

            w.AuthBox.SelectedIndex = 0;
            w.AdminBox.IsChecked = true;
            w.GatewayModeBox.SelectedIndex = 2;
            w.GatewayHostBox.Text = " gw.lan ";
            w.GatewaySameBox.IsChecked = false;
            w.GatewayUserBox.Text = " gu ";
            w.GatewayDomainBox.Text = " GD ";
            w.GatewayPasswordBox.Password = "gp";
        });

        Assert.True(result);
        Assert.Equal(string.Empty, status);
        Assert.Equal(("Oficina", ConnectionKind.Rdp, "Clientes/Acme", "10.0.0.5", 3391), (c.Name, c.Kind, c.Folder, c.Host, c.Port));
        Assert.Equal(("jefe", "CASA", "apuntes"), (c.UserName, c.Domain, c.Notes));
        Assert.NotEqual("contraseña", c.PasswordProtected);   // cifrada
        Assert.Equal("contraseña", Secrets.Unprotect(c.PasswordProtected));
        Assert.Equal(string.Empty, c.PrivateKeyPath);
        Assert.Equal(1, c.FullScreenScreen);

        Assert.False(c.RdpSmartSizing);
        Assert.Equal((1920, 1080), (c.RdpWidth, c.RdpHeight));
        Assert.Equal(24, c.RdpColorDepth);
        Assert.True(c.RdpMultiMonitor);
        Assert.False(c.RdpConnectionBar);

        Assert.Equal((1, 0), (c.RdpAudioMode, c.RdpKeyboardMode));
        Assert.True(c.RdpAudioCapture);
        Assert.False(c.RdpPrinters || c.RdpClipboard || c.RdpDrives || c.RdpSmartCards);
        Assert.True(c.RdpPorts && c.RdpDevices);

        Assert.False(c.RdpWallpaper || c.RdpFontSmoothing || c.RdpDesktopComposition || c.RdpWindowDrag
            || c.RdpMenuAnimation || c.RdpVisualStyles || c.RdpBitmapCache || c.RdpAutoReconnect);

        Assert.Equal((0, 2), (c.RdpAuthLevel, c.RdpGatewayMode));
        Assert.True(c.RdpAdminSession);
        Assert.Equal(("gw.lan", false, "gu", "GD"), (c.RdpGatewayHost, c.RdpGatewaySameCredentials, c.RdpGatewayUserName, c.RdpGatewayDomain));
        Assert.Equal("gp", Secrets.Unprotect(c.RdpGatewayPasswordProtected));

        // Lo de ficheros no se aplica a RDP.
        Assert.Equal(0, c.FtpsMode);
        Assert.False(c.UseScp);
    }

    [Fact]
    public void Guardar_ajustar_a_la_ventana_y_a_medida()
    {
        var c = new Connection { RdpSmartSizing = false, RdpWidth = 1024, RdpHeight = 768 };
        Assert.True(Edit(c, w => { w.NameBox.Text = "n"; w.HostBox.Text = "h"; w.SizeBox.SelectedIndex = 0; }).Result);
        Assert.True(c.RdpSmartSizing);
        Assert.Equal((0, 0), (c.RdpWidth, c.RdpHeight));

        Assert.True(Edit(c, w =>
        {
            w.SizeBox.SelectedIndex = ConnectionForm.CustomSizeIndex;
            w.WidthBox.Text = " 1700 ";
            w.HeightBox.Text = "950";
        }).Result);
        Assert.False(c.RdpSmartSizing);
        Assert.Equal((1700, 950), (c.RdpWidth, c.RdpHeight));
    }

    [Fact]
    public void Guardar_SFTP_con_clave_transferencias_y_SCP()
    {
        var c = new Connection();
        var file = Dir.File("id_ed25519", "clave");
        Ui.PickedFiles = [file];
        var (result, _, _) = Edit(c, w =>
        {
            w.NameBox.Text = "nas";
            w.HostBox.Text = "nas.lan";
            w.KindBox.SelectedIndex = 2;
            Ui.Click(w.BrowseButton);
            w.ScpBox.IsChecked = true;
            w.FtpsBox.SelectedIndex = 2;   // no se aplica a SFTP
            w.RemotePathBox.Text = " /srv ";
            w.LocalPathBox.Text = @" C:\bajadas ";
            w.ParallelBox.SelectedIndex = 3;
            w.RetriesBox.SelectedIndex = 4;
            w.ConflictBox.SelectedIndex = 2;
            w.PreserveTimesBox.IsChecked = false;
            w.ShowHiddenBox.IsChecked = true;
            w.KeepAliveBox.Text = "0";
            w.TimeoutBox.Text = "3";   // menos de 5: el de siempre
        });

        Assert.True(result);
        Assert.Equal((ConnectionKind.Sftp, 22), (c.Kind, c.Port));
        Assert.Equal(file, c.PrivateKeyPath);
        Assert.True(c.UseScp);
        Assert.Equal(0, c.FtpsMode);
        Assert.Equal(("/srv", @"C:\bajadas"), (c.RemotePath, c.LocalPath));
        Assert.Equal((4, 4, 2), (c.TransferParallel, c.TransferRetries, c.TransferOnConflict));
        Assert.False(c.TransferPreserveTimes);
        Assert.True(c.FilesShowHidden);
        Assert.Equal((0, 20), (c.FilesKeepAliveSeconds, c.FilesTimeoutSeconds));
    }

    [Fact]
    public void Guardar_FTP_implicito_con_un_puerto_malo_usa_el_990()
    {
        // El puerto por defecto sale de lo elegido en la ventana (FTPS implicito), no de lo que
        // tenia la conexion antes de guardar.
        var c = new Connection { Kind = ConnectionKind.Ftp, FtpsMode = 0, Port = 21 };
        var (result, _, _) = Edit(c, w =>
        {
            w.NameBox.Text = "ftp";
            w.HostBox.Text = "ftp.lan";
            w.FtpsBox.SelectedIndex = 2;
            w.PortBox.Text = "99999";
            w.ScpBox.IsChecked = true;   // solo SFTP
            w.KeyBox.Text = "no";        // solo SSH
            w.FtpModeBox.SelectedIndex = 1;
            w.FtpEncodingBox.SelectedIndex = 1;
            w.KeepAliveBox.Text = "x";
            w.TimeoutBox.Text = "45";
        });

        Assert.True(result);
        Assert.Equal((2, 990), (c.FtpsMode, c.Port));
        Assert.False(c.UseScp);
        Assert.Equal(string.Empty, c.PrivateKeyPath);
        Assert.False(c.FtpPassive);
        Assert.False(c.FtpUtf8);
        Assert.Equal((30, 45), (c.FilesKeepAliveSeconds, c.FilesTimeoutSeconds));
    }

    [Theory]
    [InlineData("", "h")]
    [InlineData("n", "   ")]
    public void Sin_nombre_o_servidor_no_guarda_y_vuelve_a_General(string name, string host)
    {
        var c = Rdp();
        var before = Snapshot(c);
        var (result, status, tab) = Edit(c, w =>
        {
            w.NameBox.Text = name;
            w.HostBox.Text = host;
            w.Sections.SelectedItem = w.AdvancedTab;
        });
        Assert.NotEqual(true, result);
        Assert.Equal(Loc.Get("NameRequired"), status);
        Assert.Equal(0, tab);
        Assert.Equal(before, Snapshot(c));
    }

    [Theory]
    [InlineData("199", "800")]
    [InlineData("1700", "")]
    [InlineData("mucho", "800")]
    public void Tamaño_a_medida_malo_no_toca_la_conexion(string width, string height)
    {
        var c = Rdp();
        var before = Snapshot(c);
        var (result, status, tab) = Edit(c, w =>
        {
            w.NameBox.Text = "Otro nombre";
            w.HostBox.Text = "otro.lan";
            w.PasswordBox.Password = "otra";
            w.WidthBox.Text = width;
            w.HeightBox.Text = height;
        });
        Assert.NotEqual(true, result);
        Assert.Equal(Loc.Get("RdpSizeInvalid"), status);
        Assert.Equal(2, tab);   // Pantalla
        Assert.Equal(before, Snapshot(c));
    }

    [Fact]
    public void Cancelar_no_cambia_nada()
    {
        var c = Rdp();
        var before = Snapshot(c);
        var (result, _, _) = Edit(c, w =>
        {
            w.NameBox.Text = "cambiado";
            w.KindBox.SelectedIndex = 3;
            w.PasswordBox.Password = "x";
        }, save: false);
        Assert.False(result);
        Assert.Equal(before, Snapshot(c));
    }

    [Fact]
    public void Examinar_la_clave_cancelado_deja_la_que_habia()
    {
        Ui.PickedFiles = null;
        var w = Open(new Connection { Kind = ConnectionKind.Ssh, PrivateKeyPath = @"C:\vieja" });
        Ui.Run(() =>
        {
            Ui.Click(w.BrowseButton);
            Assert.Equal(@"C:\vieja", w.KeyBox.Text);
            Ui.PickedFiles = [];
            Ui.Click(w.BrowseButton);
            Assert.Equal(@"C:\vieja", w.KeyBox.Text);
            Ui.PickedFiles = [@"C:\nueva.pem", @"C:\otra.pem"];
            Ui.Click(w.BrowseButton);
            Assert.Equal(@"C:\nueva.pem", w.KeyBox.Text);
            Assert.Equal(Loc.Get("BrowseTooltip"), w.BrowseButton.ToolTip);
        });
    }

    [Fact]
    public void En_ingles_los_textos_de_los_botones()
    {
        Ui.Run(() => Lang.Set("en"));
        var w = Open(new Connection());
        Ui.Run(() =>
        {
            Assert.Equal("Cancel", w.CancelButton.ToolTip);
            Assert.Equal(Loc.Get("ScreenCurrent"), ((System.Windows.Controls.ComboBoxItem)w.ScreenBox.Items[0]).Content);
        });
    }
    // ------------------------------------------------------------------ copiar de otra conexion

    private static Connection Ssh() => new()
    {
        Name = "Nuevo",
        Kind = ConnectionKind.Ssh,
        Folder = "Casa",
        Host = "nuevo.lan",
        Port = 22,
        UserName = "yo",
        Notes = "mis notas",
    };

    [Fact]
    public void Copiar_de_otra_conexion_trae_todo_menos_nombre_carpeta_servidor_y_notas()
    {
        var target = Ssh();
        var id = target.Id;
        var others = new List<Connection> { Rdp(), new() { Name = "Otra", Host = "otra.lan", Folder = "Casa" }, target };
        var copied = string.Empty;
        var listed = -1;

        Ui.Answer<ConnectionWindow>(w =>
        {
            Assert.Equal(Visibility.Visible, w.CopyFromButton.Visibility);
            Assert.Equal(Loc.Get("CopyFromTooltip"), w.CopyFromButton.ToolTip);
            Ui.Click(w.CopyFromButton);
            copied = w.CopiedText.Text;

            // Lo propio de esta conexion sigue; lo demas es de la copiada, ya en pantalla.
            Assert.Equal(("Nuevo", "Casa", "nuevo.lan", "mis notas"), (w.NameBox.Text, w.FolderBox.Text, w.HostBox.Text, w.NotesBox.Text));
            Assert.Equal(0, w.KindBox.SelectedIndex);
            Assert.Equal(("3390", "ana", "ACME", "secreta"), (w.PortBox.Text, w.UserBox.Text, w.DomainBox.Text, w.PasswordBox.Password));
            Assert.Equal(Visibility.Visible, w.DisplayTab.Visibility);
            Assert.Equal(Visibility.Collapsed, w.SshPanel.Visibility);
            Assert.Equal(Visibility.Visible, w.CustomSizePanel.Visibility);
            Assert.Equal(Visibility.Visible, w.GatewayCredsPanel.Visibility);
            Ui.Click(w.SaveButton);
        });
        Ui.Answer<ConnectionPickerWindow>(p =>
        {
            // La propia no sale; se ordenan por carpeta y nombre.
            listed = p.List.Items.Count;
            p.Search.Text = "acme";
            Assert.Single(p.List.Items);
            Assert.Equal(0, p.List.SelectedIndex);
            Ui.Click(Ui.ButtonById(p, "OkButton")!);
        });

        var result = Ui.Run(() => Dialogs.ShowModal(new ConnectionWindow(target, Folders, others)));
        Assert.Equal(0, Ui.PendingAnswers);
        Assert.True(result);
        Assert.Equal(2, listed);
        Assert.Equal(Loc.Format("CopiedFrom", "Servidor"), copied);

        Assert.Equal(id, target.Id);
        Assert.Equal(("Nuevo", "Casa", "nuevo.lan", "mis notas"), (target.Name, target.Folder, target.Host, target.Notes));
        Assert.Equal(ConnectionKind.Rdp, target.Kind);
        Assert.Equal((3390, "ana", "ACME"), (target.Port, target.UserName, target.Domain));
        Assert.Equal("secreta", Secrets.Unprotect(target.PasswordProtected));
        Assert.Equal((false, 1700, 950, 16), (target.RdpSmartSizing, target.RdpWidth, target.RdpHeight, target.RdpColorDepth));
        Assert.Equal((2, 1, true), (target.RdpAuthLevel, target.RdpGatewayMode, target.RdpAdminSession));
        Assert.Equal(("gw.acme.lan", "gwuser", "gwpass"), (target.RdpGatewayHost, target.RdpGatewayUserName, Secrets.Unprotect(target.RdpGatewayPasswordProtected)));
        Assert.False(target.RdpAutoReconnect);
    }

    [Fact]
    public void Copiar_y_cancelar_no_toca_la_conexion()
    {
        var target = Ssh();
        var before = Snapshot(target);

        Ui.Answer<ConnectionWindow>(w =>
        {
            Ui.Click(w.CopyFromButton);
            Assert.Equal(string.Empty, w.CopiedText.Text);   // cancelado en la lista: nada cambia
            Assert.Equal(1, w.KindBox.SelectedIndex);
            Ui.Click(w.CopyFromButton);
            Assert.Equal(0, w.KindBox.SelectedIndex);         // copiado, pero luego se cancela el editor
            Ui.Click(w.CancelButton);
        });
        Ui.Answer<ConnectionPickerWindow>(p => Ui.Click(Ui.ButtonById(p, "CancelButton")!));
        Ui.Answer<ConnectionPickerWindow>(p =>
        {
            Assert.False(Ui.ButtonById(p, "OkButton")!.IsEnabled);   // dos en la lista, ninguna elegida
            p.List.SelectedIndex = 1;
            Assert.True(Ui.ButtonById(p, "OkButton")!.IsEnabled);
            Ui.Click(Ui.ButtonById(p, "OkButton")!);
        });

        var result = Ui.Run(() => Dialogs.ShowModal(new ConnectionWindow(target, Folders, [new() { Name = "A", Folder = "Z" }, Rdp(), target])));
        Assert.Equal(0, Ui.PendingAnswers);
        Assert.False(result);
        Assert.Equal(before, Snapshot(target));
    }

    [Fact]
    public void En_la_lista_flecha_abajo_baja_del_buscador_y_doble_clic_elige()
    {
        var target = Ssh();
        var keys = (Down: false, Other: false);
        Ui.Answer<ConnectionWindow>(w =>
        {
            Ui.Click(w.CopyFromButton);
            Ui.Click(w.SaveButton);
        });
        Ui.Answer<ConnectionPickerWindow>(p =>
        {
            System.Windows.Input.KeyEventArgs Key(System.Windows.Input.Key k) =>
                new(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(p.Search)!, 0, k)
                { RoutedEvent = UIElement.PreviewKeyDownEvent };

            // Otra tecla no hace nada; flecha abajo elige la primera y lleva el foco a la lista.
            var other = Key(System.Windows.Input.Key.A);
            p.Search.RaiseEvent(other);
            keys.Other = other.Handled;
            var down = Key(System.Windows.Input.Key.Down);
            p.Search.RaiseEvent(down);
            keys.Down = down.Handled;
            Assert.Equal(0, p.List.SelectedIndex);

            // Doble clic fuera de una fila no elige; sobre la fila, si.
            var mouse = System.Windows.Input.Mouse.PrimaryDevice;
            p.List.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(mouse, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = Control.MouseDoubleClickEvent, Source = p.List });
            Assert.True(p.IsVisible);
            var row = (System.Windows.Controls.ListBoxItem)p.List.Items[1];
            p.List.SelectedItem = row;
            // MouseDoubleClick es directo: lo recibe la lista, con el elemento pulsado de origen.
            p.List.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(mouse, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = Control.MouseDoubleClickEvent, Source = row.Content });
        });

        var result = Ui.Run(() => Dialogs.ShowModal(new ConnectionWindow(target, Folders, [Rdp(), new() { Name = "Otra", Host = "otra.lan" }])));
        Assert.Equal(0, Ui.PendingAnswers);
        Assert.True(result);
        Assert.False(keys.Other);
        Assert.True(keys.Down);
        Assert.Equal(ConnectionKind.Rdp, target.Kind);   // la fila 1 (por carpeta: «» antes que «Clientes/Acme») es Servidor
    }

    [Fact]
    public void Sin_otras_conexiones_no_hay_boton_de_copiar()
    {
        var c = Ssh();
        Ui.Run(() =>
        {
            Assert.Equal(Visibility.Collapsed, Ui.Show(new ConnectionWindow(c, Folders)).CopyFromButton.Visibility);
            Assert.Equal(Visibility.Collapsed, Ui.Show(new ConnectionWindow(c, Folders, [c])).CopyFromButton.Visibility);
        });
    }

    [Fact]
    public void La_lista_de_copiar_busca_y_ordena_por_carpeta_y_nombre()
    {
        Connection[] all =
        [
            new() { Name = "b", Folder = "Clientes", Host = "h1" },
            new() { Name = "a", Folder = "Clientes", Host = "h2", Notes = "acme" },
            new() { Name = "z", Folder = "", Host = "h3" },
        ];
        Assert.Equal(["z", "a", "b"], ConnectionPickerWindow.Filter(all, null).Select(c => c.Name));
        Assert.Equal(["a"], ConnectionPickerWindow.Filter(all, "  ACME ").Select(c => c.Name));
        Assert.Empty(ConnectionPickerWindow.Filter(all, "nada"));
    }
}
