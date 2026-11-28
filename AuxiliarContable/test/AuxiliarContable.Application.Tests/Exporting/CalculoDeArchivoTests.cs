using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using AuxiliarContable.Exportaciones;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace AuxiliarContable.Exporting;

/// <summary>
/// El Excel de un cálculo con fórmulas (resumen ligado, hoja de cálculo, tablas) y el PDF con su documento formal.
/// Lo que puede salir mal: una fórmula que apunta a la celda equivocada, que no calcula, o un resumen que no coincide.
/// </summary>
public class CalculoDeArchivoTests
{
    private const string Finiquito = """
        {"formato":"excel","contenido":"# Finiquito de prueba\n\nCálculo al 30 de noviembre.\n\n| Concepto | Importe |\n|---|---|\n| Total | $1.00 |",
         "calculo":{
           "resumen":[
             {"concepto":"Aguinaldo proporcional","ref":"aguinaldo"},
             {"concepto":"ISR del mes","ref":"isr"},
             {"concepto":"Neto a pagar","ref":"aguinaldo - isr"}],
           "hojas":[{"nombre":"Cálculo","renglones":[
             {"concepto":"Datos"},
             {"id":"sd","concepto":"Salario diario","valor":500,"formato":"moneda"},
             {"id":"dias","concepto":"Días trabajados en el año","valor":"200","formato":"dias"},
             {"id":"aguinaldo","concepto":"Aguinaldo proporcional","formula":"=REDONDEAR(sd * 15 / 365 * dias; 2)","nota":"Art. 87 LFT"},
             {"concepto":"ISR"},
             {"id":"base","concepto":"Base gravable","valor":10000},
             {"id":"li","concepto":"Límite inferior","formula":"BUSCARV(base, tarifa, 1, VERDADERO)"},
             {"id":"cuota","concepto":"Cuota fija","formula":"BUSCARV(base; tarifa; 2; VERDADERO)"},
             {"id":"tasa","concepto":"Tasa sobre el excedente","formula":"BUSCARV(base, tarifa, 3, TRUE)","formato":"porcentaje"},
             {"id":"isr","concepto":"ISR","formula":"ROUND((base - li) * tasa + cuota, 2)","nota":"Art. 96 LISR"}]}],
           "tablas":[{"id":"tarifa","titulo":"Tarifa mensual (de prueba)","encabezados":["Límite inferior","Cuota fija","Tasa"],
             "filas":[[0.01,0,0.0192],[746.05,14.32,0.064],[6332.06,371.83,0.1088],[11128.02,893.63,0.16]]}]}}
        """;

    private static ArchivoGenerado Generar(string json)
    {
        var e = JsonDocument.Parse(json).RootElement;
        var calculo = CalculoDeArchivo.Leer(e);
        return ArchivoDesdeTexto.Generar(FormatoDeArchivo.Excel, null, e.GetProperty("contenido").GetString()!, calculo);
    }

    [Fact]
    public void El_excel_del_calculo_lleva_formulas_ligadas_y_da_los_resultados()
    {
        var archivo = Generar(Finiquito);

        archivo.Nombre.ShouldBe("Finiquito de prueba.xlsx");
        var r = archivo.Resultados!.ToDictionary(x => x.Concepto, x => x.Valor);
        r["Aguinaldo proporcional"].ShouldBe(4109.59);
        r["ISR del mes"].ShouldBe(770.9);
        r["Neto a pagar"].ShouldBe(3338.69);

        using var libro = new XLWorkbook(new MemoryStream(archivo.Contenido));
        libro.Worksheets.Select(h => h.Name).ShouldBe(["Resumen", "Cálculo", "Tablas"]);
        var resumen = libro.Worksheet("Resumen");
        resumen.Cell(5, 2).FormulaA1.ShouldBe("'Cálculo'!$B$8");
        resumen.Cell(7, 2).FormulaA1.ShouldBe("'Cálculo'!$B$8-'Cálculo'!$B$14");
        // Las notas de la conversación van debajo, sin su tabla.
        resumen.CellsUsed().ShouldContain(c => c.GetString() == "Cálculo al 30 de noviembre.");

        var calculo = libro.Worksheet("Cálculo");
        calculo.Cell(8, 2).FormulaA1.ShouldBe("ROUND('Cálculo'!$B$6*15/365*'Cálculo'!$B$7,2)");
        calculo.Cell(11, 2).FormulaA1.ShouldBe("VLOOKUP('Cálculo'!$B$10,'Tablas'!$A$5:$C$8,1,TRUE)");
        calculo.Cell(6, 2).Value.GetNumber().ShouldBe(500);
        calculo.Cell(7, 2).Value.GetNumber().ShouldBe(200);
    }

