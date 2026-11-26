using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AuxiliarContable.Conversaciones;

/// <summary>
/// Quien pregunta no puede saber si una cita está mal: confía en lo que le dicen (pedido 26-nov-2026). Antes de enseñar
/// una respuesta, cada artículo o regla que cite tiene que venir en lo que el asistente CONSULTÓ en ese turno (resultados
/// de sus herramientas). Si cita algo que no consultó, se le devuelve una vez para que lo verifique o lo quite.
///
/// <para>Es una red, no un juez: acepta la cita si en los resultados aparecen la ley y el número. Atrapa lo inventado o
/// recordado de memoria, que es el error que la persona no puede ver.</para>
/// </summary>
public static class VerificadorDeCitas
{
    private const string Leyes = "LISR|LIVA|CFF|LIEPS|LFT|LSS|LINFONAVIT|LGSM|CPEUM|RLISR|RCFF|RLIVA|LFPCA|LFDC|LAAT|LIF|RGCE|RFA";

    private static readonly Regex Articulo = new(
        @"\b(?:art(?:[íi]culos?|s?\.)|arts\.)\s*(?<num>\d{1,3}(?:[-\s]?[A-Z]\b)?(?:\s?Bis)?)(?<medio>[^\n;]{0,50}?)\b(?<ley>" + Leyes + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Regla = new(@"\bregla\s+(?<num>\d{1,2}\.\d{1,2}(?:\.\d{1,3}){1,2})\.?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Las citas de la respuesta que NO aparecen en lo consultado («art. 76 LISR», «regla 3.13.4.»).</summary>
    public static List<string> SinRespaldo(string respuesta, IEnumerable<string> consultado)
    {
        var fuentes = string.Join("\n", consultado);
        var faltan = new List<string>();
        if (string.IsNullOrWhiteSpace(respuesta))
        {
            return faltan;
        }

        foreach (Match m in Articulo.Matches(respuesta))
        {
            var ley = m.Groups["ley"].Value.ToUpperInvariant();
            var num = Regex.Replace(m.Groups["num"].Value, @"\s+", "").ToUpperInvariant();
            var base_ = Regex.Match(num, @"^\d+").Value;
            var leyEnFuentes = Regex.IsMatch(fuentes, @"\b" + Regex.Escape(ley) + @"\b", RegexOptions.IgnoreCase);
            var numEnFuentes = Regex.IsMatch(fuentes, @"(?<![\d.])" + Regex.Escape(base_) + @"(?![\d])");
            if (!(leyEnFuentes && numEnFuentes))
            {
                faltan.Add($"art. {m.Groups["num"].Value.Trim()} {ley}");
            }
        }

        foreach (Match m in Regla.Matches(respuesta))
        {
            var num = m.Groups["num"].Value.TrimEnd('.');
            if (!fuentes.Contains(num, StringComparison.Ordinal))
            {
                faltan.Add($"regla {num}.");
            }
        }

        return faltan.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string Aviso(IReadOnlyCollection<string> faltan, string borrador) =>
        "[VERIFICACIÓN AUTOMÁTICA DEL SISTEMA — no lo escribió la persona] Tu respuesta cita " + string.Join(", ", faltan) +
        ", pero eso NO viene en nada de lo que consultaste en esta conversación (puede ser de memoria o estar mal). " +
        "Verifícalo ahora con consultar_leyes o leer_ley y corrige la cita, o quítala. No respondas de memoria. " +
        "Tu borrador fue: «" + (borrador.Length > 1500 ? borrador[..1500] + "…" : borrador) + "»";
}
