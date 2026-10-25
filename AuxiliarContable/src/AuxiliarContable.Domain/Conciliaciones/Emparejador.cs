using System;
using System.Collections.Generic;
using System.Linq;

namespace AuxiliarContable.Conciliaciones;

/// <summary>
/// Empareja movimientos del banco con facturas POR REGLAS, sin IA (visión: conciliación
/// determinista, la persona confirma). Un abono se busca entre las facturas EMITIDAS (ventas) y
/// un cargo entre las RECIBIDAS (gastos):
/// <list type="number">
/// <item>mismo monto (± <see cref="ConciliacionConsts.ToleranciaDeMonto"/>) y moneda MXN;</item>
/// <item>fecha a ± <see cref="ConciliacionConsts.DiasDeTolerancia"/> días;</item>
/// <item>desempate: RFC o UUID de la factura en el concepto o la referencia del banco, y luego la fecha más cercana.</item>
/// </list>
/// Cada movimiento y cada factura se usan una sola vez. Lo ambiguo se propone igual pero lo dice.
/// </summary>
public static class Emparejador
{
    public sealed record Movimiento(Guid Id, DateTime Fecha, decimal Cargo, decimal Abono, string Concepto, string? Referencia);

    public sealed record Factura(Guid Id, DateTime Fecha, decimal Total, string Moneda, bool EsVenta, string RfcContraparte, string Uuid);

    public sealed record Pareja(Guid MovimientoId, Guid FacturaId, int Puntaje, string Motivo);

    public static List<Pareja> Emparejar(
        IEnumerable<Movimiento> movimientos,
        IEnumerable<Factura> facturas,
        ISet<(Guid Movimiento, Guid Factura)>? rechazadas = null)
    {
        rechazadas ??= new HashSet<(Guid, Guid)>();
        var libres = facturas.Where(f => f.Moneda == "MXN").ToList();
        var candidatas = new List<(Movimiento M, Factura F, int Puntaje, string Motivo)>();

        foreach (var m in movimientos)
        {
            var esAbono = m.Abono > 0;
            var monto = esAbono ? m.Abono : m.Cargo;
            if (monto <= 0)
            {
                continue;
            }

            var texto = $"{m.Concepto} {m.Referencia}".ToUpperInvariant();
            foreach (var f in libres)
            {
                if (f.EsVenta != esAbono
                    || Math.Abs(f.Total - monto) > ConciliacionConsts.ToleranciaDeMonto
                    || rechazadas.Contains((m.Id, f.Id)))
                {
                    continue;
                }

                var dias = Math.Abs((m.Fecha.Date - f.Fecha.Date).Days);
                if (dias > ConciliacionConsts.DiasDeTolerancia)
                {
                    continue;
                }

                var motivos = new List<string> { "mismo monto", dias == 0 ? "mismo día" : $"{dias} día{(dias == 1 ? "" : "s")} de diferencia" };
                var puntaje = 60 + (ConciliacionConsts.DiasDeTolerancia - dias) * 4;
                if (!string.IsNullOrEmpty(f.RfcContraparte) && texto.Contains(f.RfcContraparte.ToUpperInvariant()))
                {
                    puntaje += 20;
                    motivos.Add("el RFC aparece en el banco");
                }

                if (texto.Contains(f.Uuid.ToUpperInvariant()[..8]))
                {
                    puntaje += 20;
                    motivos.Add("el folio fiscal aparece en el banco");
                }

                candidatas.Add((m, f, Math.Min(puntaje, 100), string.Join(", ", motivos)));
            }
        }

        // Codicioso por puntaje: la mejor pareja primero, y ninguno se usa dos veces.
        var usadosM = new HashSet<Guid>();
        var usadasF = new HashSet<Guid>();
        var parejas = new List<Pareja>();
        foreach (var c in candidatas.OrderByDescending(c => c.Puntaje).ThenBy(c => Math.Abs((c.M.Fecha - c.F.Fecha).Days)))
        {
            if (usadosM.Contains(c.M.Id) || usadasF.Contains(c.F.Id))
            {
                continue;
            }

            var rivales = candidatas.Count(o => o.M.Id == c.M.Id && !usadasF.Contains(o.F.Id)) - 1;
            var motivo = rivales > 0 ? $"{c.Motivo}; había {rivales + 1} facturas posibles, revisa" : c.Motivo;
            parejas.Add(new Pareja(c.M.Id, c.F.Id, rivales > 0 ? c.Puntaje - 15 : c.Puntaje, motivo));
            usadosM.Add(c.M.Id);
            usadasF.Add(c.F.Id);
        }

        return parejas;
    }
}
