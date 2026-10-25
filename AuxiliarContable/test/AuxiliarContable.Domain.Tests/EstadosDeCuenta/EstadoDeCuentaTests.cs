using System;
using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace AuxiliarContable.EstadosDeCuenta;

public class EstadoDeCuentaTests
{
    private static readonly Guid Org = Guid.NewGuid();

    private static EstadoDeCuenta Estado(decimal? inicial, decimal? final, params (decimal Cargo, decimal Abono, decimal? Saldo)[] renglones)
    {
        var estado = new EstadoDeCuenta(Guid.NewGuid(), Org, Guid.NewGuid());
        var orden = 0;
        estado.Llenar("BBVA", "0123456789", new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), "mxn", inicial, final,
            renglones.Select(r => new MovimientoBancario(Guid.NewGuid(), estado.Id, Org, ++orden, new DateTime(2026, 10, orden),
                $"mov {orden}", null, r.Cargo, r.Abono, r.Saldo)).ToList());
        return estado;
    }

    [Fact]
    public void Cuadra_cuando_inicial_mas_abonos_menos_cargos_es_el_final()
    {
        var e = Estado(10_000m, 11_749.50m, (0, 5_000m, 15_000m), (1_250.50m, 0, 13_749.50m), (2_000m, 0, 11_749.50m));

        e.Cuadra.ShouldBeTrue();
        e.Diferencia.ShouldBe(0m);
        e.TotalAbonos.ShouldBe(5_000m);
        e.TotalCargos.ShouldBe(3_250.50m);
        e.SaltosDeSaldo.ShouldBe(0);
        e.Revision.ShouldBe(RevisionDelEstadoDeCuenta.PorConfirmar);
        e.Cuenta.ShouldBe("6789", "solo los últimos dígitos");
        e.Moneda.ShouldBe("MXN");
    }

    [Fact]
    public void Si_no_cuadra_va_a_revision_y_senala_el_renglon()
    {
        // El segundo renglón se leyó 1,520.50 en vez de 1,250.50.
        var e = Estado(10_000m, 11_749.50m, (0, 5_000m, 15_000m), (1_520.50m, 0, 13_749.50m), (2_000m, 0, 11_749.50m));

        e.Cuadra.ShouldBeFalse();
        e.Diferencia.ShouldBe(-270m);
        e.SaltosDeSaldo.ShouldBe(1);
        e.Revision.ShouldBe(RevisionDelEstadoDeCuenta.RequiereRevision);
        Should.Throw<BusinessException>(() => e.Confirmar(aunqueNoCuadre: false)).Code.ShouldBe("AuxiliarContable:EstadoDeCuentaNoCuadra");

        var malo = e.Movimientos.Single(m => m.Orden == 2);
        e.CorregirMovimiento(malo.Id, malo.Fecha, malo.Concepto, 1_250.50m, 0);

        e.Cuadra.ShouldBeTrue();
        e.SaltosDeSaldo.ShouldBe(0);
        e.Confirmar(aunqueNoCuadre: false);
        e.Revision.ShouldBe(RevisionDelEstadoDeCuenta.Confirmado);
    }

    [Fact]
    public void Sin_saldos_no_se_da_por_cuadrado()
    {
        var e = Estado(null, null, (100m, 0, null));

        e.Diferencia.ShouldBeNull();
        e.Cuadra.ShouldBeFalse();
        e.Revision.ShouldBe(RevisionDelEstadoDeCuenta.RequiereRevision);

        e.CorregirSaldos(500m, 400m);
        e.Cuadra.ShouldBeTrue();
    }

    [Fact]
    public void Una_persona_puede_aceptarlo_aunque_no_cuadre()
    {
        var e = Estado(0m, 50m, (0, 40m, null));
        e.Confirmar(aunqueNoCuadre: true);
        e.Revision.ShouldBe(RevisionDelEstadoDeCuenta.Confirmado);
    }

    [Fact]
    public void Montos_negativos_no_se_aceptan()
    {
        Should.Throw<BusinessException>(() =>
            new MovimientoBancario(Guid.NewGuid(), Guid.NewGuid(), Org, 1, DateTime.Today, "x", null, -1m, 0, null));
    }
}
