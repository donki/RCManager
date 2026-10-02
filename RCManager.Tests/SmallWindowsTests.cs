using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SocRcManager.Controls;
using SocRcManager.Localization;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>«Acerca de»: version, contacto y cambio de idioma (que la vuelve a abrir traducida).</summary>
public sealed class AboutWindowTests : UiTest
{
    private static bool Filled(Window w, Button b) =>
        Equals(((SolidColorBrush)b.Background).Color, ((SolidColorBrush)w.FindResource("Primary")).Color);

    [Fact]
    public void Enseña_la_version_sin_el_hash_y_el_idioma_activo()
    {
        var w = Ui.Run(() => Ui.Show(new AboutWindow()));
        Ui.Run(() =>
        {
            Assert.Matches(@"^v\d+(\.\d+)+$", w.VersionLabel.Text);
            Assert.NotNull(w.LogoImage.Source);
            Assert.True(Filled(w, w.SpanishButton));
            Assert.False(Filled(w, w.EnglishButton));
        });
    }

    [Fact]
    public void Contacto_abre_el_correo()
    {
        var w = Ui.Run(() => Ui.Show(new AboutWindow()));
        Ui.Run(() => Ui.Click(w.ContactButton));
        var psi = Assert.Single(Ui.Started);
        Assert.StartsWith("mailto:", psi.FileName);
        Assert.True(psi.UseShellExecute);
        Assert.Empty(Ui.Messages);
    }

    [Fact]
    public void Contacto_sin_programa_de_correo_avisa()
    {
        var start = Dialogs.Start;
        try
        {
            Dialogs.Start = _ => throw new System.ComponentModel.Win32Exception("No hay aplicacion asociada");
            var w = Ui.Run(() => Ui.Show(new AboutWindow()));
            Ui.Run(() => Ui.Click(w.ContactButton));
        }
        finally
        {
            Dialogs.Start = start;
        }
        var (title, text) = Assert.Single(Ui.Messages);
        Assert.Equal(Loc.Get("Contact"), title);
        Assert.Equal("No hay aplicacion asociada", text);
    }

    [Fact]
    public void Pulsar_el_idioma_activo_no_hace_nada()
    {
        var w = Ui.Run(() => Ui.Show(new AboutWindow()));
        Ui.Run(() => Ui.Click(w.SpanishButton));
        Assert.Equal("es", Loc.Language);
        Assert.True(Ui.Run(() => w.IsVisible));
        Assert.Empty(Ui.Modals);
    }

    [Fact]
    public void Cambiar_de_idioma_la_vuelve_a_abrir_traducida_con_el_mismo_dueño()
    {
        var owner = Ui.Run(() => Ui.Show(new Window()));
        var w = Ui.Run(() => Ui.Show(new AboutWindow { Owner = owner }));
        Window? reopened = null;
        bool englishFilled = false;
        Ui.Answer<AboutWindow>(a =>
        {
            reopened = a;
            englishFilled = Filled(a, a.EnglishButton) && !Filled(a, a.SpanishButton);
            Ui.Click(a.CloseButton);
        });
        Ui.Run(() => Ui.Click(w.EnglishButton));
        Assert.Equal("en", Loc.Language);
        Assert.False(Ui.Run(() => w.IsVisible));
        Assert.NotNull(reopened);
        Assert.NotSame(w, reopened);
        Assert.Same(owner, Ui.Run(() => reopened!.Owner));
        Assert.True(englishFilled);
        Assert.Equal(0, Ui.PendingAnswers);

        // Y de vuelta al español.
        var en = Ui.Run(() => Ui.Show(new AboutWindow()));
        Ui.Run(() => Ui.Click(en.SpanishButton));
        Assert.Equal("es", Loc.Language);
    }
}

/// <summary>Los dialogos pequeños: texto, contraseña (con y sin casilla), aviso y confirmacion.</summary>
public sealed class PromptWindowMoreTests : UiTest
{
    private static Window Owner() => Ui.Run(() => Ui.Show(new Window()));

