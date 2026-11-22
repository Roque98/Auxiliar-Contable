using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AuxiliarContable.Concentrado;

/// <summary>
/// Plazo de pago y tasa de recargos del impuesto sobre nómina de cada estado, por ejercicio: un archivo
/// isn_estados_&lt;año&gt;.json por año, sacado de las leyes de hacienda, códigos y leyes de ingresos de ese año del índice de
/// leyes (cada dato con su cita literal). Donde la ley no está en el índice el dato va vacío y la pantalla lo dice.
/// <para>
/// Si el año pedido aún no tiene archivo (p. ej. enero de 2027 antes de cargar las leyes de ingresos 2027), el plazo se
/// toma del último año cargado —sale de la ley de hacienda, que casi nunca cambia— pero la tasa de recargos NO: cambia
/// cada año con la ley de ingresos, así que va vacía y <see cref="Regla.HeredadoDe"/> dice de qué año es el plazo.
/// </para>
/// </summary>
public static class IsnPorEstado
{
    public sealed record Regla(string Clave, string Nombre, int? Dia, string FundamentoPlazo, decimal? Tasa, string FundamentoRecargos)
    {
        /// <summary>Año de las leyes de donde salió el dato.</summary>
        public int Ejercicio { get; init; }

        /// <summary>Si el año pedido no está cargado: el año del que se tomó el plazo (la tasa va vacía).</summary>
        public int? HeredadoDe { get; init; }
    }

    private static readonly Lazy<Dictionary<int, List<Regla>>> PorEjercicio = new(Cargar);

    /// <summary>Años que tienen su tabla, de menor a mayor.</summary>
    public static IReadOnlyList<int> Ejercicios => PorEjercicio.Value.Keys.Order().ToList();

    /// <summary>
    /// Por clave («MOR») o nombre («Morelos», «Ciudad de México»); también un domicilio que lo contenga. Sin año, el
    /// más reciente cargado.
    /// </summary>
    public static Regla? Buscar(string? estado, int? anio = null)
    {
        if (string.IsNullOrWhiteSpace(estado) || PorEjercicio.Value.Count == 0)
        {
            return null;
        }

        var anios = Ejercicios;
        var pedido = anio ?? anios[^1];
        var usado = anios.Where(a => a <= pedido).DefaultIfEmpty(anios[0]).Max();
        var regla = Encontrar(PorEjercicio.Value[usado], estado);
        if (regla == null || usado == pedido)
        {
            return regla;
        }

        return regla with { Tasa = null, FundamentoRecargos = "", HeredadoDe = usado };
    }

    private static Regla? Encontrar(List<Regla> reglas, string estado)
    {
        var x = Normal(estado);
        return reglas.FirstOrDefault(r => Normal(r.Clave) == x || Normal(r.Nombre) == x)
               ?? reglas.OrderByDescending(r => r.Nombre.Length).FirstOrDefault(r => x.Contains(Normal(r.Nombre)))
               ?? (x.Contains("mexico") && !x.Contains("ciudad") ? reglas.FirstOrDefault(r => r.Clave == "MEX") : null);
    }

    private static Dictionary<int, List<Regla>> Cargar()
    {
        var asm = typeof(IsnPorEstado).Assembly;
        var fuera = new Dictionary<int, List<Regla>>();
        foreach (var nombre in asm.GetManifestResourceNames().Where(n => n.StartsWith("AuxiliarContable.Concentrado.isn_estados_") && n.EndsWith(".json")))
        {
            using var s = asm.GetManifestResourceStream(nombre)!;
            using var json = JsonDocument.Parse(s);
            var anio = json.RootElement.GetProperty("ejercicio").GetInt32();
            fuera[anio] = json.RootElement.GetProperty("estados").EnumerateObject().Select(e =>
            {
                var v = e.Value;
                return new Regla(e.Name, v.GetProperty("nombre").GetString()!,
                    v.GetProperty("dia").ValueKind == JsonValueKind.Number ? v.GetProperty("dia").GetInt32() : null,
                    v.GetProperty("fundamentoPlazo").GetString() ?? "",
                    v.GetProperty("tasa").ValueKind == JsonValueKind.Number ? v.GetProperty("tasa").GetDecimal() : null,
                    v.GetProperty("fundamentoRecargos").GetString() ?? "") { Ejercicio = anio };
            }).ToList();
        }

        return fuera;
    }

    private static string Normal(string s) =>
        new string(s.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .ToLowerInvariant().Trim();
}
