using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using Volo.Abp.Content;

namespace IgasConsole.Exportaciones;

public enum FormatoColumna
{
    Texto,
    Entero,
    Litros,
    Moneda,
    Porcentaje,
    Fecha,
    FechaHora,
}

public sealed record ColumnaExcel(string Encabezado, FormatoColumna Formato = FormatoColumna.Texto, bool Sumar = false, double? Ancho = null);

/// <summary>
/// Un .xlsx de verdad para quien lo abre en Excel: título y filtros arriba, encabezados con
/// formato, números como números (litros, pesos, %), fechas como fechas, anchos razonables,
/// totales al pie y la fila de encabezados fija. Una hoja por tabla.
/// </summary>
public sealed class LibroExcel : IDisposable
{
    private readonly XLWorkbook _libro = new();
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    public LibroExcel Hoja(string nombre, string titulo, IEnumerable<string> lineas, IReadOnlyList<ColumnaExcel> columnas, IEnumerable<object?[]> filas)
    {
        var h = _libro.Worksheets.Add(Recortar(nombre));
        var fila = 1;
        h.Cell(fila, 1).Value = titulo;
        h.Cell(fila, 1).Style.Font.SetBold().Font.SetFontSize(14);
        fila++;
        foreach (var l in lineas.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            h.Cell(fila, 1).Value = l;
            h.Cell(fila, 1).Style.Font.SetFontColor(XLColor.FromHtml("#555555"));
            fila++;
        }

        fila++;
        var encabezado = fila;
        for (var c = 0; c < columnas.Count; c++)
        {
            var celda = h.Cell(fila, c + 1);
            celda.Value = columnas[c].Encabezado;
            celda.Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#1F3A5F"))
                .Alignment.SetWrapText(true)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        }

        var primera = fila + 1;
        foreach (var f in filas)
        {
            fila++;
            for (var c = 0; c < columnas.Count && c < f.Length; c++)
            {
                Poner(h.Cell(fila, c + 1), f[c], columnas[c].Formato);
            }
        }

        var ultima = fila;
        if (columnas.Any(c => c.Sumar) && ultima >= primera)
        {
            fila++;
            h.Cell(fila, 1).Value = "Total";
            for (var c = 0; c < columnas.Count; c++)
            {
                if (columnas[c].Sumar)
                {
                    var celda = h.Cell(fila, c + 1);
                    var letra = h.Column(c + 1).ColumnLetter();
                    celda.FormulaA1 = $"SUM({letra}{primera}:{letra}{ultima})";
                    Formato(celda, columnas[c].Formato);
                }
            }

            h.Row(fila).Style.Font.SetBold();
            h.Range(fila, 1, fila, columnas.Count).Style.Border.SetTopBorder(XLBorderStyleValues.Thin);
        }

        if (ultima >= primera)
        {
            h.Range(encabezado, 1, ultima, columnas.Count).SetAutoFilter();
        }

        h.SheetView.FreezeRows(encabezado);
        for (var c = 0; c < columnas.Count; c++)
        {
            var col = h.Column(c + 1);
            if (columnas[c].Ancho is { } ancho)
            {
                col.Width = ancho;
            }
            else
            {
                col.AdjustToContents(encabezado, Math.Min(ultima, encabezado + 500));
                col.Width = Math.Clamp(col.Width + 2, 10, 60);
            }
        }

        return this;
    }

    private static void Poner(IXLCell celda, object? v, FormatoColumna formato)
    {
        switch (v)
        {
            case null:
                break;
            case decimal d:
                celda.Value = formato == FormatoColumna.Porcentaje ? d / 100m : d;
                break;
            case double d:
                celda.Value = formato == FormatoColumna.Porcentaje ? d / 100 : d;
                break;
            case int or long:
                celda.Value = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                break;
            case DateTime dt:
                celda.Value = dt;
                break;
            case DateTimeOffset dto:
                celda.Value = dto.DateTime;
                break;
            case string s when formato is FormatoColumna.Fecha or FormatoColumna.FechaHora:
                if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
                {
                    // La hora local de origen, como viene en el archivo.
                    celda.Value = f.DateTime;
                }
                else
                {
                    celda.Value = s;
                }

                break;
            default:
                celda.Value = v.ToString();
                break;
        }

        Formato(celda, formato);
    }

    private static void Formato(IXLCell celda, FormatoColumna formato)
    {
        celda.Style.NumberFormat.Format = formato switch
        {
            FormatoColumna.Entero => "#,##0",
            FormatoColumna.Litros => "#,##0.000",
            FormatoColumna.Moneda => "\"$\"#,##0.00",
            FormatoColumna.Porcentaje => "0.00%",
            FormatoColumna.Fecha => "dd/mm/yyyy",
            FormatoColumna.FechaHora => "dd/mm/yyyy hh:mm:ss",
            _ => celda.Style.NumberFormat.Format,
        };
        if (formato == FormatoColumna.Texto)
        {
            celda.Style.Alignment.SetWrapText(false);
        }
    }

    public IRemoteStreamContent Contenido(string nombreArchivo)
    {
        var ms = new MemoryStream();
        _libro.SaveAs(ms);
        ms.Position = 0;
        return new RemoteStreamContent(ms, nombreArchivo, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    public void Dispose() => _libro.Dispose();

    /// <summary>«facturas-Ferretería Centro-2026-07» → «facturas-ferreteria-centro-2026-07.xlsx».</summary>
    public static string NombreDeArchivo(params string?[] partes)
    {
        var sb = new StringBuilder();
        foreach (var p in partes.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (sb.Length > 0)
            {
                sb.Append('-');
            }

            foreach (var ch in p!.Normalize(NormalizationForm.FormD).ToLowerInvariant())
            {
                if (char.IsAsciiLetterOrDigit(ch))
                {
                    sb.Append(ch);
                }
                else if ((ch is ' ' or '-' or '_' or '/' or '.') && sb.Length > 0 && sb[^1] != '-')
                {
                    sb.Append('-');
                }
            }

            while (sb.Length > 0 && sb[^1] == '-')
            {
                sb.Length--;
            }
        }

        return (sb.Length == 0 ? "exportacion" : sb.ToString()) + ".xlsx";
    }

    private static string Recortar(string n)
    {
        var limpio = new string(n.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray());
        return limpio.Length > 31 ? limpio[..31] : limpio;
    }
}
