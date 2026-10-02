using System.Windows;
using System.Windows.Forms.Integration;
using SocRcManager.Models;
using SocRcManager.Sessions;

namespace SocRcManager.Tests;

/// <summary>
/// El control RDP de Windows de verdad (mstscax), creado en una ventana fuera de la pantalla y
/// nunca conectado: que los ajustes llegan a sus propiedades y que sus eventos llegan a la sesion.
/// </summary>
public sealed class AxRdpControlTests : UiTest
{
    /// <summary>El control dentro de una ventana (asi tiene ventana nativa y el ActiveX esta vivo).</summary>
    private static AxRdpControl Live()
    {
        return Ui.Run(() =>
        {
            var rdp = new AxRdpControl();
            Ui.Show(new Window { Width = 500, Height = 400, Content = new WindowsFormsHost { Child = rdp.Control } });
            return rdp;
        });
    }

    private static Connection Full() => new()
    {
        Name = "Oficina",
        Host = "srv.invalid",
        Port = 3390,
        UserName = "ana",
        Domain = "CORP",
        RdpWidth = 1280,
        RdpHeight = 800,
        RdpColorDepth = 24,
        RdpConnectionBar = false,
        RdpAudioMode = 2,
        RdpKeyboardMode = 1,
        RdpPrinters = false,
        RdpClipboard = false,
        RdpDrives = false,
        RdpPorts = true,
        RdpWallpaper = false,
        RdpAuthLevel = 2,
        RdpAdminSession = true,
        RdpGatewayMode = 2,
        RdpGatewayHost = "gw.invalid",
        RdpGatewaySameCredentials = false,
        RdpGatewayUserName = "gwuser",
        RdpGatewayDomain = "GW",
    };

    [Fact]
    public void Los_ajustes_llegan_al_control_sin_conectar()
    {
        var rdp = Live();
        var read = Ui.Run(() =>
        {
            rdp.Apply(RdpSettings.From(Full(), "pw", 640, 480));
            dynamic ax = rdp.Control;
            dynamic adv = ax.AdvancedSettings9;
            dynamic gw = ax.TransportSettings2;
            return new
            {
                Server = (string)ax.Server,
                UserName = (string)ax.UserName,
                Domain = (string)ax.Domain,
                DesktopWidth = (int)ax.DesktopWidth,
                DesktopHeight = (int)ax.DesktopHeight,
                ColorDepth = (int)ax.ColorDepth,
                RDPPort = (int)adv.RDPPort,
                DisplayConnectionBar = (bool)adv.DisplayConnectionBar,
                AudioRedirectionMode = (uint)adv.AudioRedirectionMode,
                RedirectPrinters = (bool)adv.RedirectPrinters,
                RedirectClipboard = (bool)adv.RedirectClipboard,
                RedirectPorts = (bool)adv.RedirectPorts,
                PerformanceFlags = (int)adv.PerformanceFlags,
                AuthenticationLevel = (uint)adv.AuthenticationLevel,
                ConnectToAdministerServer = (bool)adv.ConnectToAdministerServer,
                MaxReconnectAttempts = (int)adv.MaxReconnectAttempts,
                Keyboard = (int)ax.SecuredSettings2.KeyboardHookMode,
                GatewayHostname = (string)gw.GatewayHostname,
                GatewayUsageMethod = (uint)gw.GatewayUsageMethod,
                GatewayCredSharing = (uint)gw.GatewayCredSharing,
                GatewayUsername = (string)gw.GatewayUsername,
                GatewayDomain = (string)gw.GatewayDomain,
            };
        });
        Assert.Equal("srv.invalid", read.Server);
        Assert.Equal("ana", read.UserName);
        Assert.Equal("CORP", read.Domain);
        Assert.Equal((1280, 800), (read.DesktopWidth, read.DesktopHeight));
        Assert.Equal(24, read.ColorDepth);
        Assert.Equal(3390, read.RDPPort);
        Assert.False(read.DisplayConnectionBar);
        Assert.Equal(2u, read.AudioRedirectionMode);
        Assert.False(read.RedirectPrinters);
        Assert.False(read.RedirectClipboard);
        Assert.True(read.RedirectPorts);
        Assert.Equal(0x01 | 0x80 | 0x100, read.PerformanceFlags);
        Assert.Equal(1u, read.AuthenticationLevel);
        Assert.True(read.ConnectToAdministerServer);
        Assert.Equal(20, read.MaxReconnectAttempts);
        Assert.Equal(1, read.Keyboard);
        Assert.Equal("gw.invalid", read.GatewayHostname);
        Assert.Equal(2u, read.GatewayUsageMethod);
        Assert.Equal(0u, read.GatewayCredSharing);
        Assert.Equal("gwuser", read.GatewayUsername);
        Assert.Equal("GW", read.GatewayDomain);
    }

