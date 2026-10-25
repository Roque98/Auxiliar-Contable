using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.EntityFrameworkCore;
using AuxiliarContable.Monitoring;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.AuditLogging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace AuxiliarContable.Security;

/// <summary>
/// Saca las IPs sospechosas de los rollups que ya escribe el rate limiting.
///
/// <para>Esta clase es el ÚNICO punto donde seguridad y monitoreo se tocan, y está en la
/// capa de infraestructura a propósito: es la única que conoce las dos tablas. Los módulos
/// de dominio siguen sin conocerse.</para>
/// </summary>
public class AbusiveIpSource : IAbusiveIpSource, ITransientDependency
{
    private readonly IDbContextProvider<AuxiliarContableDbContext> _dbContextProvider;

    public AbusiveIpSource(IDbContextProvider<AuxiliarContableDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public async Task<List<IpAbusiva>> GetAsync(
        DateTime desdeUtc,
        int minimoRechazos,
        int maxResultados,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        // Se agrupa, se filtra y se ordena en la BASE, no en memoria: en una ráfaga esta
        // tabla tiene una fila por IP, endpoint y minuto, y traérsela entera para sumarla
        // aquí sería justo lo que no hay que hacer cuando la aplicación está bajo carga.
        //
        // ⚠️ La proyección es un TIPO ANÓNIMO y no el record directamente: EF Core no sabe
        // traducir la construcción de un record posicional dentro de un GroupBy y lanza
        // «The LINQ expression could not be translated». Con el tipo anónimo lo traduce, y
        // el record se construye después sobre las pocas filas que ya devolvió la base.
        var filas = await dbContext.Set<RateLimitEvent>()
            .Where(e => e.LastRejectedUtc >= desdeUtc)
            .GroupBy(e => e.IpAddress)
            .Select(g => new { Ip = g.Key, Rechazos = g.Sum(e => e.RejectionCount) })
            .Where(x => x.Rechazos >= minimoRechazos)
            .OrderByDescending(x => x.Rechazos)
            .Take(maxResultados)
            .ToListAsync(cancellationToken);

        return filas.Select(x => new IpAbusiva(x.Ip, x.Rechazos)).ToList();
    }

    public async Task<List<IpEscaneando>> GetScannersAsync(
        DateTime desdeUtc,
        int minimoFallos,
        int minimoPorcentajeFallo,
        int maxResultados,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        // La señal sale del REGISTRO DE PETICIONES, no de los rechazos del rate limiter:
        // un escaneador se queda por debajo de la cuota y por eso nunca aparece allí.
        //
        // Se agrupa en la base por el mismo motivo que la otra consulta, y se filtra
        // primero por fecha, que es la parte indexada (IX_AbpAuditLogs_TenantId_ExecutionTime).
        //
        // ⚠️ Tipo anónimo y no el record dentro del GroupBy: EF Core no traduce la
        // construcción de un record posicional ahí. Misma trampa que arriba.
        var filas = await dbContext.Set<AuditLog>()
            .Where(a => a.ExecutionTime >= desdeUtc && a.ClientIpAddress != null)
            .GroupBy(a => a.ClientIpAddress!)
            .Select(g => new
            {
                Ip = g.Key,
                Total = g.Count(),
                Fallos = g.Count(a => a.HttpStatusCode >= 400),
                Rutas = g.Select(a => a.Url).Distinct().Count()
            })
            .Where(x => x.Fallos >= minimoFallos
                        && x.Fallos * 100 / x.Total >= minimoPorcentajeFallo)
            .OrderByDescending(x => x.Fallos)
            .Take(maxResultados)
            .ToListAsync(cancellationToken);

        return filas
            .Select(x => new IpEscaneando(x.Ip, x.Fallos, x.Rutas, x.Fallos * 100 / x.Total))
            .ToList();
    }
}
