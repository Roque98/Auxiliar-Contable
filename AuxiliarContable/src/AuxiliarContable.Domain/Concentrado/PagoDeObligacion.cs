using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Concentrado;

/// <summary>
/// Un pago que el cliente ya hizo de una obligación del mes (ISR, IVA, retenciones, cuotas, ISN…): lo registra el
/// despacho con la fecha del acuse o del comprobante. Con él, la hoja concentrado sabe qué sigue debiendo y desde cuándo
/// corren la actualización y los recargos de lo que falta.
/// </summary>
public class PagoDeObligacion : CreationAuditedAggregateRoot<Guid>
{
    public const int MaxClaveLength = 30;
    public const int MaxNotaLength = 300;

    public Guid OrganizacionId { get; protected set; }
    public int Anio { get; protected set; }
    public int Mes { get; protected set; }

    /// <summary>Ver <c>ObligacionesFiscales</c>: isr, iva, ieps, ret_isr_salarios, ret_isr, ret_iva, imss, infonavit, isn.</summary>
    public string Clave { get; protected set; } = "";

    /// <summary>Lo pagado, incluida la actualización y los recargos si los hubo.</summary>
    public decimal Importe { get; protected set; }

    public DateTime FechaDePago { get; protected set; }
    public string? Nota { get; protected set; }

    protected PagoDeObligacion()
    {
    }

    public PagoDeObligacion(Guid id, Guid organizacionId, int anio, int mes, string clave, decimal importe, DateTime fechaDePago,
        string? nota) : base(id)
    {
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }

        if (importe <= 0)
        {
            throw new UserFriendlyException("El importe pagado debe ser mayor que cero.");
        }

        OrganizacionId = organizacionId;
        Anio = anio;
        Mes = mes;
        Clave = Check.NotNullOrWhiteSpace(clave, nameof(clave), MaxClaveLength);
        Importe = Math.Round(importe, 2);
        FechaDePago = fechaDePago.Date;
        Nota = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim()[..Math.Min(nota.Trim().Length, MaxNotaLength)];
    }
}
