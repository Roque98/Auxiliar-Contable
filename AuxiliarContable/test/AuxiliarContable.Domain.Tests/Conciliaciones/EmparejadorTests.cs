using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Xunit;
using static AuxiliarContable.Conciliaciones.Emparejador;

namespace AuxiliarContable.Conciliaciones;

public class EmparejadorTests
{
    private static readonly DateTime Dia = new(2026, 10, 10);

    private static Movimiento Abono(decimal monto, int dias = 0, string concepto = "SPEI RECIBIDO") =>
        new(Guid.NewGuid(), Dia.AddDays(dias), 0, monto, concepto, null);

    private static Movimiento Cargo(decimal monto, int dias = 0, string concepto = "PAGO") =>
        new(Guid.NewGuid(), Dia.AddDays(dias), monto, 0, concepto, null);

    private static Factura Venta(decimal total, int dias = 0, string rfc = "ACM010101ABC") =>
        new(Guid.NewGuid(), Dia.AddDays(dias), total, "MXN", true, rfc, Guid.NewGuid().ToString().ToUpperInvariant());

    private static Factura Gasto(decimal total, int dias = 0, string rfc = "CFE370814QI0") =>
        new(Guid.NewGuid(), Dia.AddDays(dias), total, "MXN", false, rfc, Guid.NewGuid().ToString().ToUpperInvariant());

    [Fact]
    public void Un_abono_va_con_una_venta_y_un_cargo_con_un_gasto()
    {
        var abono = Abono(1160m);
        var cargo = Cargo(500m, 1);
        var venta = Venta(1160m, 2);
        var gasto = Gasto(500m);

        var parejas = Emparejar([abono, cargo], [venta, gasto]);

        parejas.Count.ShouldBe(2);
        parejas.Single(p => p.MovimientoId == abono.Id).FacturaId.ShouldBe(venta.Id);
        parejas.Single(p => p.MovimientoId == cargo.Id).FacturaId.ShouldBe(gasto.Id);
        parejas.Single(p => p.MovimientoId == abono.Id).Motivo.ShouldContain("2 días");
    }

    [Fact]
    public void No_empareja_montos_distintos_ni_fechas_lejanas_ni_venta_con_cargo()
    {
        Emparejar([Abono(1160m)], [Venta(1160.50m)]).ShouldBeEmpty();
        Emparejar([Abono(1160m)], [Venta(1160m, ConciliacionConsts.DiasDeTolerancia + 1)]).ShouldBeEmpty();
        Emparejar([Cargo(1160m)], [Venta(1160m)]).ShouldBeEmpty();
        Emparejar([Abono(1160m)], [Venta(1160m) with { Moneda = "USD" }]).ShouldBeEmpty();
    }

    [Fact]
    public void El_RFC_en_el_concepto_desempata_y_cada_uno_se_usa_una_vez()
    {
        var deAcme = Abono(1000m, 3, "SPEI RECIBIDO ACM010101ABC");
        var otro = Abono(1000m, 0);
        var ventaAcme = Venta(1000m, 0, "ACM010101ABC");
        var ventaOtra = Venta(1000m, 0, "OTR010101XYZ");

        var parejas = Emparejar([deAcme, otro], [ventaAcme, ventaOtra]);

        parejas.Count.ShouldBe(2);
        parejas.Single(p => p.MovimientoId == deAcme.Id).FacturaId.ShouldBe(ventaAcme.Id);
        parejas.Single(p => p.MovimientoId == otro.Id).FacturaId.ShouldBe(ventaOtra.Id);
        parejas.Single(p => p.MovimientoId == deAcme.Id).Motivo.ShouldContain("RFC");
    }

    [Fact]
    public void Una_pareja_rechazada_no_se_vuelve_a_proponer()
    {
        var abono = Abono(1160m);
        var venta = Venta(1160m);

        Emparejar([abono], [venta], new HashSet<(Guid, Guid)> { (abono.Id, venta.Id) }).ShouldBeEmpty();
    }

    [Fact]
    public void Lo_ambiguo_se_propone_pero_lo_dice()
    {
        var abono = Abono(1000m);
        var parejas = Emparejar([abono], [Venta(1000m, 1), Venta(1000m, -1)]);

        parejas.ShouldHaveSingleItem().Motivo.ShouldContain("revisa");
    }
}
