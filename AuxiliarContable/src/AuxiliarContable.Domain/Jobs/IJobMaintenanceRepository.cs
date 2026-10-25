using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AuxiliarContable.Jobs;

/// <summary>
/// Lectura de la cola de trabajos para el panel.
///
/// <para>⚠️ NO se puede usar <c>IBackgroundJobRepository</c> de ABP para esto: solo expone
/// <c>GetWaitingListAsync</c> —trabajos pendientes y ejecutables ahora— además de lo básico
/// de <c>IBasicRepository</c>. Justo lo que interesa mirar (lo abandonado, lo que espera un
/// reintento futuro, lo que acabó bien) queda fuera de esa consulta. Verificado en
/// <c>refs/abp</c>.</para>
///
/// <para>Para leer y escribir UN trabajo por id sí se usa el repositorio de ABP: ahí su API
/// llega, y duplicarla sería mantener dos formas de tocar la misma tabla.</para>
/// </summary>
public interface IJobMaintenanceRepository
{
    /// <summary>
    /// Trabajos más recientes, opcionalmente filtrados por estado.
    /// <para>El filtro se traduce a SQL en vez de derivar el estado en memoria: si no, el
    /// tope de filas se aplicaría antes de filtrar y pedir «abandonados» podría devolver
    /// cero teniendo la cola llena de ellos.</para>
    /// </summary>
    Task<List<JobEntry>> GetRecentAsync(
        JobState? state,
        int maxResults,
        CancellationToken cancellationToken = default);

    /// <summary>Cuántos trabajos hay en cada estado. Es el resumen de la cabecera.</summary>
    Task<Dictionary<JobState, int>> GetCountsByStateAsync(
        CancellationToken cancellationToken = default);
}
