using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Nomina;

/// <summary>
/// Un trabajador de un cliente (patrón): lo que hace falta para su nómina, su salario base de cotización y sus
/// retenciones. Pedido 18-nov-2026: apartado de nómina del despacho.
/// </summary>
public class Trabajador : FullAuditedAggregateRoot<Guid>
{
    public const int MaxNombreLength = 200;

    public Guid OrganizacionId { get; protected set; }
    public string Nombre { get; protected set; } = "";
    public string? Rfc { get; protected set; }
    public string? Curp { get; protected set; }
    public string? Nss { get; protected set; }
    public DateTime FechaIngreso { get; protected set; }
    public DateTime? FechaBaja { get; protected set; }
    public decimal SalarioDiario { get; protected set; }

    /// <summary>Si se captura, manda sobre el que se calcula con el factor de integración.</summary>
    public decimal? SalarioDiarioIntegrado { get; protected set; }

    public int DiasAguinaldo { get; protected set; } = 15;
    public decimal PrimaVacacionalPct { get; protected set; } = 25;

    /// <summary>porcentaje, cuota_fija o veces (aviso de retención del INFONAVIT); null si no tiene crédito.</summary>
    public string? CreditoInfonavitTipo { get; protected set; }

    public decimal? CreditoInfonavitValor { get; protected set; }

    /// <summary>Descuento FONACOT por periodo.</summary>
    public decimal? Fonacot { get; protected set; }

    protected Trabajador()
    {
    }

    public Trabajador(Guid id, Guid organizacionId) : base(id)
    {
        OrganizacionId = organizacionId;
    }

    public void Describir(string nombre, string? rfc, string? curp, string? nss, DateTime fechaIngreso, decimal salarioDiario,
        decimal? sdi, int diasAguinaldo, decimal primaVacacionalPct, string? creditoTipo, decimal? creditoValor, decimal? fonacot)
    {
        Nombre = Check.NotNullOrWhiteSpace(nombre, nameof(nombre), MaxNombreLength).Trim();
        Rfc = Limpio(rfc, 13);
        Curp = Limpio(curp, 18);
        Nss = Limpio(nss, 11);
        FechaIngreso = fechaIngreso.Date;
        if (salarioDiario <= 0 || salarioDiario > 1_000_000)
        {
            throw new UserFriendlyException("El salario diario debe ser mayor que cero.");
        }

        SalarioDiario = salarioDiario;
        SalarioDiarioIntegrado = sdi is > 0 ? sdi : null;
        DiasAguinaldo = Math.Clamp(diasAguinaldo, 15, 365);
        PrimaVacacionalPct = Math.Clamp(primaVacacionalPct, 25, 100);
        CreditoInfonavitTipo = creditoTipo is "porcentaje" or "cuota_fija" or "veces" && creditoValor is > 0 ? creditoTipo : null;
        CreditoInfonavitValor = CreditoInfonavitTipo == null ? null : creditoValor;
        Fonacot = fonacot is > 0 ? fonacot : null;
    }

    public void DarDeBaja(DateTime fecha) => FechaBaja = fecha.Date;

    public void Reingresar() => FechaBaja = null;

    public bool ActivoEn(DateTime inicio, DateTime fin) => FechaIngreso <= fin.Date && (FechaBaja == null || FechaBaja >= inicio.Date);

    private static string? Limpio(string? t, int max)
    {
        var v = t?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }
}
