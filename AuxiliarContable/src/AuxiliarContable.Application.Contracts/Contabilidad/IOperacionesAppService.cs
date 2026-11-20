using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// «Mi información → Todo»: las operaciones del cliente con sus tres patas (factura, transacción, control interno).
/// Lo que sube el cliente abre una operación (<see cref="DeDocumentoAsync"/>) y luego se le pregunta por las otras
/// dos patas; puede dejarlo para después. Ver: quien ve al cliente; cambiar: quien puede subir (lo ajeno = 404).
/// Un mes cerrado no se toca.
/// </summary>
public interface IOperacionesAppService : IApplicationService
{
    /// <summary>Las del mes, las más completas primero. Las facturas y los registros de efectivo y crédito del mes que aún no tienen operación se agregan solos.</summary>
    Task<OperacionesDelMesDto> GetListAsync(Guid organizacionId, int anio, int mes);

    /// <summary>
    /// Lo que dice el XML de la factura de la operación (tipo, método y forma de pago, CFDI relacionado), para que la
    /// guía del trabajo diario pregunte lo que corresponde. Null si la operación no tiene el XML.
    /// </summary>
    Task<FacturaDeLaOperacionDto?> GetFacturaAsync(Guid organizacionId, Guid operacionId);

    /// <summary>Lo que el cliente dijo que subiría después (la factura, el comprobante o el ticket), para recordárselo.</summary>
    Task<List<PorSubirDto>> GetPorSubirAsync(Guid organizacionId);

    /// <summary>La operación de un documento recién subido (factura, comprobante de pago o control interno): la que ya tiene, o una nueva.</summary>
    Task<OperacionDelClienteDto> DeDocumentoAsync(Guid organizacionId, Guid documentoId);

    /// <summary>La operación de una solicitud de factura (la pata factura queda «solicitada»).</summary>
    Task<OperacionDelClienteDto> DeSolicitudAsync(Guid organizacionId, Guid solicitudId);

    Task<OperacionDelClienteDto> CrearAsync(Guid organizacionId, GuardarOperacionDto input);

    /// <summary>
    /// Cambia los datos y las patas que vengan (null = no se toca). Si el UUID o la referencia que se capturan ya
    /// están en otra operación del cliente, se juntan en una.
    /// </summary>
    Task<OperacionDelClienteDto> ActualizarAsync(Guid organizacionId, Guid operacionId, GuardarOperacionDto input);

    /// <summary>Junta <paramref name="conOperacionId"/> en <paramref name="operacionId"/>: lo que falte en una lo pone la otra.</summary>
    Task<OperacionDelClienteDto> JuntarAsync(Guid organizacionId, Guid operacionId, Guid conOperacionId);

    /// <summary>Borra la operación; los documentos y los registros de efectivo y crédito se quedan.</summary>
    Task BorrarAsync(Guid organizacionId, Guid operacionId);
}

public class FacturaDeLaOperacionDto
{
    /// <summary>I ingreso, E egreso, P pago, N nómina, T traslado.</summary>
    public string? Tipo { get; set; }

    public string? Metodo { get; set; }
    public string? Forma { get; set; }
    public decimal? Total { get; set; }
    public string? Moneda { get; set; }
    public string? TipoRelacion { get; set; }
    public string? UuidRelacionado { get; set; }
}

public class PorSubirDto
{
    public Guid OperacionId { get; set; }
    public DateTime Fecha { get; set; }
    public string Descripcion { get; set; } = "";
    public decimal? Importe { get; set; }
    public PataDeOperacion Pata { get; set; }
    public string Motivo { get; set; } = "";

    /// <summary>Días desde que se dijo «lo subiré después».</summary>
    public int Dias { get; set; }
}

public class OperacionesDelMesDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public List<OperacionDelClienteDto> Operaciones { get; set; } = [];
    public bool PuedeEditar { get; set; }
    public bool Cerrado { get; set; }
}

