using System.IO;

namespace SocRcManager.Services;

/// <summary>
/// Registro de errores en <c>%LOCALAPPDATA%\sOCRCManager\errors.log</c> (constitucion general,
/// 6.9 y 6.12): lo tecnico, con la traza completa, va aqui y no a la cara del usuario. Al pasar de
/// 1 MB se guarda como <c>errors.old.log</c> y se empieza otro.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCRCManager");

    public static string FilePath { get; } = Path.Combine(Folder, "errors.log");

    public static void Write(string text)
    {
        // Registrar nunca puede ser otro error: si el disco falla, se pierde la linea y ya.
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1024 * 1024)
                    File.Move(FilePath, Path.Combine(Folder, "errors.old.log"), overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