    [Fact]
    public void Sin_puerta_de_enlace_ni_dominio()
    {
        var rdp = Live();
        var (usage, server) = Ui.Run(() =>
        {
            var c = new Connection { Name = "x", Host = "h.invalid", UserName = "u", RdpGatewayMode = 1, RdpGatewayHost = "gw.invalid" };
            rdp.Apply(RdpSettings.From(c, "", 800, 600));   // con las mismas credenciales
            c.RdpGatewayMode = 0;
            rdp.Apply(RdpSettings.From(c, "", 800, 600));
            dynamic ax = rdp.Control;
            return ((uint)ax.TransportSettings2.GatewayUsageMethod, (string)ax.Server);
        });
        Assert.Equal(0u, usage);
        Assert.Equal("h.invalid", server);
    }

    [Fact]
    public void Estado_y_monitores_sin_conectar()
    {
        var rdp = Live();
        var (handle, connected, full, screen, screens) = Ui.Run(() =>
        {
            rdp.SetDesktopSize(1024, 768);
            rdp.SetUseMultimon(false);
            rdp.Focus();
            return (rdp.IsHandleCreated, rdp.IsConnected, rdp.FullScreen, rdp.ScreenBounds, rdp.AllScreens);
        });
        Assert.True(handle);
        Assert.False(connected);
        Assert.False(full);
        Assert.Contains(screen, screens);
        Assert.Equal(System.Windows.Forms.Screen.AllScreens.Length, screens.Count);
        Assert.Equal((1024, 768), Ui.Run(() => { dynamic ax = rdp.Control; return ((int)ax.DesktopWidth, (int)ax.DesktopHeight); }));
    }

    [Fact]
    public void Pantalla_completa_y_escala_sin_conectar()
    {
        var rdp = Live();
        // Sin conexion el control no tiene sesion a la que pedirle la escala: o lo dice o lanza.
        var outcome = Ui.Run(() =>
        {
            try { return rdp.UpdateDisplay(1024, 768, 125) ? "ok" : "viejo"; }
            catch (Exception) { return "rechazado"; }
        });
        Assert.Contains(outcome, new[] { "ok", "viejo", "rechazado" });
        Ui.Run(() => rdp.FullScreen = false);
        Assert.False(Ui.Run(() => rdp.FullScreen));

        // Desconectar sin conexion no conecta nada (y si el control protesta, da igual).
        Ui.Run(() => { try { rdp.Disconnect(); } catch (Exception) { } });
        Assert.False(Ui.Run(() => rdp.IsConnected));
    }

    [Fact]
    public void Los_eventos_del_control_llegan_a_la_sesion()
    {
        var rdp = Live();
        var seen = new List<string>();
        rdp.Connected += () => seen.Add("connected");
        rdp.LoginComplete += () => seen.Add("login");
        rdp.AutoReconnected += () => seen.Add("autoreconnected");
        rdp.Disconnected += r => seen.Add($"disconnected {r}");
        rdp.RequestLeaveFullScreen += () => seen.Add("leave-request");
        rdp.ConfirmClose += () => seen.Add("confirm-close");
        rdp.RequestContainerMinimize += () => seen.Add("minimize");
        rdp.LeaveFullScreenMode += () => seen.Add("left");
        rdp.HandleDestroyed += () => seen.Add("destroyed");

        var allowed = Ui.Run(() =>
        {
            // Los eventos los lanza el «multicaster» que aximp genera para el control (es por donde
            // llegan los de mstscax): se crea uno sobre el control y se le llama como haria COM.
            var ax = rdp.Control;
            var type = ax.GetType().Assembly.GetType("AxMSTSCLib.AxMsRdpClient9NotSafeForScriptingEventMulticaster", throwOnError: true)!;
            var events = Activator.CreateInstance(type, ax)!;
            object?[] Call(string name, params object?[] args)
            {
                type.GetMethod(name)!.Invoke(events, args);
                return args;
            }
            Call("OnConnected");
            Call("OnLoginComplete");
            Call("OnAutoReconnected");
            Call("OnDisconnected", 516);
            Call("OnRequestLeaveFullScreen");
            var allow = (bool)Call("OnConfirmClose", false)[0]!;
            Call("OnRequestContainerMinimize");
            Call("OnLeaveFullScreenMode");
            ax.Dispose();   // destruir la ventana del control
            return allow;
        });
        Assert.True(allowed);
        Assert.Equal(["connected", "login", "autoreconnected", "disconnected 516", "leave-request", "confirm-close", "minimize", "left", "destroyed"], seen);
    }

    [Fact]
    public void La_sesion_de_verdad_crea_su_control()
    {
        var s = Ui.Run(() => new RdpSession(new Connection { Name = "x" }));
        var host = Assert.IsType<WindowsFormsHost>(s.View);
        Assert.Equal("AxMsRdpClient9NotSafeForScripting", Ui.Run(() => host.Child.GetType().Name));
        Assert.False(s.IsFullScreen);   // sin ventana todavia
    }
}
