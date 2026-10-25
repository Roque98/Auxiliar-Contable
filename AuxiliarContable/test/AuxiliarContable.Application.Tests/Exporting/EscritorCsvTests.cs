using System.Text;
using AuxiliarContable.Exporting;
using Shouldly;
using Xunit;

namespace AuxiliarContable.Exporting;

/// <summary>
/// El CSV se prueba aparte y sin base de datos porque lo que puede salir mal no tiene nada
/// que ver con los datos: es el BOM, el separador y las fórmulas.
/// </summary>
public class EscritorCsvTests
{
    private static string Texto(byte[] csv) => Encoding.UTF8.GetString(csv);

    [Fact]
    public void Empieza_por_el_BOM_de_UTF8()
    {
        var csv = EscritorCsv.Generar(["A"], [["x"]]);

        // Sin BOM, Excel abre el archivo en la codificación regional y "Ratón" sale
        // "RatÃ³n": parece un problema de la base de datos y es del lector.
        csv[0].ShouldBe((byte)0xEF);
        csv[1].ShouldBe((byte)0xBB);
        csv[2].ShouldBe((byte)0xBF);
    }

    [Fact]
    public void Los_acentos_sobreviven()
    {
        var csv = EscritorCsv.Generar(["Descripción"], [["Ratón óptico"]]);

        Texto(csv).ShouldContain("Ratón óptico");
        Texto(csv).ShouldContain("Descripción");
    }

    [Fact]
    public void Separa_por_punto_y_coma()
    {
        // Excel usa el separador de la configuración regional, no el del archivo. En español
        // es el punto y coma; con comas, toda la fila cae en la primera columna.
        var csv = Texto(EscritorCsv.Generar(["A", "B"], [["1", "2"]]));

        csv.ShouldContain("A;B");
        csv.ShouldContain("1;2");
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+34600000000")]
    [InlineData("-5")]
    [InlineData("@SUM(A1:A9)")]
    public void Neutraliza_las_celdas_que_Excel_ejecutaria(string peligrosa)
    {
        // INYECCIÓN DE FÓRMULAS. Excel y Sheets EJECUTAN lo que empiece por = + - @, y estos
        // exports llevan datos que escribe cualquiera desde fuera (URLs, nombres de usuario).
        // El apóstrofo delante fuerza a tratarlo como texto.
        var csv = Texto(EscritorCsv.Generar(["A"], [[peligrosa]]));

        csv.ShouldContain("'" + peligrosa);
    }

    [Fact]
    public void Un_valor_normal_no_se_toca()
    {
        // La protección no puede ensuciar los datos corrientes.
        var csv = Texto(EscritorCsv.Generar(["A"], [["Teclado"]]));

        csv.ShouldContain("Teclado");
        csv.ShouldNotContain("'Teclado");
    }

    [Fact]
    public void Entrecomilla_cuando_el_valor_lleva_el_separador()
    {
        var csv = Texto(EscritorCsv.Generar(["A"], [["uno;dos"]]));

        csv.ShouldContain("\"uno;dos\"");
    }

    [Fact]
    public void Duplica_las_comillas_internas()
    {
        var csv = Texto(EscritorCsv.Generar(["A"], [["dijo \"hola\""]]));

        // Regla de CSV: dentro de comillas, una comilla se escribe dos veces.
        csv.ShouldContain("\"dijo \"\"hola\"\"\"");
    }

    [Fact]
    public void Un_salto_de_linea_no_parte_la_fila()
    {
        var csv = Texto(EscritorCsv.Generar(["A", "B"], [["linea1\nlinea2", "fin"]]));

        // Si no se entrecomillara, ese salto crearía una fila fantasma y descuadraría todo
        // lo que viniera después.
        csv.ShouldContain("\"linea1\nlinea2\"");
    }

    [Fact]
    public void Un_valor_nulo_sale_como_celda_vacia()
    {
        var csv = Texto(EscritorCsv.Generar(["A", "B"], [[null, "x"]]));

        csv.ShouldContain(";x");
    }

    [Fact]
    public void La_fecha_dice_que_es_UTC_y_no_depende_de_la_cultura()
    {
        var texto = EscritorCsv.Fecha(new System.DateTime(2026, 3, 9, 14, 5, 30, System.DateTimeKind.Utc));

        // Formato invariable: si dependiera de la cultura del servidor, el mismo export
        // saldría distinto según dónde corra.
        texto.ShouldBe("2026-03-09 14:05:30 UTC");
    }

    [Fact]
    public void El_nombre_de_archivo_lleva_marca_de_tiempo()
    {
        var nombre = EscritorCsv.NombreConFecha("registro");

        nombre.ShouldStartWith("registro-");
        nombre.ShouldEndWith(".csv");
        // Dos descargas seguidas no deben pisarse en la carpeta de descargas.
        nombre.Length.ShouldBeGreaterThan("registro-.csv".Length);
    }
}
