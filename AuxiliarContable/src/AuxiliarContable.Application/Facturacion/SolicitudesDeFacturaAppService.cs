using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Facturacion;

/// <summary>Ver <see cref="ISolicitudesDeFacturaAppService"/>.</summary>
[Authorize]
public class SolicitudesDeFacturaAppService : AuxiliarContableAppService, ISolicitudesDeFacturaAppService
{
    private readonly IRepository<SolicitudDeFactura, Guid> _solicitudes;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<Organizacion, Guid> _organizaciones;
    private readonly IMotorFiscal _motor;
    private readonly Autorizador _autorizador;
    private readonly RevisorDeClaves _claves;

    public SolicitudesDeFacturaAppService(
        IRepository<SolicitudDeFactura, Guid> solicitudes,
        IRepository<Documento, Guid> documentos,
        IRepository<Organizacion, Guid> organizaciones,
        IMotorFiscal motor,
        Autorizador autorizador,
        RevisorDeClaves claves)
    {
        _claves = claves;
        _solicitudes = solicitudes;
        _documentos = documentos;
        _organizaciones = organizaciones;
        _motor = motor;
        _autorizador = autorizador;
    }

    public async Task<SolicitudesDelClienteDto> GetListAsync(Guid organizacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        var lista = await AsyncExecuter.ToListAsync(
            (await _solicitudes.GetQueryableAsync()).Where(s => s.OrganizacionId == organizacionId).OrderByDescending(s => s.CreationTime));
        var quien = await _autorizador.QuienMiraAsync();
        var nombres = await NombresDeDocumentosAsync(lista);
        return new SolicitudesDelClienteDto
        {
            Solicitudes = lista.Select(s => Describir(s, nombres, null)).ToList(),
            // El último dato capturado de cada receptor es el que vale.
            Receptores = lista.GroupBy(s => s.ReceptorRfc).Select(g => Receptor(g.First())).OrderBy(r => r.Nombre).ToList(),
            PuedePedir = quien.Puede(organizacionId, AccionEnOrganizacion.Subir),
            EsEquipo = quien.Equipo,
        };
    }

    public async Task<List<SolicitudDeFacturaDto>> GetPendientesAsync()
    {
        await ExigirEquipoAsync();
        var lista = await AsyncExecuter.ToListAsync(
            (await _solicitudes.GetQueryableAsync()).Where(s => s.Estado == EstadoDeSolicitudDeFactura.Pendiente).OrderBy(s => s.CreationTime));
        var ids = lista.Select(s => s.OrganizacionId).Distinct().ToList();
        var clientes = (await _organizaciones.GetListAsync(o => ids.Contains(o.Id))).ToDictionary(o => o.Id, o => o.Nombre);
        return lista.Select(s => Describir(s, [], clientes.GetValueOrDefault(s.OrganizacionId))).ToList();
    }

