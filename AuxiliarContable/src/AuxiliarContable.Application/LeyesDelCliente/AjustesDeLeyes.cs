using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.LeyesDelCliente;

/// <summary>
/// Lo que el despacho aceptó de las leyes que subió el cliente y que cambia los cálculos: la tasa y el día del impuesto
/// sobre nóminas, la tasa de recargos estatal y las obligaciones locales (licencias, protección civil…). Lo leen la hoja
/// concentrado y la nómina; nunca llama a Claude.
/// </summary>
public class AjustesDeLeyes : ITransientDependency
{
    private readonly IRepository<HallazgoDeLey, Guid> _hallazgos;
    private readonly IRepository<LeyDelCliente, Guid> _leyes;

    public AjustesDeLeyes(IRepository<HallazgoDeLey, Guid> hallazgos, IRepository<LeyDelCliente, Guid> leyes)
    {
        _hallazgos = hallazgos;
        _leyes = leyes;
    }

    public record Valor(decimal Numero, string Fundamento, string? Estado);

    public record Obligacion(string Titulo, string? Valor, string? Condicion, string? Periodicidad, string? FechaLimite, string Fundamento,
        string Tipo);

    public class Ajustes
    {
        public Valor? IsnTasa { get; init; }
        public Valor? IsnDia { get; init; }
        public Valor? RecargosTasa { get; init; }
        public List<Obligacion> Obligaciones { get; init; } = [];

        public bool Vacio => IsnTasa == null && IsnDia == null && RecargosTasa == null && Obligaciones.Count == 0;
    }

    /// <summary>¿Un valor tiene sentido para su clave? (lo que no, no se acepta: una tasa de 300 % sería un error de lectura)</summary>
    public static bool Razonable(string clave, decimal v) => clave switch
    {
        LeyesDelClienteConsts.IsnTasa => v > 0 && v < 0.1m,
        LeyesDelClienteConsts.RecargosTasa => v > 0 && v < 0.1m,
        LeyesDelClienteConsts.IsnDia => v >= 1 && v <= 31 && v == Math.Floor(v),
        _ => false,
    };

    public async Task<Ajustes> DeAsync(Guid organizacionId)
    {
        var aceptados = await _hallazgos.GetListAsync(h => h.OrganizacionId == organizacionId && h.Decision == DecisionDeHallazgo.Aceptado);
        if (aceptados.Count == 0)
        {
            return new Ajustes();
        }

        var ids = aceptados.Select(h => h.LeyId).Distinct().ToList();
        var leyes = (await _leyes.GetListAsync(l => ids.Contains(l.Id))).ToDictionary(l => l.Id);
        string Fundamento(HallazgoDeLey h) =>
            $"{h.Etiqueta ?? "Art. " + h.Articulo}, {Nombre(leyes.GetValueOrDefault(h.LeyId))} (ley que subió el cliente; aceptada por el despacho)";

        Valor? De(string clave)
        {
            var h = aceptados.Where(x => x.Clave == clave && x.ValorNumerico is { } v && Razonable(clave, v))
                .OrderByDescending(x => x.DecididoUtc).FirstOrDefault();
            return h == null ? null : new Valor(h.ValorNumerico!.Value, Fundamento(h), leyes.GetValueOrDefault(h.LeyId)?.Estado);
        }

        return new Ajustes
        {
            IsnTasa = De(LeyesDelClienteConsts.IsnTasa),
            IsnDia = De(LeyesDelClienteConsts.IsnDia),
            RecargosTasa = De(LeyesDelClienteConsts.RecargosTasa),
            Obligaciones = [.. aceptados.Where(h => h.Clave == null && h.Tipo is "obligacion" or "beneficio")
                .Select(h => new Obligacion(h.Titulo, h.Valor, h.Condicion, h.Periodicidad, h.FechaLimite, Fundamento(h), h.Tipo))],
        };
    }

    private static string Nombre(LeyDelCliente? l) => l?.Titulo ?? l?.Materia ?? l?.NombreDelArchivo ?? "ley local";
}
