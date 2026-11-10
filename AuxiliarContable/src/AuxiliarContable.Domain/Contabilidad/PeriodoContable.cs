using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Un mes de un cliente, para saber si ya se cerró. Sin fila = abierto: solo se crea al cerrarlo
/// la primera vez. Cerrado, la bóveda no acepta que entre, salga ni se mueva nada de ese mes (ver
/// <see cref="GestorDePeriodos"/>): lo que ya se presentó no cambia a escondidas.
/// </summary>
public class PeriodoContable : AuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }

    public int Anio { get; protected set; }

    public int Mes { get; protected set; }

    public bool Cerrado { get; protected set; }

    public DateTime? CerradoUtc { get; protected set; }

    public Guid? CerradoPor { get; protected set; }

    protected PeriodoContable()
    {
    }

    public PeriodoContable(Guid id, Guid organizacionId, int anio, int mes) : base(id)
    {
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new ArgumentException("Mes no válido.");
        }

        OrganizacionId = organizacionId;
        Anio = anio;
        Mes = mes;
    }

    public void Cerrar(Guid? usuarioId, DateTime ahoraUtc)
    {
        if (ahoraUtc.Kind != DateTimeKind.Utc)
        {
            throw new AbpException("CerradoUtc debe ser UTC (columna timestamptz).");
        }

        Cerrado = true;
        CerradoUtc = ahoraUtc;
        CerradoPor = usuarioId;
    }

    public void Reabrir()
    {
        Cerrado = false;
        CerradoUtc = null;
        CerradoPor = null;
    }
}
