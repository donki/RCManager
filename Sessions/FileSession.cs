using System.Windows;
using System.Windows.Controls;
using SocRcManager.Files;
using SocRcManager.Localization;
using SocRcManager.Models;

namespace SocRcManager.Sessions;

/// <summary>Una pestaña de ficheros (SFTP/SCP o FTP/FTPS): el explorador de dos paneles sobre el servidor.</summary>
public sealed class FileSession : ISession
{
    private readonly Connection _connection;
    private readonly FileBrowserControl _browser = new();
    private readonly ContentControl _host = new();

    public FileSession(Connection connection)
    {
        _connection = connection;
        // Hasta que la conexion entra no hay explorador que enseñar: un indicador con el nombre
        // del servidor en su sitio. Si falla, la pestaña se cierra y sale el aviso con la razon.
        _host.Content = ConnectingPanel();
        View = _host;
    }

    private UIElement ConnectingPanel()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 320 };
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock
        {
            Text = Loc.Format("Connecting", _connection.Host),
            Style = (Style)Application.Current.FindResource("HintText"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        return panel;
    }

    public FrameworkElement View { get; }

    public string Title => _connection.Name;

    public event Action<string>? TitleChanged;
    public event Action<string?>? Ended;

    public async Task ConnectAsync(string password)
    {
        Func<CancellationToken, Task<IRemoteFileSystem>> open = _connection.Kind == ConnectionKind.Ftp
            ? async ct => await FtpFileSystem.ConnectAsync(_connection, password, ct)
            : async ct => await SftpFileSystem.ConnectAsync(_connection, password, ct);
        var fs = await open(CancellationToken.None);

        var remote = new RemoteSide(fs);
        var local = new LocalSide(_connection.LocalPath);
        _browser.PaneFontSize = Math.Clamp(_connection.FontSize, 9, 28);
        _browser.Attach(local, remote, _connection.Host, _connection, open);
        // Ya conectado: el explorador ocupa el sitio del indicador.
        _host.Content = _browser;
        // La carpeta de la conexion o, si no tiene, la raiz del servidor: es donde uno espera
        // empezar, no el directorio de inicio del usuario.
        await _browser.RemotePaneNavigateAsync(_connection.RemotePath.Length > 0 ? _connection.RemotePath : "/");
        TitleChanged?.Invoke(_connection.Kind == ConnectionKind.Ftp ? (_connection.FtpsMode > 0 ? "FTPS" : "FTP") : (_connection.UseScp ? "SCP" : "SFTP"));
    }

    public void Focus() => _browser.FocusRemote();

    /// <summary>Abre un fichero del servidor en el editor integrado.</summary>
    public Task EditFileAsync(string path) => _browser.EditRemotePathAsync(path);

    public bool CanZoom => true;

    public string Zoom(int steps)
    {
        _browser.PaneFontSize = Math.Clamp(_browser.PaneFontSize + steps, 9, 28);
        _connection.FontSize = _browser.PaneFontSize;
        return $"{_browser.PaneFontSize:0} pt";
    }

    public bool HasNativeFullScreen => false;

    public void LeaveFullScreen()
    {
    }

    public void EnterFullScreen(int screen)
    {
    }

    public event Action? LeftFullScreen
    {
        add { }
        remove { }
    }

    public void Disconnect()
    {
        _browser.Shutdown();
        Ended?.Invoke(null);
    }
}
