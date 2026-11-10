using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// La última conciliación de un mes de un cliente, tal como la devolvió el motor. Se guarda para no
/// perderla al salir de la pantalla y para saber en qué va el cierre; la fuente de verdad sigue
/// siendo los archivos: si cambian (<see cref="Huella"/>), la conciliación queda desactualizada y se
/// vuelve a hacer. Una por mes y cliente: conciliar de nuevo la reemplaza.
/// </summary>
public class ConciliacionBancaria : AuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }

    public int Anio { get; protected set; }

    public int Mes { get; protected set; }

    /// <summary>El RFC con que se separaron emitidas y recibidas.</summary>
    public string Rfc { get; protected set; } = string.Empty;

    public int EstadosDeCuenta { get; protected set; }

    public int Facturas { get; protected set; }

    /// <summary>De qué archivos salió (ver <see cref="GestorDePeriodos.HuellaDe"/>).</summary>
    public string Huella { get; protected set; } = string.Empty;

    /// <summary>El <c>ResultadoDeConciliacion</c> del motor, en JSON.</summary>
    public string Resultado { get; protected set; } = string.Empty;

    public DateTime ConciliadaUtc { get; protected set; }

    protected ConciliacionBancaria()
    {
    }

    public ConciliacionBancaria(Guid id, Guid organizacionId, int anio, int mes) : base(id)
    {
        OrganizacionId = organizacionId;
        Anio = anio;
        Mes = mes;
    }

    public void Registrar(string rfc, int estadosDeCuenta, int facturas, string huella, string resultado, DateTime ahoraUtc)
    {
        if (ahoraUtc.Kind != DateTimeKind.Utc)
        {
            throw new AbpException("ConciliadaUtc debe ser UTC (columna timestamptz).");
        }

        Rfc = Check.NotNullOrWhiteSpace(rfc, nameof(rfc), 13);
        EstadosDeCuenta = estadosDeCuenta;
        Facturas = facturas;
        Huella = Check.Length(huella, nameof(huella), ContabilidadConsts.HuellaLength, ContabilidadConsts.HuellaLength)!;
        Resultado = Check.NotNullOrWhiteSpace(resultado, nameof(resultado));
        ConciliadaUtc = ahoraUtc;
    }
}
