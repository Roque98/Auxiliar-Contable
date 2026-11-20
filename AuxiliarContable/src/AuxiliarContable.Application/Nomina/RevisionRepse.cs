using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Documentos;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace AuxiliarContable.Nomina;

/// <summary>
/// La revisión REPSE de un cliente para un mes (la usan la pantalla de nómina y el recordatorio diario):
/// <list type="bullet">
/// <item>Si RECIBE servicios especializados: por cada contrato con facturas del mes, los documentos que debe obtener del
/// contratista (art. 27-V LISR: CFDI de nómina, entero de retenciones, cuotas IMSS y aportaciones INFONAVIT; art. 5-II
/// LIVA: declaración de IVA y su acuse), a más tardar el último día del mes siguiente al pago.</item>
/// <item>Si los PRESTA: entregarlos a su cliente en el mismo plazo, y el informe cuatrimestral al IMSS (ICSOE, art. 15-A
/// LSS) y al INFONAVIT (SISUB, art. 29 Bis LINFONAVIT), a más tardar el 17 de enero, mayo y septiembre.</item>
/// </list>
/// El pago se toma de las facturas del mes con la otra parte (la fecha del CFDI): una factura PPD se paga después.
/// </summary>
public class RevisionRepse : ITransientDependency
{
    public const int DiasParaAvisarVencimiento = 60;

    public static readonly (string Clave, string Texto, string Fundamento)[] DelContratista =
    [
        ("nomina", "CFDI de nómina de los trabajadores que dieron el servicio", "art. 27-V LISR"),
        ("isr", "Recibo bancario del entero de las retenciones de ISR de esos trabajadores", "art. 27-V LISR"),
        ("imss", "Pago de las cuotas obrero-patronales al IMSS", "art. 27-V LISR"),
        ("infonavit", "Pago de las aportaciones al INFONAVIT", "art. 27-V LISR"),
        ("iva", "Declaración de IVA del contratista y su acuse de pago del periodo", "art. 5-II LIVA"),
    ];

    public static readonly (string Clave, string Texto, string Fundamento)[] DelPrestador =
    [
        ("entrega", "Entregar al beneficiario los CFDI de nómina, el entero de ISR, el pago al IMSS y al INFONAVIT, y la declaración de IVA con su acuse",
            "art. 27-V LISR y 5-II LIVA"),
    ];

    public static readonly (string Clave, string Texto, string Fundamento)[] DelInforme =
    [
        ("icsoe", "ICSOE: información de los contratos al IMSS", "art. 15-A LSS"),
        ("sisub", "SISUB: información de los contratos al INFONAVIT", "art. 29 Bis LINFONAVIT"),
    ];

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-MX");

    private readonly IRepository<ContratoEspecializado, Guid> _contratos;
    private readonly IRepository<CumplimientoRepse, Guid> _cumplimientos;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly IClock _reloj;

    public ILogger<RevisionRepse> Logger { get; set; } = NullLogger<RevisionRepse>.Instance;

    public RevisionRepse(
        IRepository<ContratoEspecializado, Guid> contratos,
        IRepository<CumplimientoRepse, Guid> cumplimientos,
        IRepository<Documento, Guid> documentos,
        IRepository<PerfilFiscal, Guid> perfiles,
        AlmacenDeDocumentos almacen,
        IClock reloj)
    {
        _contratos = contratos;
        _cumplimientos = cumplimientos;
        _documentos = documentos;
        _perfiles = perfiles;
        _almacen = almacen;
        _reloj = reloj;
    }

    /// <summary>El informe cuatrimestral que vence a partir del mes: en enero el de septiembre-diciembre anterior; de
    /// febrero a mayo el de enero-abril; de junio a septiembre el de mayo-agosto; de octubre a diciembre el de
    /// septiembre-diciembre (vence el 17 de enero siguiente).</summary>
    public static (int Anio, int Cuatrimestre, DateTime Limite) InformeQueVence(int anio, int mes) => mes switch
    {
        1 => (anio - 1, 3, new DateTime(anio, 1, 17)),
        <= 5 => (anio, 1, new DateTime(anio, 5, 17)),
        <= 9 => (anio, 2, new DateTime(anio, 9, 17)),
        _ => (anio, 3, new DateTime(anio + 1, 1, 17)),
    };