public class OperacionDelClienteDto
{
    public Guid Id { get; set; }
    public DateTime Fecha { get; set; }
    public SentidoDeOperacion Sentido { get; set; }
    public decimal? Importe { get; set; }
    public string Descripcion { get; set; } = "";
    public string? Contraparte { get; set; }
    public PataDto Factura { get; set; } = new();
    public PataDto Transaccion { get; set; } = new();
    public PataDto ControlInterno { get; set; } = new();

    /// <summary>Patas en verde (amarradas).</summary>
    public int Ligadas { get; set; }

    /// <summary>Patas con respuesta (verde o ámbar).</summary>
    public int Respondidas { get; set; }
}

public class PataDto
{
    public EstadoDePata Estado { get; set; }

    /// <summary>Factura: el UUID. Transacción: la referencia o folio.</summary>
    public string? Referencia { get; set; }

    public Guid? DocumentoId { get; set; }
    public string? NombreDelDocumento { get; set; }
    public string? Motivo { get; set; }

    /// <summary>Solo transacción.</summary>
    public FormaDeTransaccion? Forma { get; set; }

    /// <summary>Solo transacción en efectivo o a crédito.</summary>
    public Guid? MovimientoFueraDelBancoId { get; set; }

    /// <summary>Solo transacción a crédito: cuándo vence y si ya se pagó.</summary>
    public DateTime? Vence { get; set; }

    public DateTime? Saldado { get; set; }

    /// <summary>Solo factura pedida en «Pedir factura».</summary>
    public Guid? SolicitudDeFacturaId { get; set; }

    /// <summary>Solo transacción: es un movimiento del estado de cuenta conciliado.</summary>
    public bool DelBanco { get; set; }

    /// <summary>Lo que Claude leyó del documento de esta pata, si ya se analizó.</summary>
    public AnalisisCortoDto? Analisis { get; set; }
}

/// <summary>Lo que Claude entendió de un ticket, nota o comprobante: qué es y qué representa para el giro.</summary>
public class AnalisisCortoDto
{
    public bool Legible { get; set; }
    public string QueEs { get; set; } = "";

    /// <summary>mercancia, insumo_directo, gasto_indirecto, gasto_operacion, inversion, deduccion_personal, ajeno, venta, no_aplica.</summary>
    public string RelacionConGiro { get; set; } = "";

    public string Explicacion { get; set; } = "";
    public string QuePrueba { get; set; } = "";
    public string Confianza { get; set; } = "";

    public static AnalisisCortoDto? De(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var a = System.Text.Json.JsonSerializer.Deserialize<MotorFiscal.AnalisisDeDocumento>(json);
        return a == null
            ? null
            : new AnalisisCortoDto
            {
                Legible = a.Legible, QueEs = a.QueEs, RelacionConGiro = a.RelacionConGiro, Explicacion = a.Explicacion,
                QuePrueba = a.QuePrueba, Confianza = a.Confianza,
            };
    }
}

public class GuardarOperacionDto
{
    public DateTime Fecha { get; set; }
    public SentidoDeOperacion Sentido { get; set; }

    [Range(0.01, 999_999_999)]
    public decimal? Importe { get; set; }

    [Required]
    [StringLength(OperacionConsts.MaxDescripcionLength, MinimumLength = 1)]
    public string Descripcion { get; set; } = "";

    [StringLength(OperacionConsts.MaxContraparteLength)]
    public string? Contraparte { get; set; }

    public GuardarPataDto? Factura { get; set; }
    public GuardarPataDto? Transaccion { get; set; }
    public GuardarPataDto? ControlInterno { get; set; }
}

public class GuardarPataDto
{
    public EstadoDePata Estado { get; set; }

    [StringLength(OperacionConsts.MaxReferenciaLength)]
    public string? Referencia { get; set; }

    /// <summary>Un documento de la bóveda del mismo cliente.</summary>
    public Guid? DocumentoId { get; set; }

    [StringLength(OperacionConsts.MaxMotivoLength)]
    public string? Motivo { get; set; }

    public FormaDeTransaccion? Forma { get; set; }

    /// <summary>A crédito: cuándo vence.</summary>
    public DateTime? Vence { get; set; }
}
