using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// El cliente no entregó nada ese mes (ni facturas, ni estados de cuenta, ni control interno): el despacho baja sus CFDI
/// del SAT con el Descargador 1 y calcula el impuesto solo con eso. Que exista la fila es la marca.
/// </summary>
public class MesSinEntrega : CreationAuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }

    public int Anio { get; protected set; }

    public int Mes { get; protected set; }

    protected MesSinEntrega()
    {
    }

    public MesSinEntrega(Guid id, Guid organizacionId, int anio, int mes) : base(id)
    {
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new ArgumentException("Mes no válido.");
        }

        OrganizacionId = organizacionId;
        Anio = anio;
        Mes = mes;
    }
}
