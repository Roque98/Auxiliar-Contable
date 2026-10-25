using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuxiliarContable.ApiKeys;

public class ApiKeyRepository :
    EfCoreRepository<AuxiliarContableDbContext, ApiKey, Guid>,
    IApiKeyRepository
{
    public ApiKeyRepository(IDbContextProvider<AuxiliarContableDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<List<ApiKey>> GetByUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        return await dbContext.Set<ApiKey>()
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreationTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<ApiKey?> FindByHashAsync(
        string keyHash, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        // IgnoreQueryFilters: la clave llega ANTES de que haya tenant resuelto —es
        // precisamente lo que se está averiguando—, así que el filtro de multi-tenancy
        // escondería la fila y ninguna clave de un tenant funcionaría jamás.
        return await dbContext.Set<ApiKey>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash, cancellationToken);
    }

    public async Task<List<ApiKey>> GetExpiringAsync(
        DateTime ahoraUtc,
        DateTime hastaUtc,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        // IgnoreQueryFilters, igual que la búsqueda por hash: esto lo llama un worker, que
        // no tiene tenant. Sin ello solo vería las claves del host y las de los tenants
        // caducarían siempre en silencio — que es justo lo que se viene a evitar.
        // El aviso se dirige luego al dueño y al tenant que guarda la propia clave.
        return await dbContext.Set<ApiKey>()
            .IgnoreQueryFilters()
            .Where(k => k.RevokedUtc == null
                        && k.ExpiresUtc != null
                        && k.ExpiresUtc > ahoraUtc
                        && k.ExpiresUtc <= hastaUtc)
            .OrderBy(k => k.ExpiresUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> ContarUtilizablesAsync(
        Guid? tenantId,
        DateTime ahoraUtc,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        // IgnoreQueryFilters y filtro de inquilino a mano, por lo mismo que el resto del
        // repositorio: `ApiKey` no es `IMultiTenant` —guarda el TenantId como una columna
        // normal para poder encontrarla ANTES de saber de qué inquilino es (§6.05)—, así que
        // aquí el filtro se escribe. Sin esta línea la cuota contaría las claves de TODA la
        // instalación y el primer inquilino en llegar dejaría a los demás sin sitio.
        return await dbContext.Set<ApiKey>()
            .IgnoreQueryFilters()
            .Where(k => k.TenantId == tenantId
                        && k.RevokedUtc == null
                        && (k.ExpiresUtc == null || k.ExpiresUtc > ahoraUtc))
            .CountAsync(cancellationToken);
    }
}
