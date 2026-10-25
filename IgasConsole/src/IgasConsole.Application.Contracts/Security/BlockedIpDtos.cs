using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace IgasConsole.Security;

public class BlockedIpDto
{
    public Guid Id { get; set; }

    public string IpAddress { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public DateTime CreationTime { get; set; }

    public DateTime? ExpiresUtc { get; set; }

    /// <summary>True si lo puso el evaluador automático. Cambia cómo se lee la fila: uno
    /// automático es la consecuencia de un umbral y caduca solo; uno manual es una decisión
    /// que alguien tomó.</summary>
    public bool IsAutomatic { get; set; }

    /// <summary>False si ya caducó. Se calcula en el servidor para que la pantalla no
    /// tenga que replicar la regla —ni depender del reloj del navegador, que puede ir mal.</summary>
    public bool IsActive { get; set; }
}

public class BlockIpInput
{
    [Required]
    [MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Cuántas horas dura el bloqueo. Null = indefinido.
    /// <para>Se pide en horas y no como fecha para no depender de la zona horaria ni del
    /// reloj del navegador: el servidor calcula el instante con el suyo.</para>
    /// </summary>
    [Range(1, 8760)]
    public int? Hours { get; set; }
}

/// <summary>
/// Administra la lista de IPs vetadas. Permiso propio
/// (<c>IgasConsole.Metrics.ManageBlockedIps</c>): ver métricas y dejar a alguien fuera de la
/// aplicación no son la misma responsabilidad.
/// </summary>
public interface IBlockedIpAppService : IApplicationService
{
    Task<List<BlockedIpDto>> GetListAsync();

    Task<BlockedIpDto> BlockAsync(BlockIpInput input);

    Task UnblockAsync(Guid id);
}
