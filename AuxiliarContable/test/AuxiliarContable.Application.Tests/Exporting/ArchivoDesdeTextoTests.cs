using System.IO;
using System.Linq;
using System.Text;
using AuxiliarContable.Exportaciones;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Shouldly;
using Xunit;

namespace AuxiliarContable.Exporting;

/// <summary>
/// Lo que escribe el asistente (títulos, listas, tablas markdown) como archivo. Sin base de datos: lo que puede salir
/// mal es que el archivo no abra (Word y PowerPoint son exigentes con su XML) o que una clave SAT se vuelva número.
/// </summary>
public class ArchivoDesdeTextoTests
{
    private const string Texto = """
        # Resumen de octubre

        Ingresos y gastos del mes, con **IVA** desglosado.

        ## Gastos

        | Concepto | Clave SAT | Importe | Tasa |
        |---|---|---:|---|
        | Foco LED 9 W | 39101600 | $1,234.50 | 16% |
        | Instalación eléctrica | 72151501 | (200.00) | 0% |

        ## Pendientes

        - Subir el estado de cuenta
        - Confirmar el régimen

        1. Primero esto
        2. Luego aquello
        """;

    [Theory]
    [InlineData("excel", "xlsx")]
    [InlineData("word", "docx")]
    [InlineData("powerpoint", "pptx")]
    [InlineData("pdf", "pdf")]
    [InlineData("csv", "csv")]
    public void Genera_cada_formato_con_su_nombre(string formato, string extension)
    {
        var archivo = ArchivoDesdeTexto.Generar(ArchivoDesdeTexto.Formato(formato)!.Value, "Gastos de octubre.xlsx", Texto);

        archivo.Nombre.ShouldBe($"Gastos de octubre.{extension}");
        archivo.Contenido.Length.ShouldBeGreaterThan(100);
    }

    [Fact]
    public void Word_y_PowerPoint_son_validos_para_Office()
    {
        var word = ArchivoDesdeTexto.Generar(FormatoDeArchivo.Word, "x", Texto).Contenido;
        using (var doc = WordprocessingDocument.Open(new MemoryStream(word), false))
        {
            new OpenXmlValidator().Validate(doc).Select(e => e.Description).ShouldBeEmpty();
            doc.MainDocumentPart!.Document!.Body!.InnerText.ShouldContain("Instalación eléctrica");
        }

        var ppt = ArchivoDesdeTexto.Generar(FormatoDeArchivo.PowerPoint, "x", Texto).Contenido;
        using (var doc = PresentationDocument.Open(new MemoryStream(ppt), false))
        {
            new OpenXmlValidator().Validate(doc).Select(e => $"{e.Path?.XPath}: {e.Description}").ShouldBeEmpty();
            // Una diapositiva por «##»; lo de antes del primero va en la del título general.
            doc.PresentationPart!.SlideParts.Count().ShouldBe(3);
        }
    }

    [Fact]
    public void En_Excel_los_importes_son_numeros_y_las_claves_texto()
    {
        var xlsx = ArchivoDesdeTexto.Generar(FormatoDeArchivo.Excel, "x", Texto).Contenido;
        using var libro = new XLWorkbook(new MemoryStream(xlsx));
        var hoja = libro.Worksheets.First();
        var celdas = hoja.CellsUsed().ToList();

        celdas.Single(c => c.GetString() == "39101600").DataType.ShouldBe(XLDataType.Text);
        celdas.Single(c => c.DataType == XLDataType.Number && c.GetDouble() == 1234.5).ShouldNotBeNull();
        celdas.Single(c => c.DataType == XLDataType.Number && c.GetDouble() == -200).ShouldNotBeNull();
        celdas.Single(c => c.DataType == XLDataType.Number && c.GetDouble() == 0.16).ShouldNotBeNull();
        celdas.ShouldNotContain(c => c.GetString().Contains("**"));
    }

    [Fact]
    public void El_pdf_es_pdf()
    {
        var pdf = ArchivoDesdeTexto.Generar(FormatoDeArchivo.Pdf, "x", Texto).Contenido;
        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
    }

    [Fact]
    public void El_csv_trae_las_tablas()
    {
        var csv = Encoding.UTF8.GetString(ArchivoDesdeTexto.Generar(FormatoDeArchivo.Csv, "x", Texto).Contenido);
        csv.ShouldContain("Concepto;Clave SAT;Importe;Tasa");
        csv.ShouldContain("Foco LED 9 W");
    }

    [Fact]
    public void Reconoce_tablas_y_formatos()
    {
        ArchivoDesdeTexto.TieneTabla(Texto).ShouldBeTrue();
        ArchivoDesdeTexto.TieneTabla("Solo texto | con una barra").ShouldBeFalse();
        ArchivoDesdeTexto.Formato("PPTX").ShouldBe(FormatoDeArchivo.PowerPoint);
        ArchivoDesdeTexto.Formato("rar").ShouldBeNull();
    }
}
