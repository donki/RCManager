namespace SocRcManager.Services;

/// <summary>
/// Lo que trae la linea de comandos al arrancar (o lo que una segunda instancia le pasa a la que ya
/// estaba abierta, que lo atiende igual).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>--open "Nombre"</c> (repetible): abre esas conexiones (accesos directos a un servidor).</item>
/// <item><c>--edit "Nombre"</c> y <c>--edit-tab N</c>: el editor de esa conexion en esa pestaña.</item>
/// <item><c>--edit-file "Conexion" "/ruta"</c>: esa conexion de ficheros y el fichero en el editor.</item>
/// <item><c>--size AnchoxAlto</c>: tamaño de la ventana (capturas de pantalla).</item>
/// <item><c>--tray</c>: arrancar escondida en el area de notificacion.</item>
/// </list>
/// Lo que no se entiende se ignora; un valor que falta al final tambien.
/// </remarks>
public sealed record StartupArgs
{
    public IReadOnlyList<string> Open { get; init; } = [];
    public string? Edit { get; init; }
    public int EditTab { get; init; }
    public string? EditFileConnection { get; init; }
    public string? EditFilePath { get; init; }
    public (int Width, int Height)? Size { get; init; }
    public bool Tray { get; init; }

    /// <summary>Si pide algo mas que enseñar la ventana (abrir, editar).</summary>
    public bool HasWork => Open.Count > 0 || Edit is not null || EditFileConnection is not null;

    /// <summary>
    /// Si la instancia que ya estaba abierta tiene que enseñarse al recibir estos argumentos: si,
    /// salvo un --tray a secas (el arranque con Windows con la aplicacion ya abierta).
    /// </summary>
    public bool ShowsExisting => !Tray || HasWork;

    public static StartupArgs Parse(IReadOnlyList<string> args)
    {
        var open = new List<string>();
        string? edit = null, editFileConnection = null, editFilePath = null;
        var editTab = 0;
        (int, int)? size = null;
        var tray = false;
        for (var i = 0; i < args.Count; i++)
        {
            var key = args[i].ToLowerInvariant();
            var hasValue = i + 1 < args.Count;
            switch (key)
            {
                case "--tray":
                    tray = true;
                    break;
                case "--open" when hasValue:
                    open.Add(args[++i]);
                    break;
                case "--edit" when hasValue:
                    edit = args[++i];
                    break;
                case "--edit-file" when i + 2 < args.Count:
                    editFileConnection = args[++i];
                    editFilePath = args[++i];
                    break;
                case "--edit-tab" when hasValue:
                    editTab = int.TryParse(args[++i], out var t) && t >= 0 ? t : 0;
                    break;
                case "--size" when hasValue:
                    if (args[++i].ToLowerInvariant().Split('x') is [var w, var h] && int.TryParse(w, out var pw) && int.TryParse(h, out var ph) && pw > 0 && ph > 0)
                        size = (pw, ph);
                    break;
            }
        }
        return new StartupArgs
        {
            Open = open,
            Edit = edit,
            EditTab = editTab,
            EditFileConnection = editFileConnection,
            EditFilePath = editFilePath,
            Size = size,
            Tray = tray,
        };
    }
}
