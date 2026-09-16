using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SocRcManager.Localization;
using SocRcManager.Services;

namespace SocRcManager;

/// <summary>
/// Ajustes: donde se guardan las conexiones (en este PC, en Google Drive o en OneDrive) y la frase
/// con la que se cifran cuando salen del equipo.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly CloudSync _sync;

    public SettingsWindow(AppSettings settings, CloudSync sync)
    {
        InitializeComponent();
        _settings = settings;
        _sync = sync;
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        SavePassphraseButton.ToolTip = Loc.Get("PassphraseSaveTooltip");
        SyncButton.ToolTip = Loc.Get("SyncNowTooltip");
        CloseButton.ToolTip = Loc.Get("Close");
        ImportButton.ToolTip = Loc.Get("ImportTooltip");
        GoogleButton.IsEnabled = CloudSync.IsAvailable(StorageMode.GoogleDrive);
        OneDriveButton.IsEnabled = CloudSync.IsAvailable(StorageMode.OneDrive);
        Paint();
    }

    /// <summary>Lo local ha cambiado por una bajada de la nube: la ventana principal repinta.</summary>
    public bool LocalReplaced { get; private set; }

    private void Paint()
    {
        Fill(LocalButton, _settings.Storage == StorageMode.Local);
        Fill(GoogleButton, _settings.Storage == StorageMode.GoogleDrive);
        Fill(OneDriveButton, _settings.Storage == StorageMode.OneDrive);

        var cloud = _settings.Storage != StorageMode.Local;
        PassphraseCard.Visibility = cloud ? Visibility.Visible : Visibility.Collapsed;
        AccountText.Text = cloud
            ? Loc.Format("StorageAccount", _settings.AccountEmail.Length > 0 ? _settings.AccountEmail : "?",
                _settings.LastSyncAt is { } t ? t.ToLocalTime().ToString("g") : Loc.Get("Never"))
            : Loc.Get("StorageLocalHint");
        PassphraseState.Text = Loc.Get(_settings.Passphrase.Length > 0 ? "PassphraseSet" : "PassphraseMissing");
    }

    private void Fill(Button button, bool selected)
    {
        button.Background = selected ? (Brush)FindResource("Primary") : Brushes.Transparent;
        button.Foreground = selected ? (Brush)FindResource("OnPrimary") : (Brush)FindResource("Primary");
    }

    private void OnLocalClick(object sender, RoutedEventArgs e)
    {
        if (_settings.Storage == StorageMode.Local)
            return;
        _sync.SignOut();
        StatusText.Text = Loc.Get("StorageNowLocal");
        Paint();
    }

    private async void OnGoogleClick(object sender, RoutedEventArgs e) => await SignInAsync(StorageMode.GoogleDrive);

    private async void OnOneDriveClick(object sender, RoutedEventArgs e) => await SignInAsync(StorageMode.OneDrive);

    private async Task SignInAsync(StorageMode mode)
    {
        SetBusy(true);
        StatusText.Text = Loc.Get("SigningInBrowser");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var email = await _sync.SignInAsync(mode, cts.Token);
            StatusText.Text = Loc.Format("SignedInAs", email);
            Paint();
            if (_settings.Passphrase.Length > 0)
                await SyncNowAsync();
            else
                PassphraseBox.Focus();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Loc.Get("SignInCancelled");
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Format("CloudFailed", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnSavePassphraseClick(object sender, RoutedEventArgs e)
    {
        var phrase = PassphraseBox.Password;
        if (phrase.Length < 8)
        {
            StatusText.Text = Loc.Get("PassphraseTooShort");
            return;
        }

        _settings.Passphrase = phrase;
        _settings.Save();
        PassphraseBox.Password = string.Empty;
        Paint();
        await SyncNowAsync();
    }

    private async void OnSyncClick(object sender, RoutedEventArgs e) => await SyncNowAsync();

    private async Task SyncNowAsync()
    {
        SetBusy(true);
        void OnStatus(string s) => Dispatcher.BeginInvoke(() => StatusText.Text = s);
        _sync.Status += OnStatus;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            if (await _sync.SyncAsync(cts.Token))
                LocalReplaced = true;
            Paint();
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Format("CloudFailed", ex.Message);
        }
        finally
        {
            _sync.Status -= OnStatus;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        LocalButton.IsEnabled = !busy;
        GoogleButton.IsEnabled = !busy && CloudSync.IsAvailable(StorageMode.GoogleDrive);
        OneDriveButton.IsEnabled = !busy && CloudSync.IsAvailable(StorageMode.OneDrive);
        SavePassphraseButton.IsEnabled = !busy;
        SyncButton.IsEnabled = !busy;
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow main)
        {
            main.ImportRdm();
            LocalReplaced = true;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
