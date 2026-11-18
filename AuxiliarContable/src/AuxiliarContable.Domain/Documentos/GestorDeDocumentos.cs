using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
    private readonly IEscanerDeArchivos _escaner;

    public GestorDeDocumentos(IRepository<Documento, Guid> documentos, AlmacenDeDocumentos almacen, IEscanerDeArchivos escaner)
    {
        _documentos = documentos;
        _almacen = almacen;
        _escaner = escaner;
    }

    /// <summary>
    /// Guarda el archivo y devuelve su documento. Si ese cliente ya había subido EXACTAMENTE
    /// el mismo archivo, devuelve el que había (<c>EsDuplicado</c>) en vez de guardarlo dos
    /// veces: subir dos veces el mismo estado de cuenta no debe duplicar sus movimientos.
    /// <paramref name="fecha"/> es a qué fecha corresponde (ver <see cref="Documento.FechaDelDocumento"/>).
    /// </summary>
    public async Task<(Documento Documento, bool EsDuplicado)> RecibirAsync(
        Guid organizacionId,
        TipoDeDocumento tipo,
        string nombreOriginal,
        byte[] contenido,
        Func<string, bool>? tipoAceptado = null,
        DateTime? fecha = null)
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
            // Uno que se subió antes de que se fecharan (o sin decir de qué mes era) toma la fecha ahora.
            if (existente.FechaDelDocumento == null && fecha != null)
            {
                existente.FecharEn(fecha.Value);
                await _documentos.UpdateAsync(existente, autoSave: true);
            }

            return (existente, true);
        }

        // Antivirus antes de guardar. Si no hay (o no responde) se acepta y se avisa en el log: un
        // ClamAV caído no debe impedir que el cliente suba su constancia. Lo vigila el health check.
        var escaneo = await _escaner.EscanearAsync(contenido);
        if (escaneo.Veredicto == VeredictoDelEscaneo.Infectado)
        {
            Logger.LogWarning("Documento rechazado por el antivirus ({Detalle}) para la organización {Organizacion}.",
                escaneo.Detalle, organizacionId);
            throw new BusinessException("AuxiliarContable:DocumentoInfectado");
        }

        if (escaneo.Veredicto == VeredictoDelEscaneo.NoDisponible)
        {
            Logger.LogInformation("Documento sin escanear: {Detalle}.", escaneo.Detalle);
        }

        var documento = new Documento(
            GuidGenerator.Create(), organizacionId, tipo, NombreSeguro(nombreOriginal), mime, contenido.Length, hash);
        if (fecha != null)
        {
            documento.FecharEn(fecha.Value);
        }

        if (tipo == TipoDeDocumento.Cfdi)
        {
            documento.Identificar(DatosDelCfdi.Leer(contenido)?.Uuid);
        }

        // Primero el contenido y luego la fila: si falla el disco no queda un documento sin
        // archivo. Si falla la base, queda un archivo huérfano cifrado, que no expone nada.
        await _almacen.GuardarAsync(documento, contenido);
        await _documentos.InsertAsync(documento, autoSave: true);
        return (documento, false);
    }

    /// <summary>
    /// Fecha las facturas que se subieron antes de que la bóveda leyera la fecha del XML (quedaron en
    /// el mes en que se subieron). Corre al arrancar; cuando ya no queda ninguna no hace nada.
    /// </summary>
    public async Task<int> FecharFacturasSinFechaAsync(int maximo = 500)
    {
        var pendientes = await _documentos.GetListAsync(d => d.Tipo == TipoDeDocumento.Cfdi && d.FechaDelDocumento == null);
        var fechadas = 0;
        foreach (var documento in pendientes.Take(maximo))
        {
            byte[] contenido;
            try
            {
                contenido = await _almacen.LeerAsync(documento);
            }
            catch (Exception ex) when (ex is IOException or CryptographicException or AbpException)
            {
                Logger.LogWarning(ex, "No se pudo abrir la factura {Documento} para fecharla.", documento.Id);
                continue;
            }

            if (FechaDelCfdi.Leer(contenido) is { } dia)
            {
                documento.FecharEn(FechaDelCfdi.ComoFecha(dia));
                await _documentos.UpdateAsync(documento);
                fechadas++;
            }
        }

        return fechadas;
    }

    /// <summary>
    /// Lee el UUID de las facturas que se subieron antes de que la bóveda lo guardara; las que no lo traen quedan como
    /// XML dañado. Corre al arrancar; cuando ya no queda ninguna no hace nada.
    /// </summary>
    public async Task<int> IdentificarFacturasAsync(int maximo = 2000)
    {
        var pendientes = await _documentos.GetListAsync(d => d.Tipo == TipoDeDocumento.Cfdi && d.Uuid == null && !d.XmlDanado);
        var hechas = 0;
        foreach (var documento in pendientes.Take(maximo))
        {
            byte[] contenido;
            try
            {
                contenido = await _almacen.LeerAsync(documento);
            }
            catch (Exception ex) when (ex is IOException or CryptographicException or AbpException)
            {
                Logger.LogWarning(ex, "No se pudo abrir la factura {Documento} para leer su UUID.", documento.Id);
                continue;
            }

            documento.Identificar(DatosDelCfdi.Leer(contenido)?.Uuid);
            await _documentos.UpdateAsync(documento);
            hechas++;
        }

        return hechas;
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
