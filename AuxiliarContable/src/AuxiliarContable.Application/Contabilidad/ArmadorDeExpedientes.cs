using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Documentos;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Arma el expediente de control interno de un mes: cada operación (movimientos del banco de la conciliación guardada
/// y registros de «Efectivo y crédito») con su evidencia y lo que le falta (<see cref="ReglaDeEvidencia"/>). Lo usan la
/// pantalla de control interno y el cierre del mes, para que digan lo mismo.
/// </summary>
public class ArmadorDeExpedientes : ITransientDependency
{
    private readonly IRepository<ConciliacionBancaria, Guid> _conciliaciones;
    private readonly IRepository<PartidaPendiente, Guid> _partidas;
    private readonly IRepository<MovimientoFueraDelBanco, Guid> _fueraDelBanco;
    private readonly IRepository<EvidenciaDeOperacion, Guid> _evidencias;
    private readonly IRepository<Documento, Guid> _documentos;

    public ArmadorDeExpedientes(
        IRepository<ConciliacionBancaria, Guid> conciliaciones,
        IRepository<PartidaPendiente, Guid> partidas,
        IRepository<MovimientoFueraDelBanco, Guid> fueraDelBanco,
        IRepository<EvidenciaDeOperacion, Guid> evidencias,
        IRepository<Documento, Guid> documentos)
    {
        _conciliaciones = conciliaciones;
        _partidas = partidas;
        _fueraDelBanco = fueraDelBanco;
        _evidencias = evidencias;
        _documentos = documentos;
    }

    /// <summary>Las operaciones del mes y si había conciliación guardada.</summary>
    public async Task<(List<OperacionDto> Operaciones, bool HayConciliacion)> ArmarAsync(Guid organizacionId, int anio, int mes)
    {
        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1);
        var conciliacion = await _conciliaciones.FirstOrDefaultAsync(c => c.OrganizacionId == organizacionId && c.Anio == anio && c.Mes == mes);
        var partidas = (await _partidas.GetListAsync(p => p.OrganizacionId == organizacionId && p.Anio == anio && p.Mes == mes))
            .GroupBy(p => p.Clave).ToDictionary(g => g.Key, g => g.First());
        var fuera = await _fueraDelBanco.GetListAsync(m => m.OrganizacionId == organizacionId && m.Fecha >= desde && m.Fecha < hasta);
        var evidencias = await _evidencias.GetListAsync(e => e.OrganizacionId == organizacionId && e.Anio == anio && e.Mes == mes);
        var docIds = evidencias.Where(e => e.DocumentoId.HasValue).Select(e => e.DocumentoId!.Value)
            .Concat(fuera.Where(m => m.DocumentoId.HasValue).Select(m => m.DocumentoId!.Value)).Distinct().ToList();
        var documentos = docIds.Count == 0 ? new Dictionary<Guid, Documento>()
            : (await _documentos.GetListAsync(d => docIds.Contains(d.Id))).ToDictionary(d => d.Id);

        var operaciones = new List<OperacionDto>();
        if (conciliacion != null)
        {
            var movimientos = GestorDeConciliaciones.Leer(conciliacion).Movimientos;
            var claves = ClaveDeMovimiento.De(movimientos);
            for (var i = 0; i < movimientos.Count; i++)
            {
                var m = movimientos[i];
                var clave = claves[i];
                var propias = evidencias.Where(e => e.Origen == OrigenDeOperacion.Banco && e.Clave == clave).ToList();
                var partida = partidas.GetValueOrDefault(clave);
                var facturas = m.Conciliacion?.Facturas ?? [];
                var esTraspaso = m.Conciliacion?.Estado == "traspaso" || partida?.Resolucion == ResolucionDePartida.TraspasoPropio;
                // Lo que el despacho ya decidió de un movimiento sin factura (comisión, préstamo…) lo explica.
                var explicada = partida is { Pendiente: false };
                operaciones.Add(new OperacionDto
                {
                    Origen = OrigenDeOperacion.Banco,
                    Clave = clave,
                    Fecha = m.Fecha,
                    Descripcion = m.Concepto.Trim(),
                    Contraparte = string.IsNullOrWhiteSpace(m.Contraparte) ? null : m.Contraparte,
                    EsSalida = m.Naturaleza == "cargo",
                    Importe = m.Importe,
                    Facturas = facturas,
                    Estado = ReglaDeEvidencia.Estado(m.Naturaleza == "cargo", esTraspaso, facturas.Count > 0, propias, explicada),
                    Evidencias = propias.Select(e => Describir(e, documentos)).ToList(),
                    Explicacion = explicada ? partida!.Nota ?? partida.Respuesta : null,
                });
            }
        }

        foreach (var m in fuera.OrderBy(m => m.Fecha))
        {
            var propias = evidencias.Where(e => e.Origen == OrigenDeOperacion.FueraDelBanco && e.MovimientoFueraDelBancoId == m.Id).ToList();
            var documento = m.DocumentoId is { } d ? documentos.GetValueOrDefault(d) : null;
            var esFactura = documento?.Tipo == TipoDeDocumento.Cfdi;
            var esSalida = m.Tipo is TipoDeMovimientoFueraDelBanco.SalidaEnEfectivo or TipoDeMovimientoFueraDelBanco.CompraACredito;
            var lista = propias.Select(e => Describir(e, documentos)).ToList();
            // Su comprobante propio (ticket, foto) cuenta como comprobante de pago; una factura ligada, como factura.
            if (documento != null && !esFactura)
            {
                lista.Insert(0, new EvidenciaDto { Tipo = TipoDeEvidencia.ComprobanteDePago, DocumentoId = documento.Id, NombreDelDocumento = documento.NombreOriginal, Cuando = m.CreationTime });
            }

            operaciones.Add(new OperacionDto
            {
                Origen = OrigenDeOperacion.FueraDelBanco,
                MovimientoFueraDelBancoId = m.Id,
                Fecha = m.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                Descripcion = m.Concepto,
                Contraparte = m.Contraparte,
                EsSalida = esSalida,
                Importe = m.Importe,
                Facturas = esFactura ? [documento!.NombreOriginal] : [],
                Estado = ReglaDeEvidencia.Estado(esSalida, false, esFactura, propias, documento != null && !esFactura),
                Evidencias = lista,
            });
        }

        return (operaciones, conciliacion != null);
    }

    private static EvidenciaDto Describir(EvidenciaDeOperacion e, Dictionary<Guid, Documento> documentos) => new()
    {
        Id = e.Id,
        Tipo = e.Tipo,
        DocumentoId = e.DocumentoId,
        NombreDelDocumento = e.DocumentoId is { } d ? documentos.GetValueOrDefault(d)?.NombreOriginal : null,
        Nota = e.Nota,
        Cuando = e.CreationTime,
    };
}
