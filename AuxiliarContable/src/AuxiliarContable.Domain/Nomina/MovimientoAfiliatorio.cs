using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Nomina;

/// <summary>
/// Un aviso al IMSS de un trabajador: alta o reingreso (08), modificación de salario (07) o baja (02). Se registra solo al
/// dar de alta, cambiar el salario o dar de baja, y queda pendiente hasta que el despacho lo marca como presentado en el
/// IDSE. El salario es el del momento: el SBC lo calcula el motor con él.
/// </summary>
public class MovimientoAfiliatorio : CreationAuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }
    public Guid TrabajadorId { get; protected set; }

    /// <summary>alta, modificacion, baja.</summary>
    public string Tipo { get; protected set; } = "";

    public DateTime Fecha { get; protected set; }
    public decimal SalarioDiario { get; protected set; }
    public decimal? SalarioDiarioIntegrado { get; protected set; }

    /// <summary>Solo bajas: 1 término de contrato, 2 separación voluntaria, 3 abandono, 4 defunción, 5 clausura, 6 otras,
    /// 7 ausentismo, 8 rescisión, 9 jubilación, A pensión.</summary>
    public string? CausaBaja { get; protected set; }

    public DateTime? PresentadoUtc { get; protected set; }

    protected MovimientoAfiliatorio()
    {
    }

    public MovimientoAfiliatorio(Guid id, Guid organizacionId, Guid trabajadorId, string tipo, DateTime fecha, decimal salarioDiario,
        decimal? sdi, string? causaBaja) : base(id)
    {
        OrganizacionId = organizacionId;
        TrabajadorId = trabajadorId;
        Tipo = tipo;
        Fecha = fecha.Date;
        SalarioDiario = salarioDiario;
        SalarioDiarioIntegrado = sdi;
        CausaBaja = causaBaja;
    }

    public void MarcarPresentado(DateTime utc) => PresentadoUtc = utc;
}
