using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Una operación del cliente (una compra, un gasto, una venta) con sus tres patas: la <b>factura</b> (UUID, el XML o la
/// solicitud de factura), la <b>transacción</b> (la referencia o folio de la transferencia o del estado de cuenta, el
/// efectivo o el crédito) y el <b>control interno</b> (ticket, nota de venta o compra, comprobante interno).
///
/// <para>Nace de lo primero que suba el cliente y se va completando: cada pata queda ligada, declarada que no hay
/// («no se facturó», «no tengo la referencia»…) o pendiente. Lo más completo es lo que mejor se sostiene.</para>
/// </summary>
public class Operacion : AuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }

    /// <summary>El día de la operación (sin hora): decide en qué mes cuenta.</summary>
    public DateTime Fecha { get; protected set; }

    public SentidoDeOperacion Sentido { get; protected set; }
    public decimal? Importe { get; protected set; }
    public string Descripcion { get; protected set; } = "";
    public string? Contraparte { get; protected set; }

    public EstadoDePata FacturaEstado { get; protected set; }

    /// <summary>El folio fiscal, en mayúsculas.</summary>
    public string? Uuid { get; protected set; }

    public Guid? FacturaDocumentoId { get; protected set; }
    public Guid? SolicitudDeFacturaId { get; protected set; }
    public string? FacturaMotivo { get; protected set; }

    public EstadoDePata TransaccionEstado { get; protected set; }
    public FormaDeTransaccion? TransaccionForma { get; protected set; }

    /// <summary>La referencia, el folio o la clave de rastreo de la transferencia.</summary>
    public string? Referencia { get; protected set; }

    public Guid? TransaccionDocumentoId { get; protected set; }

    /// <summary>Si fue en efectivo o a crédito: su registro en «Efectivo y crédito».</summary>
    public Guid? MovimientoFueraDelBancoId { get; protected set; }

    /// <summary>Si la transacción es un movimiento del estado de cuenta conciliado: su clave (<c>ClaveDeMovimiento</c>).</summary>
    public string? ClaveDeMovimiento { get; protected set; }

    public string? TransaccionMotivo { get; protected set; }

    public EstadoDePata ControlEstado { get; protected set; }
    public Guid? ControlDocumentoId { get; protected set; }
    public string? ControlMotivo { get; protected set; }

    /// <summary>Lo de la revisión contable que el despacho ya aceptó (códigos separados por coma).</summary>
    public string? Aceptados { get; protected set; }

    protected Operacion()
    {
    }

    public Operacion(Guid id, Guid organizacionId) : base(id)
    {
        OrganizacionId = organizacionId;
    }

    public void Describir(DateTime fecha, SentidoDeOperacion sentido, decimal? importe, string descripcion, string? contraparte)
    {
        if (importe is <= 0)
        {
            throw new BusinessException("AuxiliarContable:ImporteNoPositivo");
        }

        Fecha = fecha.Date;
        Sentido = sentido;
        Importe = importe is { } i ? decimal.Round(i, 2) : null;
        Descripcion = Check.NotNullOrWhiteSpace(descripcion, nameof(descripcion), OperacionConsts.MaxDescripcionLength).Trim();
        Contraparte = Recortar(contraparte, OperacionConsts.MaxContraparteLength);
    }

    public void LigarFactura(string? uuid, Guid? documentoId, Guid? solicitudId)
    {
        var limpio = string.IsNullOrWhiteSpace(uuid) ? null : uuid.Trim().ToUpperInvariant();
        if (limpio is null && documentoId is null && solicitudId is null)
        {
            throw new BusinessException("AuxiliarContable:PataSinDato");
        }

        if (limpio is not null && !Guid.TryParse(limpio, out _))
        {
            throw new BusinessException("AuxiliarContable:UuidNoValido");
        }

        FacturaEstado = EstadoDePata.Ligada;
        Uuid = limpio;
        FacturaDocumentoId = documentoId;
        SolicitudDeFacturaId = solicitudId;
        FacturaMotivo = null;
    }

    public void LigarTransaccion(FormaDeTransaccion forma, string? referencia, Guid? documentoId, Guid? movimientoId)
    {
        var limpia = Recortar(referencia, OperacionConsts.MaxReferenciaLength);
        if (forma == FormaDeTransaccion.Banco && limpia is null && documentoId is null)
        {
            throw new BusinessException("AuxiliarContable:PataSinDato");
        }

        TransaccionEstado = EstadoDePata.Ligada;
        TransaccionForma = forma;
        Referencia = limpia;
        TransaccionDocumentoId = documentoId;
        MovimientoFueraDelBancoId = forma == FormaDeTransaccion.Banco ? null : movimientoId;
        ClaveDeMovimiento = forma == FormaDeTransaccion.Banco ? ClaveDeMovimiento : null;
        TransaccionMotivo = null;
    }

    /// <summary>La transacción es un movimiento del estado de cuenta (su clave sobrevive a otra conciliación).</summary>
    public void LigarMovimientoDelBanco(string clave, string? referencia)
    {
        TransaccionEstado = EstadoDePata.Ligada;
        TransaccionForma = FormaDeTransaccion.Banco;
        ClaveDeMovimiento = Check.NotNullOrWhiteSpace(clave, nameof(clave), ContabilidadConsts.ClaveLength);
        Referencia = Recortar(referencia, OperacionConsts.MaxReferenciaLength);
        TransaccionDocumentoId = null;
        MovimientoFueraDelBancoId = null;
        TransaccionMotivo = null;
    }

    public void LigarControl(Guid documentoId)
    {
        ControlEstado = EstadoDePata.Ligada;
        ControlDocumentoId = documentoId;
        ControlMotivo = null;
    }

    /// <summary>«No hay»: el motivo es lo que dijo el cliente («no se facturó», «no tengo la referencia»…).</summary>
    public void DeclararQueNoHay(PataDeOperacion pata, string motivo)
    {
        var m = Check.NotNullOrWhiteSpace(motivo, nameof(motivo), OperacionConsts.MaxMotivoLength).Trim();
        Vaciar(pata);
        switch (pata)
        {
            case PataDeOperacion.Factura:
                FacturaEstado = EstadoDePata.NoHay;
                FacturaMotivo = m;
                break;
            case PataDeOperacion.Transaccion:
                TransaccionEstado = EstadoDePata.NoHay;
                TransaccionMotivo = m;
                break;
            default:
                ControlEstado = EstadoDePata.NoHay;
                ControlMotivo = m;
                break;
        }
    }

    /// <summary>
    /// «Lo subiré después» (o «ya le pedí la factura al contador»): la pata sigue pendiente, pero con lo que dijo el
    /// cliente, para recordárselo.
    /// </summary>
    public void Posponer(PataDeOperacion pata, string motivo)
    {
        var m = Check.NotNullOrWhiteSpace(motivo, nameof(motivo), OperacionConsts.MaxMotivoLength).Trim();
        Vaciar(pata);
        switch (pata)
        {
            case PataDeOperacion.Factura:
                FacturaMotivo = m;
                break;
            case PataDeOperacion.Transaccion:
                TransaccionMotivo = m;
                break;
            default:
                ControlMotivo = m;
                break;
        }
    }

    /// <summary>Se deja para después: la pata vuelve a gris.</summary>
    public void Vaciar(PataDeOperacion pata)
    {
        switch (pata)
        {
            case PataDeOperacion.Factura:
                FacturaEstado = EstadoDePata.Pendiente;
                Uuid = null;
                FacturaDocumentoId = null;
                SolicitudDeFacturaId = null;
                FacturaMotivo = null;
                break;
            case PataDeOperacion.Transaccion:
                TransaccionEstado = EstadoDePata.Pendiente;
                TransaccionForma = null;
                Referencia = null;
                TransaccionDocumentoId = null;
                MovimientoFueraDelBancoId = null;
                ClaveDeMovimiento = null;
                TransaccionMotivo = null;
                break;
            default:
                ControlEstado = EstadoDePata.Pendiente;
                ControlDocumentoId = null;
                ControlMotivo = null;
                break;
        }
    }

    public EstadoDePata Estado(PataDeOperacion pata) => pata switch
    {
        PataDeOperacion.Factura => FacturaEstado,
        PataDeOperacion.Transaccion => TransaccionEstado,
        _ => ControlEstado,
    };

    /// <summary>
    /// Junta otra operación en esta: cada pata que aquí está pendiente toma la de la otra (y la descripción, el importe
    /// o la contraparte que falten). La otra se borra después.
    /// </summary>
    public void Absorber(Operacion otra)
    {
        if (otra.OrganizacionId != OrganizacionId || otra.Id == Id)
        {
            throw new ArgumentException("Solo se juntan operaciones distintas del mismo cliente.");
        }

        if (FacturaEstado == EstadoDePata.Pendiente)
        {
            FacturaEstado = otra.FacturaEstado;
            Uuid = otra.Uuid;
            FacturaDocumentoId = otra.FacturaDocumentoId;
            SolicitudDeFacturaId = otra.SolicitudDeFacturaId;
            FacturaMotivo = otra.FacturaMotivo;
        }

        if (TransaccionEstado == EstadoDePata.Pendiente)
        {
            TransaccionEstado = otra.TransaccionEstado;
            TransaccionForma = otra.TransaccionForma;
            Referencia = otra.Referencia;
            TransaccionDocumentoId = otra.TransaccionDocumentoId;
            MovimientoFueraDelBancoId = otra.MovimientoFueraDelBancoId;
            ClaveDeMovimiento = otra.ClaveDeMovimiento;
            TransaccionMotivo = otra.TransaccionMotivo;
        }

        if (ControlEstado == EstadoDePata.Pendiente)
        {
            ControlEstado = otra.ControlEstado;
            ControlDocumentoId = otra.ControlDocumentoId;
            ControlMotivo = otra.ControlMotivo;
        }

        Importe ??= otra.Importe;
        Contraparte ??= otra.Contraparte;
    }

    public IReadOnlyList<string> CodigosAceptados =>
        string.IsNullOrWhiteSpace(Aceptados) ? [] : Aceptados.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>El despacho acepta un aviso que detiene la póliza (p. ej. «sí es del negocio»).</summary>
    public void Aceptar(string codigo)
    {
        var c = Check.NotNullOrWhiteSpace(codigo, nameof(codigo), 40).Trim().ToUpperInvariant();
        if (!CodigosAceptados.Contains(c))
        {
            Aceptados = Check.Length(string.Join(',', CodigosAceptados.Append(c)), nameof(Aceptados), OperacionConsts.MaxAceptadosLength);
        }
    }

    /// <summary>Cuántas patas están amarradas (verde).</summary>
    public int Ligadas => (FacturaEstado == EstadoDePata.Ligada ? 1 : 0) + (TransaccionEstado == EstadoDePata.Ligada ? 1 : 0)
                          + (ControlEstado == EstadoDePata.Ligada ? 1 : 0);

    /// <summary>Cuántas patas ya tienen respuesta (verde o ámbar).</summary>
    public int Respondidas => (FacturaEstado != EstadoDePata.Pendiente ? 1 : 0) + (TransaccionEstado != EstadoDePata.Pendiente ? 1 : 0)
                              + (ControlEstado != EstadoDePata.Pendiente ? 1 : 0);

    private static string? Recortar(string? texto, int max) =>
        string.IsNullOrWhiteSpace(texto) ? null : Check.Length(texto.Trim(), nameof(texto), max);
}
