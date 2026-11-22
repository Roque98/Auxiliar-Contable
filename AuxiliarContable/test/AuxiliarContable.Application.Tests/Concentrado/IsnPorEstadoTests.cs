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
        IsnPorEstado.Buscar("Baja California Sur").ShouldNotBeNull().Clave.ShouldBe("BCS");
        IsnPorEstado.Buscar(null).ShouldBeNull();
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