    public async Task<SolicitudDeFacturaDto> CrearAsync(Guid organizacionId, CrearSolicitudDeFacturaDto input)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var r = input.Receptor ?? throw new UserFriendlyException("Faltan los datos de a quién se le factura.");
        var conceptos = (input.Conceptos ?? []).Select(c => new ConceptoDeFactura(
            (c.ClaveProdServ ?? "").Trim(), (c.Descripcion ?? "").Trim(), c.Cantidad, (c.ClaveUnidad ?? "").Trim().ToUpperInvariant(),
            c.PrecioUnitario, c.TasaIva)).ToList();
        var s = new SolicitudDeFactura(GuidGenerator.Create(), organizacionId, r.Rfc, r.Nombre, r.CodigoPostal, r.Regimen,
            input.UsoCfdi, input.FormaDePago, input.MetodoDePago, conceptos, input.Nota);
        await _solicitudes.InsertAsync(s, autoSave: true);
        return Describir(s, [], null);
    }

    public async Task<SolicitudDeFacturaDto> CancelarAsync(Guid organizacionId, Guid solicitudId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var s = await DelClienteAsync(organizacionId, solicitudId);
        s.Cancelar();
        await _solicitudes.UpdateAsync(s, autoSave: true);
        return Describir(s, [], null);
    }

    public async Task<SolicitudDeFacturaDto> AtenderAsync(Guid organizacionId, Guid solicitudId, AtenderSolicitudDto input)
    {
        await ExigirEquipoAsync();
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        var s = await DelClienteAsync(organizacionId, solicitudId);
        // El XML timbrado y el PDF tienen que estar en la bóveda de ESE cliente.
        var xml = await _documentos.FirstOrDefaultAsync(d => d.Id == input.XmlDocumentoId && d.OrganizacionId == organizacionId)
                  ?? throw new EntityNotFoundException(typeof(Documento), input.XmlDocumentoId);
        if (xml.Tipo != TipoDeDocumento.Cfdi)
        {
            throw new UserFriendlyException("El archivo de la factura tiene que ser el XML timbrado.");
        }

        if (input.PdfDocumentoId is { } pdf && !await _documentos.AnyAsync(d => d.Id == pdf && d.OrganizacionId == organizacionId))
        {
            throw new EntityNotFoundException(typeof(Documento), pdf);
        }

        s.Atender(xml.Id, input.PdfDocumentoId, input.Respuesta, CurrentUser.Id, Clock.Now.ToUniversalTime());
        await _solicitudes.UpdateAsync(s, autoSave: true);
        return Describir(s, await NombresDeDocumentosAsync([s]), null);
    }

    public async Task<SolicitudDeFacturaDto> RechazarAsync(Guid organizacionId, Guid solicitudId, RechazarSolicitudDto input)
    {
        await ExigirEquipoAsync();
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        var s = await DelClienteAsync(organizacionId, solicitudId);
        s.Rechazar(input.Respuesta, CurrentUser.Id, Clock.Now.ToUniversalTime());
        await _solicitudes.UpdateAsync(s, autoSave: true);
        return Describir(s, [], null);
    }

    public async Task<ReceptorDto> LeerConstanciaDelReceptorAsync(Guid organizacionId, LeerConstanciaDelReceptorDto input)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        byte[] pdf;
        try
        {
            pdf = Convert.FromBase64String(input.PdfBase64 ?? "");
        }
        catch (FormatException)
        {
            throw new UserFriendlyException("La constancia no llegó bien. Vuelve a elegirla.");
        }

        if (pdf.Length is 0 or > 5 * 1024 * 1024)
        {
            throw new UserFriendlyException("La constancia está vacía o pesa más de 5 MB.");
        }

        ConstanciaLeida leida;
        try
        {
            leida = await _motor.LeerConstanciaAsync(new ArchivoParaElMotor("constancia.pdf", pdf));
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor fiscal no leyó la constancia de un receptor");
            throw new UserFriendlyException("No se pudo leer la constancia en este momento: captura los datos a mano.");
        }

        if (!leida.EsConstancia)
        {
            throw new UserFriendlyException("No parece la Constancia de Situación Fiscal descargada del SAT (una foto o un escaneo no se leen): captura los datos a mano.");
        }

        return new ReceptorDto
        {
            Rfc = leida.Rfc,
            Nombre = leida.Nombre,
            CodigoPostal = leida.CodigoPostal,
            // «612 - Personas Físicas con…»: la clave son los primeros tres dígitos del primer régimen vigente.
            Regimen = leida.Regimenes.Select(x => new string(x.TakeWhile(char.IsAsciiDigit).ToArray())).FirstOrDefault(x => x.Length == 3) ?? "",
        };
    }

    private async Task ExigirEquipoAsync()
    {
        if (!(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new AbpAuthorizationException("Solo el despacho atiende las solicitudes de factura.");
        }
    }

    private async Task<SolicitudDeFactura> DelClienteAsync(Guid organizacionId, Guid solicitudId) =>
        await _solicitudes.FirstOrDefaultAsync(s => s.Id == solicitudId && s.OrganizacionId == organizacionId)
        ?? throw new EntityNotFoundException(typeof(SolicitudDeFactura), solicitudId);

    private async Task<Dictionary<Guid, string>> NombresDeDocumentosAsync(IEnumerable<SolicitudDeFactura> lista)
    {
        var ids = lista.SelectMany(s => new[] { s.XmlDocumentoId, s.PdfDocumentoId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        return ids.Count == 0 ? [] : (await _documentos.GetListAsync(d => ids.Contains(d.Id))).ToDictionary(d => d.Id, d => d.NombreOriginal);
    }

    private static ReceptorDto Receptor(SolicitudDeFactura s) => new()
    {
        Rfc = s.ReceptorRfc, Nombre = s.ReceptorNombre, CodigoPostal = s.ReceptorCodigoPostal, Regimen = s.ReceptorRegimen,
    };

    private static SolicitudDeFacturaDto Describir(SolicitudDeFactura s, Dictionary<Guid, string> nombres, string? cliente) => new()
    {
        Id = s.Id,
        OrganizacionId = s.OrganizacionId,
        Cliente = cliente,
        Estado = s.Estado,
        PedidaUtc = s.CreationTime,
        Receptor = Receptor(s),
        UsoCfdi = s.UsoCfdi,
        FormaDePago = s.FormaDePago,
        MetodoDePago = s.MetodoDePago,
        Conceptos = s.LeerConceptos().Select(c => new ConceptoDto
        {
            ClaveProdServ = c.ClaveProdServ, Descripcion = c.Descripcion, Cantidad = c.Cantidad, ClaveUnidad = c.ClaveUnidad,
            PrecioUnitario = c.PrecioUnitario, TasaIva = c.TasaIva, Importe = c.Importe, Iva = c.Iva,
        }).ToList(),
        Subtotal = s.Subtotal,
        Iva = s.Iva,
        Total = s.Total,
        Nota = s.NotaDelCliente,
        Respuesta = s.Respuesta,
        AtendidaUtc = s.AtendidaUtc,
        XmlDocumentoId = s.XmlDocumentoId,
        NombreDelXml = s.XmlDocumentoId is { } x ? nombres.GetValueOrDefault(x) : null,
        PdfDocumentoId = s.PdfDocumentoId,
        NombreDelPdf = s.PdfDocumentoId is { } p ? nombres.GetValueOrDefault(p) : null,
    };

    public async Task<BusquedaDeClaveDto> BuscarClaveAsync(Guid organizacionId, BuscarClaveDto input)
    {
        var json = await ConMotorAsync(() => _claves.BuscarAsync(organizacionId, input.Descripcion));
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var fuera = new BusquedaDeClaveDto { Aviso = Texto(r, "aviso") };
        if (r.TryGetProperty("resultados", out var lista) && lista.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in lista.EnumerateArray())
            {
                var clave = new ClaveSugeridaDto { Clave = Texto(x, "clave") ?? "", Descripcion = Texto(x, "descripcion") ?? "" };
                if (x.TryGetProperty("unidades", out var us) && us.ValueKind == JsonValueKind.Array)
                {
                    clave.Unidades = us.EnumerateArray()
                        .Select(u => new UnidadSugeridaDto { Clave = Texto(u, "clave") ?? "", Nombre = Texto(u, "nombre") ?? "" })
                        .Where(u => u.Clave.Length > 0).ToList();
                }

                fuera.Claves.Add(clave);
            }
        }

        if (r.TryGetProperty("entendido", out var e) && e.TryGetProperty("ignoradas", out var ig) && ig.ValueKind == JsonValueKind.Array)
        {
            fuera.Ignoradas = ig.EnumerateArray().Select(i => Texto(i, "palabra")).OfType<string>().ToList();
        }

        return fuera;
    }

    public async Task<List<AlertaDeConceptoDto>> RevisarConceptosAsync(Guid organizacionId, RevisarConceptosDto input)
    {
        var conceptos = input.Conceptos.Select(c => new ConceptoParaRevisar(c.ClaveProdServ, c.ClaveUnidad, c.Descripcion)).ToList();
        var json = await ConMotorAsync(() => _claves.RevisarAsync(organizacionId, conceptos));
        using var doc = JsonDocument.Parse(json);
        var fuera = new List<AlertaDeConceptoDto>();
        if (doc.RootElement.TryGetProperty("conceptos", out var lista) && lista.ValueKind == JsonValueKind.Array)
        {
            // El motor recibe solo los conceptos con clave o descripción: se mapean de vuelta a su índice en la forma.
            var indices = input.Conceptos.Select((c, i) => (c, i))
                .Where(x => !string.IsNullOrWhiteSpace(x.c.ClaveProdServ) || !string.IsNullOrWhiteSpace(x.c.Descripcion))
                .Select(x => x.i).ToList();
            var n = 0;
            foreach (var c in lista.EnumerateArray())
            {
                var indice = n < indices.Count ? indices[n] : n;
                n++;
                if (!c.TryGetProperty("alertas", out var alertas) || alertas.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                fuera.AddRange(alertas.EnumerateArray().Select(a => new AlertaDeConceptoDto
                {
                    Concepto = indice, Nivel = Texto(a, "nivel") ?? "medio", Texto = Texto(a, "texto") ?? "",
                }));
            }
        }

        return fuera;
    }

    private static string? Texto(JsonElement e, string propiedad) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static async Task<string> ConMotorAsync(Func<Task<string>> llamada)
    {
        try
        {
            return await llamada();
        }
        catch (MotorFiscalNoDisponibleException)
        {
            throw new UserFriendlyException("La búsqueda de claves no está disponible en este momento. Intenta en unos minutos.");
        }
    }
}
