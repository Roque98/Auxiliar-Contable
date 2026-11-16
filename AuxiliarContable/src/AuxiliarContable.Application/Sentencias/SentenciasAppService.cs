using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Authorization;

namespace AuxiliarContable.Sentencias;

/// <summary>Ver <see cref="ISentenciasAppService"/>.</summary>
[Authorize]
public class SentenciasAppService : AuxiliarContableAppService, ISentenciasAppService
{
    /// <summary>Tesis por página; «Ver más» trae las siguientes.</summary>
    public const int PorPagina = 10;

    private const string NoDisponible = "La librería de sentencias no está disponible en este momento. Intenta de nuevo en unos minutos.";

    private readonly IMotorFiscal _motor;
    private readonly AlcanceDeOrganizaciones _alcance;

    public SentenciasAppService(IMotorFiscal motor, AlcanceDeOrganizaciones alcance)
    {
        _motor = motor;
        _alcance = alcance;
    }

    public async Task<PaginaDeSentenciasDto> BuscarAsync(BuscarSentenciasDto input)
    {
        var consulta = (input.Consulta ?? "").Trim();
        if (consulta.Length < 2)
        {
            throw new UserFriendlyException("Escribe qué quieres buscar.");
        }

        var fuente = (input.Fuente ?? "").Trim().ToUpperInvariant();
        if (fuente.Length > 0 && fuente is not ("TFJA" or "SCJN"))
        {
            throw new UserFriendlyException("La fuente es TFJA o SCJN.");
        }

        try
        {
            // Uno de más: si llega, hay otra página.
            var r = await _motor.BuscarSentenciasAsync(consulta, fuente.Length == 0 ? null : fuente, PorPagina + 1,
                (Math.Max(input.Pagina, 1) - 1) * PorPagina);
            return new PaginaDeSentenciasDto { Resultados = r.Take(PorPagina).ToList(), HayMas = r.Count > PorPagina };
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor fiscal no respondió a una búsqueda de sentencias");
            throw new UserFriendlyException(NoDisponible);
        }
    }

    public async Task<EstadoDeLaLibreriaDto> GetEstadoAsync()
    {
        return new EstadoDeLaLibreriaDto
        {
            Estado = await EstadoAsync(),
            PuedeActualizar = await _alcance.VeTodasAsync(),
        };
    }

    public async Task<EstadoDeLaLibreriaDto> ActualizarAsync()
    {
        // Solo el equipo (quien ve a todos los clientes): cada actualización baja de los sitios oficiales y gasta del
        // plan de Claude al estudiar.
        if (!await _alcance.VeTodasAsync())
        {
            throw new AbpAuthorizationException("Solo el equipo del despacho actualiza la librería.");
        }

        try
        {
            if (await _motor.ActualizarSentenciasAsync())
            {
                Logger.LogInformation("Actualización de la librería de sentencias iniciada por {Usuario}", CurrentUser.Id);
            }
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor fiscal no aceptó actualizar las sentencias");
            throw new UserFriendlyException(NoDisponible);
        }

        return new EstadoDeLaLibreriaDto { Estado = await EstadoAsync(), PuedeActualizar = true };
    }

    private async Task<EstadoDeSentencias> EstadoAsync()
    {
        try
        {
            return await _motor.EstadoDeSentenciasAsync();
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor fiscal no dio el estado de las sentencias");
            throw new UserFriendlyException(NoDisponible);
        }
    }
}
