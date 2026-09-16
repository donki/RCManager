using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SocRcManager.Localization;
using SocRcManager.Services;

namespace SocRcManager;

/// <summary>
/// Los tres dialogos pequeños —una linea de texto, una contraseña y una confirmacion— con el
/// aspecto de la aplicacion en vez de los del sistema (constitucion 6.2). Se montan en codigo
/// porque son tres variantes de lo mismo.
/// </summary>
public sealed class PromptWindow : Window
{
    private readonly TextBox? _text;
    private readonly Controls.RevealPasswordBox? _password;
    private readonly CheckBox? _check;

    private PromptWindow(Window owner, string title, string message, string? initial, bool password, bool confirm, string? checkText = null)
    {
        Owner = owner;
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)FindResource("PageBackground");
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(18, 14, 18, 16) };
        var stack = new StackPanel();
        card.Child = stack;

        stack.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"),
        });

        if (!confirm)
        {
            if (password)
            {
                _password = new Controls.RevealPasswordBox { Margin = new Thickness(0, 10, 0, 0) };
                stack.Children.Add(_password);
                if (checkText is not null)
                {
                    _check = new CheckBox { Style = (Style)FindResource("Check"), Content = checkText, Margin = new Thickness(0, 10, 0, 0) };
                    stack.Children.Add(_check);
                }
            }
            else
            {
                _text = new TextBox { Style = (Style)FindResource("Field"), Margin = new Thickness(0, 10, 0, 0), Text = initial ?? string.Empty };
                stack.Children.Add(_text);
            }
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("Cancel"), IsCancel = true };
        var ok = new Button
        {
            Style = (Style)FindResource(confirm ? "DangerIconButton" : "IconButton"),
            Content = confirm ? "" : "",
            ToolTip = confirm ? Loc.Get("Delete") : Loc.Get("Save"),
            IsDefault = true,
        };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(card);
        root.Children.Add(buttons);
        Content = root;

        Loaded += (_, _) =>
        {
            if (_text is not null) { _text.Focus(); _text.SelectAll(); }
            _password?.Focus();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
    }

    /// <summary>Una linea de texto. Null si se cancela.</summary>
    public static string? Ask(Window owner, string title, string message, string initial)
    {
        var w = new PromptWindow(owner, title, message, initial, password: false, confirm: false);
        return w.ShowDialog() == true ? w._text!.Text : null;
    }

    /// <summary>Una contraseña. Null si se cancela.</summary>
    public static string? AskPassword(Window owner, string title, string message)
    {
        var w = new PromptWindow(owner, title, message, null, password: true, confirm: false);
        return w.ShowDialog() == true ? w._password!.Password : null;
    }

    /// <summary>Una contraseña con la casilla de «guardarla». Null si se cancela.</summary>
    public static (string Password, bool Save)? AskPassword(Window owner, string title, string message, string saveText)
    {
        var w = new PromptWindow(owner, title, message, null, password: true, confirm: false, checkText: saveText);
        return w.ShowDialog() == true ? (w._password!.Password, w._check!.IsChecked == true) : null;
    }

    public static bool Confirm(Window owner, string title, string message) =>
        new PromptWindow(owner, title, message, null, password: false, confirm: true).ShowDialog() == true;
}
