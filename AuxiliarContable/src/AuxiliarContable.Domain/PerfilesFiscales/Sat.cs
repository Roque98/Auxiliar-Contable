using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AuxiliarContable.PerfilesFiscales;

/// <summary>
/// Formatos y catálogos del SAT que se validan con código. Lo que esté aquí no lo decide la IA.
///
/// <para>Si aparece el Fiscal Knowledge Engine (FKE) del dueño, el catálogo de regímenes y las
/// validaciones de RFC deberían salir de ahí: esto es lo mínimo para no aceptar basura.</para>
/// </summary>
public static partial class Sat
{
    /// <summary>
    /// Catálogo c_RegimenFiscal del CFDI 4.0 (los 19 regímenes). Clave → descripción.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Regimenes = new Dictionary<string, string>
    {
        ["601"] = "General de Ley Personas Morales",
        ["603"] = "Personas Morales con Fines no Lucrativos",
        ["605"] = "Sueldos y Salarios e Ingresos Asimilados a Salarios",
        ["606"] = "Arrendamiento",
        ["607"] = "Régimen de Enajenación o Adquisición de Bienes",
        ["608"] = "Demás ingresos",
        ["610"] = "Residentes en el Extranjero sin Establecimiento Permanente en México",
        ["611"] = "Ingresos por Dividendos (socios y accionistas)",
        ["612"] = "Personas Físicas con Actividades Empresariales y Profesionales",
        ["614"] = "Ingresos por intereses",
        ["615"] = "Régimen de los ingresos por obtención de premios",
        ["616"] = "Sin obligaciones fiscales",
        ["620"] = "Sociedades Cooperativas de Producción que optan por diferir sus ingresos",
        ["621"] = "Incorporación Fiscal",
        ["622"] = "Actividades Agrícolas, Ganaderas, Silvícolas y Pesqueras",
        ["623"] = "Opcional para Grupos de Sociedades",
        ["624"] = "Coordinados",
        ["625"] = "Régimen de las Actividades Empresariales con ingresos a través de Plataformas Tecnológicas",
        ["626"] = "Régimen Simplificado de Confianza",
    };

    /// <summary>12 caracteres = persona moral; 13 = persona física.</summary>
    [GeneratedRegex(@"\b([A-ZÑ&]{3,4})(\d{2})(\d{2})(\d{2})([A-Z\d]{2})([A\d])\b")]
    private static partial Regex PatronRfc();

    [GeneratedRegex(@"\b[A-Z][AEIOUX][A-Z]{2}\d{2}(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])[HMX](AS|BC|BS|CC|CL|CM|CS|CH|DF|DG|GT|GR|HG|JC|MC|MN|MS|NT|NL|OC|PL|QT|QR|SP|SL|SR|TC|TS|TL|VZ|YN|ZS|NE)[B-DF-HJ-NP-TV-Z]{3}[A-Z\d]\d\b")]
    private static partial Regex PatronCurp();

    public static bool EsRfc(string? rfc) => !string.IsNullOrEmpty(rfc) && PatronRfc().Match(rfc).Value == rfc;

    public static bool EsCurp(string? curp) => !string.IsNullOrEmpty(curp) && PatronCurp().Match(curp).Value == curp;

    public static string? PrimerRfc(string texto) => PatronRfc().Match(texto) is { Success: true } m ? m.Value : null;

    public static string? PrimeraCurp(string texto) => PatronCurp().Match(texto) is { Success: true } m ? m.Value : null;

    /// <summary>
    /// ¿Cuadra el dígito verificador (el último carácter)? Algoritmo del SAT: cada carácter vale
    /// su posición en «0123456789ABCDEFGHIJKLMN&amp;OPQRSTUVWXYZ Ñ», se pondera por 13..2 sobre el
    /// RFC de 13 posiciones (a una moral se le antepone un espacio) y el dígito es 11 − (suma
    /// mod 11), con 10 = «A» y 11 = «0». Los genéricos (XAXX…, XEXX…) se aceptan tal cual.
    ///
    /// <para>Se usa como AVISO, no como rechazo: si alguna vez el SAT asignó un RFC que no lo
    /// cumple, el cliente no debe quedarse sin poder registrarse.</para>
    /// </summary>
    public static bool DigitoVerificadorCuadra(string rfc)
    {
        if (!EsRfc(rfc))
        {
            return false;
        }

        if (rfc is "XAXX010101000" or "XEXX010101000")
        {
            return true;
        }

        const string Valores = "0123456789ABCDEFGHIJKLMN&OPQRSTUVWXYZ Ñ";
        var base13 = rfc.Length == 12 ? " " + rfc : rfc;
        var suma = 0;
        for (var i = 0; i < 12; i++)
        {
            var valor = Valores.IndexOf(base13[i], StringComparison.Ordinal);
            if (valor < 0)
            {
                return false;
            }

            suma += valor * (13 - i);
        }

        var resto = 11 - (suma % 11);
        var esperado = resto switch
        {
            11 => '0',
            10 => 'A',
            _ => (char)('0' + resto),
        };
        return base13[12] == esperado;
    }

    private static readonly string[] Meses =
        ["ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE"];

    [GeneratedRegex(@"\b(\d{1,2})\s+DE\s+([A-ZÁÉÍÓÚ]+)\s+DE\s+(\d{4})\b")]
    private static partial Regex PatronFechaLarga();

    /// <summary>«15 DE OCTUBRE DE 2026» → 2026-10-15. Como lo escribe el SAT en la CSF.</summary>
    public static DateOnly? FechaLarga(string texto)
    {
        var m = PatronFechaLarga().Match(texto.ToUpperInvariant());
        if (!m.Success)
        {
            return null;
        }

        var mes = Array.IndexOf(Meses, m.Groups[2].Value.Replace('É', 'E')) + 1;
        return mes > 0 && DateOnly.TryParseExact(
                   $"{m.Groups[3].Value}-{mes:00}-{int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture):00}",
                   "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            ? fecha
            : null;
    }
}
