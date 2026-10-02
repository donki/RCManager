using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SocRcManager.Models;
using SocRcManager.Services;
using SocRcManager.Sessions;

namespace SocRcManager.Tests;

/// <summary>Una sesion de mentira: no habla con ningun servidor y apunta lo que se le pide.</summary>
public sealed class FakeSession(Connection connection) : ISession
{
    public Connection Connection { get; } = connection;
    public FrameworkElement View { get; } = new Border();
    public string Title => Connection.Name;
    public List<string> Log { get; } = [];
    public string? Password { get; private set; }

    /// <summary>Lo que hace al conectar (por defecto, entrar a la primera).</summary>
    public Func<string, Task> Connect { get; set; } = _ => Task.CompletedTask;

    public bool HasNativeFullScreen { get; set; }
    public bool CanZoom { get; set; } = true;
    public int ZoomLevel { get; private set; } = 14;

    public event Action<string>? TitleChanged;
    public event Action<string?>? Ended;
    public event Action? LeftFullScreen { add { } remove { } }

    public Task ConnectAsync(string password)
    {
        Password = password;
        Log.Add("connect");
        return Connect(password);
    }

    public void Focus() => Log.Add("focus");
    public void Disconnect() => Log.Add("disconnect");
    public void EnterFullScreen(int screen) => Log.Add($"fullscreen {screen}");
    public void LeaveFullScreen() => Log.Add("leave");

    public string Zoom(int steps)
    {
        ZoomLevel += steps;
        return $"{ZoomLevel} pt";
    }

    public void RaiseTitle(string title) => TitleChanged?.Invoke(title);
    public void RaiseEnded(string? reason) => Ended?.Invoke(reason);
}

/// <summary>El escritorio de mentira: monitores fuera de la pantalla de verdad y arrastres simulados.</summary>
internal sealed class FakeDesktop : IDesktop
{
    /// <summary>Dos monitores lejos de cualquier pantalla real (las ventanas que se muevan alli no se ven).</summary>
    public List<MonitorInfo> Screens { get; } =
    [
        new(new PixelRect(-40000, -40000, 1920, 1080), new PixelRect(-40000, -40000, 1920, 1040), true),
        new(new PixelRect(-38080, -40000, 1280, 1024), new PixelRect(-38080, -40000, 1280, 984), false),
    ];

    public (int X, int Y) Cursor { get; set; } = (-39000, -39500);
    public IntPtr Root { get; set; } = new(0x7777);
    public List<(DependencyObject Source, object Data, DragDropEffects Allowed)> Drags { get; } = [];

    /// <summary>Lo que pasa mientras se arrastra (soltar en algun sitio, pulsar Esc...).</summary>
    public Action<DependencyObject, IDataObject>? DuringDrag { get; set; }

    public List<UIElement> Captured { get; } = [];
    public List<Window> Foreground { get; } = [];
    public List<ContextMenu> Menus { get; } = [];

    public IReadOnlyList<MonitorInfo> Monitors() => Screens;
    public (int X, int Y) CursorPosition() => Cursor;
    public IntPtr RootWindowAt(int x, int y) => Root;

    public DragDropEffects DoDragDrop(DependencyObject source, object data, DragDropEffects allowed)
    {
        Drags.Add((source, data, allowed));
        DuringDrag?.Invoke(source, (IDataObject)data);
        return DragDropEffects.Move;
    }

    public void CaptureMouse(UIElement element) => Captured.Add(element);
    public void ForceForeground(Window window) => Foreground.Add(window);
    public void OpenMenu(ContextMenu menu) => Menus.Add(menu);
}

/// <summary>El area de notificacion de mentira: apunta las llamadas.</summary>
internal sealed class FakeTrayShell : ITrayShell
{
    public List<string> Calls { get; } = [];
    public List<(int Id, string? Text)> LastMenu { get; } = [];

    public void Add(IntPtr hwnd, int callbackMessage, string tip) => Calls.Add($"add {callbackMessage:X} {tip}");
    public void Balloon(IntPtr hwnd, string title, string text) => Calls.Add($"balloon {title}|{text}");
    public void Delete(IntPtr hwnd) => Calls.Add("delete");

