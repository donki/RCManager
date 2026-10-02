using System.Windows.Controls;
using SocRcManager.Services;

namespace SocRcManager.Tests;

/// <summary>Que el hilo de interfaz de las pruebas funciona: ventanas con los recursos de la App y modales contestadas.</summary>
public sealed class HarnessTests : UiTest
{
    [Fact]
    public void La_ventana_principal_se_crea_con_los_estilos_y_una_modal_se_contesta()
    {
        Ui.Answer(w =>
        {
            Ui.Find<TextBox>(w)!.Text = "Clientes";
            Ui.Click(Ui.ButtonById(w, "OkButton")!);
            return true;
        });
        var headers = Ui.Run(() =>
        {
            var main = Ui.Show(new MainWindow());
            Ui.Click(main.NewFolderButton);
            return main.Tree.Items.OfType<TreeViewItem>().Select(i => Ui.FindAll<TextBlock>((System.Windows.DependencyObject)i.Header).Last().Text).ToList();
        });
        Assert.Equal(["Clientes"], headers);
        Assert.Single(Ui.Modals);
        Assert.Equal(0, Ui.PendingAnswers);
        Assert.Contains("Clientes", File.ReadAllText(Store.Location));
    }
}
