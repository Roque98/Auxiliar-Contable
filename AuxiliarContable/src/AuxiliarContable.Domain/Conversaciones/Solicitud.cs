using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Conversaciones;

/// <summary>
/// Una solicitud entre el cliente y el despacho, hecha desde el chat (o la pantalla): el cliente pide un cambio o una
/// aclaración que él no puede hacer (su contabilidad la cambia el despacho), o el despacho le pide al cliente un documento
/// o un dato. Quien la recibe la atiende o la rechaza con una respuesta. No cambia nada por sí misma: es el registro de lo
/// que se pidió, quién lo pidió (<see cref="CreationAuditedAggregateRoot{TKey}.CreatorId"/>) y qué se respondió.
/// </summary>
public class Solicitud : CreationAuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }
    public SentidoDeSolicitud Sentido { get; protected set; }
    public TipoDeSolicitud Tipo { get; protected set; }
    public string Texto { get; protected set; } = string.Empty;

    /// <summary>La operación de la que se habla, si es una.</summary>
    public Guid? OperacionId { get; protected set; }

    public EstadoDeSolicitud Estado { get; protected set; }
    public string? Respuesta { get; protected set; }
    public Guid? AtendidaPorId { get; protected set; }
    public DateTime? AtendidaUtc { get; protected set; }

    protected Solicitud()
    {
    }

    public Solicitud(Guid id, Guid organizacionId, SentidoDeSolicitud sentido, TipoDeSolicitud tipo, string texto, Guid? operacionId)
        : base(id)
    {
        OrganizacionId = organizacionId;
        Sentido = sentido;
        Tipo = tipo;
        Texto = Check.NotNullOrWhiteSpace(texto, nameof(texto), SolicitudConsts.MaxTextoLength).Trim();
        OperacionId = operacionId;
        Estado = EstadoDeSolicitud.Pendiente;
    }

    /// <summary>Atendida o rechazada, una sola vez. Rechazar pide decir por qué.</summary>
    public void Resolver(EstadoDeSolicitud estado, string? respuesta, Guid quien, DateTime utc)
    {
        if (Estado != EstadoDeSolicitud.Pendiente)
        {
            throw new BusinessException("AuxiliarContable:SolicitudYaResuelta");
        }

        if (estado == EstadoDeSolicitud.Pendiente)
        {
            throw new BusinessException("AuxiliarContable:SolicitudSinResolver");
        }

        var limpia = string.IsNullOrWhiteSpace(respuesta) ? null : respuesta.Trim();
        if (estado == EstadoDeSolicitud.Rechazada && limpia == null)
        {
            throw new BusinessException("AuxiliarContable:SolicitudRechazadaSinMotivo");
        }

        if (limpia is { Length: > SolicitudConsts.MaxRespuestaLength })
        {
            limpia = limpia[..SolicitudConsts.MaxRespuestaLength];
        }

        Estado = estado;
        Respuesta = limpia;
        AtendidaPorId = quien;
        AtendidaUtc = utc;
    }
}
