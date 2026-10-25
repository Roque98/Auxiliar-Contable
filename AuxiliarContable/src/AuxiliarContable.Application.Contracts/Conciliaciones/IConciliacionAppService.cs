using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Conciliaciones;

/// <summary>
/// Conciliación de un mes de un cliente: el banco contra sus facturas. El sistema PROPONE por
/// reglas; una persona confirma, rechaza o une a mano. Lo que sobra son los faltantes
/// («depósito sin factura», «gasto sin factura»). Ver = <c>Ver</c>; cambiar = <c>Subir</c>.
/// </summary>
public interface IConciliacionAppService : IApplicationService
{
    Task<ConciliacionDelMesDto> GetDelMesAsync(Guid organizacionId, int anio, int mes);

    /// <summary>Corre las reglas sobre lo que aún no está unido y guarda las propuestas nuevas.</summary>
    Task<ConciliacionDelMesDto> ProponerAsync(Guid organizacionId, int anio, int mes);

    Task ConfirmarAsync(Guid organizacionId, Guid conciliacionId);

    Task RechazarAsync(Guid organizacionId, Guid conciliacionId);

    Task ConciliarAManoAsync(Guid organizacionId, ConciliarAManoDto input);

    /// <summary>«Este movimiento no lleva factura» (con el porqué). Sin porqué, quita la marca.</summary>
    Task MarcarSinFacturaAsync(Guid organizacionId, MarcarSinFacturaDto input);
}

public class ConciliarAManoDto
{
    public Guid MovimientoId { get; set; }
    public Guid CfdiId { get; set; }
}

public class MarcarSinFacturaDto
{
    public Guid MovimientoId { get; set; }

    [StringLength(500)]
    public string? Porque { get; set; }
}

public class ConciliacionDelMesDto
{
    public Guid OrganizacionId { get; set; }
    public int Anio { get; set; }
    public int Mes { get; set; }
    public bool PuedeEditar { get; set; }

    /// <summary>Estados de cuenta del mes que no cuadran: sus movimientos no entran hasta revisarlos.</summary>
    public int EstadosSinRevisar { get; set; }

    /// <summary>¿Hay al menos un estado de cuenta del mes? Sin él no hay contra qué conciliar.</summary>
    public bool HayEstadosDeCuenta { get; set; }

    public List<ParejaDto> Propuestas { get; set; } = [];
    public List<ParejaDto> Confirmadas { get; set; } = [];
    public List<MovimientoDelBancoDto> DepositosSinFactura { get; set; } = [];
    public List<MovimientoDelBancoDto> CargosSinFactura { get; set; } = [];
    public List<MovimientoDelBancoDto> SinFacturaJustificados { get; set; } = [];

    /// <summary>Ventas de contado (PUE) sin depósito que les corresponda.</summary>
    public List<FacturaDelMesDto> VentasSinDeposito { get; set; } = [];

    /// <summary>Gastos de contado (PUE) sin cargo en el banco (pudieron pagarse en efectivo).</summary>
    public List<FacturaDelMesDto> GastosSinCargo { get; set; } = [];
}

public class ParejaDto
{
    public Guid Id { get; set; }
    public int Puntaje { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public MovimientoDelBancoDto Movimiento { get; set; } = null!;
    public FacturaDelMesDto Factura { get; set; } = null!;
}

public class MovimientoDelBancoDto
{
    public Guid Id { get; set; }
    public DateTime Fecha { get; set; }
    public string Banco { get; set; } = string.Empty;
    public string Concepto { get; set; } = string.Empty;
    public decimal Cargo { get; set; }
    public decimal Abono { get; set; }
    public string? SinFacturaPorque { get; set; }
}

public class FacturaDelMesDto
{
    public Guid Id { get; set; }
    public DateTime Fecha { get; set; }
    public bool EsVenta { get; set; }
    public string Contraparte { get; set; } = string.Empty;
    public string RfcContraparte { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? MetodoDePago { get; set; }
    public string Uuid { get; set; } = string.Empty;
}
