using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SocRcManager.Localization;

namespace SocRcManager.Controls;

/// <summary>
/// Una casilla de contraseña con el boton del ojo para verla (constitucion general, seccion 6:
/// toda casilla de contraseña lleva un boton para enseñarla). Por dentro son un PasswordBox y un
/// TextBox superpuestos: WPF no deja quitar los puntos a un PasswordBox, asi que al pulsar el ojo
/// se enseña el TextBox con el mismo texto y se esconde al soltar el foco o al volver a pulsar.
/// </summary>
public sealed class RevealPasswordBox : Grid
{
    private readonly PasswordBox _hidden = new();
    private readonly TextBox _shown = new() { Visibility = Visibility.Collapsed };
    private readonly ToggleButton _eye = new();
    private bool _syncing;

    public RevealPasswordBox()
    {
        _hidden.Style = (Style)FindResource("PasswordField");
        _shown.Style = (Style)FindResource("Field");
        _hidden.Padding = _shown.Padding = new Thickness(8, 0, 34, 0);

        _eye.Style = (Style)FindResource("GhostIconButton") is { } ghost ? EyeStyle(ghost) : null;
        _eye.Content = "";
        _eye.FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        _eye.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
        _eye.Width = _eye.Height = 26;
        _eye.FontSize = 15;
        _eye.HorizontalAlignment = HorizontalAlignment.Right;
        _eye.VerticalAlignment = VerticalAlignment.Center;
        _eye.Margin = new Thickness(0, 0, 4, 0);
        _eye.Focusable = false;
        _eye.ToolTip = Loc.Get("ShowPassword");
        _eye.Checked += (_, _) => Reveal(true);
        _eye.Unchecked += (_, _) => Reveal(false);

        _hidden.PasswordChanged += (_, _) => { if (!_syncing) { _syncing = true; _shown.Text = _hidden.Password; _syncing = false; PasswordChanged?.Invoke(this, EventArgs.Empty); } };
        _shown.TextChanged += (_, _) => { if (!_syncing) { _syncing = true; _hidden.Password = _shown.Text; _syncing = false; PasswordChanged?.Invoke(this, EventArgs.Empty); } };

        Children.Add(_hidden);
        Children.Add(_shown);
        Children.Add(_eye);
    }

    private static Style EyeStyle(Style ghostButton)
    {
        // El estilo de boton fantasma es de Button; el ojo es un ToggleButton con el mismo aspecto.
        var style = new Style(typeof(ToggleButton));
        foreach (var setter in ghostButton.Setters.OfType<Setter>())
            if (setter.Property.Name is "FontFamily" or "FontSize" or "Foreground" or "Cursor")
                style.Setters.Add(new Setter(setter.Property, setter.Value));
        style.Setters.Add(new Setter(BackgroundProperty, System.Windows.Media.Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        var template = new ControlTemplate(typeof(ToggleButton));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    public event EventHandler? PasswordChanged;

    public string Password
    {
        get => _hidden.Password;
        set { _hidden.Password = value; }
    }

    private void Reveal(bool show)
    {
        _eye.Content = show ? "" : "";
        _eye.ToolTip = Loc.Get(show ? "HidePassword" : "ShowPassword");
        _shown.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _hidden.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (show) { _shown.Focus(); _shown.CaretIndex = _shown.Text.Length; }
        else { _hidden.Focus(); }
    }

    public new bool Focus() => _shown.Visibility == Visibility.Visible ? _shown.Focus() : _hidden.Focus();
}
