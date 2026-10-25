using System.Linq;
using Volo.Abp;

namespace AuxiliarContable.Notifications;

/// <summary>
/// Deja un teléfono como lo pide Brevo: solo dígitos, con el código de país delante.
///
/// <para>No se adivina el país. Un número de 10 dígitos puede ser de México, de Colombia o
/// de cualquier sitio; ponerle un prefijo por defecto mandaría el SMS a otra persona. Por
/// eso un número sin código de país se rechaza con un mensaje que dice qué falta.</para>
/// </summary>
public static class NumeroDeTelefono
{
    public const int MinimoDeDigitos = 11;
    public const int MaximoDeDigitos = 15;

    public static string Normalizar(string? telefono)
    {
        var digitos = new string((telefono ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        // «00» es la otra forma de escribir el «+» internacional.
        if (digitos.StartsWith("00"))
        {
            digitos = digitos[2..];
        }

        if (digitos.Length < MinimoDeDigitos || digitos.Length > MaximoDeDigitos)
        {
            throw new BusinessException(AuxiliarContableDomainErrorCodes.TelefonoSinCodigoDePais)
                .WithData("telefono", telefono ?? string.Empty);
        }

        return digitos;
    }
}
