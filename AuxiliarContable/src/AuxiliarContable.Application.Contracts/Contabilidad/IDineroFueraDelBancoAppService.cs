using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Efectivo y crédito: el dinero de un cliente que no se ve en su estado de cuenta (ventas y gastos en efectivo,
/// cortes de caja, compras y ventas a crédito). Lo registran el cliente (con permiso de subir) y el despacho; un mes
/// cerrado no se toca. Ver: quien ve al cliente (lo ajeno = 404).
/// </summary>
public interface IDineroFueraDelBancoAppService : IApplicationService
{
    /// <summary>Lo del mes, más lo a crédito de meses anteriores que sigue sin pagarse.</summary>
    Task<DineroFueraDelBancoDelMesDto> GetAsync(Guid organizacionId, int anio, int mes);

    Task<MovimientoFueraDelBancoDto> CrearAsync(Guid organizacionId, GuardarMovimientoFueraDelBancoDto input);

    Task<MovimientoFueraDelBancoDto> ActualizarAsync(Guid organizacionId, Guid movimientoId, GuardarMovimientoFueraDelBancoDto input);

    Task BorrarAsync(Guid organizacionId, Guid movimientoId);

    /// <summary>Una compra o venta a crédito ya se pagó (o cobró) en esa fecha; null la regresa a pendiente.</summary>
    Task<MovimientoFueraDelBancoDto> SaldarAsync(Guid organizacionId, Guid movimientoId, DateTime? fecha);
}

public class GuardarMovimientoFueraDelBancoDto
{
    public TipoDeMovimientoFueraDelBanco Tipo { get; set; }

    public DateTime Fecha { get; set; }

    [Range(0.01, 999_999_999)]
    public decimal Importe { get; set; }

    [Required]
    [StringLength(DineroFueraDelBancoConsts.MaxConceptoLength, MinimumLength = 1)]
    public string Concepto { get; set; } = "";

    [StringLength(DineroFueraDelBancoConsts.MaxContraparteLength)]
    public string? Contraparte { get; set; }

    /// <summary>Un documento de la bóveda del mismo cliente (ticket, foto, factura).</summary>
    public Guid? DocumentoId { get; set; }

    public DateTime? Vence { get; set; }

    [StringLength(DineroFueraDelBancoConsts.MaxNotaLength)]
    public string? Nota { get; set; }
}

public class MovimientoFueraDelBancoDto
{
    public Guid Id { get; set; }
    public TipoDeMovimientoFueraDelBanco Tipo { get; set; }
    public DateTime Fecha { get; set; }
    public decimal Importe { get; set; }
    public string Concepto { get; set; } = "";
    public string? Contraparte { get; set; }
    public Guid? DocumentoId { get; set; }
    public string? NombreDelDocumento { get; set; }
    public DateTime? Vence { get; set; }
    public DateTime? Saldado { get; set; }
    public string? Nota { get; set; }

    /// <summary>A crédito y sin pagar.</summary>
    public bool Pendiente { get; set; }

    /// <summary>A crédito, sin pagar y ya pasó su fecha de vencimiento.</summary>
    public bool Vencido { get; set; }

    /// <summary>Es de un mes anterior: aparece porque sigue sin pagarse.</summary>
    public bool DeOtroMes { get; set; }
}

public class DineroFueraDelBancoDelMesDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public List<MovimientoFueraDelBancoDto> Movimientos { get; set; } = [];

    public decimal EntradasEnEfectivo { get; set; }
    public decimal SalidasEnEfectivo { get; set; }

    /// <summary>Todo lo comprado a crédito que sigue sin pagarse (de este mes y de antes).</summary>
    public decimal PorPagar { get; set; }

    /// <summary>Todo lo vendido a crédito que sigue sin cobrarse.</summary>
    public decimal PorCobrar { get; set; }

    /// <summary>Quien pregunta puede agregar y cambiar (tiene permiso de subir y el mes está abierto).</summary>
    public bool PuedeEditar { get; set; }

    public bool Cerrado { get; set; }
}
