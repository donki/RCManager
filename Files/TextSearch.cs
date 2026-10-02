namespace SocRcManager.Files;

/// <summary>La busqueda del editor de texto: sin distinguir mayusculas, y al llegar al final vuelve a empezar.</summary>
public static class TextSearch
{
    /// <summary>
    /// Donde esta la siguiente aparicion de <paramref name="needle"/> despues de la seleccion (o la
    /// anterior, que acabe antes de donde empieza la seleccion, con <paramref name="backwards"/>); si
    /// no hay mas en ese sentido, la primera (o la ultima) del texto. -1 si no aparece.
    /// </summary>
    public static int Find(string text, string needle, int selectionStart, int selectionLength, bool backwards)
    {
        if (needle.Length == 0 || text.Length == 0)
            return -1;
        int index;
        if (backwards)
        {
            var end = Math.Min(selectionStart, text.Length);
            index = end > 0 ? text.LastIndexOf(needle, end - 1, StringComparison.CurrentCultureIgnoreCase) : -1;
            if (index < 0) index = text.LastIndexOf(needle, StringComparison.CurrentCultureIgnoreCase);
        }
        else
        {
            var from = selectionStart + selectionLength;
            index = text.IndexOf(needle, Math.Min(from, text.Length), StringComparison.CurrentCultureIgnoreCase);
            if (index < 0) index = text.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase);
        }
        return index;
    }
}
