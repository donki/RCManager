using System.Diagnostics;
using System.Windows;

namespace SocRcManager.Services;

/// <summary>
/// La unica puerta por la que la aplicacion enseña ventanas, pide ficheros o abre programas
/// externos. En la aplicacion hace lo de siempre; las pruebas la cambian para abrir las ventanas
/// fuera de la pantalla y contestarlas, y para no lanzar nada de verdad.
/// </summary>
public static class Dialogs
{
    /// <summary>Ventana modal (ShowDialog).</summary>
    internal static Func<Window, bool?> ShowModal { get; set; } = w => w.ShowDialog();

    /// <summary>Ventana no modal (Show).</summary>
    internal static Action<Window> ShowWindow { get; set; } = w => w.Show();

    /// <summary>Elegir ficheros que existen (dialogo del sistema). Null si se cancela.</summary>
    internal static Func<Window?, string, bool, string[]?> PickFiles { get; set; } = PickFilesWithSystem;

    /// <summary>Elegir donde guardar un fichero (dialogo del sistema). Null si se cancela.</summary>
    internal static Func<Window?, string, string, string?> PickSaveFile { get; set; } = PickSaveFileWithSystem;

    /// <summary>Escribir un fichero (las pruebas lo cambian para no tocar el disco del usuario).</summary>
    internal static Action<string, string> WriteFile { get; set; } = System.IO.File.WriteAllText;

    /// <summary>Abrir un programa, un fichero o una direccion con lo que tenga Windows.</summary>
    internal static Action<ProcessStartInfo> Start { get; set; } = psi => Process.Start(psi);

    /// <summary>Aviso del sistema (MessageBox).</summary>
    internal static Action<Window, string, string> SystemMessage { get; set; } = (owner, text, title) => MessageBox.Show(owner, text, title);

    private static string? PickSaveFileWithSystem(Window? owner, string filter, string fileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = fileName, OverwritePrompt = true, AddExtension = true };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    private static string[]? PickFilesWithSystem(Window? owner, string filter, bool multiselect)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter, CheckFileExists = true, Multiselect = multiselect };
        return dialog.ShowDialog(owner) == true ? dialog.FileNames : null;
    }
}
