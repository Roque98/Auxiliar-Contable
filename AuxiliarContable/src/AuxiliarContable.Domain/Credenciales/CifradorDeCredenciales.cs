using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.Credenciales;

/// <summary>Sección <c>Credenciales</c>.</summary>
public class CredencialesOptions
{
    public const string Seccion = "Credenciales";

    /// <summary>
    /// Clave AES-256 en base64 (32 bytes), DISTINTA de la de documentos: quien tenga la de la bóveda no lee contraseñas.
    /// ⚠️ Secreto: <c>Credenciales__ClaveDeCifrado</c> en <c>/etc/auxiliar-contable/env</c>, y guardada también fuera
    /// del servidor (sin ella las credenciales no se pueden leer).
    /// </summary>
    public string? ClaveDeCifrado { get; set; }
}

/// <summary>
/// Cifra cada credencial con AES-256-GCM. El dato asociado (AAD) es el id del registro más el nombre del campo: la
/// contraseña de la e.firma de un cliente no se descifra como su CIEC, ni como la de otro cliente.
/// Formato: <c>[versión=1][nonce 12][tag 16][cifrado]</c>, igual que la bóveda.
/// </summary>
public class CifradorDeCredenciales : ISingletonDependency
{
    private const byte Version = 1;
    private const int TamanoNonce = 12;
    private const int TamanoTag = 16;
    private const int Cabecera = 1 + TamanoNonce + TamanoTag;

    private readonly IOptions<CredencialesOptions> _opciones;

    public CifradorDeCredenciales(IOptions<CredencialesOptions> opciones)
    {
        _opciones = opciones;
    }

    public byte[] Cifrar(Guid id, string campo, ReadOnlySpan<byte> claro)
    {
        var salida = new byte[Cabecera + claro.Length];
        salida[0] = Version;
        var nonce = salida.AsSpan(1, TamanoNonce);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(Clave(), TamanoTag);
        aes.Encrypt(nonce, claro, salida.AsSpan(Cabecera), salida.AsSpan(1 + TamanoNonce, TamanoTag), Asociado(id, campo));
        return salida;
    }

    public byte[] Descifrar(Guid id, string campo, ReadOnlySpan<byte> cifrado)
    {
        if (cifrado.Length < Cabecera || cifrado[0] != Version)
        {
            throw new AbpException("La credencial guardada no tiene el formato esperado.");
        }

        var claro = new byte[cifrado.Length - Cabecera];
        using var aes = new AesGcm(Clave(), TamanoTag);
        try
        {
            aes.Decrypt(cifrado.Slice(1, TamanoNonce), cifrado[Cabecera..], cifrado.Slice(1 + TamanoNonce, TamanoTag), claro, Asociado(id, campo));
        }
        catch (AuthenticationTagMismatchException ex)
        {
            throw new AbpException("La credencial guardada no se pudo verificar (¿clave distinta o dato alterado?).", ex);
        }

        return claro;
    }

    public byte[] CifrarTexto(Guid id, string campo, string texto) => Cifrar(id, campo, Encoding.UTF8.GetBytes(texto));

    public string DescifrarTexto(Guid id, string campo, ReadOnlySpan<byte> cifrado) => Encoding.UTF8.GetString(Descifrar(id, campo, cifrado));

    private static byte[] Asociado(Guid id, string campo) => [.. id.ToByteArray(), .. Encoding.UTF8.GetBytes(campo)];

    private byte[] Clave()
    {
        var base64 = _opciones.Value.ClaveDeCifrado;
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new AbpException("Falta Credenciales:ClaveDeCifrado: sin ella no se guardan credenciales.");
        }

        byte[] clave;
        try
        {
            clave = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException ex)
        {
            throw new AbpException("Credenciales:ClaveDeCifrado no es base64.", ex);
        }

        return clave.Length == 32
            ? clave
            : throw new AbpException("Credenciales:ClaveDeCifrado tiene que ser de 32 bytes (AES-256).");
    }
}
