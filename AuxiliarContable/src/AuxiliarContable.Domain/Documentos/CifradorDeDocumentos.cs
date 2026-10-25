using System;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.Documentos;

/// <summary>
/// Configuración de los documentos, sección <c>Documentos</c>.
/// </summary>
public class DocumentosOptions
{
    public const string Seccion = "Documentos";

    /// <summary>
    /// Clave AES-256 en base64 (32 bytes). ⚠️ Es un secreto: user-secrets en desarrollo y
    /// <c>Documentos__ClaveDeCifrado</c> en <c>/etc/auxiliar-contable/env</c>. Y tiene que estar
    /// TAMBIÉN fuera del servidor: sin ella los documentos (y sus copias) no se pueden leer.
    /// </summary>
    public string? ClaveDeCifrado { get; set; }

    /// <summary>Carpeta del disco donde se guardan (el host la usa). Vacía = la de desarrollo.</summary>
    public string? Carpeta { get; set; }
}

/// <summary>
/// Cifra cada documento con AES-256-GCM antes de que toque el disco.
///
/// <para>Formato: <c>[versión=1][nonce 12][tag 16][cifrado]</c>. El id del documento va como
/// dato asociado (AAD): un archivo cifrado solo se descifra como el documento al que
/// pertenece, así que intercambiar archivos en el disco (o en una copia) no sirve para que un
/// cliente vea lo de otro — falla el descifrado.</para>
/// </summary>
public class CifradorDeDocumentos : ISingletonDependency
{
    private const byte Version = 1;
    private const int TamanoNonce = 12;
    private const int TamanoTag = 16;
    private const int Cabecera = 1 + TamanoNonce + TamanoTag;

    private readonly IOptions<DocumentosOptions> _opciones;

    public CifradorDeDocumentos(IOptions<DocumentosOptions> opciones)
    {
        _opciones = opciones;
    }

    public byte[] Cifrar(Guid documentoId, ReadOnlySpan<byte> claro)
    {
        var salida = new byte[Cabecera + claro.Length];
        salida[0] = Version;
        var nonce = salida.AsSpan(1, TamanoNonce);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(Clave(), TamanoTag);
        aes.Encrypt(nonce, claro, salida.AsSpan(Cabecera), salida.AsSpan(1 + TamanoNonce, TamanoTag), documentoId.ToByteArray());
        return salida;
    }

    public byte[] Descifrar(Guid documentoId, ReadOnlySpan<byte> cifrado)
    {
        if (cifrado.Length < Cabecera || cifrado[0] != Version)
        {
            throw new AbpException("El documento guardado no tiene el formato esperado.");
        }

        var claro = new byte[cifrado.Length - Cabecera];
        using var aes = new AesGcm(Clave(), TamanoTag);
        try
        {
            aes.Decrypt(cifrado.Slice(1, TamanoNonce), cifrado[Cabecera..], cifrado.Slice(1 + TamanoNonce, TamanoTag), claro, documentoId.ToByteArray());
        }
        catch (AuthenticationTagMismatchException ex)
        {
            // Clave equivocada, archivo alterado o archivo de OTRO documento.
            throw new AbpException("El documento guardado no se pudo verificar (¿clave distinta o archivo alterado?).", ex);
        }

        return claro;
    }

    private byte[] Clave()
    {
        var base64 = _opciones.Value.ClaveDeCifrado;
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new AbpException("Falta Documentos:ClaveDeCifrado: sin ella no se guardan documentos.");
        }

        byte[] clave;
        try
        {
            clave = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException ex)
        {
            throw new AbpException("Documentos:ClaveDeCifrado no es base64.", ex);
        }

        return clave.Length == 32
            ? clave
            : throw new AbpException("Documentos:ClaveDeCifrado tiene que ser de 32 bytes (AES-256).");
    }
}
