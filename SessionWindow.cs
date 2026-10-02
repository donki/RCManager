using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SocRcManager.Services;

namespace SocRcManager;

/// <summary>
/// Una pestaña sacada de la ventana principal a una ventana propia (para tenerla en otro monitor o
/// al lado de otra). Arriba, la misma cabecera que tenia la pestaña (nombre, zoom, pantalla completa,
/// volver y desconectar); debajo, la sesion tal cual, sin reconectar.
/// </summary>
/// <remarks>
/// <para><b>La sesion no se corta.</b> Lo que se mueve es el control de la sesion: el terminal y el
/// explorador de ficheros son WPF puro; el escritorio remoto es el control ActiveX de Windows dentro de
/// un <c>WindowsFormsHost</c>, y WPF, al quitarlo de una ventana, aparca su ventana nativa (la misma
/// que ya pasa al cambiar de pestaña) y la vuelve a colgar de la ventana nueva. El control no se
/// destruye ni se vuelve a crear, asi que la conexion sigue viva. Por eso el control se suelta
/// (<see cref="ReleaseView"/>) <b>antes</b> de cerrar la ventana: al destruirse una ventana, Windows
/// destruye tambien sus ventanas hijas.</para>
///
/// <para>Cerrar esta ventana con la X la devuelve a la principal (no desconecta: para eso esta el
/// boton rojo). La monta en codigo, como <see cref="PromptWindow"/>.</para>
/// </remarks>
public sealed class SessionWindow : Window
{
    private readonly Border _bar;
    private readonly ContentControl _headerHost = new() { VerticalAlignment = VerticalAlignment.Center, Focusable = false };
    private readonly Border _body;

    /// <summary>La X de la ventana: quiere volver a la principal.</summary>
    public event Action? AttachRequested;

    /// <summary>Se ha pulsado en la barra y se arrastra (para soltarla sobre la principal).</summary>
    public event Action? BarDragStarted;

    /// <summary>F11 / Ctrl+Esc: pantalla completa de la ventana (las sesiones sin pantalla completa propia).</summary>
    public event Action? FullScreenToggleRequested;

    /// <summary>Cerrar sin devolver nada a la principal (la principal se cierra, o la sesion se acabo).</summary>
    public bool ForceClose { get; set; }

    public SessionWindow(string title, FrameworkElement header, FrameworkElement view)
    {
        Title = title;
        Width = 1024;
        Height = 700;
        MinWidth = SessionPlacement.MinWidth;
        MinHeight = SessionPlacement.MinHeight;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Background = (System.Windows.Media.Brush)FindResource("PageBackground");
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "SessionWindow");
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        _headerHost.Content = header;
        // Lo que la cabecera heredaba de la pestaña.
        _headerHost.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary");
        // A la izquierda una asa para arrastrar (y toda la barra vale): soltada sobre la ventana
        // principal, la pestaña vuelve.
        var grip = new TextBlock
        {
            Text = "",
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 12,
            Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var barContent = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(grip, Dock.Left);
        DockPanel.SetDock(_headerHost, Dock.Left);
        barContent.Children.Add(grip);
        barContent.Children.Add(_headerHost);
        _bar = new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("CardBackground"),
            Padding = new Thickness(10, 4, 10, 4),
            Child = barContent,
            Cursor = Cursors.SizeAll,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_bar, "SessionBar");
        _body = new Border { Background = (System.Windows.Media.Brush)FindResource("MirrorBackground"), Child = view };

        var root = new DockPanel();
        DockPanel.SetDock(_bar, Dock.Top);
        root.Children.Add(_bar);
        root.Children.Add(_body);
        Content = root;

        // Con el raton capturado: un movimiento rapido saca el puntero de la barra (hacia la barra de
        // titulo, que no es de WPF) antes de pasar el umbral del arrastre.
        _bar.PreviewMouseLeftButtonDown += (_, e) => BarPressed(e.OriginalSource as DependencyObject, e.GetPosition(this));
        _bar.PreviewMouseMove += (_, e) => BarMoved(e.GetPosition(this), e.LeftButton == MouseButtonState.Pressed);
        _bar.PreviewMouseLeftButtonUp += (_, _) => BarReleased();

