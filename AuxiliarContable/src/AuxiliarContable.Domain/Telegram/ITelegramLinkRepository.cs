using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Telegram;

public interface ITelegramLinkRepository : IRepository<TelegramLink, Guid>
{
    Task<TelegramLink?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Busca una vinculación pendiente por el HASH del token (nunca por el token
    /// en claro, que no se guarda). Devuelve null si no existe o si ya caducó.</summary>
    Task<TelegramLink?> FindByTokenHashAsync(
        string tokenHash, DateTime ahoraUtc, CancellationToken cancellationToken = default);
}
