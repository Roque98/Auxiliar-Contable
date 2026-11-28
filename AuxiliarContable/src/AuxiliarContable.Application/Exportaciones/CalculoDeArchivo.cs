using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Volo.Abp;

namespace AuxiliarContable.Exportaciones;

/// <summary>Un renglón de la hoja de cálculo: un dato (valor), una cuenta (fórmula) o, sin ninguno, un subtítulo.</summary>
public sealed record RenglonDeCalculo(string? Id, string Concepto, double? Valor, string? Texto, string? Formula, string? Formato, string? Nota);

public sealed record HojaDeCalculoPedida(string Nombre, List<RenglonDeCalculo> Renglones);

/// <summary>Una tabla de apoyo (tarifa, tabla de días, factores) que las fórmulas usan por su id, p. ej. en BUSCARV.</summary>
public sealed record TablaDeApoyo(string Id, string Titulo, List<string> Encabezados, List<List<object?>> Filas, string? Nota);

/// <summary>Lo que va en la primera hoja: el concepto y de qué renglón (o cuenta) sale.</summary>
public sealed record ResultadoDelResumen(string Concepto, string Ref, string? Formato);

public sealed record ResultadoCalculado(string Concepto, double? Valor);

/// <summary>
/// El desglose de un cálculo (finiquito, retención, conciliación…) para el Excel: la primera hoja es el resumen,
/// ligado con fórmulas a la hoja de cálculo; las tablas de apoyo van en su propia hoja. Las fórmulas se escriben con
/// el id de cada renglón (<c>sd * 15</c>), nunca con celdas: aquí se traducen a la celda que les toca, y el libro se
/// calcula antes de entregarlo para que una fórmula mal hecha se detecte y se corrija, no le llegue a la persona.
/// </summary>
public sealed record CalculoDeArchivo(List<ResultadoDelResumen> Resumen, List<HojaDeCalculoPedida> Hojas, List<TablaDeApoyo> Tablas)
{
    public const int MaxHojas = 6;
    public const int MaxRenglones = 200;
    public const int MaxTablas = 8;
    public const int MaxFilasDeTabla = 400;

    private static readonly string[] Formatos = ["moneda", "porcentaje", "numero", "entero", "dias", "factor", "texto", "fecha"];

    /// <summary>Del pedido del modelo; null si no trae «calculo». Lo mal formado se explica para que lo corrija.</summary>
    public static CalculoDeArchivo? Leer(JsonElement entrada)
    {
        if (entrada.ValueKind != JsonValueKind.Object || !entrada.TryGetProperty("calculo", out var c) || c.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (c.ValueKind != JsonValueKind.Object)
        {
            throw new UserFriendlyException("«calculo» va como objeto con «hojas», «resumen» y, si hacen falta, «tablas».");
        }

        var hojas = Lista(c, "hojas").Take(MaxHojas + 1).Select(h => new HojaDeCalculoPedida(
            Cadena(h, "nombre") ?? "Cálculo",
            Lista(h, "renglones").Concat(Lista(h, "filas")).Take(MaxRenglones + 1).Select(r => new RenglonDeCalculo(
                Cadena(r, "id"),
                Cadena(r, "concepto") ?? "",
                Numero(r, "valor"),
                r.TryGetProperty("valor", out var v) && v.ValueKind == JsonValueKind.String && Numero(r, "valor") is null ? v.GetString() : null,
                Cadena(r, "formula"),
                Cadena(r, "formato")?.ToLowerInvariant(),
                Cadena(r, "nota"))).ToList())).ToList();
        var tablas = Lista(c, "tablas").Take(MaxTablas + 1).Select(t => new TablaDeApoyo(
            Cadena(t, "id") ?? "",
            Cadena(t, "titulo") ?? Cadena(t, "id") ?? "Tabla",
            Lista(t, "encabezados").Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.ToString()).ToList(),
            Lista(t, "filas").Take(MaxFilasDeTabla + 1).Select(f => f.ValueKind == JsonValueKind.Array
                ? f.EnumerateArray().Select(Celda).ToList()
                : [Celda(f)]).ToList(),
            Cadena(t, "nota"))).ToList();
        var resumen = Lista(c, "resumen").Select(r => new ResultadoDelResumen(
            Cadena(r, "concepto") ?? "", Cadena(r, "ref") ?? Cadena(r, "formula") ?? Cadena(r, "id") ?? "", Cadena(r, "formato")?.ToLowerInvariant())).ToList();

        var calculo = new CalculoDeArchivo(resumen, hojas, tablas);
        calculo.Validar();
        return calculo;
    }

