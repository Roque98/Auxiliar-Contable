using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IgasConsole.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;

namespace IgasConsole.Security;

public class SecurityLogFilterSource : ISecurityLogFilterSource, ITransientDependency
{
    private readonly IDbContextProvider<IgasConsoleDbContext> _dbContextProvider;

    public SecurityLogFilterSource(IDbContextProvider<IgasConsoleDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public async Task<List<string>> GetActionsAsync(CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        // DISTINCT sobre una columna de baja cardinalidad: son un puñado de valores aunque
        // la tabla tenga muchas filas, y esto se pide una vez al abrir la pantalla.
        return await dbContext.Set<IdentitySecurityLog>()
            .Where(l => l.Action != null && l.Action != "")
            .Select(l => l.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(cancellationToken);
    }
}
