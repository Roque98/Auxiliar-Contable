using Shouldly;
using Xunit;

namespace AuxiliarContable.Leyes;

/// <summary>El texto de un artículo en pantalla, sin el encabezado entre corchetes que le pone el índice.</summary>
public class SinEncabezadoTests
{
    [Fact]
    public void Quita_los_renglones_entre_corchetes_del_principio()
    {
        const string texto = "[LEY del Impuesto sobre la Renta — última reforma DOF 2024-04-01]\r\n" +
                             "[Ubicación: TÍTULO II > CAPÍTULO II]\r\n" +
                             "Artículo 36. La deducción de las inversiones [se sujetará] a las reglas siguientes:";

        LeyesAppService.SinEncabezado(texto)
            .ShouldBe("Artículo 36. La deducción de las inversiones [se sujetará] a las reglas siguientes:");
    }

    [Fact]
    public void Sin_encabezado_el_texto_queda_igual()
    {
        LeyesAppService.SinEncabezado("Artículo 27. Las deducciones…").ShouldBe("Artículo 27. Las deducciones…");
        LeyesAppService.SinEncabezado("").ShouldBe("");
    }
}
