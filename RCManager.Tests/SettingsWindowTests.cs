using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SocRcManager.Localization;
using SocRcManager.Models;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>
/// Ajustes de verdad (fuera de la pantalla) con la nube de mentira: almacenamiento local o en la
/// nube, entrada con cuenta, frase de cifrado, sincronizar, importar y los interruptores.
/// </summary>
public sealed class SettingsWindowTests : UiTest
{
    private readonly DCloud _cloud = new();
    private readonly List<IDisposable> _dispose = [];

    public override void Dispose()
    {
        try { Ui.Reset(); } catch (Exception) { }
        foreach (var d in _dispose)
            d.Dispose();
        base.Dispose();
    }

    private CloudSync Sync(AppSettings settings, Store? store = null)
    {
        var sync = new CloudSync(settings, store ?? new Store(), _cloud.Http());
        _dispose.Add(sync);
        return sync;
    }

    private DBrowser Browser(Func<string, string>? query)
    {
        var b = new DBrowser(query);
        _dispose.Add(b);
        return b;
    }

    private static AppSettings Cloud(string passphrase = "", string email = "ana@example.org", DateTimeOffset? last = null) => new()
    {
        Storage = StorageMode.GoogleDrive,
        AccountEmail = email,
        Passphrase = passphrase,
        Tokens = DCloud.Tokens(),
        LastSyncAt = last,
    };

    private static SettingsWindow Open(AppSettings settings, CloudSync sync, Window? owner = null) =>
        Ui.Run(() => Ui.Show(new SettingsWindow(settings, sync) { Owner = owner }));