    /// <summary>El 17 de mayo, septiembre o enero siguiente (art. 15-A LSS, 29 Bis LINFONAVIT).</summary>
    public static DateTime LimiteDelInforme(int anio, int cuatrimestre) => cuatrimestre switch
    {
        1 => new DateTime(anio, 5, 17),
        2 => new DateTime(anio, 9, 17),
        _ => new DateTime(anio + 1, 1, 17),
    };

    public static (DateTime Desde, DateTime Hasta) MesesDe(int anio, int cuatrimestre)
    {
        var desde = new DateTime(anio, (cuatrimestre - 1) * 4 + 1, 1);
        return (desde, desde.AddMonths(4).AddDays(-1));
    }

    public static string NombreDeMeses(int anio, int cuatrimestre)
    {
        var (desde, hasta) = MesesDe(anio, cuatrimestre);
        return $"{desde.ToString("MMMM", Es)} a {hasta.ToString("MMMM", Es)} de {anio}";
    }

    /// <summary>Último día para juntar o entregar los documentos de lo pagado en el mes: el último del mes siguiente.</summary>
    public static DateTime LimiteDelMes(int anio, int mes) => new DateTime(anio, mes, 1).AddMonths(2).AddDays(-1);

    public async Task<bool> TieneContratosAsync(Guid organizacionId) => await _contratos.AnyAsync(c => c.OrganizacionId == organizacionId);

    public async Task<RepseDelMesDto> RevisarAsync(Guid organizacionId, int anio, int mes)
    {
        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1).AddDays(-1);
        var hoy = _reloj.Now.Date;
        var contratos = (await _contratos.GetListAsync(c => c.OrganizacionId == organizacionId))
            .OrderBy(c => c.Rol).ThenBy(c => c.ContraparteNombre).ToList();
        var r = new RepseDelMesDto { Anio = anio, Mes = mes, Contratos = [.. contratos.Select(Describir)] };
        if (contratos.Count == 0)
        {
            return r;
        }

        var (anioInf, cuatrimestre, limiteInf) = InformeQueVence(anio, mes);
        var marcas = await _cumplimientos.GetListAsync(c => c.OrganizacionId == organizacionId
                                                            && ((c.ContratoId != null && c.Anio == anio && c.Periodo == mes)
                                                                || (c.ContratoId == null && c.Anio == anioInf && c.Periodo == cuatrimestre)));
        bool Marcada(Guid? contrato, string clave) => marcas.Any(m => m.ContratoId == contrato && m.Clave == clave);

        var vigentes = contratos.Where(c => c.VigenteEn(desde, hasta)).ToList();
        var facturas = vigentes.Count > 0 ? await FacturasDelMesAsync(organizacionId, desde, hasta) : [];
        var limite = LimiteDelMes(anio, mes);
        foreach (var c in vigentes)
        {
            var suyas = facturas.Where(f => c.EsPrestador
                    ? f.Emitida && f.Otro == c.ContraparteRfc
                    : !f.Emitida && f.Otro == c.ContraparteRfc)
                .ToList();
            var casillas = (c.EsPrestador ? DelPrestador : DelContratista)
                .Select(x => new CasillaRepseDto { Clave = x.Clave, Texto = x.Texto, Fundamento = x.Fundamento, Cumplida = Marcada(c.Id, x.Clave) })
                .ToList();
            r.DelMes.Add(new ContratoDelMesDto
            {
                ContratoId = c.Id, Rol = c.Rol, Contraparte = c.ContraparteNombre, Facturas = suyas.Count, Importe = suyas.Sum(f => f.Total),
                Limite = limite, Casillas = casillas,
                Vencido = suyas.Count > 0 && hoy > limite && casillas.Any(x => !x.Cumplida),
            });
        }

        if (contratos.Any(c => c.EsPrestador))
        {
            var (d, h) = MesesDe(anioInf, cuatrimestre);
            if (contratos.Any(c => c.EsPrestador && c.VigenteEn(d, h)))
            {
                r.Informe = new InformeRepseDto
                {
                    Anio = anioInf, Cuatrimestre = cuatrimestre, Meses = NombreDeMeses(anioInf, cuatrimestre), Limite = limiteInf,
                    Casillas = [.. DelInforme.Select(x => new CasillaRepseDto { Clave = x.Clave, Texto = x.Texto, Fundamento = x.Fundamento, Cumplida = Marcada(null, x.Clave) })],
                };
            }
        }

