using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.MotorFiscal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace AuxiliarContable.Leyes;

/// <summary>Ver <see cref="ILeyesAppService"/>.</summary>
[Authorize]
public class LeyesAppService : AuxiliarContableAppService, ILeyesAppService
{
    /// <summary>Artículos por página; «Ver más» trae los siguientes.</summary>
    public const int PorPagina = 10;

    private readonly IMotorFiscal _motor;

    public LeyesAppService(IMotorFiscal motor)
    {
        _motor = motor;
    }

    public async Task<PaginaDeLeyesDto> BuscarAsync(BuscarEnLeyesDto input)
    {
        var consulta = (input.Consulta ?? "").Trim();
        if (consulta.Length < 2)
        {
            throw new UserFriendlyException("Escribe qué quieres buscar.");
        }

        IReadOnlyList<ResultadoDeLey> resultados;
        try
        {
            // Uno de más: si llega, hay otra página.
            resultados = await _motor.BuscarEnLeyesAsync(consulta, string.IsNullOrWhiteSpace(input.Ley) ? null : input.Ley.Trim(),
                PorPagina + 1, (Math.Max(input.Pagina, 1) - 1) * PorPagina);
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor fiscal no respondió a una búsqueda en leyes");
            throw new UserFriendlyException("La consulta de leyes no está disponible en este momento. Intenta de nuevo en unos minutos.");
        }

        return new PaginaDeLeyesDto
        {
            HayMas = resultados.Count > PorPagina,
            Resultados = resultados.Take(PorPagina).Select(r => new ArticuloDeLeyDto
        {
            Ley = r.Clave,
            Ordenamiento = r.Ordenamiento,
            Articulo = r.Articulo,
            Ubicacion = r.Ubicacion,
            UltimaReformaDof = r.UltimaReformaDof,
            NotaDeVigencia = r.NotaDeVigencia,
            Url = r.Url,
            Texto = SinEncabezado(r.Texto),
            }).ToList(),
        };
    }

    /// <summary>
    /// El índice antepone a cada artículo «[LEY … — última reforma …]» y «[Ubicación: …]»: en pantalla eso ya va
    /// arriba, separado. Se quitan solo esos renglones entre corchetes (y los vacíos) del principio.
    /// </summary>
    public static string SinEncabezado(string texto)
    {
        var lineas = (texto ?? "").Replace("\r\n", "\n").Split('\n').ToList();
        while (lineas.Count > 0 && lineas[0].Trim() is var l && (l.Length == 0 || (l.StartsWith('[') && l.EndsWith(']'))))
        {
            lineas.RemoveAt(0);
        }

        return string.Join('\n', lineas);
    }
}
