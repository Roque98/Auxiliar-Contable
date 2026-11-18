using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Documentos;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Agrega a la bóveda de un cliente los XML de un .zip bajado del SAT (paquete del Descargador 1 o de la Descarga
/// Masiva): solo los que son CFDI del RFC del cliente y cuyo UUID aún no está; los de meses cerrados no entran; el XML
/// dañado que el cliente subió de esa misma factura se cambia por el bueno.
/// </summary>
public class ImportadorDeCfdi : ITransientDependency
{
    /// <summary>Contra un .zip armado a mano: lo que se descomprime y cuántos archivos.</summary>
    public const long MaxBytesDescomprimidos = 400L * 1024 * 1024;

    public const int MaxArchivos = 30000;
    public const int MaxBytesPorXml = 5 * 1024 * 1024;

    private readonly IRepository<Documento, Guid> _documentos;
    private readonly GestorDeDocumentos _gestor;
    private readonly GestorDePeriodos _periodos;

    public ImportadorDeCfdi(IRepository<Documento, Guid> documentos, GestorDeDocumentos gestor, GestorDePeriodos periodos)
    {
        _documentos = documentos;
        _gestor = gestor;
        _periodos = periodos;
    }

    public sealed class Conteo
    {
        public int Nuevas { get; set; }
        public int Repetidas { get; set; }
        public int Rechazadas { get; set; }
        public int EnMesCerrado { get; set; }
        public int Reemplazadas { get; set; }

        /// <summary>XML dañados del cliente que siguen sin su bueno (su nombre no trae el UUID o no llegó).</summary>
        public int DanadasQueQuedan { get; set; }
    }

    public async Task<Conteo> AgregarAsync(Guid organizacionId, string rfc, ZipArchive zip, Func<ZipArchiveEntry, bool> esXml)
    {
        if (zip.Entries.Count > MaxArchivos)
        {
            throw new UserFriendlyException("El paquete trae demasiados archivos.");
        }

        var r = new Conteo();
        var danadas = await _documentos.GetListAsync(d => d.OrganizacionId == organizacionId && d.Tipo == TipoDeDocumento.Cfdi && d.XmlDanado);
        long descomprimidos = 0;
        foreach (var entrada in zip.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && esXml(e)))
        {
            if (entrada.Length > MaxBytesPorXml)
            {
                r.Rechazadas++;
                continue;
            }

            descomprimidos += entrada.Length;
            if (descomprimidos > MaxBytesDescomprimidos)
            {
                throw new UserFriendlyException("El paquete descomprimido es demasiado grande.");
            }

            var xml = await LeerAsync(entrada);
            if (xml == null || xml.Length == 0)
            {
                r.Rechazadas++;
                continue;
            }

            var datos = DatosDelCfdi.Leer(xml);
            var uuid = datos?.Uuid;
            var esDelCliente = datos != null && (string.Equals(datos.EmisorRfc, rfc, StringComparison.OrdinalIgnoreCase)
                                                 || string.Equals(datos.ReceptorRfc, rfc, StringComparison.OrdinalIgnoreCase));
            if (uuid is not { Length: DocumentoConsts.UuidLength } || !esDelCliente || FechaDelCfdi.Leer(xml) is not { } dia)
            {
                r.Rechazadas++;
                continue;
            }

            if (await _documentos.AnyAsync(d => d.OrganizacionId == organizacionId && d.Uuid == uuid))
            {
                r.Repetidas++;
                continue;
            }

            var fecha = FechaDelCfdi.ComoFecha(dia);
            if (await _periodos.EstaCerradoAsync(organizacionId, fecha.Year, fecha.Month))
            {
                r.EnMesCerrado++;
                continue;
            }

            var (_, duplicado) = await _gestor.RecibirAsync(organizacionId, TipoDeDocumento.Cfdi, uuid + ".xml", xml,
                mime => mime == DetectorDeDocumento.Xml, fecha);
            if (duplicado)
            {
                r.Repetidas++;
                continue;
            }

            r.Nuevas++;
            // El dañado que el cliente subió de esta misma factura (su nombre trae el UUID): ya sobra.
            foreach (var danada in danadas.Where(d => d.NombreOriginal.Contains(uuid, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                await _documentos.DeleteAsync(danada, autoSave: true);
                danadas.Remove(danada);
                r.Reemplazadas++;
            }
        }

        r.DanadasQueQuedan = danadas.Count;
        return r;
    }

    /// <summary>Con tope: el tamaño que declara el .zip puede mentir. Null si se pasa.</summary>
    private static async Task<byte[]?> LeerAsync(ZipArchiveEntry entrada)
    {
        await using var flujo = entrada.Open();
        using var memoria = new MemoryStream();
        var bufer = new byte[81920];
        int leidos;
        while ((leidos = await flujo.ReadAsync(bufer)) > 0)
        {
            if (memoria.Length + leidos > MaxBytesPorXml)
            {
                return null;
            }

            memoria.Write(bufer, 0, leidos);
        }

        return memoria.ToArray();
    }
}
