using System.Windows;
using SocRcManager.Services;

namespace SocRcManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeManager.Apply();
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