    private void Validar()
    {
        if (Hojas.Count == 0 || Hojas.All(h => h.Renglones.Count == 0))
        {
            throw new UserFriendlyException("«calculo» necesita al menos una hoja con sus renglones.");
        }

        if (Hojas.Count > MaxHojas || Tablas.Count > MaxTablas)
        {
            throw new UserFriendlyException($"Son hasta {MaxHojas} hojas de cálculo y {MaxTablas} tablas.");
        }

        if (Hojas.Any(h => h.Renglones.Count > MaxRenglones) || Tablas.Any(t => t.Filas.Count > MaxFilasDeTabla))
        {
            throw new UserFriendlyException($"Son hasta {MaxRenglones} renglones por hoja y {MaxFilasDeTabla} filas por tabla.");
        }

        if (Resumen.Count == 0)
        {
            throw new UserFriendlyException("«resumen» lleva los resultados que van en la primera hoja (concepto y el id del renglón de donde salen).");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in Hojas.SelectMany(h => h.Renglones).Select(r => r.Id).Where(i => i is not null).Concat(Tablas.Select(t => t.Id)))
        {
            if (!EsId(id!))
            {
                throw new UserFriendlyException($"El id «{id}» no sirve: usa letras, números y guion bajo, empezando con letra y sin que parezca celda (p. ej. «salario_diario»).");
            }

            if (!ids.Add(id!))
            {
                throw new UserFriendlyException($"El id «{id}» está repetido.");
            }
        }

        foreach (var r in Hojas.SelectMany(h => h.Renglones))
        {
            if (r.Formato is not null && !Formatos.Contains(r.Formato))
            {
                throw new UserFriendlyException($"«formato» de «{r.Concepto}» va como {string.Join(", ", Formatos)}.");
            }

            if (r.Valor is not null && !string.IsNullOrWhiteSpace(r.Formula))
            {
                throw new UserFriendlyException($"«{r.Concepto}» trae valor y fórmula: es un dato (valor) o una cuenta (fórmula).");
            }
        }
    }

    /// <summary>Letras, dígitos y guion bajo, empezando con letra; no VERDADERO ni FALSO. (Que parezca celda no importa: se traduce antes.)</summary>
    internal static bool EsId(string id) =>
        id.Length is > 0 and <= 40 && char.IsLetter(id[0]) && id.All(ch => char.IsLetterOrDigit(ch) || ch == '_')
        && id.ToUpperInvariant() is not ("TRUE" or "FALSE" or "VERDADERO" or "FALSO");

    // ---- El libro ----------------------------------------------------------------------------------

    private const string Azul = "#1F3A5F";
    private const string Dato = "#FFF7D6";

