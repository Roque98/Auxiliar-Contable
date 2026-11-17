using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// La balanza con la que el cliente llega al despacho: saldos de cada cuenta (código agrupador) al iniciar
/// <see cref="Anio"/>/<see cref="Mes"/>. De ahí parte la balanza acumulada y la contabilidad electrónica. Uno por cliente.
/// </summary>
public class SaldosDeApertura : AuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }
    public int Anio { get; protected set; }
    public int Mes { get; protected set; }

    /// <summary>JSON de <c>SaldoDeApertura</c> (cuenta, deudor, acreedor).</summary>
    public string Saldos { get; protected set; } = "[]";

    protected SaldosDeApertura()
    {
    }

    public SaldosDeApertura(Guid id, Guid organizacionId) : base(id)
    {
        OrganizacionId = organizacionId;
    }

    public void Poner(int anio, int mes, string saldos)
    {
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new ArgumentException("Mes no válido.");
        }

        Anio = anio;
        Mes = mes;
        Saldos = Check.NotNullOrWhiteSpace(saldos, nameof(saldos));
    }
}
