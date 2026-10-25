using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace IgasConsole.Jobs;

/// <summary>
/// Un intento fallido de ejecutar un trabajo en segundo plano.
///
/// <para>⚠️ EXISTE PORQUE ABP NO GUARDA EL MOTIVO DEL FALLO. <c>BackgroundJobRecord</c>
/// —la tabla <c>AbpBackgroundJobs</c>— tiene <c>TryCount</c>, <c>NextTryTime</c> y
/// <c>IsAbandoned</c>, pero ni un solo campo para la excepción (verificado en
/// <c>refs/abp</c>). Sin esto, un panel de trabajos puede decir «falló cinco veces» y no
/// «falló porque el token del bot está caducado», que es la única mitad que sirve para
/// arreglar algo. La otra mitad ya la escribe el framework.</para>
///
/// <para>Es una tabla APARTE en vez de columnas añadidas a la de ABP: la del framework la
/// mapea <c>ConfigureBackgroundJobs()</c> y ampliarla ataría cada actualización del módulo
/// a una migración nuestra.</para>
///
/// <para>Una fila POR INTENTO, no una por trabajo. Un trabajo que falla siempre por lo
/// mismo y otro que falla por una causa distinta cada vez son problemas diferentes, y con
/// solo el último error los dos se ven igual.</para>
///
/// <para>No es multi-tenant (los trabajos de ABP tampoco lo son a nivel de fila) ni lleva
/// soft delete: la purga por retención tiene que borrar de verdad.</para>
/// </summary>
public class JobFailure : Entity<Guid>
{
    /// <summary>
    /// Id de la fila de <c>AbpBackgroundJobs</c>. No es una FK declarada: el trabajo
    /// desaparece al completarse o al borrarlo desde el panel, y una FK obligaría a
    /// perder el historial justo cuando por fin sale bien —que es cuando más interesa
    /// saber cuánto costó.
    /// </summary>
    public Guid JobId { get; protected set; }

    /// <summary>
    /// Nombre del trabajo (el de <c>[BackgroundJobName]</c>). Se copia aquí porque la fila
    /// original puede haberse borrado ya cuando alguien mire el historial.
    /// </summary>
    public string JobName { get; protected set; }

    public DateTime AttemptedUtc { get; protected set; }

    /// <summary>Qué número de intento era. Con esto se lee la progresión de la espera.</summary>
    public int TryCount { get; protected set; }

    public string ExceptionType { get; protected set; }

    public string Message { get; protected set; }

    public string? StackTrace { get; protected set; }

    protected JobFailure()
    {
        JobName = string.Empty;
        ExceptionType = string.Empty;
        Message = string.Empty;
    }

    public JobFailure(
        Guid id,
        Guid jobId,
        string jobName,
        DateTime attemptedUtc,
        int tryCount,
        string exceptionType,
        string message,
        string? stackTrace)
        : base(id)
    {
        if (attemptedUtc.Kind != DateTimeKind.Utc)
        {
            throw new AbpException(
                "JobFailure.AttemptedUtc debe ser UTC: la columna es timestamptz y Npgsql " +
                "lanza en tiempo de ejecución si el Kind no es Utc.");
        }

        JobId = jobId;
        AttemptedUtc = attemptedUtc;
        TryCount = tryCount;

        // Se trunca en vez de rechazar: este constructor corre DENTRO del manejo de un
        // fallo. Reventar aquí por un mensaje largo cambiaría un trabajo que falla por un
        // trabajo que además no deja rastro de por qué.
        JobName = Recortar(jobName, JobConsts.MaxJobNameLength) ?? string.Empty;
        ExceptionType = Recortar(exceptionType, JobConsts.MaxExceptionTypeLength) ?? string.Empty;
        Message = Recortar(message, JobConsts.MaxMessageLength) ?? string.Empty;
        StackTrace = Recortar(stackTrace, JobConsts.MaxStackTraceLength);
    }

    private static string? Recortar(string? texto, int maximo)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return texto;
        }

        return texto.Length <= maximo ? texto : texto[..maximo];
    }
}