        r.Avisos = Avisos(contratos.Where(c => c.Fin == null || c.Fin >= desde).ToList(), hoy);
        return r;
    }

    /// <summary>Registro REPSE faltante, vencido o por vencer, y prestadores sin trabajadores asignados.</summary>
    public static List<string> Avisos(IEnumerable<ContratoEspecializado> contratos, DateTime hoy)
    {
        var r = new List<string>();
        foreach (var c in contratos)
        {
            var quien = c.EsPrestador ? "del cliente" : "del contratista";
            if (c.RegistroRepse == null)
            {
                r.Add($"{c.ContraparteNombre}: falta el folio del registro REPSE {quien}. Sin registro la subcontratación no está permitida "
                      + "(art. 13 y 15 LFT) y el beneficiario no puede deducir ni acreditar el IVA (art. 27-V LISR, 5-II LIVA).");
            }
            else if (c.RegistroVigenteHasta is { } vence && vence < hoy)
            {
                r.Add($"{c.ContraparteNombre}: el registro REPSE {quien} venció el {vence.ToString("d 'de' MMMM 'de' yyyy", Es)}. Se renueva cada tres años (art. 15 LFT).");
            }
            else if (c.RegistroVigenteHasta is { } v && v <= hoy.AddDays(DiasParaAvisarVencimiento))
            {
                r.Add($"{c.ContraparteNombre}: el registro REPSE {quien} vence el {v.ToString("d 'de' MMMM 'de' yyyy", Es)}. Hay que renovarlo (cada tres años, art. 15 LFT).");
            }
            else if (c.RegistroVigenteHasta == null)
            {
                r.Add($"{c.ContraparteNombre}: falta la fecha en que vence el registro REPSE {quien} (se renueva cada tres años, art. 15 LFT).");
            }

            if (c.EsPrestador && c.Asignados().Count == 0)
            {
                r.Add($"{c.ContraparteNombre}: el contrato no tiene trabajadores asignados; el informe cuatrimestral pide su nombre, CURP, NSS "
                      + "y salario base de cotización (art. 15-A LSS, fracción II).");
            }
        }

        return r;
    }

    private sealed record FacturaDelMes(bool Emitida, string Otro, decimal Total);

    /// <summary>Facturas de ingreso del mes del cliente (sin duplicados por UUID): emitidas o recibidas, con la otra parte.</summary>
    private async Task<List<FacturaDelMes>> FacturasDelMesAsync(Guid organizacionId, DateTime desde, DateTime hasta)
    {
        var perfil = await _perfiles.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId);
        var rfc = perfil?.Dato(CamposDelPerfil.Rfc)?.Valor?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(rfc))
        {
            return [];
        }

        var hastaExclusivo = hasta.AddDays(1);
        var docs = await _documentos.GetListAsync(d => d.OrganizacionId == organizacionId && d.Tipo == TipoDeDocumento.Cfdi
                                                      && d.FechaDelDocumento >= desde && d.FechaDelDocumento < hastaExclusivo);
        var r = new List<FacturaDelMes>();
        foreach (var d in docs.GroupBy(x => x.Uuid ?? x.Id.ToString()).Select(g => g.First()))
        {
            try
            {
                var c = DatosDelCfdi.Leer(await _almacen.LeerAsync(d));
                if (c is not { Tipo: "I" })
                {
                    continue;
                }

                var emisor = c.EmisorRfc?.ToUpperInvariant();
                var receptor = c.ReceptorRfc?.ToUpperInvariant();
                if (emisor == rfc && receptor != null)
                {
                    r.Add(new FacturaDelMes(true, receptor, c.Total ?? 0));
                }
                else if (receptor == rfc && emisor != null)
                {
                    r.Add(new FacturaDelMes(false, emisor, c.Total ?? 0));
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "No se pudo leer el CFDI {Documento} para la revisión REPSE", d.Id);
            }
        }

        return r;
    }

    public static ContratoEspecializadoDto Describir(ContratoEspecializado c) => new()
    {
        Id = c.Id, Rol = c.Rol, ContraparteRfc = c.ContraparteRfc, ContraparteNombre = c.ContraparteNombre, Objeto = c.Objeto,
        Inicio = c.Inicio, Fin = c.Fin, TrabajadoresAproximados = c.TrabajadoresAproximados, RegistroRepse = c.RegistroRepse,
        RegistroVigenteHasta = c.RegistroVigenteHasta, Trabajadores = c.Asignados(),
    };
}
