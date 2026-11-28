using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AuxiliarContable.Exportaciones;

public enum FormatoDeArchivo
{
    Excel,
    Pdf,
    Word,
    PowerPoint,
    Csv,
}

public sealed record ArchivoGenerado(string Nombre, string TipoMime, byte[] Contenido, IReadOnlyList<ResultadoCalculado>? Resultados = null);

/// <summary>
/// Convierte lo que escribe el asistente (títulos «#», párrafos, listas y tablas markdown) en un archivo:
/// Excel, PDF, Word, PowerPoint o CSV. Sin IA: el mismo texto da siempre el mismo archivo, así que no se
/// guarda; se vuelve a armar cada vez que alguien lo descarga.
/// </summary>
public static class ArchivoDesdeTexto
{
    /// <summary>Tope del texto que se convierte (el de una respuesta larga cabe de sobra).</summary>
    public const int MaxCaracteres = 60_000;

    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    static ArchivoDesdeTexto()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static FormatoDeArchivo? Formato(string? texto) => (texto ?? "").Trim().ToLowerInvariant() switch
    {
        "excel" or "xlsx" or "xls" => FormatoDeArchivo.Excel,
        "pdf" => FormatoDeArchivo.Pdf,
        "word" or "docx" or "doc" => FormatoDeArchivo.Word,
        "powerpoint" or "pptx" or "ppt" or "presentacion" or "presentación" => FormatoDeArchivo.PowerPoint,
        "csv" => FormatoDeArchivo.Csv,
        _ => null,
    };

    public static string Extension(FormatoDeArchivo f) => f switch
    {
        FormatoDeArchivo.Excel => "xlsx",
        FormatoDeArchivo.Pdf => "pdf",
        FormatoDeArchivo.Word => "docx",
        FormatoDeArchivo.PowerPoint => "pptx",
        _ => "csv",
    };

    /// <summary>¿Trae al menos una tabla? (para ofrecer «Descargar en Excel» bajo una respuesta).</summary>
    public static bool TieneTabla(string? texto) => Leer(texto ?? "").OfType<Tabla>().Any();

    /// <summary>El nombre con que se descarga: el que se pidió o, si no, el primer título.</summary>
    public static string NombrePara(FormatoDeArchivo formato, string? nombre, string contenido)
    {
        var titulo = string.IsNullOrWhiteSpace(nombre) ? Leer(contenido.Length > MaxCaracteres ? contenido[..MaxCaracteres] : contenido).OfType<Titulo>().FirstOrDefault()?.Texto : null;
        return $"{NombreDeArchivo(string.IsNullOrWhiteSpace(nombre) ? titulo ?? "archivo" : nombre)}.{Extension(formato)}";
    }

