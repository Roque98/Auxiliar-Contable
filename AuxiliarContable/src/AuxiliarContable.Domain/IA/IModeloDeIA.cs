using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.IA;

/// <summary>
/// El modelo de IA de la aplicación (Claude Haiku, ver <c>ClaudeModeloDeIA</c> en el host).
///
/// <para>Solo hace UNA cosa, a propósito: leer documentos y devolver datos con un esquema
/// fijo. No hay «chat» ni texto libre: lo que sale de aquí se valida por código antes de
/// guardarse (en un estado de cuenta, que saldo inicial + abonos − cargos = saldo final) y, si
/// no cuadra, va a revisión humana. Ver CLAUDE.md §1.0 y §1.2.8.</para>
///
/// <para>Las llamadas cuestan dinero y tardan segundos: van en trabajos en segundo plano, no en
/// la petición del usuario. Un fallo lanza <see cref="ErrorDeIAException"/>; si es
/// <see cref="ErrorDeIAException.EsReintentable"/>, que lo reintente el trabajo.</para>
/// </summary>
public interface IModeloDeIA
{
    /// <summary>¿Hay API key? Sin ella, <see cref="ExtraerAsync{T}"/> lanza sin llamar a nadie.</summary>
    bool EstaConfigurado { get; }

    /// <summary>
    /// Manda las instrucciones y el contenido y devuelve los datos deserializados a
    /// <typeparamref name="T"/>. El esquema de la solicitud y <typeparamref name="T"/> tienen
    /// que coincidir: las propiedades se leen en camelCase.
    /// </summary>
    Task<ResultadoDeIA<T>> ExtraerAsync<T>(SolicitudDeIA solicitud, CancellationToken cancellationToken = default)
        where T : class;
}

/// <summary>Sin host (DbMigrator, tests) no hay modelo: cualquier uso lanza.</summary>
[Dependency(TryRegister = true)]
[ExposeServices(typeof(IModeloDeIA))]
public class ModeloDeIASinConfigurar : IModeloDeIA, ISingletonDependency
{
    public bool EstaConfigurado => false;

    public Task<ResultadoDeIA<T>> ExtraerAsync<T>(SolicitudDeIA solicitud, CancellationToken cancellationToken = default)
        where T : class
    {
        throw new ErrorDeIAException(MotivoDeErrorDeIA.NoConfigurado, "No hay modelo de IA en este proceso.");
    }
}
