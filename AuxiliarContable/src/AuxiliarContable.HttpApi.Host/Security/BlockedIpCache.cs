using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Copia en memoria de las IPs bloqueadas. Es contra esto —y no contra la base— contra lo
/// que decide el middleware en cada petición.
///
/// <para>Singleton a propósito: la lista es una sola para todo el proceso. Y por eso mismo
/// NO puede guardar servicios con ámbito (repositorios, DbContext): se resuelven al vuelo
/// desde <c>IServiceScopeFactory</c> cada vez que toca releer.</para>
/// </summary>
public class BlockedIpCache : IBlockedIpCache, ISingletonDependency
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BlockedIpCache> _logger;

    /// <summary>
    /// Se reemplaza ENTERO en cada refresco en vez de mutarse.
    /// <para>Así el middleware nunca lee un diccionario a medio actualizar y no hace falta
    /// ningún lock en el camino caliente, que es el de todas las peticiones.</para>
    /// </summary>
    private volatile Dictionary<string, DateTime?> _bloqueadas = new(StringComparer.OrdinalIgnoreCase);

    public BlockedIpCache(IServiceScopeFactory scopeFactory, ILogger<BlockedIpCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public bool EstaBloqueada(string? ipAddress)
    {
        if (ipAddress.IsNullOrWhiteSpace())
        {
            return false;
        }

        if (!_bloqueadas.TryGetValue(ipAddress!, out var expira))
        {
            return false;
        }

        // La caducidad se comprueba AQUÍ además de al leer de la base: entre dos refrescos
        // puede vencer un bloqueo, y no tiene sentido seguir vetando a alguien cuyo castigo
        // ya terminó solo porque toque releer dentro de veinte segundos.
        return expira == null || expira.Value > DateTime.UtcNow;
    }

    public async Task RefrescarAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var repositorio = scope.ServiceProvider.GetRequiredService<IBlockedIpRepository>();

            // Unidad de trabajo a mano: esto puede correr desde un worker, no solo desde
            // una petición, y sin ella el DbContext no se puede abrir.
            using var uow = uowManager.Begin(requiresNew: true);

            var vigentes = await repositorio.GetVigentesAsync(DateTime.UtcNow);

            var nuevo = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
            foreach (var fila in vigentes)
            {
                nuevo[fila.IpAddress] = fila.ExpiresUtc;
            }

            _bloqueadas = nuevo;

            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            // Que falle el refresco NO puede tumbar la aplicación ni vaciar la lista: se
            // conserva la copia anterior. Preferimos seguir bloqueando con datos de hace un
            // minuto a dejar entrar a todo el mundo porque la base tuvo un hipo.
            _logger.LogWarning(ex, "No se pudo refrescar la lista de IPs bloqueadas; se mantiene la anterior.");
        }
    }
}
