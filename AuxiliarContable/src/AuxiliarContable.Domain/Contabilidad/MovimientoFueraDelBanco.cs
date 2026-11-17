using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Un movimiento de dinero que el estado de cuenta no ve: una venta o un gasto en efectivo (o el corte de caja del
/// día), o una compra o venta a crédito que aún no se paga. Lo registra el cliente o el despacho para que el mes esté
/// completo: lo que no pasa por el banco también es ingreso, gasto o deuda.
///
/// <para>Cuenta en el mes de su <see cref="Fecha"/>; con el mes cerrado no se agrega, cambia ni borra
/// (<see cref="GestorDePeriodos"/>). Lo a crédito sigue «por pagar» (o por cobrar) hasta <see cref="MarcarSaldado"/>.</para>
/// </summary>
public class MovimientoFueraDelBanco : AuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }
    public TipoDeMovimientoFueraDelBanco Tipo { get; protected set; }

    /// <summary>El día del movimiento (sin hora).</summary>
    public DateTime Fecha { get; protected set; }

    public decimal Importe { get; protected set; }
    public string Concepto { get; protected set; } = "";

    /// <summary>Proveedor, cliente o a quién se le pagó (nombre o RFC).</summary>
    public string? Contraparte { get; protected set; }

    /// <summary>El comprobante en la bóveda (ticket, foto, factura), si lo hay.</summary>
    public Guid? DocumentoId { get; protected set; }

    /// <summary>Solo a crédito: cuándo vence.</summary>
    public DateTime? Vence { get; protected set; }

    /// <summary>Solo a crédito: cuándo se pagó (o cobró). Null = sigue pendiente.</summary>
    public DateTime? Saldado { get; protected set; }

    public string? Nota { get; protected set; }

    protected MovimientoFueraDelBanco()
    {
    }

    public MovimientoFueraDelBanco(Guid id, Guid organizacionId) : base(id)
    {
        OrganizacionId = organizacionId;
    }

    public bool EsACredito => Tipo is TipoDeMovimientoFueraDelBanco.CompraACredito or TipoDeMovimientoFueraDelBanco.VentaACredito;

    public bool Pendiente => EsACredito && Saldado is null;

    public void Poner(TipoDeMovimientoFueraDelBanco tipo, DateTime fecha, decimal importe, string concepto, string? contraparte,
        Guid? documentoId, DateTime? vence, string? nota)
    {
        if (importe <= 0)
        {
            throw new BusinessException("AuxiliarContable:ImporteNoPositivo");
        }

        Tipo = tipo;
        Fecha = fecha.Date;
        Importe = decimal.Round(importe, 2);
        Concepto = Check.NotNullOrWhiteSpace(concepto, nameof(concepto), DineroFueraDelBancoConsts.MaxConceptoLength).Trim();
        Contraparte = string.IsNullOrWhiteSpace(contraparte) ? null : Check.Length(contraparte.Trim(), nameof(contraparte), DineroFueraDelBancoConsts.MaxContraparteLength);
        DocumentoId = documentoId;
        Vence = EsACredito ? vence?.Date : null;
        Nota = string.IsNullOrWhiteSpace(nota) ? null : Check.Length(nota.Trim(), nameof(nota), DineroFueraDelBancoConsts.MaxNotaLength);
        if (!EsACredito)
        {
            Saldado = null;
        }
    }

    public void MarcarSaldado(DateTime? fecha)
    {
        if (!EsACredito)
        {
            throw new BusinessException("AuxiliarContable:SoloLoACreditoSeSalda");
        }

        Saldado = fecha?.Date;
    }
}
