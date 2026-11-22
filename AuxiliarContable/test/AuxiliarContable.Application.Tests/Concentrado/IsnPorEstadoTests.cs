using System;
using AuxiliarContable.Concentrado;
using Shouldly;
using Xunit;

namespace AuxiliarContable.Concentrado;

public class IsnPorEstadoTests
{
    [Fact]
    public void Plazo_y_recargos_del_estado_salen_de_su_ley()
    {
        var mor = IsnPorEstado.Buscar("Morelos").ShouldNotBeNull();
        (mor.Clave, mor.Dia, mor.Tasa).ShouldBe(("MOR", 17, 0.0142m));
        IsnPorEstado.Buscar("Oaxaca").ShouldNotBeNull().Dia.ShouldBeNull(); // bimestral
        var bc = IsnPorEstado.Buscar("BC").ShouldNotBeNull();
        (bc.Dia, bc.Tasa).ShouldBe((25, 0.0225m));
        IsnPorEstado.Buscar("Ciudad de México").ShouldNotBeNull().Clave.ShouldBe("CDMX");
        IsnPorEstado.Buscar("Av. Juárez 1, Toluca, Estado de México").ShouldNotBeNull().Clave.ShouldBe("MEX");
        var bcs = IsnPorEstado.Buscar("Baja California Sur").ShouldNotBeNull();
        (bcs.Clave, bcs.Dia, bcs.Tasa).ShouldBe(("BCS", 15, 0.01m));
        IsnPorEstado.Buscar("Jalisco").ShouldNotBeNull().Dia.ShouldBe(12);
        IsnPorEstado.Buscar("Puebla").ShouldNotBeNull().Dia.ShouldBe(17);
        IsnPorEstado.Buscar("Sinaloa").ShouldNotBeNull().Tasa.ShouldBe(0.0225m);
        IsnPorEstado.Buscar("Chihuahua").ShouldNotBeNull().Tasa.ShouldBe(0.0207m);
        IsnPorEstado.Buscar("Veracruz").ShouldNotBeNull().Tasa.ShouldBe(0.015m); // del dictamen de su LI 2026
        IsnPorEstado.Buscar("Tabasco").ShouldNotBeNull().Tasa.ShouldBe(0.0147m); // 0.98 % × 1.5 (CF art. 22)
        IsnPorEstado.Buscar("Sonora").ShouldNotBeNull().Tasa.ShouldBe(0.015m);
        IsnPorEstado.Buscar(null).ShouldBeNull();
    }

    [Fact]
    public void Un_anio_sin_sus_leyes_hereda_el_plazo_pero_no_la_tasa()
    {
        IsnPorEstado.Ejercicios.ShouldContain(2026);
        var mor26 = IsnPorEstado.Buscar("Morelos", 2026).ShouldNotBeNull();
        (mor26.Ejercicio, mor26.HeredadoDe, mor26.Tasa).ShouldBe((2026, (int?)null, 0.0142m));

        var mor27 = IsnPorEstado.Buscar("Morelos", 2027).ShouldNotBeNull();
        mor27.Dia.ShouldBe(17);
        mor27.HeredadoDe.ShouldBe(2026);
        mor27.Tasa.ShouldBeNull();
    }

    [Fact]
    public void Fecha_limite_y_meses_de_mora()
    {
        ConcentradoAppService.Limite(2026, 10).ShouldBe(new DateTime(2026, 11, 17));
        ConcentradoAppService.Limite(2026, 10, 25).ShouldBe(new DateTime(2026, 11, 25));
        ConcentradoAppService.Limite(2026, 1, 15).ShouldBe(new DateTime(2026, 2, 16)); // domingo -> lunes
        ConcentradoAppService.MesesDeMora(new DateTime(2026, 2, 17), new DateTime(2026, 2, 17)).ShouldBe(0);
        ConcentradoAppService.MesesDeMora(new DateTime(2026, 2, 17), new DateTime(2026, 3, 18)).ShouldBe(2);
    }
}
