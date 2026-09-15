using System.Windows;
using SocRcManager.Localization;
using SocRcManager.Models;
using SocRcManager.Services;
using Microsoft.Win32;

namespace SocRcManager;

/// <summary>Alta y edicion de una conexion. Escribe sobre el objeto que recibe solo al guardar.</summary>
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
        SmartSizingBox.IsChecked = connection.RdpSmartSizing;
        ClipboardBox.IsChecked = connection.RdpClipboard;
        DrivesBox.IsChecked = connection.RdpDrives;

        ShowKindFields();
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

    private void ShowKindFields()
    {
        var ssh = Kind == ConnectionKind.Ssh;
        SshPanel.Visibility = ssh ? Visibility.Visible : Visibility.Collapsed;
        RdpPanel.Visibility = ssh ? Visibility.Collapsed : Visibility.Visible;
        DomainPanel.Visibility = ssh ? Visibility.Collapsed : Visibility.Visible;
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
            return;
        }

        _connection.Name = name;
        _connection.Kind = Kind;
        _connection.Folder = (FolderBox.Text ?? string.Empty).Trim().Trim('/');
        _connection.Host = host;
        _connection.Port = int.TryParse(PortBox.Text.Trim(), out var port) && port is > 0 and < 65536 ? port : _connection.DefaultPort;
        _connection.UserName = UserBox.Text.Trim();
        _connection.Domain = DomainBox.Text.Trim();
        _connection.PasswordProtected = Secrets.Protect(PasswordBox.Password);
        _connection.PrivateKeyPath = Kind == ConnectionKind.Ssh ? KeyBox.Text.Trim() : string.Empty;
        _connection.Notes = NotesBox.Text.Trim();
        _connection.RdpSmartSizing = SmartSizingBox.IsChecked == true;
        _connection.RdpClipboard = ClipboardBox.IsChecked == true;
        _connection.RdpDrives = DrivesBox.IsChecked == true;

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
