using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Nomina;

/// <summary>
/// Una casilla cumplida de la revisión REPSE: un documento del mes de un contrato (<see cref="ContratoId"/> y
/// <see cref="Periodo"/> = mes) o el informe cuatrimestral del cliente (sin contrato, <see cref="Periodo"/> = cuatrimestre
/// 1-3). Si la casilla se desmarca, la fila se borra.
/// </summary>
public class CumplimientoRepse : CreationAuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }
    public Guid? ContratoId { get; protected set; }
    public int Anio { get; protected set; }

    /// <summary>Mes (1-12) de un contrato, o cuatrimestre (1-3) del informe al IMSS y al INFONAVIT.</summary>
    public int Periodo { get; protected set; }

    /// <summary>Ver <c>RevisionRepse</c>: nomina, isr, imss, infonavit, iva, entrega, icsoe, sisub.</summary>
    public string Clave { get; protected set; } = "";

    protected CumplimientoRepse()
    {
    }

    public CumplimientoRepse(Guid id, Guid organizacionId, Guid? contratoId, int anio, int periodo, string clave) : base(id)
    {
        OrganizacionId = organizacionId;
        ContratoId = contratoId;
        Anio = anio;
        Periodo = periodo;
        Clave = clave;
    }
}