    private static void Raise(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    private static void WaitIdle(SettingsWindow w) => Ui.WaitUntil(() => w.Busy.Visibility == Visibility.Collapsed);

    private static bool Filled(SettingsWindow w, Button b) =>
        Equals(((SolidColorBrush)b.Background).Color, ((SolidColorBrush)w.FindResource("Primary")).Color);

    [Fact]
    public void En_local_sin_frase_ni_cuenta()
    {
        var settings = new AppSettings { TrayOnMinimize = false, AskBeforeClosingDetached = true };
        var w = Open(settings, Sync(settings));
        Ui.Run(() =>
        {
            Assert.Equal(Loc.Get("StorageLocalHint"), w.AccountText.Text);
            Assert.Equal(Visibility.Collapsed, w.PassphraseCard.Visibility);
            Assert.True(Filled(w, w.LocalButton));
            Assert.False(Filled(w, w.GoogleButton));
            Assert.Equal(Brushes.Transparent, w.OneDriveButton.Background);
            Assert.False(w.TrayBox.IsChecked);
            Assert.True(w.AskDetachedBox.IsChecked);
            Assert.Equal(Loc.Get("PassphraseMissing"), w.PassphraseState.Text);
            Assert.Equal(Loc.Get("SyncNowTooltip"), w.SyncButton.ToolTip);
            Assert.Equal(CloudSync.IsAvailable(StorageMode.GoogleDrive), w.GoogleButton.IsEnabled);

            // Ya en local: pulsar Local no hace nada.
            Ui.Click(w.LocalButton);
            Assert.Equal(string.Empty, w.StatusText.Text);
        });
        Assert.False(File.Exists(AppSettings.FilePath));
    }

    [Fact]
    public void En_la_nube_enseña_la_cuenta_y_la_ultima_sincronizacion()
    {
        var last = new DateTimeOffset(2026, 9, 30, 10, 15, 0, TimeSpan.Zero);
        var settings = Cloud("frase larga", last: last);
        var w = Open(settings, Sync(settings));
        Ui.Run(() =>
        {
            Assert.Equal(Loc.Format("StorageAccount", "ana@example.org", last.ToLocalTime().ToString("g")), w.AccountText.Text);
            Assert.Equal(Visibility.Visible, w.PassphraseCard.Visibility);
            Assert.Equal(Loc.Get("PassphraseSet"), w.PassphraseState.Text);
            Assert.True(Filled(w, w.GoogleButton));
            Assert.False(Filled(w, w.LocalButton));
        });
    }

    [Fact]
    public void En_la_nube_sin_correo_ni_sincronizacion()
    {
        var settings = Cloud(email: "");
        settings.Storage = StorageMode.OneDrive;
        var w = Open(settings, Sync(settings));
        Ui.Run(() =>
        {
            Assert.Equal(Loc.Format("StorageAccount", "?", Loc.Get("Never")), w.AccountText.Text);
            Assert.Equal(Loc.Get("PassphraseMissing"), w.PassphraseState.Text);
            Assert.True(Filled(w, w.OneDriveButton));
        });
    }

    [Fact]
    public void Volver_a_local_sale_de_la_cuenta()
    {
        var settings = Cloud("frase larga", last: DateTimeOffset.UtcNow);
        var w = Open(settings, Sync(settings));
        Ui.Run(() => Ui.Click(w.LocalButton));
        Ui.Run(() =>
        {
            Assert.Equal(Loc.Get("StorageNowLocal"), w.StatusText.Text);
            Assert.Equal(Loc.Get("StorageLocalHint"), w.AccountText.Text);
            Assert.Equal(Visibility.Collapsed, w.PassphraseCard.Visibility);
        });
        Assert.Equal(StorageMode.Local, settings.Storage);
        Assert.Null(settings.Tokens);
        Assert.Null(settings.LastSyncAt);
        Assert.Equal(StorageMode.Local, AppSettings.Load().Storage);
    }

    [Fact]
    public void Los_interruptores_se_guardan_al_momento()
    {
        var settings = new AppSettings();
        var w = Open(settings, Sync(settings));
        Ui.Run(() => Ui.Click(w.TrayBox));
        Assert.False(settings.TrayOnMinimize);
        Assert.False(AppSettings.Load().TrayOnMinimize);
        Ui.Run(() => Ui.Click(w.AskDetachedBox));
        Assert.False(settings.AskBeforeClosingDetached);
        Assert.False(AppSettings.Load().AskBeforeClosingDetached);
        Ui.Run(() => { Ui.Click(w.TrayBox); Ui.Click(w.AskDetachedBox); });
        var saved = AppSettings.Load();
        Assert.True(saved.TrayOnMinimize && saved.AskBeforeClosingDetached);
    }

    [Fact]
    public void Una_frase_corta_no_se_guarda()
    {
        var settings = Cloud();
        var w = Open(settings, Sync(settings));
        Ui.Run(() =>
        {
            w.PassphraseBox.Password = "corta";
            Ui.Click(w.SavePassphraseButton);
            Assert.Equal(Loc.Get("PassphraseTooShort"), w.StatusText.Text);
            Assert.Equal("corta", w.PassphraseBox.Password);
        });
        Assert.Equal(string.Empty, settings.Passphrase);
        Assert.Empty(_cloud.Handler.Requests);
    }

    [Fact]
    public void Guardar_la_frase_y_subir_lo_local()
    {
        _cloud.Slow = true;   // los avisos llegan desde otro hilo
        var settings = Cloud();
        var store = new Store();
        store.Connections.Add(new Connection { Name = "uno", Host = "uno.lan" });
        store.Save();
        var w = Open(settings, Sync(settings, store));
        Ui.Run(() =>
        {
            w.PassphraseBox.Password = "frase muy larga";
            Ui.Click(w.SavePassphraseButton);
        });
        WaitIdle(w);
        Ui.Run(() =>
        {
            Assert.Equal(string.Empty, w.PassphraseBox.Password);
            Assert.Equal(Loc.Get("PassphraseSet"), w.PassphraseState.Text);
            Assert.True(w.SyncButton.IsEnabled && w.LocalButton.IsEnabled && w.SavePassphraseButton.IsEnabled);
        });
        Ui.WaitUntil(() => w.StatusText.Text == Loc.Format("CloudUploaded", "Google Drive"));
        Assert.Equal("frase muy larga", AppSettings.Load().Passphrase);
        Assert.Equal(1, _cloud.Uploads);
        Assert.Contains("uno.lan", Vault.Decrypt(_cloud.Content!, "frase muy larga"));
        Assert.False(w.LocalReplaced);
    }

    [Fact]
    public void Sincronizar_baja_lo_mas_nuevo_y_avisa_de_repintar()
    {
        // En la nube, una lista guardada despues que la local (vacia).
        var remote = new Store();
        remote.Connections.Add(new Connection { Name = "remota", Host = "r.lan" });
        remote.Save();
        _cloud.Content = Vault.Encrypt(remote.ExportPortable(), "frase larga");
        var settings = Cloud("frase larga");
        var local = new Store();
        var w = Open(settings, Sync(settings, local));

        Ui.Run(() => Ui.Click(w.SyncButton));
        WaitIdle(w);
        Assert.True(w.LocalReplaced);
        Assert.Equal("remota", Assert.Single(local.Connections).Name);
        Ui.WaitUntil(() => w.StatusText.Text == Loc.Format("CloudDownloaded", 1));
        Ui.Run(() => Assert.NotEqual(Loc.Format("StorageAccount", "ana@example.org", Loc.Get("Never")), w.AccountText.Text));
    }

    [Fact]
    public void Si_la_nube_falla_se_dice()
    {
        _cloud.DriveStatus = HttpStatusCode.InternalServerError;
        var settings = Cloud("frase larga");
        var w = Open(settings, Sync(settings));
        Ui.Run(() => Ui.Click(w.SyncButton));
        WaitIdle(w);
        Ui.Flush();
        Ui.Run(() => Assert.StartsWith(Loc.Format("CloudFailed", string.Empty), w.StatusText.Text));
        Assert.False(w.LocalReplaced);
    }

    [Fact]
    public void Sin_cuenta_el_fallo_no_lo_tapa_el_aviso_de_sincronizando()
    {
        // El fallo llega antes de esperar a la red: el «Sincronizando…» no puede quedar encima.
        var settings = Cloud("frase larga");
        settings.Tokens = null;
        var w = Open(settings, Sync(settings));
        Ui.Run(() => Ui.Click(w.SyncButton));
        WaitIdle(w);
        Ui.Flush();
        Ui.Run(() => Assert.Equal(Loc.Format("CloudFailed", Loc.Get("CloudNotSignedIn")), w.StatusText.Text));
        Assert.Empty(_cloud.Handler.Requests);
    }

    [Fact]
    public void Entrar_en_Google_sin_frase_pide_la_frase()
    {
        var browser = Browser(state => $"code=C&state={state}");
        var settings = new AppSettings();
        var w = Open(settings, Sync(settings));
        Ui.Run(() =>
        {
            Raise(w.GoogleButton);
            Assert.Equal(Loc.Get("SigningInBrowser"), w.StatusText.Text);
            Assert.False(w.LocalButton.IsEnabled);
        });
        WaitIdle(w);
        Ui.Run(() =>
        {
            Assert.Equal(Loc.Format("SignedInAs", "ana@example.org"), w.StatusText.Text);
            Assert.Equal(Visibility.Visible, w.PassphraseCard.Visibility);
            Assert.True(Filled(w, w.GoogleButton));
        });
        Assert.StartsWith("https://accounts.google.com/", Assert.Single(browser.Opened));
        Assert.Equal(StorageMode.GoogleDrive, settings.Storage);
        Assert.Equal(0, _cloud.Uploads);   // sin frase no se sube nada
    }

    [Fact]
    public void Entrar_en_Google_con_frase_sincroniza_despues()
    {
        Browser(state => $"code=C&state={state}");
        var settings = new AppSettings { Passphrase = "frase larga" };
        var w = Open(settings, Sync(settings));
        Ui.Run(() => Raise(w.GoogleButton));
        Ui.WaitUntil(() => _cloud.Uploads == 1 && w.Busy.Visibility == Visibility.Collapsed);
        Assert.Equal(StorageMode.GoogleDrive, settings.Storage);
        Assert.NotNull(settings.LastSyncAt);
    }

    [Fact]
    public void Entrar_en_OneDrive_y_el_usuario_dice_que_no()
    {
        var browser = Browser(state => $"error=access_denied&error_description=Cancelado&state={state}");
        var settings = new AppSettings();
        var w = Open(settings, Sync(settings));
        Ui.Run(() => Raise(w.OneDriveButton));
        WaitIdle(w);
        Ui.Run(() => Assert.Equal(Loc.Format("CloudFailed", "Cancelado"), w.StatusText.Text));
        Assert.StartsWith("https://login.microsoftonline.com/", Assert.Single(browser.Opened));
        Assert.Equal(StorageMode.Local, settings.Storage);
    }

    [Fact]
    public void Entrada_cortada_se_dice_cancelada()
    {
        _cloud.TokenTimesOut = true;
        Browser(state => $"code=C&state={state}");
        var settings = new AppSettings();
        var w = Open(settings, Sync(settings));
        Ui.Run(() => Raise(w.OneDriveButton));
        WaitIdle(w);
        Ui.Run(() => Assert.Equal(Loc.Get("SignInCancelled"), w.StatusText.Text));
        Assert.Equal(StorageMode.Local, settings.Storage);
    }

    [Fact]
    public void En_modo_aislado_no_se_entra_en_la_nube()
    {
        var browser = Browser(state => $"code=C&state={state}");
        Sandbox.IsOn = true;
        var settings = new AppSettings();
        var w = Open(settings, Sync(settings));
        Ui.Run(() => { Raise(w.GoogleButton); Raise(w.OneDriveButton); });
        Ui.Run(() => Assert.Equal(string.Empty, w.StatusText.Text));
        Assert.Empty(browser.Opened);
        Assert.Empty(_cloud.Handler.Requests);
    }

    [Fact]
    public void Importar_lo_hace_la_ventana_principal()
    {
        var settings = new AppSettings();
        Ui.PickedFiles = null;   // el usuario cancela el dialogo de abrir
        var main = Ui.Run(() => Ui.Show(new MainWindow()));
        var w = Open(settings, Sync(settings), main);
        Ui.Run(() => Ui.Click(w.ImportButton));
        Assert.True(w.LocalReplaced);
        Assert.Equal(Loc.Get("ImportTooltip"), Ui.Run(() => w.ImportButton.ToolTip));
    }

    [Fact]
    public void Exportar_desde_ajustes_exporta_todo()
    {
        var settings = new AppSettings();
        var store = new Store();
        store.Connections.Add(new Models.Connection { Name = "A", Host = "a.lan" });
        store.Save();
        var main = Ui.Run(() => Ui.Show(new MainWindow()));
        var w = Open(settings, Sync(settings), main);
        Ui.Answer<PromptWindow>(p => Ui.Click(Ui.ButtonById(p, "CancelButton")!));
        Ui.Run(() => Ui.Click(w.ExportButton));
        Assert.Equal(0, Ui.PendingAnswers);
        Assert.Equal(Loc.Get("ExportTooltip"), Ui.Run(() => w.ExportButton.ToolTip));

        // Sin ventana principal, nada.
        var alone = Open(settings, Sync(settings));
        Ui.Run(() => Ui.Click(alone.ExportButton));
    }

    [Fact]
    public void Importar_sin_ventana_principal_no_hace_nada()
    {
        var settings = new AppSettings();
        var w = Open(settings, Sync(settings));
        Ui.Run(() => Ui.Click(w.ImportButton));
        Assert.False(w.LocalReplaced);
    }

    [Fact]
    public void Cerrar()
    {
        var settings = new AppSettings();
        var w = Open(settings, Sync(settings));
        Ui.Run(() => Ui.Click(w.CloseButton));
        Assert.False(Ui.Run(() => w.IsVisible));
    }
}