    /// <param name="calculo">En Excel: el desglose con fórmulas (resumen ligado, hoja de cálculo y tablas).</param>
    /// <param name="formal">En PDF y Word: el documento formal que va en hoja aparte después del resumen (p. ej. el finiquito para firmar).</param>
    public static ArchivoGenerado Generar(FormatoDeArchivo formato, string? nombre, string contenido, CalculoDeArchivo? calculo = null, string? formal = null)
    {
        var piezas = Leer(contenido.Length > MaxCaracteres ? contenido[..MaxCaracteres] : contenido);
        var titulo = piezas.OfType<Titulo>().FirstOrDefault()?.Texto;
        var limpio = NombreDeArchivo(string.IsNullOrWhiteSpace(nombre) ? titulo ?? "archivo" : nombre);
        var archivo = $"{limpio}.{Extension(formato)}";
        var piezasFormales = string.IsNullOrWhiteSpace(formal) ? [] : Leer(formal.Length > MaxCaracteres ? formal[..MaxCaracteres] : formal);
        if (formato == FormatoDeArchivo.Excel && calculo is not null)
        {
            var (libro, resultados) = calculo.Excel(titulo ?? limpio, piezas);
            return new(archivo, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", libro, resultados);
        }

        return formato switch
        {
            FormatoDeArchivo.Excel => new(archivo, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Excel(piezas, limpio)),
            FormatoDeArchivo.Pdf => new(archivo, "application/pdf", Pdf(piezas, piezasFormales)),
            FormatoDeArchivo.Word => new(archivo, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", Word(piezas, piezasFormales)),
            FormatoDeArchivo.PowerPoint => new(archivo, "application/vnd.openxmlformats-officedocument.presentationml.presentation", PowerPoint(piezas, titulo ?? limpio)),
            _ => new(archivo, "text/csv", Csv(piezas)),
        };
    }

    // ---- Lectura del texto -------------------------------------------------------------------------

    internal abstract record Pieza;

    internal sealed record Titulo(int Nivel, string Texto) : Pieza;

    internal sealed record Parrafo(string Texto) : Pieza;

    internal sealed record Lista(List<string> Elementos, bool Numerada) : Pieza;

    internal sealed record Tabla(List<string> Encabezados, List<List<string>> Filas) : Pieza;

    /// <summary>«[[firma: Nombre | Puesto]]»: una línea para firmar con el nombre (y el puesto) debajo.</summary>
    internal sealed record Firma(List<string> Lineas) : Pieza;

    private sealed record SaltoDePagina : Pieza;

    private static readonly Regex DeFirma = new(@"^\[\[\s*firma\s*:\s*(.+?)\s*\]\]$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Numerado = new(@"^\d{1,3}[.)]\s+", RegexOptions.Compiled);
    private static readonly Regex Separador = new(@"^\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?$", RegexOptions.Compiled);
    private static readonly Regex DobleGuion = new(@"(?<!_)__(?!_)", RegexOptions.Compiled);
    private static readonly Regex Enlace = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);

    internal static List<Pieza> Leer(string texto)
    {
        var piezas = new List<Pieza>();
        var lineas = texto.Replace("\r\n", "\n").Split('\n');
        var parrafo = new List<string>();

        void CerrarParrafo()
        {
            if (parrafo.Count > 0)
            {
                piezas.Add(new Parrafo(string.Join(" ", parrafo)));
                parrafo.Clear();
            }
        }

        for (var i = 0; i < lineas.Length; i++)
        {
            var l = lineas[i].Trim();
            if (l.Length == 0 || l is "---" or "***")
            {
                CerrarParrafo();
                continue;
            }

            if (DeFirma.Match(l) is { Success: true } firma)
            {
                CerrarParrafo();
                piezas.Add(new Firma(firma.Groups[1].Value.Split('|').Select(x => Limpio(x)).Where(x => x.Length > 0).ToList()));
                continue;
            }

            if (l.StartsWith('#'))
            {
                CerrarParrafo();
                var nivel = l.TakeWhile(c => c == '#').Count();
                piezas.Add(new Titulo(Math.Min(nivel, 3), Limpio(l[nivel..])));
                continue;
            }

            if (l.StartsWith('|') && i + 1 < lineas.Length && Separador.IsMatch(lineas[i + 1].Trim()))
            {
                CerrarParrafo();
                var encabezados = Celdas(l);
                var filas = new List<List<string>>();
                i += 2;
                while (i < lineas.Length && lineas[i].Trim().StartsWith('|'))
                {
                    var celdas = Celdas(lineas[i].Trim());
                    while (celdas.Count < encabezados.Count)
                    {
                        celdas.Add("");
                    }

                    filas.Add(celdas.Take(Math.Max(encabezados.Count, 1)).ToList());
                    i++;
                }

                i--;
                piezas.Add(new Tabla(encabezados, filas));
                continue;
            }

            var esVineta = l.StartsWith("- ") || l.StartsWith("* ") || l.StartsWith("• ");
            var esNumero = Numerado.IsMatch(l);
            if (esVineta || esNumero)
            {
                CerrarParrafo();
                var elemento = Limpio(esVineta ? l[2..] : Numerado.Replace(l, ""));
                if (piezas.LastOrDefault() is Lista previa && previa.Numerada == esNumero)
                {
                    previa.Elementos.Add(elemento);
                }
                else
                {
                    piezas.Add(new Lista([elemento], esNumero));
                }

                continue;
            }

            parrafo.Add(Limpio(l));
        }

        CerrarParrafo();
        return piezas;
    }

    private static List<string> Celdas(string linea)
    {
        var t = linea.Trim();
        if (t.StartsWith('|'))
        {
            t = t[1..];
        }

        if (t.EndsWith('|'))
        {
            t = t[..^1];
        }

        return t.Split('|').Select(c => Limpio(c)).ToList();
    }

    /// <summary>Sin marcas de formato: **negritas**, `código` y enlaces quedan como texto.</summary>
    private static string Limpio(string texto)
    {
        var t = Enlace.Replace(texto, m => m.Groups[1].Value == m.Groups[2].Value ? m.Groups[1].Value : $"{m.Groups[1].Value} ({m.Groups[2].Value})");
        // «__negritas__» se quitan, pero «________» (espacio para llenar a mano) se queda.
        return DobleGuion.Replace(t.Replace("**", ""), "").Replace("`", "").Trim();
    }

    private static string NombreDeArchivo(string nombre)
    {
        var invalidos = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var limpio = new string(Limpio(nombre).Select(c => invalidos.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
        limpio = Regex.Replace(limpio, @"\s+", " ").Trim().TrimEnd('.');
        // Si el modelo ya puso la extensión, no se repite.
        limpio = Regex.Replace(limpio, @"\.(xlsx|xls|pdf|docx|doc|pptx|ppt|csv)$", "", RegexOptions.IgnoreCase);
        if (limpio.Length > 80)
        {
            limpio = limpio[..80].Trim();
        }

        return limpio.Length == 0 ? "archivo" : limpio;
    }

    /// <summary>«$1,234.50», «-16%», «2,000» como número; claves y folios («39101600», «0012») se quedan como texto.</summary>
    internal static bool ComoNumero(string celda, out double valor, out bool porcentaje, out bool moneda)
    {
        valor = 0;
        var t = celda.Trim();
        porcentaje = t.EndsWith('%');
        moneda = t.Contains('$');
        var sinSignos = t.Replace("$", "").Replace("%", "").Replace("MXN", "").Replace(" ", "").Trim();
        if (sinSignos.Length == 0 || sinSignos.Length > 20)
        {
            return false;
        }

        var negativo = sinSignos.StartsWith('(') && sinSignos.EndsWith(')');
        if (negativo)
        {
            sinSignos = sinSignos[1..^1];
        }

        if (!Regex.IsMatch(sinSignos, @"^-?(\d{1,3}(,\d{3})+|\d+)(\.\d+)?$"))
        {
            return false;
        }

        var digitos = sinSignos.TrimStart('-');
        var soloEntero = !digitos.Contains(',') && !digitos.Contains('.');
        if (soloEntero && !porcentaje && !moneda && (digitos.Length > 7 || (digitos.Length > 1 && digitos.StartsWith('0'))))
        {
            return false;
        }

        if (!double.TryParse(sinSignos.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out valor))
        {
            return false;
        }

        if (negativo)
        {
            valor = -valor;
        }

        if (porcentaje)
        {
            valor /= 100;
        }

        return true;
    }

    // ---- Excel -------------------------------------------------------------------------------------

    private static byte[] Excel(List<Pieza> piezas, string nombre)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add(NombreDeHoja(nombre));
        var fila = 1;
        var maxColumnas = 1;
        foreach (var pieza in piezas)
        {
            switch (pieza)
            {
                case Titulo t:
                    hoja.Cell(fila, 1).Value = t.Texto;
                    hoja.Cell(fila, 1).Style.Font.SetBold().Font.SetFontSize(t.Nivel == 1 ? 15 : 12.5);
                    fila += 1;
                    break;
                case Parrafo p:
                    hoja.Cell(fila, 1).Value = p.Texto;
                    fila += 2;
                    break;
                case Lista l:
                    for (var i = 0; i < l.Elementos.Count; i++)
                    {
                        hoja.Cell(fila, 1).Value = (l.Numerada ? $"{i + 1}. " : "• ") + l.Elementos[i];
                        fila++;
                    }

                    fila++;
                    break;
                case Tabla t:
                    maxColumnas = Math.Max(maxColumnas, t.Encabezados.Count);
                    for (var c = 0; c < t.Encabezados.Count; c++)
                    {
                        var celda = hoja.Cell(fila, c + 1);
                        celda.Value = t.Encabezados[c];
                        celda.Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                            .Fill.SetBackgroundColor(XLColor.FromHtml("#1F3A5F"))
                            .Alignment.SetWrapText(true);
                    }

                    var inicio = fila;
                    fila++;
                    foreach (var f in t.Filas)
                    {
                        for (var c = 0; c < f.Count; c++)
                        {
                            var celda = hoja.Cell(fila, c + 1);
                            if (ComoNumero(f[c], out var v, out var porcentaje, out var moneda))
                            {
                                celda.Value = v;
                                celda.Style.NumberFormat.Format = porcentaje ? "0.00%" : moneda ? "$#,##0.00;-$#,##0.00" : v % 1 == 0 ? "#,##0" : "#,##0.00";
                            }
                            else
                            {
                                celda.Value = f[c];
                                celda.Style.Alignment.SetWrapText(true);
                            }
                        }

                        fila++;
                    }

                    TotalConFormula(hoja, t, inicio + 1, fila - 1);
                    if (t.Filas.Count > 0)
                    {
                        hoja.Range(inicio, 1, fila - 1, Math.Max(t.Encabezados.Count, 1)).Style
                            .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                            .Border.SetInsideBorder(XLBorderStyleValues.Hair);
                    }

                    fila++;
                    break;
            }
        }

        for (var c = 1; c <= maxColumnas; c++)
        {
            hoja.Column(c).AdjustToContents(1, fila, 8, 60);
        }

        using var ms = new MemoryStream();
        libro.SaveAs(ms);
        return ms.ToArray();
    }

    private static readonly Regex DeTotal = new(@"^(gran\s+)?(total|totales|suma)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Si el último renglón de la tabla es «Total» y cada importe es la suma de los de arriba, queda como SUMA(): al
    /// cambiar un renglón en Excel, el total se mueve. Si no cuadra, se deja el número que se dio (no se corrige a escondidas).
    /// </summary>
    private static void TotalConFormula(IXLWorksheet hoja, Tabla t, int primera, int ultima)
    {
        if (t.Filas.Count < 2 || !DeTotal.IsMatch(t.Filas[^1].FirstOrDefault() ?? ""))
        {
            return;
        }

        for (var c = 1; c < t.Filas[^1].Count; c++)
        {
            if (!ComoNumero(t.Filas[^1][c], out var total, out var porcentaje, out _) || porcentaje)
            {
                continue;
            }

            var suma = 0.0;
            var alguno = false;
            foreach (var f in t.Filas.Take(t.Filas.Count - 1))
            {
                if (c < f.Count && ComoNumero(f[c], out var v, out var esPorcentaje, out _) && !esPorcentaje)
                {
                    suma += v;
                    alguno = true;
                }
            }

            if (alguno && Math.Abs(suma - total) < 0.015)
            {
                var letra = hoja.Column(c + 1).ColumnLetter();
                hoja.Cell(ultima, c + 1).FormulaA1 = $"SUM({letra}{primera}:{letra}{ultima - 1})";
            }
        }

        hoja.Row(ultima).Style.Font.SetBold();
    }

    private static string NombreDeHoja(string nombre)
    {
        var limpio = new string(nombre.Where(c => c is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\')).ToArray()).Trim();
        return limpio.Length == 0 ? "Hoja1" : limpio.Length > 31 ? limpio[..31] : limpio;
    }

    // ---- CSV ---------------------------------------------------------------------------------------

    /// <summary>
    /// Las tablas, una tras otra (separadas por un renglón vacío); sin tablas, el texto renglón por renglón. Lo escribe
    /// <see cref="AuxiliarContable.Exporting.EscritorCsv"/>: BOM para Excel y celdas que parecen fórmula neutralizadas.
    /// </summary>
    private static byte[] Csv(List<Pieza> piezas)
    {
        var tablas = piezas.OfType<Tabla>().ToList();
        if (tablas.Count == 0)
        {
            return AuxiliarContable.Exporting.EscritorCsv.Generar(["Texto"], piezas.SelectMany(TextoPlano).Select(l => new[] { l }));
        }

        var bom = Encoding.UTF8.GetPreamble().Length;
        var salida = new List<byte>();
        for (var i = 0; i < tablas.Count; i++)
        {
            var csv = AuxiliarContable.Exporting.EscritorCsv.Generar(tablas[i].Encabezados, tablas[i].Filas);
            if (i > 0)
            {
                salida.AddRange([13, 10]);
                salida.AddRange(csv.Skip(bom));
            }
            else
            {
                salida.AddRange(csv);
            }
        }

        return salida.ToArray();
    }

    private static IEnumerable<string> TextoPlano(Pieza p) => p switch
    {
        Titulo t => [t.Texto],
        Parrafo x => [x.Texto],
        Lista l => l.Elementos.Select((e, i) => (l.Numerada ? $"{i + 1}. " : "• ") + e),
        Tabla t => new[] { string.Join(" | ", t.Encabezados) }.Concat(t.Filas.Select(f => string.Join(" | ", f))),
        Firma f => ["_____________________________", .. f.Lineas],
        _ => [],
    };

    // ---- PDF ---------------------------------------------------------------------------------------

    private static byte[] Pdf(List<Pieza> piezas, List<Pieza> formales)
    {
        var horizontal = piezas.OfType<Tabla>().Any(t => t.Encabezados.Count > 5);
        return Document.Create(doc =>
        {
            doc.Page(pagina =>
            {
                Pagina(pagina, horizontal);
                pagina.Content().Column(col => Pintar(col, piezas, formal: false));
            });

            // El documento formal (p. ej. el finiquito para firmar) en sus propias hojas, vertical y sin colores.
            if (formales.Count > 0)
            {
                doc.Page(pagina =>
                {
                    Pagina(pagina, horizontal: false);
                    pagina.Content().Column(col => Pintar(col, formales, formal: true));
                });
            }
        }).GeneratePdf();
    }

    private static void Pagina(PageDescriptor pagina, bool horizontal)
    {
        pagina.Size(horizontal ? PageSizes.Letter.Landscape() : PageSizes.Letter);
        pagina.Margin(40);
        pagina.DefaultTextStyle(x => x.FontSize(10).FontColor("#222222"));
        pagina.Footer().AlignCenter().Text(x =>
        {
            x.DefaultTextStyle(s => s.FontSize(8).FontColor("#777777"));
            x.CurrentPageNumber();
            x.Span(" de ");
            x.TotalPages();
        });
    }

    private static void Pintar(ColumnDescriptor col, List<Pieza> piezas, bool formal)
    {
        col.Spacing(formal ? 8 : 6);
        var color = formal ? "#111111" : "#1F3A5F";
        for (var n = 0; n < piezas.Count; n++)
        {
            switch (piezas[n])
            {
                case Titulo t:
                    var titulo = col.Item().PaddingTop(t.Nivel == 1 ? 0 : 6);
                    (formal && t.Nivel == 1 ? titulo.AlignCenter() : titulo).Text(t.Texto).Bold()
                        .FontSize(t.Nivel == 1 ? (formal ? 14 : 17) : t.Nivel == 2 ? (formal ? 12 : 13.5f) : 11.5f).FontColor(color);
                    break;
                case Parrafo p:
                    if (formal)
                    {
                        col.Item().Text(p.Texto).LineHeight(1.45f).Justify();
                    }
                    else
                    {
                        col.Item().Text(p.Texto).LineHeight(1.3f);
                    }

                    break;
                case Lista l:
                    for (var i = 0; i < l.Elementos.Count; i++)
                    {
                        var marca = l.Numerada ? $"{i + 1}." : "•";
                        var texto = l.Elementos[i];
                        col.Item().PaddingLeft(8).Row(r =>
                        {
                            r.ConstantItem(16).Text(marca);
                            r.RelativeItem().Text(texto).LineHeight(1.25f);
                        });
                    }

                    break;
                case Tabla t:
                    col.Item().PaddingVertical(4).Table(tabla =>
                    {
                        var columnas = Math.Max(t.Encabezados.Count, 1);
                        tabla.ColumnsDefinition(c =>
                        {
                            for (var i = 0; i < columnas; i++)
                            {
                                c.RelativeColumn();
                            }
                        });
                        tabla.Header(h =>
                        {
                            foreach (var e in t.Encabezados)
                            {
                                var celda = h.Cell().Background(formal ? "#E6E6E6" : "#1F3A5F").Padding(4).Text(e).Bold().FontSize(9);
                                if (!formal)
                                {
                                    celda.FontColor(Colors.White);
                                }
                            }
                        });
                        var total = t.Filas.Count >= 2 && DeTotal.IsMatch(t.Filas[^1].FirstOrDefault() ?? "");
                        for (var i = 0; i < t.Filas.Count; i++)
                        {
                            foreach (var celda in t.Filas[i])
                            {
                                var numero = ComoNumero(celda, out _, out _, out _);
                                var c = tabla.Cell().BorderBottom(0.5f).BorderColor("#D5DBE3").Padding(4);
                                var texto = (numero ? c.AlignRight() : c).Text(celda).FontSize(9);
                                if (total && i == t.Filas.Count - 1)
                                {
                                    texto.Bold();
                                }
                            }
                        }
                    });
                    break;
                case Firma:
                    // Las firmas seguidas van lado a lado (dos por renglón).
                    var firmas = new List<Firma>();
                    while (n < piezas.Count && piezas[n] is Firma f)
                    {
                        firmas.Add(f);
                        n++;
                    }

                    n--;
                    foreach (var par in firmas.Chunk(2))
                    {
                        col.Item().PaddingTop(46).Row(r =>
                        {
                            r.Spacing(40);
                            foreach (var f in par)
                            {
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().BorderTop(0.8f).BorderColor("#222222");
                                    for (var i = 0; i < f.Lineas.Count; i++)
                                    {
                                        var linea = c.Item().PaddingTop(i == 0 ? 4 : 0).AlignCenter().Text(f.Lineas[i]).FontSize(i == 0 ? 10 : 9);
                                        if (i == 0)
                                        {
                                            linea.Bold();
                                        }
                                    }
                                });
                            }

                            if (par.Length == 1 && firmas.Count > 1)
                            {
                                r.RelativeItem();
                            }
                        });
                    }

                    break;
            }
        }
    }

    // ---- Word --------------------------------------------------------------------------------------

    private static byte[] Word(List<Pieza> piezas, List<Pieza> formales)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var principal = doc.AddMainDocumentPart();
            var cuerpo = new W.Body();
            principal.Document = new W.Document(cuerpo);

            // El documento formal va después de un salto de página.
            var todas = formales.Count == 0 ? piezas : [.. piezas, new SaltoDePagina(), .. formales];
            foreach (var pieza in todas)
            {
                switch (pieza)
                {
                    case Titulo t:
                        cuerpo.Append(ParrafoWord(t.Texto, negrita: true, tamano: t.Nivel == 1 ? 32 : t.Nivel == 2 ? 26 : 23, color: "1F3A5F", antes: 200));
                        break;
                    case Parrafo p:
                        cuerpo.Append(ParrafoWord(p.Texto));
                        break;
                    case Lista l:
                        for (var i = 0; i < l.Elementos.Count; i++)
                        {
                            cuerpo.Append(ParrafoWord((l.Numerada ? $"{i + 1}. " : "• ") + l.Elementos[i], sangria: 360, despues: 60));
                        }

                        break;
                    case Tabla t:
                        cuerpo.Append(TablaWord(t));
                        cuerpo.Append(ParrafoWord(""));
                        break;
                    case Firma f:
                        cuerpo.Append(ParrafoWord("", antes: 600, despues: 0));
                        cuerpo.Append(ParrafoWord("______________________________", despues: 0));
                        for (var i = 0; i < f.Lineas.Count; i++)
                        {
                            cuerpo.Append(ParrafoWord(f.Lineas[i], negrita: i == 0, despues: i == f.Lineas.Count - 1 ? 200 : 0));
                        }

                        break;
                    case SaltoDePagina:
                        cuerpo.Append(new W.Paragraph(new W.Run(new W.Break { Type = W.BreakValues.Page })));
                        break;
                }
            }

            cuerpo.Append(new W.SectionProperties(
                new W.PageSize { Width = 12240, Height = 15840 },
                new W.PageMargin { Top = 1134, Bottom = 1134, Left = 1134, Right = 1134, Header = 708, Footer = 708, Gutter = 0 }));
            principal.Document.Save();
        }

        return ms.ToArray();
    }

    private static W.Paragraph ParrafoWord(string texto, bool negrita = false, int tamano = 21, string? color = null, int antes = 0,
        int despues = 120, int sangria = 0)
    {
        var propiedades = new W.RunProperties(new W.RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" });
        if (negrita)
        {
            propiedades.Append(new W.Bold());
        }

        if (color is not null)
        {
            propiedades.Append(new W.Color { Val = color });
        }

        propiedades.Append(new W.FontSize { Val = tamano.ToString(CultureInfo.InvariantCulture) });
        var formato = new W.ParagraphProperties(new W.SpacingBetweenLines
        {
            Before = antes.ToString(CultureInfo.InvariantCulture),
            After = despues.ToString(CultureInfo.InvariantCulture),
        });
        if (sangria > 0)
        {
            formato.Append(new W.Indentation { Left = sangria.ToString(CultureInfo.InvariantCulture) });
        }

        return new W.Paragraph(formato, new W.Run(propiedades, new W.Text(texto) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static W.Table TablaWord(Tabla t)
    {
        W.TableCell Celda(string texto, bool encabezado)
        {
            var parrafo = ParrafoWord(texto, negrita: encabezado, tamano: 19, color: encabezado ? "FFFFFF" : null, despues: 0);
            if (!encabezado && ComoNumero(texto, out _, out _, out _))
            {
                parrafo.ParagraphProperties!.Append(new W.Justification { Val = W.JustificationValues.Right });
            }

            var propiedades = new W.TableCellProperties();
            if (encabezado)
            {
                propiedades.Append(new W.Shading { Val = W.ShadingPatternValues.Clear, Color = "auto", Fill = "1F3A5F" });
            }

            return new W.TableCell(propiedades, parrafo);
        }

        W.BorderType Borde<TB>() where TB : W.BorderType, new() =>
            new TB { Val = W.BorderValues.Single, Size = 4, Color = "B7C2D0" };

        var tabla = new W.Table(new W.TableProperties(
            new W.TableWidth { Type = W.TableWidthUnitValues.Pct, Width = "5000" },
            new W.TableBorders(Borde<W.TopBorder>(), Borde<W.LeftBorder>(), Borde<W.BottomBorder>(), Borde<W.RightBorder>(),
                Borde<W.InsideHorizontalBorder>(), Borde<W.InsideVerticalBorder>()),
            new W.TableCellMarginDefault(new W.TopMargin { Width = "60", Type = W.TableWidthUnitValues.Dxa },
                new W.BottomMargin { Width = "60", Type = W.TableWidthUnitValues.Dxa })));
        var columnas = Math.Max(t.Encabezados.Count, 1);
        tabla.Append(new W.TableGrid(Enumerable.Range(0, columnas).Select(_ => new W.GridColumn { Width = (9972 / columnas).ToString(CultureInfo.InvariantCulture) })));
        var encabezado = new W.TableRow(new W.TableRowProperties(new W.TableHeader()));
        encabezado.Append(t.Encabezados.Select(e => Celda(e, true)));
        tabla.Append(encabezado);
        foreach (var f in t.Filas)
        {
            tabla.Append(new W.TableRow(f.Select(c => Celda(c, false))));
        }

        return tabla;
    }

    // ---- PowerPoint --------------------------------------------------------------------------------

    private const long AnchoDiapositiva = 12192000;
    private const long AltoDiapositiva = 6858000;

    /// <summary>Una diapositiva por título «##» (o por «#» si no hay de segundo nivel): su texto, listas y tablas debajo.</summary>
    private static byte[] PowerPoint(List<Pieza> piezas, string tituloGeneral)
    {
        var nivelDeCorte = piezas.OfType<Titulo>().Any(t => t.Nivel == 2) ? 2 : 1;
        var diapositivas = new List<(string Titulo, List<Pieza> Contenido)>();
        foreach (var pieza in piezas)
        {
            if (pieza is Titulo t && t.Nivel <= nivelDeCorte)
            {
                diapositivas.Add((t.Texto, []));
            }
            else
            {
                if (diapositivas.Count == 0)
                {
                    diapositivas.Add((tituloGeneral, []));
                }

                diapositivas[^1].Contenido.Add(pieza);
            }
        }

        if (diapositivas.Count == 0)
        {
            diapositivas.Add((tituloGeneral, []));
        }

        using var ms = new MemoryStream();
        using (var doc = PresentationDocument.Create(ms, PresentationDocumentType.Presentation))
        {
            var presentacion = doc.AddPresentationPart();
            presentacion.Presentation = new P.Presentation();

            var maestro = presentacion.AddNewPart<SlideMasterPart>("rId1");
            var tema = maestro.AddNewPart<ThemePart>("rId5");
            tema.Theme = Tema();
            var diseno = maestro.AddNewPart<SlideLayoutPart>("rId1");
            diseno.SlideLayout = new P.SlideLayout(
                new P.CommonSlideData(ArbolVacio()),
                new P.ColorMapOverride(new A.MasterColorMapping()))
            { Type = P.SlideLayoutValues.Blank };
            diseno.AddPart(maestro);
            maestro.SlideMaster = new P.SlideMaster(
                new P.CommonSlideData(ArbolVacio()),
                new P.ColorMap
                {
                    Background1 = A.ColorSchemeIndexValues.Light1, Text1 = A.ColorSchemeIndexValues.Dark1,
                    Background2 = A.ColorSchemeIndexValues.Light2, Text2 = A.ColorSchemeIndexValues.Dark2,
                    Accent1 = A.ColorSchemeIndexValues.Accent1, Accent2 = A.ColorSchemeIndexValues.Accent2,
                    Accent3 = A.ColorSchemeIndexValues.Accent3, Accent4 = A.ColorSchemeIndexValues.Accent4,
                    Accent5 = A.ColorSchemeIndexValues.Accent5, Accent6 = A.ColorSchemeIndexValues.Accent6,
                    Hyperlink = A.ColorSchemeIndexValues.Hyperlink, FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink,
                },
                new P.SlideLayoutIdList(new P.SlideLayoutId { Id = 2147483649U, RelationshipId = "rId1" }));

            var ids = new P.SlideIdList();
            uint id = 256;
            var n = 0;
            foreach (var (titulo, contenido) in diapositivas)
            {
                n++;
                var parte = presentacion.AddNewPart<SlidePart>($"rId{n + 10}");
                parte.Slide = Diapositiva(titulo, contenido);
                parte.AddPart(diseno);
                ids.Append(new P.SlideId { Id = id++, RelationshipId = $"rId{n + 10}" });
            }

            presentacion.Presentation.Append(
                new P.SlideMasterIdList(new P.SlideMasterId { Id = 2147483648U, RelationshipId = "rId1" }),
                ids,
                new P.SlideSize { Cx = (int)AnchoDiapositiva, Cy = (int)AltoDiapositiva },
                new P.NotesSize { Cx = 6858000, Cy = 9144000 });
            presentacion.Presentation.Save();
        }

        return ms.ToArray();
    }

    private static P.ShapeTree ArbolVacio() => new(
        new P.NonVisualGroupShapeProperties(
            new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
            new P.NonVisualGroupShapeDrawingProperties(),
            new P.ApplicationNonVisualDrawingProperties()),
        new P.GroupShapeProperties(new A.TransformGroup(
            new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 0, Cy = 0 },
            new A.ChildOffset { X = 0, Y = 0 }, new A.ChildExtents { Cx = 0, Cy = 0 })));

    private static P.Slide Diapositiva(string titulo, List<Pieza> contenido)
    {
        var arbol = ArbolVacio();
        uint id = 2;
        const long margen = 457200;
        arbol.Append(CuadroDeTexto(id++, margen, 300000, AnchoDiapositiva - (2 * margen), 900000,
            [ParrafoPpt(titulo, 3200, negrita: true, color: "1F3A5F")]));

        var parrafos = new List<A.Paragraph>();
        var tablas = new List<Tabla>();
        foreach (var pieza in contenido)
        {
            switch (pieza)
            {
                case Titulo t:
                    parrafos.Add(ParrafoPpt(t.Texto, 2000, negrita: true, color: "1F3A5F"));
                    break;
                case Parrafo p:
                    parrafos.Add(ParrafoPpt(p.Texto, 1800));
                    break;
                case Lista l:
                    parrafos.AddRange(l.Elementos.Select((e, i) => ParrafoPpt((l.Numerada ? $"{i + 1}. " : "• ") + e, 1800)));
                    break;
                case Tabla t:
                    tablas.Add(t);
                    break;
            }
        }

        var y = 1300000L;
        if (parrafos.Count > 0)
        {
            var alto = tablas.Count > 0 ? 1600000L : AltoDiapositiva - y - 400000;
            arbol.Append(CuadroDeTexto(id++, margen, y, AnchoDiapositiva - (2 * margen), alto, parrafos));
            y += alto + 150000;
        }

        foreach (var t in tablas)
        {
            var filas = t.Filas.Take(14).ToList();
            var altoFila = 330000L;
            arbol.Append(TablaPpt(id++, t.Encabezados, filas, margen, y, AnchoDiapositiva - (2 * margen), altoFila));
            y += altoFila * (filas.Count + 1) + 200000;
        }

        return new P.Slide(new P.CommonSlideData(arbol), new P.ColorMapOverride(new A.MasterColorMapping()));
    }

    private static A.Paragraph ParrafoPpt(string texto, int tamano, bool negrita = false, string color = "222222", bool derecha = false)
    {
        var propiedades = new A.RunProperties(new A.SolidFill(new A.RgbColorModelHex { Val = color }))
        {
            Language = "es-MX",
            FontSize = tamano,
            Bold = negrita,
            Dirty = false,
        };
        var parrafo = new A.Paragraph();
        if (derecha)
        {
            parrafo.Append(new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Right });
        }

        parrafo.Append(new A.Run(propiedades, new A.Text(texto)));
        return parrafo;
    }

    private static P.Shape CuadroDeTexto(uint id, long x, long y, long ancho, long alto, IEnumerable<A.Paragraph> parrafos)
    {
        var cuerpo = new P.TextBody(
            new A.BodyProperties(new A.NormalAutoFit()) { Wrap = A.TextWrappingValues.Square },
            new A.ListStyle());
        cuerpo.Append(parrafos);
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = $"Texto {id}" },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }) { TextBox = true },
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(new A.Offset { X = x, Y = y }, new A.Extents { Cx = ancho, Cy = alto }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.NoFill()),
            cuerpo);
    }

    private static P.GraphicFrame TablaPpt(uint id, List<string> encabezados, List<List<string>> filas, long x, long y, long ancho, long altoFila)
    {
        var columnas = Math.Max(encabezados.Count, 1);
        var anchoColumna = ancho / columnas;
        var tamano = columnas > 5 ? 1100 : 1300;

        A.TableCell Celda(string texto, bool encabezado)
        {
            var propiedades = new A.TableCellProperties();
            if (encabezado)
            {
                propiedades.Append(new A.SolidFill(new A.RgbColorModelHex { Val = "1F3A5F" }));
            }

            var derecha = !encabezado && ComoNumero(texto, out _, out _, out _);
            return new A.TableCell(
                new A.TextBody(new A.BodyProperties(), new A.ListStyle(),
                    ParrafoPpt(texto, tamano, negrita: encabezado, color: encabezado ? "FFFFFF" : "222222", derecha: derecha)),
                propiedades);
        }

        var tabla = new A.Table(
            new A.TableProperties { FirstRow = true, BandRow = true },
            new A.TableGrid(Enumerable.Range(0, columnas).Select(_ => new A.GridColumn { Width = anchoColumna })));
        tabla.Append(new A.TableRow(encabezados.Select(e => Celda(e, true))) { Height = altoFila });
        foreach (var f in filas)
        {
            tabla.Append(new A.TableRow(f.Select(c => Celda(c, false))) { Height = altoFila });
        }

        return new P.GraphicFrame(
            new P.NonVisualGraphicFrameProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = $"Tabla {id}" },
                new P.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.Transform(new A.Offset { X = x, Y = y }, new A.Extents { Cx = ancho, Cy = altoFila * (filas.Count + 1) }),
            new A.Graphic(new A.GraphicData(tabla) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/table" }));
    }

    private static A.Theme Tema()
    {
        A.SolidFill Solido(string hex) => new(new A.RgbColorModelHex { Val = hex });
        A.SolidFill DelEsquema() => new(new A.SchemeColor { Val = A.SchemeColorValues.PhColor });
        A.Outline Linea(int ancho) => new(DelEsquema()) { Width = ancho, CapType = A.LineCapValues.Flat, CompoundLineType = A.CompoundLineValues.Single, Alignment = A.PenAlignmentValues.Center };
        A.EffectStyle SinEfecto() => new(new A.EffectList());

        return new A.Theme(
            new A.ThemeElements(
                new A.ColorScheme(
                    new A.Dark1Color(new A.SystemColor { Val = A.SystemColorValues.WindowText, LastColor = "000000" }),
                    new A.Light1Color(new A.SystemColor { Val = A.SystemColorValues.Window, LastColor = "FFFFFF" }),
                    new A.Dark2Color(new A.RgbColorModelHex { Val = "1F3A5F" }),
                    new A.Light2Color(new A.RgbColorModelHex { Val = "EEF2F7" }),
                    new A.Accent1Color(new A.RgbColorModelHex { Val = "1F3A5F" }),
                    new A.Accent2Color(new A.RgbColorModelHex { Val = "2E7D6B" }),
                    new A.Accent3Color(new A.RgbColorModelHex { Val = "C27C0E" }),
                    new A.Accent4Color(new A.RgbColorModelHex { Val = "8E3B46" }),
                    new A.Accent5Color(new A.RgbColorModelHex { Val = "5B6B7F" }),
                    new A.Accent6Color(new A.RgbColorModelHex { Val = "7A8F3A" }),
                    new A.Hyperlink(new A.RgbColorModelHex { Val = "0563C1" }),
                    new A.FollowedHyperlinkColor(new A.RgbColorModelHex { Val = "954F72" }))
                { Name = "Auxiliar" },
                new A.FontScheme(
                    new A.MajorFont(new A.LatinFont { Typeface = "Calibri" }, new A.EastAsianFont { Typeface = "" }, new A.ComplexScriptFont { Typeface = "" }),
                    new A.MinorFont(new A.LatinFont { Typeface = "Calibri" }, new A.EastAsianFont { Typeface = "" }, new A.ComplexScriptFont { Typeface = "" }))
                { Name = "Auxiliar" },
                new A.FormatScheme(
                    new A.FillStyleList(DelEsquema(), DelEsquema(), DelEsquema()),
                    new A.LineStyleList(Linea(6350), Linea(12700), Linea(19050)),
                    new A.EffectStyleList(SinEfecto(), SinEfecto(), SinEfecto()),
                    new A.BackgroundFillStyleList(DelEsquema(), Solido("F5F7FA"), Solido("EEF2F7")))
                { Name = "Auxiliar" }),
            new A.ObjectDefaults(),
            new A.ExtraColorSchemeList())
        { Name = "Auxiliar" };
    }
}