    /// <summary>Arma el libro y lo calcula. Devuelve el .xlsx y los resultados del resumen (para que el modelo los coteje).</summary>
    internal (byte[] Contenido, List<ResultadoCalculado> Resultados) Excel(string titulo, IReadOnlyList<ArchivoDesdeTexto.Pieza> notas)
    {
        using var libro = new XLWorkbook();
        var nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string NombreUnico(string n)
        {
            var b = NombreDeHoja(n);
            var r = b;
            for (var i = 2; !nombres.Add(r); i++)
            {
                r = NombreDeHoja($"{(b.Length > 27 ? b[..27] : b)} {i}");
            }

            return r;
        }

        var resumen = libro.Worksheets.Add(NombreUnico("Resumen"));
        var hojas = Hojas.Select(h => (Pedida: h, Hoja: libro.Worksheets.Add(NombreUnico(h.Nombre)))).ToList();
        var apoyo = Tablas.Count > 0 ? libro.Worksheets.Add(NombreUnico("Tablas")) : null;

        // Dónde quedó cada id: la celda del importe de un renglón o el rango de datos de una tabla.
        var refs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        const int PrimerRenglon = 5;
        foreach (var (pedida, hoja) in hojas)
        {
            for (var i = 0; i < pedida.Renglones.Count; i++)
            {
                if (pedida.Renglones[i].Id is { } id)
                {
                    refs[id] = $"{Hoja(hoja.Name)}!$B${PrimerRenglon + i}";
                }
            }
        }

        if (apoyo is not null)
        {
            apoyo.Cell(1, 1).Value = "Tablas que usa el cálculo";
            apoyo.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
            var fila = 3;
            var maxColumnas = 1;
            foreach (var t in Tablas)
            {
                var columnas = Math.Max(1, Math.Max(t.Encabezados.Count, t.Filas.Count == 0 ? 0 : t.Filas.Max(f => f.Count)));
                maxColumnas = Math.Max(maxColumnas, columnas);
                apoyo.Cell(fila, 1).Value = t.Titulo;
                apoyo.Cell(fila, 1).Style.Font.SetBold().Font.SetFontSize(12);
                fila++;
                for (var c = 0; c < t.Encabezados.Count; c++)
                {
                    Encabezado(apoyo.Cell(fila, c + 1), t.Encabezados[c]);
                }

                if (t.Encabezados.Count > 0)
                {
                    fila++;
                }

                var desde = fila;
                foreach (var f in t.Filas)
                {
                    for (var c = 0; c < f.Count; c++)
                    {
                        var celda = apoyo.Cell(fila, c + 1);
                        switch (f[c])
                        {
                            case double d:
                                celda.Value = d;
                                celda.Style.NumberFormat.Format = d % 1 == 0 ? "#,##0" : "#,##0.00####";
                                break;
                            case string s:
                                celda.Value = s;
                                break;
                        }
                    }

                    fila++;
                }

                var hasta = Math.Max(desde, fila - 1);
                refs[t.Id] = $"{Hoja(apoyo.Name)}!$A${desde}:${Columna(columnas)}${hasta}";
                if (t.Filas.Count > 0)
                {
                    apoyo.Range(desde, 1, hasta, columnas).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                        .Border.SetInsideBorder(XLBorderStyleValues.Hair);
                }

                if (!string.IsNullOrWhiteSpace(t.Nota))
                {
                    apoyo.Cell(fila, 1).Value = t.Nota;
                    apoyo.Cell(fila, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.FromHtml("#555555"));
                    fila++;
                }

                fila++;
            }

            for (var c = 1; c <= maxColumnas; c++)
            {
                apoyo.Column(c).AdjustToContents(1, fila, 10, 40);
            }
        }

        // Las hojas de cálculo: concepto, importe (dato en amarillo o fórmula) y fundamento.
        var formulas = new List<(IXLCell Celda, string Concepto, string Hoja)>();
        foreach (var (pedida, hoja) in hojas)
        {
            hoja.Cell(1, 1).Value = pedida.Nombre;
            hoja.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
            hoja.Cell(2, 1).Value = "Las celdas amarillas son datos: si cambias uno, todo lo demás (y el resumen) se recalcula.";
            hoja.Cell(2, 1).Style.Font.SetFontColor(XLColor.FromHtml("#555555"));
            Encabezado(hoja.Cell(4, 1), "Concepto");
            Encabezado(hoja.Cell(4, 2), "Importe");
            Encabezado(hoja.Cell(4, 3), "Cómo se calcula / fundamento");
            for (var i = 0; i < pedida.Renglones.Count; i++)
            {
                var r = pedida.Renglones[i];
                var fila = PrimerRenglon + i;
                hoja.Cell(fila, 1).Value = r.Concepto;
                hoja.Cell(fila, 1).Style.Alignment.SetWrapText(true);
                var importe = hoja.Cell(fila, 2);
                if (!string.IsNullOrWhiteSpace(r.Formula))
                {
                    importe.FormulaA1 = Traducir(r.Formula, refs, r.Concepto);
                    Formato(importe, r.Formato);
                    formulas.Add((importe, r.Concepto, pedida.Nombre));
                }
                else if (r.Valor is { } v)
                {
                    importe.Value = v;
                    Formato(importe, r.Formato);
                    importe.Style.Fill.SetBackgroundColor(XLColor.FromHtml(Dato));
                }
                else if (r.Texto is { } t)
                {
                    importe.Value = t;
                    importe.Style.Fill.SetBackgroundColor(XLColor.FromHtml(Dato));
                }
                else
                {
                    // Sin valor ni fórmula: subtítulo de sección.
                    hoja.Range(fila, 1, fila, 3).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#E8EDF4"));
                }

                if (!string.IsNullOrWhiteSpace(r.Nota))
                {
                    hoja.Cell(fila, 3).Value = r.Nota;
                    hoja.Cell(fila, 3).Style.Alignment.SetWrapText(true).Font.SetFontColor(XLColor.FromHtml("#444444"));
                }
            }

            var ultima = PrimerRenglon + Math.Max(pedida.Renglones.Count, 1) - 1;
            hoja.Range(4, 1, ultima, 3).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorder(XLBorderStyleValues.Hair);
            hoja.Column(1).Width = 46;
            hoja.Column(2).Width = 18;
            hoja.Column(3).Width = 60;
            hoja.SheetView.FreezeRows(4);
        }

        // La primera hoja: los resultados, cada uno ligado a su renglón.
        resumen.Cell(1, 1).Value = titulo;
        resumen.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(15);
        resumen.Cell(2, 1).Value = $"Cada importe sale de la hoja «{hojas[0].Hoja.Name}»: si cambias un dato allá, aquí se actualiza.";
        resumen.Cell(2, 1).Style.Font.SetFontColor(XLColor.FromHtml("#555555"));
        Encabezado(resumen.Cell(4, 1), "Concepto");
        Encabezado(resumen.Cell(4, 2), "Importe");
        var enResumen = new List<(IXLCell Celda, string Concepto)>();
        for (var i = 0; i < Resumen.Count; i++)
        {
            var r = Resumen[i];
            var fila = 5 + i;
            resumen.Cell(fila, 1).Value = r.Concepto;
            var celda = resumen.Cell(fila, 2);
            celda.FormulaA1 = Traducir(r.Ref, refs, r.Concepto);
            Formato(celda, r.Formato ?? FormatoDe(r.Ref));
            enResumen.Add((celda, r.Concepto));
            formulas.Add((celda, r.Concepto, resumen.Name));
        }

        var finResumen = 4 + Resumen.Count;
        resumen.Range(4, 1, finResumen, 2).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorder(XLBorderStyleValues.Hair);
        resumen.Row(finResumen).Style.Font.SetBold();

        // Lo que se explicó en la conversación, como notas debajo (sin sus tablas: los números son los de arriba).
        var filaNota = finResumen + 2;
        foreach (var p in notas)
        {
            switch (p)
            {
                case ArchivoDesdeTexto.Titulo t when filaNota > finResumen + 2 || t.Texto != titulo:
                    resumen.Cell(filaNota, 1).Value = t.Texto;
                    resumen.Cell(filaNota, 1).Style.Font.SetBold();
                    filaNota++;
                    break;
                case ArchivoDesdeTexto.Parrafo x:
                    resumen.Cell(filaNota, 1).Value = x.Texto;
                    filaNota++;
                    break;
                case ArchivoDesdeTexto.Lista l:
                    for (var i = 0; i < l.Elementos.Count; i++)
                    {
                        resumen.Cell(filaNota, 1).Value = (l.Numerada ? $"{i + 1}. " : "• ") + l.Elementos[i];
                        filaNota++;
                    }

                    break;
            }
        }

        resumen.Column(1).Width = 50;
        resumen.Column(2).Width = 20;
        resumen.SetTabActive();

        // Se calcula aquí: un error (#¿NOMBRE?, #REF!, #DIV/0!, referencia circular) se devuelve para corregirlo.
        try
        {
            libro.RecalculateAllFormulas();
        }
        catch (Exception ex) when (ex is not UserFriendlyException)
        {
            throw new UserFriendlyException($"No pude calcular el libro ({ex.Message}). Revisa que ninguna fórmula se use a sí misma y que las funciones sean comunes (SUMA, SI, BUSCARV, REDONDEAR, MAX, MIN).");
        }

        var errores = formulas.Where(f => f.Celda.CachedValue.IsError)
            .Select(f => $"«{f.Concepto}» ({f.Hoja}) da {f.Celda.CachedValue.GetError()}: {f.Celda.FormulaA1}")
            .Take(8).ToList();
        if (errores.Count > 0)
        {
            throw new UserFriendlyException("Hay fórmulas que no se pueden calcular; corrígelas y vuelve a pedir el archivo:\n" + string.Join("\n", errores));
        }

        var resultados = enResumen.Select(r => new ResultadoCalculado(r.Concepto,
            r.Celda.CachedValue.IsNumber ? Math.Round(r.Celda.CachedValue.GetNumber(), 4) : null)).ToList();

        using var ms = new System.IO.MemoryStream();
        libro.SaveAs(ms);
        return (ms.ToArray(), resultados);
    }

