using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace IgasConsole.Auditing;

public class GetAuditLogsInput
{
    [Range(1, 168)]
    public int Hours { get; set; } = 24;

    [Range(1, 500)]
    public int MaxResults { get; set; } = 200;

    /// <summary>Solo peticiones con este código o superior. 400 = solo lo que falló.</summary>
    [Range(100, 599)]
    public int? MinStatusCode { get; set; }

    /// <summary>IP exacta. Pensado para pegar una de las que aparecen en el panel de
    /// tráfico bloqueado y ver qué más hizo esa dirección.</summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    /// <summary>Parte del nombre de usuario. Vacío = todos, incluidos los anónimos.</summary>
    [MaxLength(256)]
    public string? UserName { get; set; }
}

public class AuditLogEntryDto
{
    public DateTime ExecutionTime { get; set; }
    public int ExecutionDuration { get; set; }
    public string? HttpMethod { get; set; }
    public string? Url { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ClientIpAddress { get; set; }
    public string? UserName { get; set; }
    public bool HasException { get; set; }
}

public interface IAuditLogsAppService : IApplicationService
{
    Task<List<AuditLogEntryDto>> GetRecentAsync(GetAuditLogsInput input);

    /// <summary>
    /// Las mismas filas que <see cref="GetRecentAsync"/>, en CSV.
    /// <para>Comparte input a propósito: lo que se descarga es exactamente lo que se está
    /// viendo, con los mismos filtros. Un export que trae "todo" cuando en pantalla hay un
    /// filtro puesto sorprende a quien lo abre.</para>
    /// </summary>
    Task<IRemoteStreamContent> GetRecentCsvAsync(GetAuditLogsInput input);
}