    public void ShowMenu(IntPtr hwnd, IReadOnlyList<(int Id, string? Text)> items)
    {
        Calls.Add("menu");
        LastMenu.Clear();
        LastMenu.AddRange(items);
    }

    public void Activate(Window window, IntPtr hwnd) => Calls.Add("activate");
}

/// <summary>Un origen de entrada de mentira para eventos de teclado en ventanas que no se han enseñado.</summary>
internal sealed class FakeInputSource : PresentationSource
{
    private Visual? _root;
    public override Visual? RootVisual { get => _root; set => _root = value; }
    public override bool IsDisposed => false;
    protected override CompositionTarget? GetCompositionTargetCore() => null;
}

/// <summary>Eventos de raton, teclado y arrastre como los de verdad (los que no tienen constructor publico, por reflexion).</summary>
internal static class Input
{
    public static void Mouse(UIElement target, RoutedEvent routedEvent) =>
        target.RaiseEvent(new MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = routedEvent });

    public static void Move(UIElement target, RoutedEvent routedEvent) =>
        target.RaiseEvent(new MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = routedEvent });

    public static KeyEventArgs Key(UIElement target, Key key, RoutedEvent routedEvent)
    {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target) ?? new FakeInputSource(), 0, key) { RoutedEvent = routedEvent };
        target.RaiseEvent(e);
        return e;
    }

    public static DragEventArgs Drag(UIElement target, RoutedEvent routedEvent, IDataObject data)
    {
        var ctor = typeof(DragEventArgs).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var e = (DragEventArgs)ctor.Invoke([data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move | DragDropEffects.Copy, target, new Point(1, 1)]);
        e.RoutedEvent = routedEvent;
        target.RaiseEvent(e);
        return e;
    }

    /// <summary>Lo que WPF manda a la ventana de origen durante un arrastre (Esc pulsado o no).</summary>
    public static void QueryContinue(UIElement source, bool escape)
    {
        var ctor = typeof(QueryContinueDragEventArgs).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var e = (QueryContinueDragEventArgs)ctor.Invoke([escape, DragDropKeyStates.None]);
        e.RoutedEvent = DragDrop.QueryContinueDragEvent;
        source.RaiseEvent(e);
    }

    public static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
}

/// <summary>Lo privado de los objetos de la aplicacion (lo justo para comprobar estados internos).</summary>
internal static class Peek
{
    public static T Field<T>(object o, string name) =>
        (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o)!;

    public static object? Call(object o, string name, params object?[] args) =>
        o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(o, args);

    /// <summary>Lanza un evento de un objeto (su delegado de respaldo).</summary>
    public static void Raise(object o, string eventName, params object?[] args) =>
        ((Delegate?)o.GetType().GetField(eventName, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o))?.DynamicInvoke(args);
}

/// <summary>
/// Base de las pruebas de la ventana principal: escritorio, area de notificacion y sesiones de
/// mentira; las ventanas no modales (sesiones sueltas) se registran pero no se enseñan.
/// </summary>
public abstract class MainWindowTest : UiTest
{
    internal FakeDesktop Desk { get; } = new();
    internal FakeTrayShell TrayShell { get; } = new();
    protected List<FakeSession> Sessions { get; } = [];

    /// <summary>Lo que hara la proxima sesion al crearse (conectar, pantalla completa...).</summary>
    protected Action<FakeSession>? Setup { get; set; }

    private readonly IDesktop _desktop = Desktop.Current;
    private readonly ITrayShell _shell = TrayIcon.Shell;
    private readonly Func<Connection, ISession> _factory = SessionFactory.Create;
    private readonly Func<AppSettings, Store, CloudSync> _sync = MainWindow.CreateSync;
    private readonly Action<Window> _showWindow = Dialogs.ShowWindow;

    protected MainWindowTest()
    {
        Desktop.Current = Desk;
        TrayIcon.Shell = TrayShell;
        SessionFactory.Create = c =>
        {
            var s = new FakeSession(c);
            Setup?.Invoke(s);
            Sessions.Add(s);
            return s;
        };
        // Las ventanas sueltas no se enseñan: asi se pueden poner a pantalla completa sin que se vean.
        Dialogs.ShowWindow = w =>
        {
            Ui.Hide(w);
            Ui.Shown.Add(w);
        };
    }

