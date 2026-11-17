using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Volo.Abp;

namespace AuxiliarContable.Credenciales;

/// <summary>Lo que dice el .cer de una e.firma del SAT.</summary>
public record DatosDeEfirma(string? Rfc, string? NumeroDeCertificado, DateTime VigenteDesdeUtc, DateTime VigenteHastaUtc);

/// <summary>
/// Lee el certificado (.cer) de una e.firma y comprueba que la llave privada (.key) abre con su contraseña y es la de
/// ESE certificado. Así no se guarda una contraseña equivocada que solo se descubriría el día que haga falta.
///
/// <para>El SAT pone en el sujeto del certificado «RFC / CURP» (OID 2.5.4.45) y el número de certificado (20 dígitos)
/// codificado en ASCII dentro del número de serie.</para>
/// </summary>
public static partial class LectorDeEfirma
{
    [GeneratedRegex(@"\b([A-ZÑ&]{3,4}\d{6}[A-Z0-9]{3})\b")]
    private static partial Regex Rfc();

    public static DatosDeEfirma LeerCer(byte[] cer)
    {
        X509Certificate2 certificado;
        try
        {
            certificado = X509CertificateLoader.LoadCertificate(cer);
        }
        catch (CryptographicException ex)
        {
            throw new UserFriendlyException("El archivo .cer no es un certificado válido.", innerException: ex);
        }

        using (certificado)
        {
            return new DatosDeEfirma(
                Rfc().Match(certificado.Subject.ToUpperInvariant()) is { Success: true } m ? m.Groups[1].Value : null,
                NumeroDeCertificado(certificado),
                certificado.NotBefore.ToUniversalTime(),
                certificado.NotAfter.ToUniversalTime());
        }
    }

    /// <summary>¿La contraseña abre la llave y la llave es de este certificado? Si no, explica cuál de las dos falla.</summary>
    public static void ComprobarLlave(byte[] cer, byte[] key, string contrasena)
    {
        using var certificado = X509CertificateLoader.LoadCertificate(cer);
        using var publica = certificado.GetRSAPublicKey()
                            ?? throw new UserFriendlyException("El certificado no trae una llave RSA (no parece una e.firma del SAT).");
        using var privada = RSA.Create();
        try
        {
            privada.ImportEncryptedPkcs8PrivateKey(contrasena.AsSpan(), key, out _);
        }
        catch (CryptographicException ex)
        {
            throw new UserFriendlyException("La contraseña no abre el archivo .key (o el archivo no es una llave del SAT).", innerException: ex);
        }

        if (!publica.ExportParameters(false).Modulus!.SequenceEqual(privada.ExportParameters(false).Modulus!))
        {
            throw new UserFriendlyException("El archivo .key no corresponde a ese .cer: son de e.firmas distintas.");
        }
    }

    /// <summary>El SAT codifica los 20 dígitos del número de certificado como ASCII en el serial; si no, el serial tal cual.</summary>
    private static string NumeroDeCertificado(X509Certificate2 certificado)
    {
        var serial = certificado.SerialNumber;
        try
        {
            var texto = Encoding.ASCII.GetString(Convert.FromHexString(serial));
            if (texto.Length > 0 && texto.All(char.IsAsciiDigit))
            {
                return texto;
            }
        }
        catch (FormatException)
        {
        }

        return serial;
    }
}
