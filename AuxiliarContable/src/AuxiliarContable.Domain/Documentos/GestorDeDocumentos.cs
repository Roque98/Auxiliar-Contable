using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace AuxiliarContable.Documentos;

/// <summary>
/// La única puerta de entrada de archivos: valida, detecta duplicados y guarda cifrado.
/// No decide QUIÉN puede subir a qué cliente: eso lo comprueba el servicio de aplicación con
/// el alcance del usuario antes de llamar aquí.
/// </summary>
public class GestorDeDocumentos : DomainService
{
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly AlmacenDeDocumentos _almacen;

    public GestorDeDocumentos(IRepository<Documento, Guid> documentos, AlmacenDeDocumentos almacen)
    {
        _documentos = documentos;
        _almacen = almacen;
    }

    /// <summary>
    /// Guarda el archivo y devuelve su documento. Si ese cliente ya había subido EXACTAMENTE
    /// el mismo archivo, devuelve el que había (<c>EsDuplicado</c>) en vez de guardarlo dos
    /// veces: subir dos veces el mismo estado de cuenta no debe duplicar sus movimientos.
    /// </summary>
    public async Task<(Documento Documento, bool EsDuplicado)> RecibirAsync(
        Guid organizacionId,
        TipoDeDocumento tipo,
        string nombreOriginal,
        byte[] contenido,
        Func<string, bool>? tipoAceptado = null)
    {
        if (contenido.Length == 0)
        {
            throw new BusinessException("AuxiliarContable:DocumentoVacio");
        }

        if (contenido.Length > DocumentoConsts.MaxBytes)
        {
            throw new BusinessException("AuxiliarContable:DocumentoDemasiadoGrande")
                .WithData("max", DocumentoConsts.MaxBytes / (1024 * 1024));
        }

        var mime = DetectorDeDocumento.Detectar(contenido);
        if (mime == null || (tipoAceptado != null && !tipoAceptado(mime)))
        {
            throw new BusinessException("AuxiliarContable:DocumentoTipoNoAdmitido");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(contenido));
        var existente = await _documentos.FirstOrDefaultAsync(d => d.OrganizacionId == organizacionId && d.Sha256 == hash);
        if (existente != null)
        {
            return (existente, true);
        }

        var documento = new Documento(
            GuidGenerator.Create(), organizacionId, tipo, NombreSeguro(nombreOriginal), mime, contenido.Length, hash);

        // Primero el contenido y luego la fila: si falla el disco no queda un documento sin
        // archivo. Si falla la base, queda un archivo huérfano cifrado, que no expone nada.
        await _almacen.GuardarAsync(documento, contenido);
        await _documentos.InsertAsync(documento, autoSave: true);
        return (documento, false);
    }

    /// <summary>Solo el nombre (sin carpetas) y sin caracteres de control: se enseña, nunca se usa como ruta.</summary>
    public static string NombreSeguro(string? nombre)
    {
        // Se corta por las dos barras: en Linux, Path.GetFileName no quita una ruta de Windows.
        var soloNombre = (nombre ?? string.Empty).Split('/', '\\').Last();
        var limpio = new string(soloNombre.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (limpio.Length == 0)
        {
            limpio = "documento";
        }

        return limpio.Length <= DocumentoConsts.MaxNombreLength ? limpio : limpio[..DocumentoConsts.MaxNombreLength];
    }
}