    public override void Dispose()
    {
        try
        {
            // Las principales, sin preguntar por sus ventanas sueltas (ni dejar la pregunta para la prueba siguiente).
            Ui.Dispatcher.Invoke(() =>
            {
                foreach (var main in Application.Current.Windows.OfType<MainWindow>().ToList())
                {
                    try { main.CloseForced(); } catch (Exception) { }
                }
            });
            Ui.Reset();
        }
        finally
        {
            Desktop.Current = _desktop;
            TrayIcon.Shell = _shell;
            SessionFactory.Create = _factory;
            MainWindow.CreateSync = _sync;
            Dialogs.ShowWindow = _showWindow;
            base.Dispose();
        }
    }

    protected static Connection Conn(string name, string folder = "", ConnectionKind kind = ConnectionKind.Rdp, string? host = null, string password = "pw") => new()
    {
        Name = name,
        Folder = folder,
        Kind = kind,
        Host = host ?? name.ToLowerInvariant() + ".lan",
        Port = kind switch { ConnectionKind.Ssh or ConnectionKind.Sftp => 22, ConnectionKind.Ftp => 21, _ => 3389 },
        UserName = "pepe",
        PasswordProtected = password.Length > 0 ? Secrets.Protect(password) : string.Empty,
    };

    /// <summary>Deja en el almacen esas conexiones (y carpetas vacias) antes de abrir la ventana.</summary>
    protected static void Seed(IEnumerable<Connection> connections, params string[] emptyFolders)
    {
        var store = new Store();
        store.Connections.AddRange(connections);
        store.EmptyFolders.AddRange(emptyFolders);
        store.Save();
    }

    protected static Store Saved()
    {
        var store = new Store();
        store.Load();
        return store;
    }

    /// <summary>La ventana principal, enseñada fuera de la pantalla (o sin enseñar: para la pantalla completa).</summary>
    protected static MainWindow NewMain(bool show = true) => Ui.Run(() =>
    {
        var main = new MainWindow();
        Ui.Hide(main);
        if (show)
            main.Show();
        return main;
    });

    protected static IEnumerable<TreeViewItem> Items(MainWindow main) =>
        main.Tree.Items.OfType<TreeViewItem>().SelectMany(Flat);

    private static IEnumerable<TreeViewItem> Flat(TreeViewItem i) => new[] { i }.Concat(i.Items.OfType<TreeViewItem>().SelectMany(Flat));

    protected static List<string> Texts(TreeViewItem item) => ((Panel)item.Header).Children.OfType<TextBlock>().Skip(1).Select(t => t.Text).ToList();

    protected static string Text(TreeViewItem item) => Texts(item)[0];

    protected static TreeViewItem Item(MainWindow main, string text) => Items(main).Single(i => Text(i) == text);

    protected static void Select(MainWindow main, string text) => Item(main, text).IsSelected = true;

    /// <summary>Las pestañas abiertas, por el titulo de su cabecera.</summary>
    protected static List<string> TabTitles(MainWindow main) =>
        main.Tabs.Items.OfType<TabItem>().Select(t => ((Panel)t.Header).Children.OfType<TextBlock>().First().Text).ToList();

    protected static TabItem Tab(MainWindow main, int index = 0) => main.Tabs.Items.OfType<TabItem>().ElementAt(index);

    /// <summary>Boton de la cabecera de una sesion (pestaña o ventana suelta) por su AutomationId.</summary>
    protected static Button HeaderButton(Panel header, string id) =>
        header.Children.OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == id);

    protected static Panel Header(TabItem tab) => (Panel)tab.Header;

    /// <summary>Abre la conexion con ese nombre como lo haria el usuario (seleccionar y conectar) y espera a que acabe.</summary>
    protected static void Connect(MainWindow main, string name)
    {
        Ui.Run(() =>
        {
            Select(main, name);
            Ui.Click(main.ConnectButton);
        });
        Ui.Flush();
    }

    /// <summary>Las ventanas sueltas abiertas ahora (llamar en el hilo de interfaz).</summary>
    protected static List<SessionWindow> Detached() => Application.Current.Windows.OfType<SessionWindow>().ToList();
}
