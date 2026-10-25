using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;

namespace AuxiliarContable.Jobs;

/// <summary>
/// Panel de la cola de trabajos en segundo plano.
///
/// <para>Solo lee y hace dos operaciones de mantenimiento. Los trabajos los ejecuta el
/// worker de ABP, no esto: «reintentar» no lanza nada aquí, se limita a dejar la fila en
/// un estado en el que el worker la vuelva a coger.</para>
/// </summary>
// Ver la cola es el permiso base; reintentar y borrar piden el hijo, declarado metodo a
// metodo. Mirar por que fallo algo y volver a lanzarlo —que puede acabar mandando un
// mensaje o tocando datos— no son la misma responsabilidad.
[Authorize(AuxiliarContablePermissions.Jobs.Default)]
public class JobAppService : AuxiliarContableAppService, IJobAppService
{
    private readonly IJobMaintenanceRepository _consulta;
    private readonly IJobFailureRepository _fallos;
    private readonly IBackgroundJobRepository _trabajos;

    public JobAppService(
        IJobMaintenanceRepository consulta,
        IJobFailureRepository fallos,
        IBackgroundJobRepository trabajos)
    {
        _consulta = consulta;
        _fallos = fallos;
        _trabajos = trabajos;
    }

    public async Task<List<JobDto>> GetListAsync(GetJobsInput input)
    {
        var estado = ParsearEstado(input.State);

        var filas = await _consulta.GetRecentAsync(estado, input.MaxResults);

        // Los fallos de TODA la página en una consulta, no uno por fila.
        var ultimos = await _fallos.GetLatestByJobAsync(filas.Select(f => f.Id).ToList());

        return filas.Select(f => Mapear(f, ultimos.GetValueOrDefault(f.Id))).ToList();
    }

    public async Task<JobSummaryDto> GetSummaryAsync()
    {
        var cuentas = await _consulta.GetCountsByStateAsync();

        return new JobSummaryDto
        {
            Esperando = cuentas.GetValueOrDefault(JobState.Esperando),
            Reintentando = cuentas.GetValueOrDefault(JobState.Reintentando),
            Abandonado = cuentas.GetValueOrDefault(JobState.Abandonado),
            Completado = cuentas.GetValueOrDefault(JobState.Completado)
        };
    }

    public async Task<List<JobFailureDto>> GetFailureHistoryAsync(Guid id)
    {
        var historial = await _fallos.GetHistoryAsync(id);

        return historial.Select(MapearFallo).ToList();
    }

    [Authorize(AuxiliarContablePermissions.Jobs.Manage)]
    public async Task<JobDto> RetryAsync(Guid id)
    {
        var trabajo = await _trabajos.FindAsync(id);

        if (trabajo == null)
        {
            throw new UserFriendlyException("Ese trabajo ya no está en la cola.");
        }

        if (trabajo.CompletionTime != null)
        {
            // Reintentar algo que salió bien lo ejecutaría OTRA VEZ. Un aviso duplicado es
            // ruido, pero un cobro o un correo duplicado no, y desde el panel no se puede
            // saber cuál de los dos es.
            throw new UserFriendlyException(
                "Ese trabajo ya se completó. Volver a lanzarlo repetiría lo que hizo.");
        }

        // Esto es TODO lo que hace un reintento: quitar la marca de abandonado y adelantar
        // el próximo intento. El worker de ABP lo recoge en su siguiente pasada (5 s).
        trabajo.IsAbandoned = false;
        trabajo.NextTryTime = DateTime.UtcNow;

        // TryCount NO se pone a cero a propósito: es la cuenta de lo que ya se intentó y
        // ponerla a cero borraría del panel la señal de que esto lleva días fallando.
        //
        // ⚠️ Efecto de dejarla: ABP abandona un trabajo cuando el próximo intento caería
        // más de DefaultTimeout (2 días) después de su CREACIÓN. Un trabajo viejo se
        // ejecuta una vez más y, si vuelve a fallar, se abandona en el acto en lugar de
        // reintentar con espera creciente. Es lo que se quiere: un empujón manual, no
        // reabrir el ciclo entero.
        await _trabajos.UpdateAsync(trabajo, autoSave: true);

        var ultimo = await _fallos.GetLatestByJobAsync(new[] { id });

        return Mapear(Proyectar(trabajo), ultimo.GetValueOrDefault(id));
    }

    [Authorize(AuxiliarContablePermissions.Jobs.Manage)]
    public async Task DeleteAsync(Guid id)
    {
        // El historial de fallos NO se borra con el trabajo: sobrevive a propósito para que
        // «esto ya nos pasó el mes pasado» siga siendo contestable. Lo limpia la purga por
        // retención, no esto.
        await _trabajos.DeleteAsync(id);
    }

    /// <summary>
    /// El estado llega como texto desde la API. Lo que no encaje se trata como «sin filtro»
    /// en vez de lanzar: el panel es de solo lectura y un filtro mal escrito no justifica
    /// romper la pantalla.
    /// </summary>
    private static JobState? ParsearEstado(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        return Enum.TryParse<JobState>(texto, ignoreCase: true, out var estado) ? estado : null;
    }

    private static JobEntry Proyectar(BackgroundJobRecord trabajo)
    {
        return new JobEntry
        {
            Id = trabajo.Id,
            JobName = trabajo.JobName,
            JobArgs = trabajo.JobArgs,
            TryCount = trabajo.TryCount,
            CreationTime = trabajo.CreationTime,
            NextTryTime = trabajo.NextTryTime,
            LastTryTime = trabajo.LastTryTime,
            IsAbandoned = trabajo.IsAbandoned,
            CompletionTime = trabajo.CompletionTime
        };
    }

    private static JobDto Mapear(JobEntry entrada, JobFailure? ultimoFallo)
    {
        return new JobDto
        {
            Id = entrada.Id,
            JobName = entrada.JobName,
            JobArgs = entrada.JobArgs,
            TryCount = entrada.TryCount,
            CreationTime = entrada.CreationTime,
            NextTryTime = entrada.NextTryTime,
            LastTryTime = entrada.LastTryTime,
            CompletionTime = entrada.CompletionTime,
            State = entrada.State.ToString(),
            LastFailure = ultimoFallo == null ? null : MapearFallo(ultimoFallo)
        };
    }

    private static JobFailureDto MapearFallo(JobFailure fallo)
    {
        return new JobFailureDto
        {
            AttemptedUtc = fallo.AttemptedUtc,
            TryCount = fallo.TryCount,
            ExceptionType = fallo.ExceptionType,
            Message = fallo.Message,
            StackTrace = fallo.StackTrace
        };
    }
}
