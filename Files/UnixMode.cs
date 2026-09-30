namespace SocRcManager.Files;

/// <summary>
/// Permisos de Unix (bits rwx de propietario, grupo y otros) entre sus tres formas: los bits, el
/// octal que se escribe («755») y las nueve casillas de la ventana de permisos.
/// </summary>
public static class UnixMode
{
    /// <summary>Bit de la casilla <paramref name="index"/> (0 = lectura del propietario … 8 = ejecucion de otros).</summary>
    public static int Bit(int index) => 1 << (8 - index);

    /// <summary>Las nueve casillas a partir de los bits.</summary>
    public static bool[] ToBits(int mode) => Enumerable.Range(0, 9).Select(i => (mode & Bit(i)) != 0).ToArray();

    /// <summary>Los bits a partir de las nueve casillas.</summary>
    public static int FromBits(IReadOnlyList<bool> bits)
    {
        var mode = 0;
        for (var i = 0; i < 9; i++)
            if (bits[i])
                mode |= Bit(i);
        return mode;
    }

    /// <summary>«644», «755», «007»: siempre con tres cifras.</summary>
    public static string ToOctal(int mode) => Convert.ToString(mode, 8).PadLeft(3, '0');

    /// <summary>
    /// Lo que se escribe en la casilla del octal: tres o cuatro cifras de 0 a 7 («0755» vale; de las
    /// cuatro solo cuentan las tres ultimas). Cualquier otra cosa, null (a medio escribir).
    /// </summary>
    public static int? ParseOctal(string text)
    {
        text = text.Trim();
        if (text.Length is >= 3 and <= 4 && text.All(ch => ch is >= '0' and <= '7'))
            return Convert.ToInt32(text[^3..], 8);
        return null;
    }

    /// <summary>«rwxr-xr-x» a partir de los bits.</summary>
    public static string ToText(int mode) => string.Concat(Enumerable.Range(0, 9).Select(i => (mode & Bit(i)) != 0 ? "rwx"[i % 3] : '-'));

    /// <summary>El numero que entiende <c>SITE CHMOD</c> de FluentFTP: los digitos octales leidos como decimal (0o644 → 644).</summary>
    public static int ToFtpChmod(int mode) => Convert.ToInt32(Convert.ToString(mode, 8));

    /// <summary>Lo contrario: el 644 de un listado FTP a sus bits.</summary>
    public static int FromFtpChmod(int chmod) => Convert.ToInt32(chmod.ToString(), 8);
}

/// <summary>Lo que se pide en la ventana de permisos: que cambiar y si bajar a lo de dentro.</summary>
public sealed record PermissionChange(int? Mode, bool ChangeMode, string Owner, string Group, bool ChangeOwner, bool Recursive)
{
    /// <summary>
    /// Se cambia el modo si hay uno valido y no es el de partida; el propietario o el grupo, si se
    /// ha escrito algo distinto de lo que habia (vacio = no tocar).
    /// </summary>
    public static PermissionChange From(int? mode, int? originalMode, string owner, string originalOwner, string group, string originalGroup, bool recursive) =>
        new(mode, mode is not null && mode != originalMode, owner, group,
            (owner.Length > 0 && owner != originalOwner) || (group.Length > 0 && group != originalGroup), recursive);
}
