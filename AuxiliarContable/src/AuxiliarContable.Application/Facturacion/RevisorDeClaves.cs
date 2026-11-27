using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace AuxiliarContable.Facturacion;

/// <summary>
/// La clave SAT de lo que se vende o se compra: buscarla por cómo se llama el producto y revisar si la de una factura
/// corresponde a su descripción y a su unidad. La usan el asistente y la pantalla de Facturar. Al motor van solo los
/// nombres de las actividades de la constancia y los conceptos; nunca el RFC ni el nombre del cliente.
/// </summary>
public class RevisorDeClaves : ITransientDependency
{
    private readonly Autorizador _autorizador;
    private readonly IPerfilFiscalAppService _perfiles;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly IAsyncQueryableExecuter _async;
    private readonly IMotorFiscal _motor;

    public RevisorDeClaves(Autorizador autorizador, IPerfilFiscalAppService perfiles, IRepository<Documento, Guid> documentos,
        AlmacenDeDocumentos almacen, IAsyncQueryableExecuter ejecutor, IMotorFiscal motor)
    {
        _autorizador = autorizador;
        _perfiles = perfiles;
        _documentos = documentos;
        _almacen = almacen;
        _async = ejecutor;
        _motor = motor;
    }

    /// <summary>Las actividades de la constancia, como las escribe el SAT (una por renglón o separadas por «;»).</summary>
    public async Task<IReadOnlyList<string>> ActividadesAsync(Guid organizacionId)
    {
        var perfil = await _perfiles.GetAsync(organizacionId);
        var texto = perfil.Campos.FirstOrDefault(c => c.Clave == CamposDelPerfil.ActividadesEconomicas)?.Valor ?? "";
        return texto.Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => a.Length > 200 ? a[..200] : a)
            .Take(30)
            .ToList();
    }

    public async Task<string> BuscarAsync(Guid organizacionId, string descripcion, int cuantas = 8)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        return await BuscarConAsync(descripcion, await ActividadesAsync(organizacionId), cuantas);
    }

    /// <summary>
    /// Desde la conversación de la cartera (solo el equipo; el asistente lo garantiza): sin cliente, sin constancia que
    /// revisar contra el Anexo 6.
    /// </summary>
    public Task<string> BuscarSinClienteAsync(string descripcion) => BuscarConAsync(descripcion, [], 8);

    public Task<string> RevisarSinClienteAsync(IReadOnlyList<ConceptoParaRevisar> conceptos) => RevisarConAsync(conceptos, []);

    private async Task<string> BuscarConAsync(string descripcion, IReadOnlyList<string> actividades, int cuantas)
    {
        descripcion = (descripcion ?? "").Trim();
        if (descripcion.Length < 2)
        {
            throw new UserFriendlyException("Describe el producto o servicio (por ejemplo «reflector LED 50 W»).");
        }

        return await _motor.BuscarClavesAsync(descripcion.Length > 300 ? descripcion[..300] : descripcion, actividades, cuantas);
    }

    public async Task<string> RevisarAsync(Guid organizacionId, IReadOnlyList<ConceptoParaRevisar> conceptos)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        return await RevisarConAsync(conceptos, await ActividadesAsync(organizacionId));
    }

    private async Task<string> RevisarConAsync(IReadOnlyList<ConceptoParaRevisar> conceptos, IReadOnlyList<string> actividades)
    {
        var limpios = conceptos
            .Where(c => !string.IsNullOrWhiteSpace(c.Clave) || !string.IsNullOrWhiteSpace(c.Descripcion))
            .Take(ConceptosDelCfdi.Max)
            .Select(c => new ConceptoParaRevisar(Corto(c.Clave, 12), Corto(c.Unidad, 6), Corto(c.Descripcion, 1000)))
            .ToList();
        if (limpios.Count == 0)
        {
            throw new UserFriendlyException("No hay conceptos que revisar: hace falta la clave SAT y la descripción.");
        }

        return await _motor.RevisarClavesAsync(limpios, actividades);
    }

    /// <summary>
    /// Revisa los conceptos de una factura de su bóveda (emitida o recibida) buscada por su folio fiscal. Lo ajeno no
    /// existe: otra organización o un UUID que no está en su bóveda dan el mismo «no la encontré».
    /// </summary>
    public async Task<string> RevisarFacturaAsync(Guid organizacionId, string uuid)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        var limpio = (uuid ?? "").Trim().ToUpperInvariant();
        var documento = await _async.FirstOrDefaultAsync(
            (await _documentos.GetQueryableAsync()).Where(d => d.OrganizacionId == organizacionId
                                                                && d.Tipo == TipoDeDocumento.Cfdi && d.Uuid == limpio));
        if (documento is null)
        {
            throw new UserFriendlyException("No encontré esa factura en sus documentos. Revisa el folio fiscal (UUID).");
        }

        var xml = await _almacen.LeerAsync(documento);
        var datos = DatosDelCfdi.Leer(xml);
        var conceptos = ConceptosDelCfdi.Leer(xml);
        if (conceptos.Count == 0)
        {
            throw new UserFriendlyException("Esa factura no trae conceptos que revisar.");
        }

        var perfil = await _perfiles.GetAsync(organizacionId);
        var rfc = perfil.Campos.FirstOrDefault(c => c.Clave == CamposDelPerfil.Rfc)?.Valor?.Trim().ToUpperInvariant();
        var lado = rfc is null ? null : datos?.EmisorRfc == rfc ? "emitida" : datos?.ReceptorRfc == rfc ? "recibida" : null;

        var revision = JsonNode.Parse(await _motor.RevisarClavesAsync(conceptos, await ActividadesAsync(organizacionId)));
        var fuera = new JsonObject
        {
            ["factura"] = new JsonObject
            {
                ["uuid"] = limpio,
                ["lado"] = lado,
                ["fecha"] = datos?.Fecha?.ToString("yyyy-MM-dd"),
                ["contraparte"] = lado == "emitida" ? datos?.ReceptorNombre : datos?.EmisorNombre,
                ["total"] = datos?.Total,
            },
            ["revision"] = revision,
        };
        return fuera.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) });
    }

    private static string Corto(string? texto, int max)
    {
        var t = (texto ?? "").Trim();
        return t.Length > max ? t[..max] : t;
    }
}
