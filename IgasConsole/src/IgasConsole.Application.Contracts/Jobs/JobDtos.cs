using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace IgasConsole.Jobs;

public class JobFailureDto
{
    public DateTime AttemptedUtc { get; set; }

    public int TryCount { get; set; }

    public string ExceptionType { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? StackTrace { get; set; }
}

public class JobDto
{
    public Guid Id { get; set; }

    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// Los argumentos tal cual se guardaron (JSON).
    /// <para>Se muestran a propósito: sin ellos, «falló el envío del enlace» no dice a quién.
    /// Y se pueden mostrar precisamente porque en este proyecto los argumentos NUNCA llevan
    /// secretos —el enlace de recuperación encola el id de usuario y genera el token dentro
    /// del trabajo—. Esta pantalla es la razón práctica de esa regla: cualquiera con permiso
    /// para mirar la cola lee este campo entero.</para>
    /// </summary>
    public string JobArgs { get; set; } = string.Empty;

    public int TryCount { get; set; }

    public DateTime CreationTime { get; set; }

    /// <summary>Cuándo volverá a intentarse. Sin sentido si ya está abandonado o completado.</summary>
    public DateTime NextTryTime { get; set; }

    public DateTime? LastTryTime { get; set; }

    public DateTime? CompletionTime { get; set; }

    /// <summary>Ver <c>JobState</c>: se deriva en el servidor, no en la pantalla.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// El último fallo, si lo hubo. Null en un trabajo que aún no se ha intentado —y también
    /// en uno que falló antes de que existiera esta pantalla.
    /// </summary>
    public JobFailureDto? LastFailure { get; set; }
}

public class GetJobsInput
{
    /// <summary>
    /// Nombre del estado (<c>Esperando</c>, <c>Reintentando</c>, <c>Abandonado</c>,
    /// <c>Completado</c>). Vacío = todos.
    /// <para>Viaja como texto y no como entero para que la URL se lea sola y no dependa del
    /// orden en que estén declarados los valores del enum.</para>
    /// </summary>
    public string? State { get; set; }

    [Range(1, JobConsts.MaxResults)]
    public int MaxResults { get; set; } = 50;
}

public class JobSummaryDto
{
    public int Esperando { get; set; }

    public int Reintentando { get; set; }

    public int Abandonado { get; set; }

    public int Completado { get; set; }
}

/// <summary>
/// Panel de la cola de trabajos en segundo plano.
///
/// <para>Existe porque un trabajo que falla no se ve por ningún otro sitio: ABP reintenta
/// con espera creciente y lo abandona a los dos días, todo en silencio. Los avisos de
/// monitoreo y los enlaces de recuperación de contraseña viven ahí.</para>
/// </summary>
public interface IJobAppService : IApplicationService
{
    Task<List<JobDto>> GetListAsync(GetJobsInput input);

    Task<JobSummaryDto> GetSummaryAsync();

    /// <summary>Todos los intentos fallidos de un trabajo, del más reciente al más antiguo.</summary>
    Task<List<JobFailureDto>> GetFailureHistoryAsync(Guid id);

    /// <summary>
    /// Vuelve a poner en cola un trabajo abandonado (o adelanta el próximo intento de uno
    /// que está esperando).
    /// </summary>
    Task<JobDto> RetryAsync(Guid id);

    /// <summary>Lo saca de la cola para siempre. Para lo que nunca va a funcionar.</summary>
    Task DeleteAsync(Guid id);
}
