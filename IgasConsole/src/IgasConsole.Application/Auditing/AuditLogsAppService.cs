using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using IgasConsole.Exporting;
using IgasConsole.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Auditing;
using Volo.Abp.Content;

namespace IgasConsole.Auditing;

/// <summary>
/// Lectura del registro de peticiones que ya escribe la auditoría de ABP. Solo lee: la
/// escritura la hace el propio framework en cada petición.
/// </summary>
[Authorize(IgasConsolePermissions.AuditLogs.Default)]
[DisableAuditing]
public class AuditLogsAppService : IgasConsoleAppService, IAuditLogsAppService
{
    private readonly IAuditLogMaintenanceRepository _auditLogRepository;

    public AuditLogsAppService(IAuditLogMaintenanceRepository auditLogRepository)
    {
        _auditLogRepository = auditLogRepository;
    }

    public async Task<List<AuditLogEntryDto>> GetRecentAsync(GetAuditLogsInput input)
    {
        var desde = DateTime.UtcNow.AddHours(-input.Hours);

        var entradas = await _auditLogRepository.GetRecentAsync(
            desde, input.MaxResults, input.MinStatusCode, input.IpAddress, input.UserName);

        return entradas.Select(Mapear).ToList();
    }

    public async Task<IRemoteStreamContent> GetRecentCsvAsync(GetAuditLogsInput input)
    {
        // Se reutiliza GetRecentAsync en vez de repetir la consulta: así el CSV y la tabla
        // no pueden discrepar nunca, ni siquiera si mañana cambian los filtros.
        var entradas = await GetRecentAsync(input);

        var csv = EscritorCsv.Generar(
            ["Instante", "Duración (ms)", "Método", "URL", "Código", "IP", "Usuario", "Excepción"],
            entradas.Select(e => new[]
            {
                EscritorCsv.Fecha(e.ExecutionTime),
                EscritorCsv.Numero(e.ExecutionDuration),
                e.HttpMethod,
                e.Url,
                e.HttpStatusCode?.ToString(),
                e.ClientIpAddress,
                e.UserName,
                e.HasException ? "sí" : "no"
            }));

        return new RemoteStreamContent(
            new MemoryStream(csv),
            EscritorCsv.NombreConFecha("registro-peticiones"),
            "text/csv");
    }

    private static AuditLogEntryDto Mapear(AuditLogEntry e)
    {
        return new AuditLogEntryDto
        {
            ExecutionTime = e.ExecutionTime,
            ExecutionDuration = e.ExecutionDuration,
            HttpMethod = e.HttpMethod,
            Url = e.Url,
            HttpStatusCode = e.HttpStatusCode,
            ClientIpAddress = e.ClientIpAddress,
            UserName = e.UserName,
            HasException = e.HasException
        };
    }
}
