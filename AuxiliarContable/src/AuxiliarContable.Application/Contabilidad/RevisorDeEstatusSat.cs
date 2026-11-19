using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AuxiliarContable.Documentos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Pregunta al SAT (servicio público de verificación, sin contraseña ni captcha) el estatus de las facturas y lo guarda
/// en cada documento. Si una cambia —se canceló, entró en proceso de cancelación—, el documento cambia y el impuesto de
/// su mes queda «por actualizar» solo (<see cref="MarcadorDeImpuestos"/>).
///
/// <para>Cada noche: primero las que están en proceso de cancelación, luego las que nunca se han revisado (último año)
/// y luego las del mes en curso y el anterior. Las canceladas no se vuelven a preguntar: es definitivo.</para>
/// </summary>
public class RevisorDeEstatusSat : ITransientDependency
{
    /// <summary>Cuántas por noche, para no cargar al SAT (que limita a quien pregunta de más).</summary>
    public const int MaximoPorNoche = 1500;

    /// <summary>Cuántas con el botón «Revisar estatus en el SAT» (que la petición no tarde de más).</summary>
    public const int MaximoAMano = 150;

    /// <summary>Tras tantas fallas seguidas se deja para la siguiente: el SAT está caído.</summary>
    private const int FallasParaParar = 5;

    private readonly IRepository<Documento, Guid> _documentos;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly IVerificadorDeCfdi _verificador;
    private readonly IUnitOfWorkManager _unidades;
    private readonly IClock _reloj;

    public ILogger<RevisorDeEstatusSat> Logger { get; set; } = NullLogger<RevisorDeEstatusSat>.Instance;

    /// <summary>Pausa entre consultas (en las pruebas, cero).</summary>
    public TimeSpan Pausa { get; set; } = TimeSpan.FromMilliseconds(300);

    public RevisorDeEstatusSat(
        IRepository<Documento, Guid> documentos, AlmacenDeDocumentos almacen, IVerificadorDeCfdi verificador, IUnitOfWorkManager unidades,
        IClock reloj)
    {
        _documentos = documentos;
        _almacen = almacen;
        _verificador = verificador;
        _unidades = unidades;
        _reloj = reloj;
    }

    public sealed class Resultado
    {
        public int Revisadas { get; set; }
        public int Cambiaron { get; set; }
        public int Canceladas { get; set; }
        public int EnProceso { get; set; }
        public int NoEncontradas { get; set; }
        public int Fallaron { get; set; }

        /// <summary>Quedaron por revisar (por el tope o porque el SAT dejó de responder).</summary>
        public int Faltan { get; set; }

        public bool SatSinResponder { get; set; }
    }

    /// <summary>La vuelta de la noche, de todos los clientes.</summary>
    public async Task<Resultado> RevisarLaNocheAsync(DateTime hoyEnMexico, int maximo = MaximoPorNoche)
    {
        var mesActual = new DateTime(hoyEnMexico.Year, hoyEnMexico.Month, 1);
        var desdeMeses = mesActual.AddMonths(-1);
        var haceUnAnio = mesActual.AddMonths(-12);
        List<Guid> ids;
        using (var uow = _unidades.Begin(requiresNew: true))
        {
            var q = (await _documentos.GetQueryableAsync())
                .Where(d => d.Tipo == TipoDeDocumento.Cfdi && !d.XmlDanado && d.Uuid != null && d.EstatusSat != EstatusDelSat.Cancelado)
                .Where(d => d.EstatusSat == EstatusDelSat.EnProcesoDeCancelacion
                            || (d.EstatusSat == null && d.FechaDelDocumento >= haceUnAnio)
                            || d.FechaDelDocumento >= desdeMeses)
                .OrderBy(d => d.EstatusSat == EstatusDelSat.EnProcesoDeCancelacion ? 0 : d.EstatusSat == null ? 1 : 2)
                .ThenByDescending(d => d.FechaDelDocumento)
                .Select(d => d.Id);
            ids = [.. q.Take(maximo + 1)];
            await uow.CompleteAsync();
        }

        var r = await RevisarAsync(ids.Take(maximo).ToList());
        r.Faltan += Math.Max(0, ids.Count - maximo);
        return r;
    }