    [Fact]
    public void Una_formula_mal_hecha_se_explica_para_corregirla()
    {
        Should.Throw<UserFriendlyException>(() => Generar(Finiquito.Replace("sd * 15", "sdi * 15")))
            .Message.ShouldContain("«sdi»");
        Should.Throw<UserFriendlyException>(() => Generar(Finiquito.Replace("sd * 15", "B6 * 15")))
            .Message.ShouldContain("no celdas");
        Should.Throw<UserFriendlyException>(() => Generar(Finiquito.Replace("sd * 15 / 365", "sd * 15 / (dias - dias)")))
            .Message.ShouldContain("Aguinaldo proporcional");
        Should.Throw<UserFriendlyException>(() => Generar(Finiquito.Replace("\"id\":\"cuota\"", "\"id\":\"li\"")))
            .Message.ShouldContain("repetido");
    }

    [Fact]
    public void En_un_listado_el_total_que_cuadra_queda_como_suma()
    {
        const string texto = """
            | Concepto | Importe |
            |---|---|
            | Renta | $1,000.00 |
            | Luz | $250.50 |
            | Total | $1,250.50 |

            | Concepto | Importe |
            |---|---|
            | Renta | $1,000.00 |
            | Total | $9.00 |
            """;
        using var libro = new XLWorkbook(new MemoryStream(ArchivoDesdeTexto.Generar(FormatoDeArchivo.Excel, "x", texto).Contenido));
        var hoja = libro.Worksheets.First();
        hoja.Cell(4, 2).FormulaA1.ShouldBe("SUM(B2:B3)");
        // El que no cuadra se queda con el número que se dio.
        hoja.Cell(8, 2).HasFormula.ShouldBeFalse();
        hoja.Cell(8, 2).GetDouble().ShouldBe(9);
    }

    [Fact]
    public void El_pdf_y_el_word_llevan_el_documento_formal_con_firmas()
    {
        const string formal = """
            # FINIQUITO

            En Cuernavaca, Morelos, a ________, recibo la cantidad de $3,338.68 (tres mil trescientos treinta y ocho pesos 68/100 M.N.).

            [[firma: Nombre del trabajador | Trabajador]]
            [[firma: Representante | Patrón]]
            """;
        var pdf = ArchivoDesdeTexto.Generar(FormatoDeArchivo.Pdf, "x", "# Resumen\n\nTexto.", formal: formal).Contenido;
        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
        pdf.Length.ShouldBeGreaterThan(ArchivoDesdeTexto.Generar(FormatoDeArchivo.Pdf, "x", "# Resumen\n\nTexto.").Contenido.Length);

        var word = ArchivoDesdeTexto.Generar(FormatoDeArchivo.Word, "x", "# Resumen\n\nTexto.", formal: formal).Contenido;
        using var doc = WordprocessingDocument.Open(new MemoryStream(word), false);
        new OpenXmlValidator().Validate(doc).Select(e => e.Description).ShouldBeEmpty();
        var texto = doc.MainDocumentPart!.Document!.Body!.InnerText;
        texto.ShouldContain("Nombre del trabajador");
        texto.ShouldNotContain("[[firma");
        // Los espacios para llenar a mano no se pierden.
        texto.ShouldContain("a ________, recibo");
    }

    [Fact]
    public void Sin_calculo_no_cambia_nada()
    {
        CalculoDeArchivo.Leer(JsonDocument.Parse("""{"formato":"excel","contenido":"x"}""").RootElement).ShouldBeNull();
        Should.Throw<UserFriendlyException>(() => CalculoDeArchivo.Leer(JsonDocument.Parse("""{"calculo":{"hojas":[]}}""").RootElement));
        var ids = new List<string> { "salario_diario", "isr1", "B5" };
        ids.ShouldAllBe(i => CalculoDeArchivo.EsId(i));
        CalculoDeArchivo.EsId("1x").ShouldBeFalse();
        CalculoDeArchivo.EsId("VERDADERO").ShouldBeFalse();
    }
}
