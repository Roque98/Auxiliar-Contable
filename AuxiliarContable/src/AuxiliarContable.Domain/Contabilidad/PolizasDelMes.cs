using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Lo último que se contabilizó de un mes de un cliente: pólizas, revisión, balanza y pendientes de cierre (JSON de
/// <c>ResultadoContable</c>). Se rehace cada vez que el despacho lo pide; queda para consultarlo sin volver al motor.
/// </summary>
public class PolizasDelMes : AuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }
    public int Anio { get; protected set; }
    public int Mes { get; protected set; }
    public string Resultado { get; protected set; } = "{}";
    public DateTime GeneradoUtc { get; protected set; }

    protected PolizasDelMes()
    {
    }

    public PolizasDelMes(Guid id, Guid organizacionId, int anio, int mes) : base(id)
    {
        OrganizacionId = organizacionId;
        Anio = anio;
        Mes = mes;
    }

    public void Guardar(string resultado, DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
        {
            throw new AbpException("GeneradoUtc debe ser UTC (columna timestamptz).");
        }

        Resultado = Check.NotNullOrWhiteSpace(resultado, nameof(resultado));
        GeneradoUtc = utc;
    }
}
