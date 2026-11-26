using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Conversaciones;

/// <summary>
/// Solicitudes entre el cliente y el despacho. El cliente consulta todo lo suyo pero no cambia su contabilidad: lo que
/// quiere cambiar o aclarar se lo pide al despacho, que lo atiende o lo rechaza con una respuesta. El despacho, a su vez,
/// le pide documentos o datos al cliente. Ver al cliente basta para pedir (pedir no cambia nada); lo que se le pide al
/// despacho solo lo resuelve el despacho, y lo que se le pide al cliente, el cliente o el despacho. Lo ajeno = 404.
/// </summary>
public interface ISolicitudesAppService : IApplicationService
{
    /// <summary>Las del cliente, las pendientes primero y luego las más recientes.</summary>
    Task<List<SolicitudDto>> GetListAsync(Guid organizacionId, bool soloPendientes = false);

    /// <summary>
    /// Pide algo. El sentido lo pone quien pide: el cliente le pide al despacho; el despacho, al cliente.
    /// </summary>
    Task<SolicitudDto> CrearAsync(Guid organizacionId, CrearSolicitudDto input);

    /// <summary>Atendida o rechazada (rechazar pide la razón).</summary>
    Task<SolicitudDto> ResolverAsync(Guid organizacionId, Guid solicitudId, ResolverSolicitudDto input);

    /// <summary>Solo el despacho: lo que los clientes le han pedido y sigue pendiente, de toda la cartera.</summary>
    Task<List<SolicitudDto>> GetPendientesDelDespachoAsync();
}

public class SolicitudDto
{
    public Guid Id { get; set; }
    public Guid OrganizacionId { get; set; }
    public string? NombreDelCliente { get; set; }
    public SentidoDeSolicitud Sentido { get; set; }
    public TipoDeSolicitud Tipo { get; set; }
    public string Texto { get; set; } = "";
    public Guid? OperacionId { get; set; }
    public EstadoDeSolicitud Estado { get; set; }
    public string? Respuesta { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? AtendidaUtc { get; set; }

    /// <summary>Quien la ve puede atenderla o rechazarla.</summary>
    public bool PuedeResolver { get; set; }
}

public class CrearSolicitudDto
{
    public TipoDeSolicitud Tipo { get; set; }

    [Required]
    [StringLength(SolicitudConsts.MaxTextoLength, MinimumLength = 5)]
    public string Texto { get; set; } = "";

    /// <summary>Una operación del mismo cliente, si la solicitud es sobre ella.</summary>
    public Guid? OperacionId { get; set; }
}

public class ResolverSolicitudDto
{
    /// <summary>Atendida o Rechazada.</summary>
    public EstadoDeSolicitud Estado { get; set; }

    [StringLength(SolicitudConsts.MaxRespuestaLength)]
    public string? Respuesta { get; set; }
}
