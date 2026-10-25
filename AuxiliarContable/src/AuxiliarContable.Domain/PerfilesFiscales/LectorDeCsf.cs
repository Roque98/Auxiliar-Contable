using System;
using System.Text.RegularExpressions;

namespace AuxiliarContable.PerfilesFiscales;

/// <summary>Lo que se saca de una CSF SOLO con código: los datos de formato fijo.</summary>
public sealed record IdentificadoresDeCsf(
    bool PareceCsf,
    string? Rfc,
    string? Curp,
    string? CodigoPostal,
    DateOnly? FechaDeEmision,
    string? TipoDePersona);

/// <summary>
/// La parte determinista de la lectura de la Constancia de Situación Fiscal: identificadores
/// con formato fijo (RFC, CURP, código postal, fecha de emisión). Son los que más importa no
/// equivocar y los que el código lee mejor que nadie; lo que va en tablas (regímenes,
/// obligaciones, actividades) lo lee el modelo y aquí se valida contra esto.
/// </summary>
public static partial class LectorDeCsf
{
    [GeneratedRegex(@"C[ÓO]DIGO\s+POSTAL\s*:?\s*(\d{5})\b", RegexOptions.IgnoreCase)]
    private static partial Regex PatronCodigoPostal();

    [GeneratedRegex(@"FECHA\s+DE\s+EMISI[ÓO]N(.{0,200})", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PatronEmision();

    [GeneratedRegex(@"RFC\s*:?\s*([A-ZÑ&]{3,4}\d{6}[A-Z\d]{3})\b")]
    private static partial Regex PatronRfcConEtiqueta();

    public static IdentificadoresDeCsf Leer(string texto)
    {
        var mayusculas = texto.ToUpperInvariant();
        var pareceCsf = mayusculas.Contains("CONSTANCIA DE SITUACI", StringComparison.Ordinal)
                        || mayusculas.Contains("CÉDULA DE IDENTIFICACIÓN FISCAL", StringComparison.Ordinal)
                        || mayusculas.Contains("CEDULA DE IDENTIFICACION FISCAL", StringComparison.Ordinal);

        // El RFC con su etiqueta primero: en la CSF puede salir antes el del SAT u otro.
        var rfc = PatronRfcConEtiqueta().Match(mayusculas) is { Success: true } m ? m.Groups[1].Value : Sat.PrimerRfc(mayusculas);
        if (rfc != null && !Sat.EsRfc(rfc))
        {
            rfc = null;
        }

        var emision = PatronEmision().Match(mayusculas) is { Success: true } e ? Sat.FechaLarga(e.Groups[1].Value) : null;

        return new IdentificadoresDeCsf(
            pareceCsf,
            rfc,
            Sat.PrimeraCurp(mayusculas),
            PatronCodigoPostal().Match(mayusculas) is { Success: true } cp ? cp.Groups[1].Value : null,
            emision,
            rfc == null ? null : rfc.Length == 12 ? "moral" : "fisica");
    }
}
