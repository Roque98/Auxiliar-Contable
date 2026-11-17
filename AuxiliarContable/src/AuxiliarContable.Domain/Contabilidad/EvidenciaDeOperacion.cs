using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Una evidencia del expediente de una operación (control interno): el documento o la explicación que demuestra que
/// una salida o entrada de dinero ocurrió y para qué. Es lo que sostiene la materialidad ante una revisión del SAT:
/// una factura sola no basta si no hay contrato, entregable o pedido que la respalde.
///
/// <para>La operación es un movimiento del banco (por su <see cref="Clave"/>, que sobrevive a otra conciliación) o un
/// registro de «Efectivo y crédito» (<see cref="MovimientoFueraDelBancoId"/>).</para>
/// </summary>
public class EvidenciaDeOperacion : CreationAuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }
    public int Anio { get; protected set; }
    public int Mes { get; protected set; }
    public OrigenDeOperacion Origen { get; protected set; }
    public string? Clave { get; protected set; }
    public Guid? MovimientoFueraDelBancoId { get; protected set; }
    public TipoDeEvidencia Tipo { get; protected set; }

    /// <summary>El archivo en la bóveda (del mismo cliente), si lo hay.</summary>
    public Guid? DocumentoId { get; protected set; }

    /// <summary>El testimonio o la explicación.</summary>
    public string? Nota { get; protected set; }

    protected EvidenciaDeOperacion()
    {
    }

    public EvidenciaDeOperacion(Guid id, Guid organizacionId, int anio, int mes, OrigenDeOperacion origen, string? clave,
        Guid? movimientoFueraDelBancoId, TipoDeEvidencia tipo, Guid? documentoId, string? nota) : base(id)
    {
        if (documentoId is null && string.IsNullOrWhiteSpace(nota))
        {
            throw new BusinessException("AuxiliarContable:EvidenciaVacia");
        }

        if ((origen == OrigenDeOperacion.Banco) != (clave is not null) || (origen == OrigenDeOperacion.FueraDelBanco) != movimientoFueraDelBancoId.HasValue)
        {
            throw new ArgumentException("La evidencia va ligada a un movimiento del banco o a uno de efectivo y crédito.");
        }

        OrganizacionId = organizacionId;
        Anio = anio;
        Mes = mes;
        Origen = origen;
        Clave = clave;
        MovimientoFueraDelBancoId = movimientoFueraDelBancoId;
        Tipo = tipo;
        DocumentoId = documentoId;
        Nota = string.IsNullOrWhiteSpace(nota) ? null : Check.Length(nota.Trim(), nameof(nota), ControlInternoConsts.MaxNotaLength);
    }

    /// <summary>Lo que prueba que la operación ocurrió, más allá del pago y de la factura.</summary>
    public bool ProbaraMaterialidad => Tipo is TipoDeEvidencia.Contrato or TipoDeEvidencia.EntregableOFoto or TipoDeEvidencia.PedidoOCotizacion;
}