    /// <summary>Las de un mes de un cliente (el botón de la mesa), salvo las ya canceladas.</summary>
    public async Task<Resultado> RevisarMesAsync(Guid organizacionId, int anio, int mes, int maximo = MaximoAMano)
    {
        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1);
        var ids = (await _documentos.GetListAsync(d => d.OrganizacionId == organizacionId && d.Tipo == TipoDeDocumento.Cfdi && !d.XmlDanado
                                                       && d.Uuid != null && d.EstatusSat != EstatusDelSat.Cancelado
                                                       && d.FechaDelDocumento >= desde && d.FechaDelDocumento < hasta))
            .OrderBy(d => d.EstatusSatUtc ?? DateTime.MinValue)
            .Select(d => d.Id).ToList();
        var r = await RevisarAsync(ids.Take(maximo).ToList());
        r.Faltan += Math.Max(0, ids.Count - maximo);
        return r;
    }

    private async Task<Resultado> RevisarAsync(IReadOnlyList<Guid> ids)
    {
        var r = new Resultado();
        if (!_verificador.EstaConfigurado)
        {
            r.SatSinResponder = true;
            r.Faltan = ids.Count;
            return r;
        }

        var seguidas = 0;
        var fallasDelSat = 0;
        for (var i = 0; i < ids.Count; i++)
        {
            if (seguidas >= FallasParaParar)
            {
                r.SatSinResponder = true;
                r.Faltan += ids.Count - i;
                break;
            }

            try
            {
                using var uow = _unidades.Begin(requiresNew: true);
                var d = await _documentos.GetAsync(ids[i]);
                var xml = await _almacen.LeerAsync(d);
                var datos = DatosDelCfdi.Leer(xml);
                if (datos is { EsRetencion: true })
                {
                    // Los CFDI de retenciones no se consultan en la verificación de facturas: ni falla ni cuenta.
                    await uow.CompleteAsync();
                    continue;
                }

                var total = DatosDelCfdi.TotalTalCual(xml);
                if (datos?.EmisorRfc is not { } emisor || datos.ReceptorRfc is not { } receptor || total == null || d.Uuid == null)
                {
                    r.Fallaron++;
                    await uow.CompleteAsync();
                    continue;
                }

                var estatus = (await _verificador.ConsultarAsync(emisor, receptor, total, d.Uuid)).ParaGuardar;
                seguidas = 0;
                r.Revisadas++;
                r.Canceladas += estatus == EstatusDelSat.Cancelado ? 1 : 0;
                r.EnProceso += estatus == EstatusDelSat.EnProcesoDeCancelacion ? 1 : 0;
                r.NoEncontradas += estatus == EstatusDelSat.NoEncontrado ? 1 : 0;
                // Solo si cambió: si no, el impuesto del mes se recalcularía cada noche por nada.
                if (d.EstatusSat != estatus)
                {
                    d.PonerEstatusSat(estatus, _reloj.Now.ToUniversalTime());
                    await _documentos.UpdateAsync(d);
                    r.Cambiaron++;
                }

                await uow.CompleteAsync();
            }
            catch (VerificadorNoDisponibleException ex)
            {
                seguidas++;
                fallasDelSat++;
                r.Fallaron++;
                Logger.LogInformation("El SAT no respondió la verificación: {Mensaje}", ex.Message);
            }
            catch (Exception ex) when (ex is IOException or CryptographicException or AbpException)
            {
                r.Fallaron++;
                Logger.LogWarning(ex, "No se pudo revisar el estatus de la factura {Documento}", ids[i]);
            }

            if (Pausa > TimeSpan.Zero && i < ids.Count - 1)
            {
                await Task.Delay(Pausa);
            }
        }

        // Nada salió bien y el SAT fue el que falló: que se note (la pantalla lo dice; la noche lo intenta mañana).
        r.SatSinResponder |= fallasDelSat > 0 && r.Revisadas == 0;
        return r;
    }
}
