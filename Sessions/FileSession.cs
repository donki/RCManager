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
        IRemoteFileSystem fs = _connection.Kind == ConnectionKind.Ftp
            ? await FtpFileSystem.ConnectAsync(_connection, password, CancellationToken.None)
            : await SftpFileSystem.ConnectAsync(_connection, password, CancellationToken.None);

        var remote = new RemoteSide(fs);
        var local = new LocalSide(_connection.LocalPath);
        _browser.Attach(local, remote, _connection.Host);
        if (_connection.RemotePath.Length > 0)
            await _browser.RemotePaneNavigateAsync(_connection.RemotePath);
        TitleChanged?.Invoke(_connection.Kind == ConnectionKind.Ftp ? (_connection.FtpsMode > 0 ? "FTPS" : "FTP") : (_connection.UseScp ? "SCP" : "SFTP"));
    }

    public void Focus() => _browser.FocusRemote();

    public bool HasNativeFullScreen => false;

    public void EnterFullScreen()
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
