using System;
using Volo.Abp.DependencyInjection;

namespace IgasConsole.BackgroundJobs;

/// <summary>
/// Buzón de una sola casilla para pasar la excepción del ejecutor al worker.
///
/// <para>Hace falta porque las dos piezas de ABP que saben cada mitad del problema no se
/// hablan: <c>BackgroundJobExecuter</c> tiene la excepción pero no sabe de qué fila de la
/// base viene, y <c>BackgroundJobWorker</c> tiene la fila pero recibe el fallo ya
/// convertido en un <c>catch</c> sin parámetros (verificado en <c>refs/abp</c>,
/// <c>BackgroundJobWorker.TryExecuteJobAsync</c>).</para>
///
/// <para>⚠️ NO vale un <c>AsyncLocal</c>: un valor asignado dentro de un método llamado
/// no vuelve al llamador, y el sentido de la escritura es justo ese —del ejecutor, que
/// está más adentro, hacia el worker, que está más afuera—.</para>
///
/// <para>Es SCOPED, y de ahí sale su seguridad frente a ejecuciones simultáneas: cuando el
/// worker corre trabajos en paralelo abre un ámbito por trabajo, así que cada uno tiene su
/// propia casilla; cuando corre en serie (lo de serie es el modo por defecto) comparten
/// ámbito, pero se ejecutan uno detrás de otro y <see cref="Tomar"/> vacía la casilla al
/// leerla.</para>
/// </summary>
public interface IUltimoFalloDeJob
{
    void Registrar(Exception excepcion);

    /// <summary>Devuelve la excepción y vacía la casilla.</summary>
    Exception? Tomar();
}

public class UltimoFalloDeJob : IUltimoFalloDeJob, IScopedDependency
{
    private Exception? _excepcion;

    public void Registrar(Exception excepcion)
    {
        _excepcion = excepcion;
    }

    public Exception? Tomar()
    {
        var actual = _excepcion;
        _excepcion = null;
        return actual;
    }
}
