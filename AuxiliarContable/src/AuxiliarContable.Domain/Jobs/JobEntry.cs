using System;

namespace AuxiliarContable.Jobs;

/// <summary>
/// En qué punto está un trabajo.
///
/// <para>⚠️ NO es una columna: <c>BackgroundJobRecord</c> no guarda un estado, lo deja
/// implícito en la combinación de <c>CompletionTime</c>, <c>IsAbandoned</c> y
/// <c>TryCount</c>. Se deriva en un solo sitio para que la pantalla no tenga que repetir
/// esa lógica —y para que el filtro del panel signifique exactamente lo mismo que la
/// etiqueta de la fila.</para>
/// </summary>
public enum JobState
{
    /// <summary>Encolado y aún sin intentar.</summary>
    Esperando = 0,

    /// <summary>Ya falló al menos una vez y espera al siguiente intento.</summary>
    Reintentando = 1,

    /// <summary>Falló durante dos días seguidos: ABP no volverá a intentarlo solo.</summary>
    Abandonado = 2,

    /// <summary>Terminó bien. Solo aparece si se conservan los completados.</summary>
    Completado = 3
}

/// <summary>
/// Proyección de <c>BackgroundJobRecord</c> para el panel, con el estado ya derivado.
///
/// <para>Es un read model y no la entidad de ABP porque <c>JobArgs</c> puede llegar a un
/// megabyte (así lo mapea el módulo) y una pantalla no debería arrastrar el agregado
/// entero por fila. Además, el estado que se pinta se calcula aquí una sola vez.</para>
/// </summary>
public class JobEntry
{
    public Guid Id { get; set; }

    public string JobName { get; set; } = string.Empty;

    public string JobArgs { get; set; } = string.Empty;

    public int TryCount { get; set; }

    public DateTime CreationTime { get; set; }

    public DateTime NextTryTime { get; set; }

    public DateTime? LastTryTime { get; set; }

    public bool IsAbandoned { get; set; }

    public DateTime? CompletionTime { get; set; }

    public JobState State => Derivar(IsAbandoned, CompletionTime, TryCount);

    /// <summary>
    /// El orden importa: un trabajo abandonado nunca tiene <c>CompletionTime</c>, pero uno
    /// completado tras un reintento SÍ tiene <c>TryCount &gt; 0</c>. Comprobar primero el
    /// éxito evita que un trabajo que acabó bien se lea como «reintentando».
    /// </summary>
    public static JobState Derivar(bool isAbandoned, DateTime? completionTime, int tryCount)
    {
        if (completionTime != null)
        {
            return JobState.Completado;
        }

        if (isAbandoned)
        {
            return JobState.Abandonado;
        }

        return tryCount > 0 ? JobState.Reintentando : JobState.Esperando;
    }
}
