using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Conciliaciones;

/// <summary>
/// Un movimiento del banco unido a su factura. La propone <see cref="Emparejador"/> o la hace una
/// persona a mano; solo cuenta como conciliada cuando está <see cref="EstadoDeConciliacion.Confirmada"/>.
/// Las rechazadas se guardan para no volver a proponer la misma pareja. Quién confirmó o rechazó
/// queda en la auditoría (<c>LastModifierId</c>).
/// </summary>
public class Conciliacion : FullAuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }
    public Guid MovimientoId { get; protected set; }
    public Guid CfdiId { get; protected set; }
    public EstadoDeConciliacion Estado { get; protected set; }

    /// <summary>0-100: qué tan segura es la pareja según las reglas. 100 = hecha a mano.</summary>
    public int Puntaje { get; protected set; }

    /// <summary>Por qué se propuso, en palabras («mismo monto, 1 día de diferencia, el RFC aparece en el banco»).</summary>
    public string Motivo { get; protected set; } = string.Empty;

    protected Conciliacion()
    {
    }

    public Conciliacion(Guid id, Guid organizacionId, Guid movimientoId, Guid cfdiId, int puntaje, string motivo, bool aMano = false)
        : base(id)
    {
        OrganizacionId = organizacionId;
        MovimientoId = movimientoId;
        CfdiId = cfdiId;
        Puntaje = aMano ? 100 : puntaje;
        var texto = aMano ? "Hecha a mano" : motivo;
        Motivo = texto.Length <= ConciliacionConsts.MaxMotivoLength ? texto : texto[..ConciliacionConsts.MaxMotivoLength];
        Estado = aMano ? EstadoDeConciliacion.Confirmada : EstadoDeConciliacion.Propuesta;
    }

    public void Confirmar() => Estado = EstadoDeConciliacion.Confirmada;

    public void Rechazar() => Estado = EstadoDeConciliacion.Rechazada;
}
