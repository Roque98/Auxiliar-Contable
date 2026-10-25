using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.Documentos;

/// <summary>
/// Antivirus de lo que suben los clientes (en producción, ClamAV del propio VPS: <c>ClamAvEscaner</c>
/// en el host). Se llama ANTES de guardar: un archivo infectado no llega ni cifrado al disco.
/// </summary>
public interface IEscanerDeArchivos
{
    Task<ResultadoDelEscaneo> EscanearAsync(byte[] contenido, CancellationToken cancellationToken = default);
}

public enum VeredictoDelEscaneo
{
    Limpio,
    Infectado,

    /// <summary>No hay antivirus o no respondió. Qué se hace lo decide <see cref="GestorDeDocumentos"/>.</summary>
    NoDisponible,
}

public sealed record ResultadoDelEscaneo(VeredictoDelEscaneo Veredicto, string? Detalle = null)
{
    public static readonly ResultadoDelEscaneo Limpio = new(VeredictoDelEscaneo.Limpio);
}

/// <summary>Sin host (tests, DbMigrator) o sin ClamAV configurado: no se escanea.</summary>
[Dependency(TryRegister = true)]
[ExposeServices(typeof(IEscanerDeArchivos))]
public class SinEscaner : IEscanerDeArchivos, ISingletonDependency
{
    public Task<ResultadoDelEscaneo> EscanearAsync(byte[] contenido, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResultadoDelEscaneo(VeredictoDelEscaneo.NoDisponible, "sin antivirus configurado"));
}
