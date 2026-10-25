using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Security;

public interface IBlockedIpRepository : IRepository<BlockedIp, Guid>
{
    /// <summary>Todas, las más recientes primero. Incluye las caducadas: en el panel
    /// interesa ver que algo estuvo bloqueado y ya no lo está.</summary>
    Task<List<BlockedIp>> GetAllAsync(int maxResults, CancellationToken cancellationToken = default);

    /// <summary>Solo las que aplican ahora mismo. Es lo que carga el middleware.</summary>
    Task<List<BlockedIp>> GetVigentesAsync(DateTime ahoraUtc, CancellationToken cancellationToken = default);

    Task<BlockedIp?> FindByIpAsync(string ipAddress, CancellationToken cancellationToken = default);
}