        Closing += (_, e) =>
        {
            if (ForceClose)
                return;
            // La X devuelve la pestaña a la principal: no se pierde una sesion por un clic.
            e.Cancel = true;
            Dispatcher.BeginInvoke(() => AttachRequested?.Invoke());
        };
    }

    private Point? _dragStart;

    /// <summary>La barra de arriba (asa y cabecera).</summary>
    internal Border Bar => _bar;

    /// <summary>Pulsar en la barra (fuera de sus botones) prepara el arrastre.</summary>
    internal void BarPressed(DependencyObject? source, Point position)
    {
        _dragStart = source is not null && IsInButton(source) ? null : position;
        if (_dragStart is not null)
            _bar.CaptureMouse();
    }

    /// <summary>Mover con el boton pulsado: pasado el umbral del sistema, empieza el arrastre.</summary>
    internal void BarMoved(Point position, bool pressed)
    {
        if (_dragStart is not { } start || !pressed)
            return;
        var delta = position - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _dragStart = null;
        _bar.ReleaseMouseCapture();
        BarDragStarted?.Invoke();
    }

    internal void BarReleased()
    {
        _dragStart = null;
        _bar.ReleaseMouseCapture();
    }

    private static bool IsInButton(DependencyObject d)
    {
        for (var o = d; o is not null; o = System.Windows.Media.VisualTreeHelper.GetParent(o))
            if (o is System.Windows.Controls.Primitives.ButtonBase)
                return true;
        return false;
    }

    /// <summary>Suelta el control de la sesion (antes de cerrar la ventana o de devolverlo a la principal).</summary>
    public FrameworkElement? ReleaseView()
    {
        var view = _body.Child as FrameworkElement;
        _body.Child = null;
        return view;
    }

    /// <summary>Suelta la cabecera (para devolverla a la pestaña).</summary>
    public FrameworkElement? ReleaseHeader()
    {
        var header = _headerHost.Content as FrameworkElement;
        _headerHost.Content = null;
        return header;
    }

    /// <summary>Medidas para recordar donde estaba.</summary>
    public WindowBounds Bounds
    {
        get
        {
            var r = WindowState == WindowState.Normal || _fullScreen ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (_fullScreen)
                r = _beforeFullScreen;
            return new WindowBounds(r.Left, r.Top, r.Width, r.Height, WindowState == WindowState.Maximized && !_fullScreen);
        }
    }

    public void Place(WindowBounds b)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = b.Left;
        Top = b.Top;
        Width = b.Width;
        Height = b.Height;
        if (b.Maximized)
            Loaded += (_, _) => WindowState = WindowState.Maximized;
    }

    /// <summary>Al frente: restaurada si estaba minimizada.</summary>
    public void BringToFront(bool activate = true)
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        if (activate)
            Activate();
    }

    // ------------------------------------------------------------------ Pantalla completa (sin la del control)

    private bool _fullScreen;
    private Rect _beforeFullScreen;
    private WindowState _stateBeforeFullScreen;

    public bool IsFullScreen => _fullScreen;

    public void SetFullScreen(bool on)
    {
        if (_fullScreen == on)
            return;
        _fullScreen = on;
        if (on)
        {
            _beforeFullScreen = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _stateBeforeFullScreen = WindowState;
            _bar.Visibility = Visibility.Collapsed;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _stateBeforeFullScreen == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            _bar.Visibility = Visibility.Visible;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (HandleKey(e.Key, Keyboard.Modifiers))
            e.Handled = true;
    }

    /// <summary>F11, o Ctrl+Esc ya en pantalla completa: pide cambiarla. Devuelve si la tecla se ha usado.</summary>
    internal bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if (key != Key.F11 && !(key == Key.Escape && _fullScreen && (modifiers & ModifierKeys.Control) != 0))
            return false;
        FullScreenToggleRequested?.Invoke();
        return true;
    }

    /// <summary>Lleva la ventana al monitor pedido (1..n; 0 = donde esta) antes de la pantalla completa.</summary>
    public static void MoveToScreen(Window window, int screen) =>
        MoveToScreen(window, screen, [.. System.Windows.Forms.Screen.AllScreens.Select(s => s.Bounds)]);

    /// <summary>Lo mismo con los monitores dados (sus medidas en pixeles, en el orden de Windows).</summary>
    internal static void MoveToScreen(Window window, int screen, IReadOnlyList<System.Drawing.Rectangle> screens)
    {
        if (screen <= 0 || screen > screens.Count)
            return;
        var bounds = screens[screen - 1];
        var source = PresentationSource.FromVisual(window);
        var m = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var topLeft = m.Transform(new Point(bounds.Left, bounds.Top));
        if (window.WindowState != WindowState.Normal)
            window.WindowState = WindowState.Normal;
        window.Left = topLeft.X + 40;
        window.Top = topLeft.Y + 40;
    }
}
