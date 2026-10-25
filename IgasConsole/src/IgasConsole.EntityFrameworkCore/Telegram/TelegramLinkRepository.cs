using System;
using System.Threading;
using System.Threading.Tasks;
using IgasConsole.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace IgasConsole.Telegram;

public class TelegramLinkRepository :
    EfCoreRepository<IgasConsoleDbContext, TelegramLink, Guid>,
    ITelegramLinkRepository
{
    public TelegramLinkRepository(IDbContextProvider<IgasConsoleDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<TelegramLink?> FindByUserIdAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        return await dbContext.Set<TelegramLink>()
            .FirstOrDefaultAsync(v => v.UserId == userId, cancellationToken);
    }

    public async Task<TelegramLink?> FindByTokenHashAsync(
        string tokenHash, DateTime ahoraUtc, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        // La caducidad se comprueba en la CONSULTA, no despues en memoria: asi un token
        // vencido no llega ni a materializarse y no hay forma de usarlo por descuido.
        return await dbContext.Set<TelegramLink>()
            .FirstOrDefaultAsync(
                v => v.TokenHash == tokenHash && v.TokenExpiresUtc > ahoraUtc,
                cancellationToken);
    }
}
