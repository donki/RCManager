using System.Security.Cryptography;
using System.Text;

namespace Connections.Services;

/// <summary>
/// Contraseñas cifradas con DPAPI, ligadas al usuario de Windows.
/// </summary>
/// <remarks>
/// <para>Es la API del sistema (<see cref="ProtectedData"/>, ambito <c>CurrentUser</c>): la clave la
/// custodia Windows y va con la sesion del usuario, asi que el fichero de conexiones copiado a otro
/// equipo o leido por otra cuenta no revela nada. No hay contraseña maestra que recordar ni que
/// perder. Lo que se cifra se marca con <c>dpapi1:</c> para poder cambiar de esquema mas adelante.</para>
///
/// <para>Hasta donde llega: protege el fichero en reposo. Un programa que corra con la sesion del
/// usuario puede descifrarlo igual que esta aplicacion; para eso esta el sistema, no nosotros.</para>
/// </remarks>
public static class Secrets
{
    private const string Prefix = "dpapi1:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("sOCConnections");

    public static string Protect(string plain)
    {
        if (plain.Length == 0)
            return string.Empty;

        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal))
            return string.Empty;

        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(stored[Prefix.Length..]), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            // Otro usuario u otro equipo: la contraseña no es legible aqui. Se pedira al conectar.
            return string.Empty;
        }
    }
}
