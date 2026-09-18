using System.Windows;
using SocRcManager.Files;
using SocRcManager.Models;

namespace SocRcManager.Sessions;

/// <summary>Una pestaña de ficheros (SFTP/SCP o FTP/FTPS): el explorador de dos paneles sobre el servidor.</summary>
public sealed class FileSession : ISession
{
    private readonly Connection _connection;
    private readonly FileBrowserControl _browser = new();

    public FileSession(Connection connection)
    {
        _connection = connection;
        View = _browser;
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
