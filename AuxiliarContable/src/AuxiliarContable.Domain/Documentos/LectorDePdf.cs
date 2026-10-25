using System;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;

namespace AuxiliarContable.Documentos;

/// <summary>
/// El texto de un PDF, para leerlo con código antes de recurrir a la IA. Un PDF escaneado
/// (imágenes sin capa de texto) devuelve casi nada: ahí <see cref="TieneTexto"/> es false y
/// quien llama sabe que tiene que mandar el PDF al modelo.
/// </summary>
public static class LectorDePdf
{
    /// <summary>Por debajo de esto se considera que el PDF es una imagen escaneada.</summary>
    public const int MinimoDeCaracteres = 200;

    public static string Texto(byte[] pdf, int maxPaginas = 10)
    {
        try
        {
            using var documento = PdfDocument.Open(pdf);
            var texto = new StringBuilder();
            foreach (var pagina in documento.GetPages().Take(maxPaginas))
            {
                // Por palabras y no page.Text: page.Text pega palabras sin espacios cuando el
                // PDF las posiciona por separado (pasa en los del SAT).
                texto.AppendLine(string.Join(' ', pagina.GetWords().Select(p => p.Text)));
            }

            return texto.ToString();
        }
        catch (Exception ex) when (ex is PdfDocumentFormatException or InvalidOperationException or ArgumentException)
        {
            return string.Empty;
        }
    }

    public static bool TieneTexto(string texto) => texto.Count(char.IsLetterOrDigit) >= MinimoDeCaracteres;
}
