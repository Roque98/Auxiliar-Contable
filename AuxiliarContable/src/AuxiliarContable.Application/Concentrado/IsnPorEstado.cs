using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AuxiliarContable.Concentrado;

/// <summary>
/// Plazo de pago y tasa de recargos del impuesto sobre nómina de cada estado, sacados de sus leyes de hacienda, códigos y
/// leyes de ingresos 2026 del índice de leyes (isn_estados.json, revisado el 21-nov-2026). Donde la ley no está en el
/// índice el dato va vacío y la pantalla lo dice: no se inventa.
/// </summary>
public static class IsnPorEstado
{
    public sealed record Regla(string Clave, string Nombre, int? Dia, string FundamentoPlazo, decimal? Tasa, string FundamentoRecargos);

    private static readonly Lazy<List<Regla>> Reglas = new(Cargar);

    /// <summary>Por clave («MOR») o nombre («Morelos», «Ciudad de México»); también un domicilio que lo contenga.</summary>
    public static Regla? Buscar(string? estado)
    {
        if (string.IsNullOrWhiteSpace(estado))
        {
            return null;
        }

        var x = Normal(estado);
        return Reglas.Value.FirstOrDefault(r => Normal(r.Clave) == x || Normal(r.Nombre) == x)
               ?? Reglas.Value.OrderByDescending(r => r.Nombre.Length).FirstOrDefault(r => x.Contains(Normal(r.Nombre)))
               ?? (x.Contains("mexico") && !x.Contains("ciudad") ? Reglas.Value.FirstOrDefault(r => r.Clave == "MEX") : null);
    }

    private static List<Regla> Cargar()
    {
        using var s = typeof(IsnPorEstado).Assembly.GetManifestResourceStream("AuxiliarContable.Concentrado.isn_estados.json")
                      ?? throw new InvalidOperationException("Falta isn_estados.json embebido.");
        using var json = JsonDocument.Parse(s);
        return json.RootElement.GetProperty("estados").EnumerateObject().Select(e =>
        {
            var v = e.Value;
            return new Regla(e.Name, v.GetProperty("nombre").GetString()!,
                v.GetProperty("dia").ValueKind == JsonValueKind.Number ? v.GetProperty("dia").GetInt32() : null,
                v.GetProperty("fundamentoPlazo").GetString() ?? "",
                v.GetProperty("tasa").ValueKind == JsonValueKind.Number ? v.GetProperty("tasa").GetDecimal() : null,
                v.GetProperty("fundamentoRecargos").GetString() ?? "");
        }).ToList();
    }

    private static string Normal(string s) =>
        new string(s.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .ToLowerInvariant().Trim();
}
