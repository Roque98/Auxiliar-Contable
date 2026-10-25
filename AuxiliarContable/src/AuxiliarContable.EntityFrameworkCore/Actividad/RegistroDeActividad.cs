using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.AuditLogging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;

namespace AuxiliarContable.Actividad;

/// <summary>Ver <see cref="IRegistroDeActividad"/>. Solo lectura, proyectando a read models.</summary>
public class RegistroDeActividad : IRegistroDeActividad, ITransientDependency
{
    private readonly IDbContextProvider<AuxiliarContableDbContext> _dbContextProvider;

    public RegistroDeActividad(IDbContextProvider<AuxiliarContableDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public async Task<List<AccionRegistrada>> AccionesAsync(
        DateTime desde,
        DateTime antesDe,
        IReadOnlyCollection<string> servicios,
        IReadOnlyCollection<string> metodos,
        IReadOnlyCollection<Guid>? usuarios,
        int maximo,
        CancellationToken cancellationToken = default)
    {
        var db = await _dbContextProvider.GetDbContextAsync();

        // El rango de fechas primero: es lo indexado (IX_AbpAuditLogs_TenantId_ExecutionTime).
        var registros = db.Set<AuditLog>()
            .Where(a => a.ExecutionTime >= desde && a.ExecutionTime < antesDe && a.UserId != null)
            .Where(a => (a.HttpStatusCode == null || a.HttpStatusCode < 400) && (a.Exceptions == null || a.Exceptions == ""));

        if (usuarios != null)
        {
            registros = registros.Where(a => usuarios.Contains(a.UserId!.Value));
        }

        return await registros
            .SelectMany(a => a.Actions
                .Where(x => servicios.Contains(x.ServiceName) && metodos.Contains(x.MethodName))
                .Select(x => new AccionRegistrada
                {
                    Fecha = a.ExecutionTime,
                    UserId = a.UserId!.Value,
                    Ip = a.ClientIpAddress,
                    Servicio = x.ServiceName,
                    Metodo = x.MethodName,
                    Parametros = x.Parameters,
                }))
            .OrderByDescending(x => x.Fecha)
            .Take(maximo)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SesionRegistrada>> SesionesAsync(
        DateTime desde,
        DateTime antesDe,
        IReadOnlyCollection<string> acciones,
        IReadOnlyCollection<Guid>? usuarios,
        int maximo,
        CancellationToken cancellationToken = default)
    {
        var db = await _dbContextProvider.GetDbContextAsync();

        var consulta = db.Set<IdentitySecurityLog>()
            .Where(s => s.CreationTime >= desde && s.CreationTime < antesDe && s.UserId != null)
            .Where(s => acciones.Contains(s.Action));

        if (usuarios != null)
        {
            consulta = consulta.Where(s => usuarios.Contains(s.UserId!.Value));
        }

        return await consulta
            .OrderByDescending(s => s.CreationTime)
            .Take(maximo)
            .Select(s => new SesionRegistrada
            {
                Fecha = s.CreationTime,
                UserId = s.UserId!.Value,
                Accion = s.Action,
                Ip = s.ClientIpAddress,
                Navegador = s.BrowserInfo,
            })
            .ToListAsync(cancellationToken);
    }
}