    private static void Press(Window w, Key key) => w.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w)!, 0, key)
    {
        RoutedEvent = Keyboard.KeyDownEvent,
    });

    [Fact]
    public void Contraseña_sin_casilla()
    {
        var owner = Owner();
        Ui.Answer<PromptWindow>(w =>
        {
            Assert.Empty(Ui.FindAll<CheckBox>(w));
            Ui.Find<RevealPasswordBox>(w)!.Password = "s3creta";
            Ui.Click(Ui.ButtonById(w, "OkButton")!);
        });
        Assert.Equal("s3creta", Ui.Run(() => PromptWindow.AskPassword(owner, "Titulo", "Contraseña de ana")));
    }

    [Fact]
    public void Contraseña_cancelada_es_null()
    {
        var owner = Owner();
        Ui.Answer<PromptWindow>(w =>
        {
            Ui.Find<RevealPasswordBox>(w)!.Password = "no";
            Ui.Click(Ui.ButtonById(w, "CancelButton")!);
        });
        Assert.Null(Ui.Run(() => PromptWindow.AskPassword(owner, "T", "M")));
        Assert.Null(Ui.Run(() => PromptWindow.AskPassword(owner, "T", "M", "Guardar")));   // sin respuesta: cancela
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Contraseña_con_la_casilla_de_guardarla(bool save)
    {
        var owner = Owner();
        string? checkText = null;
        Ui.Answer<PromptWindow>(w =>
        {
            var check = Assert.Single(Ui.FindAll<CheckBox>(w));
            checkText = (string)check.Content;
            check.IsChecked = save;
            Ui.Find<RevealPasswordBox>(w)!.Password = "pw";
            Ui.Click(Ui.ButtonById(w, "OkButton")!);
        });
        Assert.Equal(("pw", save), Ui.Run(() => PromptWindow.AskPassword(owner, "T", "M", "Guardar la contraseña")));
        Assert.Equal("Guardar la contraseña", checkText);
    }

    [Fact]
    public void Aviso_solo_tiene_aceptar()
    {
        var owner = Owner();
        int buttons = 0;
        string? message = null;
        Ui.Answer<PromptWindow>(w =>
        {
            buttons = Ui.FindAll<Button>(w).Count;
            message = Ui.FindAll<TextBlock>(w).Select(t => t.Text).FirstOrDefault(t => t == "Ya esta");
            Assert.Null(Ui.ButtonById(w, "CancelButton"));
            Assert.Empty(Ui.FindAll<TextBox>(w));
            Assert.Equal(Loc.Get("Ok"), Ui.ButtonById(w, "OkButton")!.ToolTip);
            Ui.Click(Ui.ButtonById(w, "OkButton")!);
        });
        Ui.Run(() => PromptWindow.Alert(owner, "Aviso", "Ya esta"));
        Assert.Equal(1, buttons);
        Assert.Equal("Ya esta", message);
        Assert.Equal(0, Ui.PendingAnswers);
    }

    [Fact]
    public void Confirmar_con_el_boton_de_borrar_o_uno_propio()
    {
        var owner = Owner();
        object? tooltip = null;
        Ui.Answer<PromptWindow>(w =>
        {
            tooltip = Ui.ButtonById(w, "OkButton")!.ToolTip;
            Ui.Click(Ui.ButtonById(w, "OkButton")!);
        });
        Assert.True(Ui.Run(() => PromptWindow.Confirm(owner, "Borrar", "¿Seguro?")));
        Assert.Equal(Loc.Get("Delete"), tooltip);

        object? glyph = null;
        Ui.Answer<PromptWindow>(w =>
        {
            var ok = Ui.ButtonById(w, "OkButton")!;
            (tooltip, glyph) = (ok.ToolTip, ok.Content);
            Ui.Click(Ui.ButtonById(w, "CancelButton")!);
        });
        Assert.False(Ui.Run(() => PromptWindow.Confirm(owner, "Cerrar", "¿Cerrar todo?", "Cerrar todo", "")));
        Assert.Equal(("Cerrar todo", (object)""), (tooltip, glyph));
    }

    [Fact]
    public void Escape_cancela_y_otra_tecla_no()
    {
        var owner = Owner();
        bool stillOpen = false;
        Ui.Answer<PromptWindow>(w =>
        {
            Press(w, Key.A);
            stillOpen = w.IsVisible;
            Press(w, Key.Escape);
        });
        Assert.Null(Ui.Run(() => PromptWindow.Ask(owner, "T", "M", "texto")));
        Assert.True(stillOpen);
        Assert.Equal(0, Ui.PendingAnswers);
    }

    [Fact]
    public void Texto_abre_con_lo_inicial_seleccionado()
    {
        var owner = Owner();
        string? selected = null;
        Ui.Answer<PromptWindow>(w =>
        {
            selected = Ui.Find<TextBox>(w)!.SelectedText;
            Ui.Click(Ui.ButtonById(w, "OkButton")!);
        });
        Assert.Equal("Inicial", Ui.Run(() => PromptWindow.Ask(owner, "T", "M", "Inicial")));
        Assert.Equal("Inicial", selected);
    }
}

/// <summary>La casilla de contraseña con el ojo para verla.</summary>
public sealed class RevealPasswordBoxTests : UiTest
{
    private sealed record Parts(RevealPasswordBox Box, PasswordBox Hidden, TextBox Shown, ToggleButton Eye);

    private static Parts Make()
    {
        var host = Ui.Run(() =>
        {
            var box = new RevealPasswordBox();
            Ui.Show(new Window { Content = box, Width = 300, Height = 120 });
            return box;
        });
        return Ui.Run(() => new Parts(host, host.Children.OfType<PasswordBox>().Single(), host.Children.OfType<TextBox>().Single(),
            host.Children.OfType<ToggleButton>().Single()));
    }