    /// <summary>El formato del renglón al que apunta un resultado del resumen, si es solo su id.</summary>
    private string? FormatoDe(string referencia)
    {
        var id = referencia.Trim().TrimStart('=').Trim();
        return Hojas.SelectMany(h => h.Renglones).FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase))?.Formato ?? "moneda";
    }

    private static void Encabezado(IXLCell celda, string texto)
    {
        celda.Value = texto;
        celda.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml(Azul)).Alignment.SetWrapText(true);
    }

    private static void Formato(IXLCell celda, string? formato) => celda.Style.NumberFormat.Format = formato switch
    {
        "porcentaje" => "0.00%",
        "entero" or "dias" => "#,##0",
        "numero" => "#,##0.00",
        "factor" => "0.000000",
        "fecha" => "dd/mm/yyyy",
        "texto" => "@",
        _ => "$#,##0.00;-$#,##0.00",
    };

    private static string Hoja(string nombre) => "'" + nombre.Replace("'", "''") + "'";

    private static string Columna(int n)
    {
        var s = "";
        while (n > 0)
        {
            var m = (n - 1) % 26;
            s = (char)('A' + m) + s;
            n = (n - m - 1) / 26;
        }

        return s;
    }

    private static string NombreDeHoja(string nombre)
    {
        var limpio = new string(nombre.Where(c => c is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\' or '\'')).ToArray()).Trim();
        return limpio.Length == 0 ? "Cálculo" : limpio.Length > 31 ? limpio[..31].Trim() : limpio;
    }

    // ---- Fórmulas ----------------------------------------------------------------------------------

    /// <summary>Las funciones en español, como las escribe un contador, a como las guarda Excel.</summary>
    private static readonly Dictionary<string, string> Funciones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SUMA"] = "SUM", ["SI"] = "IF", ["Y"] = "AND", ["O"] = "OR", ["NO"] = "NOT", ["BUSCARV"] = "VLOOKUP",
        ["BUSCARH"] = "HLOOKUP", ["REDONDEAR"] = "ROUND", ["REDONDEAR.MAS"] = "ROUNDUP", ["REDONDEAR.MENOS"] = "ROUNDDOWN",
        ["PROMEDIO"] = "AVERAGE", ["ENTERO"] = "INT", ["TRUNCAR"] = "TRUNC", ["INDICE"] = "INDEX", ["ÍNDICE"] = "INDEX",
        ["COINCIDIR"] = "MATCH", ["SI.ERROR"] = "IFERROR", ["SUMAPRODUCTO"] = "SUMPRODUCT", ["FECHA"] = "DATE",
        ["DIAS"] = "DAYS", ["DÍAS"] = "DAYS", ["RESIDUO"] = "MOD", ["POTENCIA"] = "POWER", ["RAIZ"] = "SQRT", ["RAÍZ"] = "SQRT",
        ["SUMAR.SI"] = "SUMIF", ["CONTAR"] = "COUNT", ["CONTARA"] = "COUNTA", ["CONTAR.SI"] = "COUNTIF", ["AÑO"] = "YEAR",
        ["MES"] = "MONTH", ["DIA"] = "DAY", ["DÍA"] = "DAY", ["HOY"] = "TODAY", ["MAXIMO"] = "MAX", ["MÁXIMO"] = "MAX",
        ["MINIMO"] = "MIN", ["MÍNIMO"] = "MIN", ["ELEGIR"] = "CHOOSE",
    };

    /// <summary>
    /// «= sd * 15 * dias_trabajados / 365» → «'Cálculo'!$B$5*15*'Cálculo'!$B$9/365». Los ids van a su celda, las funciones
    /// en español a su nombre en Excel, «;» a «,» y VERDADERO/FALSO a TRUE/FALSE. Una celda escrita a mano («B5») o un id
    /// que no existe es error: así nadie apunta a la celda equivocada.
    /// </summary>
    internal static string Traducir(string formula, IReadOnlyDictionary<string, string> refs, string concepto)
    {
        var f = formula.Trim();
        if (f.StartsWith('='))
        {
            f = f[1..];
        }

        if (f.Trim().Length == 0)
        {
            throw new UserFriendlyException($"La fórmula de «{concepto}» está vacía.");
        }

        var sb = new StringBuilder();
        for (var i = 0; i < f.Length;)
        {
            var ch = f[i];
            if (ch == '"')
            {
                var fin = f.IndexOf('"', i + 1);
                fin = fin < 0 ? f.Length - 1 : fin;
                sb.Append(f, i, fin - i + 1);
                i = fin + 1;
            }
            else if (char.IsLetter(ch) || ch == '_')
            {
                var j = i;
                while (j < f.Length && (char.IsLetterOrDigit(f[j]) || f[j] is '_' or '.'))
                {
                    j++;
                }

                var palabra = f[i..j].TrimEnd('.');
                j = i + palabra.Length;
                var k = j;
                while (k < f.Length && f[k] == ' ')
                {
                    k++;
                }

                if (k < f.Length && f[k] == '(')
                {
                    sb.Append(Funciones.TryGetValue(palabra, out var en) ? en : palabra.ToUpperInvariant());
                }
                else if (refs.TryGetValue(palabra, out var r))
                {
                    sb.Append(r);
                }
                else if (palabra.ToUpperInvariant() is "VERDADERO" or "TRUE")
                {
                    sb.Append("TRUE");
                }
                else if (palabra.ToUpperInvariant() is "FALSO" or "FALSE")
                {
                    sb.Append("FALSE");
                }
                else
                {
                    throw new UserFriendlyException(
                        $"En la fórmula de «{concepto}» no conozco «{palabra}»: usa el id de un renglón o de una tabla (no celdas como B5).");
                }

                i = j;
            }
            else if (char.IsDigit(ch))
            {
                var j = i;
                while (j < f.Length && (char.IsDigit(f[j]) || f[j] == '.'))
                {
                    j++;
                }

                if (j < f.Length && (char.IsLetter(f[j]) || f[j] == '_'))
                {
                    throw new UserFriendlyException($"En la fórmula de «{concepto}» hay «{f[i..Math.Min(f.Length, j + 4)]}»: los ids empiezan con letra.");
                }

                sb.Append(f, i, j - i);
                i = j;
            }
            else
            {
                sb.Append(ch switch { ';' => ',', '[' or ']' or '!' or '$' or '\'' => throw new UserFriendlyException(
                    $"En la fórmula de «{concepto}» no van referencias a hojas o celdas («{ch}»): usa los ids."), _ => ch });
                i++;
            }
        }

        return sb.ToString().Replace(" ", "");
    }

    // ---- Lectura del JSON --------------------------------------------------------------------------

    private static IEnumerable<JsonElement> Lista(JsonElement e, string nombre) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nombre, out var l) && l.ValueKind == JsonValueKind.Array ? l.EnumerateArray() : [];

    private static string? Cadena(JsonElement e, string nombre) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nombre, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString()!.Trim(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            }
            : null;

    private static double? Numero(JsonElement e, string nombre)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(nombre, out var v))
        {
            return null;
        }

        if (v.ValueKind == JsonValueKind.Number)
        {
            return v.GetDouble();
        }

        return v.ValueKind == JsonValueKind.String && ArchivoDesdeTexto.ComoNumero(v.GetString() ?? "", out var d, out _, out _) ? d : null;
    }

    private static object? Celda(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.String => ArchivoDesdeTexto.ComoNumero(e.GetString() ?? "", out var d, out _, out _) ? d : e.GetString(),
        JsonValueKind.True => "Sí",
        JsonValueKind.False => "No",
        _ => null,
    };
}
