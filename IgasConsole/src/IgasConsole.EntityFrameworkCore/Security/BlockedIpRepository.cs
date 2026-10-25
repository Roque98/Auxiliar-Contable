using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IgasConsole.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace IgasConsole.Security;

public class BlockedIpRepository :
    EfCoreRepository<IgasConsoleDbContext, BlockedIp, Guid>,
    IBlockedIpRepository
{
    public BlockedIpRepository(IDbContextProvider<IgasConsoleDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<List<BlockedIp>> GetAllAsync(
        int maxResults, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        return await dbContext.Set<BlockedIp>()
            .OrderByDescending(b => b.CreationTime)
            .Take(maxResults)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<BlockedIp>> GetVigentesAsync(
        DateTime ahoraUtc, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        // El filtro va en SQL y no con EstaVigente(): un método del dominio no lo puede
        // traducir EF y traería la tabla entera a memoria para filtrarla aquí.
        return await dbContext.Set<BlockedIp>()
            .Where(b => b.ExpiresUtc == null || b.ExpiresUtc > ahoraUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<BlockedIp?> FindByIpAsync(
        string ipAddress, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        return await dbContext.Set<BlockedIp>()
            .FirstOrDefaultAsync(b => b.IpAddress == ipAddress, cancellationToken);
    }
}