    [Fact]
    public void Password_ida_y_vuelta_y_el_texto_visible_sigue()
    {
        var p = Make();
        var changes = 0;
        Ui.Run(() =>
        {
            p.Box.PasswordChanged += (_, _) => changes++;
            p.Box.Password = "uno";
            Assert.Equal("uno", p.Box.Password);
            Assert.Equal("uno", p.Hidden.Password);
            Assert.Equal("uno", p.Shown.Text);
            Assert.Equal(1, changes);

            // Lo escrito con el ojo abierto va a la contraseña.
            p.Shown.Text = "dos";
            Assert.Equal("dos", p.Box.Password);
            Assert.Equal(2, changes);
        });
    }

    [Fact]
    public void El_ojo_enseña_y_esconde()
    {
        var p = Make();
        Ui.Run(() =>
        {
            p.Box.Password = "secreto";
            Assert.Equal(Visibility.Collapsed, p.Shown.Visibility);
            Assert.Equal(Loc.Get("ShowPassword"), p.Eye.ToolTip);
            Assert.False(p.Eye.Focusable);
            Assert.IsType<FontFamily>(p.Eye.FontFamily);

            p.Eye.IsChecked = true;
            Assert.Equal(Visibility.Visible, p.Shown.Visibility);
            Assert.Equal(Visibility.Collapsed, p.Hidden.Visibility);
            Assert.Equal(Loc.Get("HidePassword"), p.Eye.ToolTip);
            Assert.Equal("", p.Eye.Content);
            Assert.Equal("secreto".Length, p.Shown.CaretIndex);
            p.Box.Focus();

            p.Eye.IsChecked = false;
            Assert.Equal(Visibility.Collapsed, p.Shown.Visibility);
            Assert.Equal(Visibility.Visible, p.Hidden.Visibility);
            Assert.Equal(Loc.Get("ShowPassword"), p.Eye.ToolTip);
            Assert.Equal("", p.Eye.Content);
            p.Box.Focus();
        });
    }

    [Fact]
    public void El_ojo_se_parece_al_boton_fantasma()
    {
        var p = Make();
        Ui.Run(() =>
        {
            Assert.NotNull(p.Eye.Style);
            Assert.Equal(typeof(ToggleButton), p.Eye.Style.TargetType);
            Assert.NotNull(p.Eye.Template);
            Assert.Equal(Brushes.Transparent, p.Eye.Background);
        });
    }
}

/// <summary>Los restos: la puerta de dialogos, el modo aislado y el tema.</summary>
public sealed class DialogsSandboxThemeTests : UiTest
{
    [Fact]
    public void Sandbox_sin_la_variable_no_hace_nada()
    {
        var before = Environment.GetEnvironmentVariable(Sandbox.Variable);
        var folder = Store.Folder;
        try
        {
            Environment.SetEnvironmentVariable(Sandbox.Variable, null);
            Sandbox.Apply();
            Assert.False(Sandbox.IsOn);
            Assert.Equal(folder, Store.Folder);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Sandbox.Variable, before);
        }
    }

    [Fact]
    public void Tema_si_el_registro_falla_es_claro()
    {
        Assert.False(ThemeManager.PrefersDark(() => throw new UnauthorizedAccessException()));
        Assert.False(ThemeManager.PrefersDark(() => null));
        Assert.False(ThemeManager.PrefersDark(() => 1));
        Assert.False(ThemeManager.PrefersDark(() => "0"));
        Assert.True(ThemeManager.PrefersDark(() => 0));
    }

    [Fact]
    public void Tema_aplicado_a_la_aplicacion_sigue_a_Windows()
    {
        var keys = ThemeManager.Palette(false).Keys.ToList();
        var saved = Ui.Run(() => keys.ToDictionary(k => k, k => Application.Current.Resources[k]));
        try
        {
            Ui.Run(ThemeManager.Apply);
            Assert.Equal(ThemeManager.PrefersDark(), ThemeManager.IsDark);
            var page = Ui.Run(() => ((SolidColorBrush)Application.Current.Resources["PageBackground"]).Color.ToString());
            Assert.Equal("#FF" + ThemeManager.Palette(ThemeManager.IsDark)["PageBackground"].TrimStart('#'), page);
        }
        finally
        {
            Ui.Run(() =>
            {
                foreach (var (k, v) in saved)
                    Application.Current.Resources[k] = v;
            });
        }
    }

    [Fact]
    public void Tema_en_una_ventana_sin_abrir_no_hace_nada()
    {
        Ui.Run(() =>
        {
            var w = new Window();
            ThemeManager.ApplyToWindow(w);   // sin handle todavia
            Ui.Show(w);
            ThemeManager.ApplyToWindow(w);   // con handle: la barra de titulo sigue al tema
        });
    }
}
