using IgasConsole.Branding;
using Shouldly;
using Xunit;

namespace IgasConsole.Branding;

/// <summary>
/// El validador del color es lo único que separa «el inquilino eligió un color» de «el
/// inquilino escribió CSS que se inyecta en la pantalla de login de toda su organización»,
/// así que se prueba aparte y sin base de datos.
/// </summary>
public class ColorDeMarcaTests
{
    [Theory]
    [InlineData("#000000", "#000000")]
    [InlineData("#FFFFFF", "#ffffff")]   // se normaliza a minúsculas
    [InlineData("  #1B6AC9  ", "#1b6ac9")] // y se recortan los espacios
    public void Acepta_un_RRGGBB(string entrada, string esperado)
    {
        ColorDeMarca.Normalizar(entrada).ShouldBe(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("rojo")]
    [InlineData("red")]
    [InlineData("#abc")]              // forma corta de 3 dígitos
    [InlineData("#12345")]            // uno menos
    [InlineData("#1234567")]          // uno más
    [InlineData("#12345g")]           // 'g' no es hexadecimal
    [InlineData("1b6ac9")]            // sin almohadilla
    [InlineData("rgb(27,106,201)")]
    [InlineData("hsl(210,76%,45%)")]
    [InlineData("#1b6ac999")]         // con transparencia
    public void Rechaza_cualquier_otra_cosa(string? entrada)
    {
        ColorDeMarca.Normalizar(entrada).ShouldBeNull();
    }

    [Theory]
    // Las tres formas de escapar del bloque `:root{--lpx-brand:AQUÍ;}` para inyectar reglas
    // propias. Ninguna pasa, y no porque se busquen estos caracteres sino porque la lista
    // blanca solo deja pasar dígitos hexadecimales.
    [InlineData("#123456; } body { display:none")]
    [InlineData("#123456;}html{background:url(https://ejemplo/x)}")]
    [InlineData("red</style><script>alert(1)</script>")]
    [InlineData("#123456 !important")]
    public void Rechaza_los_intentos_de_inyectar_CSS(string ataque)
    {
        ColorDeMarca.Normalizar(ataque).ShouldBeNull();
    }

    [Fact]
    public void Al_LEER_un_valor_corrupto_se_cae_al_de_serie_en_vez_de_fallar()
    {
        // Camino de lectura: si alguien tocó la fila a mano, la aplicación se pinta del
        // color de siempre. Reventar aquí dejaría sin pantalla a todo el inquilino.
        ColorDeMarca.OPorDefecto("basura").ShouldBe(BrandingConsts.DefaultAccentColor);
        ColorDeMarca.OPorDefecto(null).ShouldBe(BrandingConsts.DefaultAccentColor);
        ColorDeMarca.OPorDefecto("#1b6ac9").ShouldBe("#1b6ac9");
    }
}
