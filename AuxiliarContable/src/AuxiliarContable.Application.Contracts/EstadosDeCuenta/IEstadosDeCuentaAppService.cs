using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.EstadosDeCuenta;

/// <summary>
/// Los estados de cuenta leídos de un cliente y su revisión humana: ver, corregir renglones o
/// saldos y confirmarlos. Ver = <c>Ver</c>; cambiar = <c>Subir</c>.
/// </summary>
public interface IEstadosDeCuentaAppService : IApplicationService
{
    Task<List<EstadoDeCuentaResumenDto>> GetListAsync(Guid organizacionId);

    Task<EstadoDeCuentaDto> GetDetalleAsync(Guid organizacionId, Guid estadoDeCuentaId);

    Task<EstadoDeCuentaDto> ConfirmarAsync(Guid organizacionId, Guid estadoDeCuentaId, bool aunqueNoCuadre);

    Task<EstadoDeCuentaDto> CorregirMovimientoAsync(Guid organizacionId, Guid estadoDeCuentaId, CorregirMovimientoDto input);

    Task<EstadoDeCuentaDto> CorregirSaldosAsync(Guid organizacionId, Guid estadoDeCuentaId, CorregirSaldosDto input);

    /// <summary>Lo vuelve a leer con el modelo (en segundo plano). Se pierden las correcciones a mano.</summary>
    Task VolverALeerAsync(Guid organizacionId, Guid documentoId);
}

public class EstadoDeCuentaResumenDto
{
    public Guid Id { get; set; }
    public Guid DocumentoId { get; set; }
    public string Banco { get; set; } = string.Empty;
    public string? Cuenta { get; set; }
    public DateTime? PeriodoInicio { get; set; }
    public DateTime? PeriodoFin { get; set; }
    public string Moneda { get; set; } = "MXN";
    public decimal? SaldoInicial { get; set; }
    public decimal? SaldoFinal { get; set; }
    public decimal TotalAbonos { get; set; }
    public decimal TotalCargos { get; set; }
    public decimal? Diferencia { get; set; }
    public bool Cuadra { get; set; }
    public int SaltosDeSaldo { get; set; }
    public int Movimientos { get; set; }
    public RevisionDelEstadoDeCuenta Revision { get; set; }
}

public class EstadoDeCuentaDto : EstadoDeCuentaResumenDto
{
    public bool PuedeEditar { get; set; }
    public List<MovimientoBancarioDto> Renglones { get; set; } = [];
}

public class MovimientoBancarioDto
{
    public Guid Id { get; set; }
    public int Orden { get; set; }
    public DateTime Fecha { get; set; }
    public string Concepto { get; set; } = string.Empty;
    public string? Referencia { get; set; }
    public decimal Cargo { get; set; }
    public decimal Abono { get; set; }
    public decimal? Saldo { get; set; }

    /// <summary>Su saldo no sigue del renglón anterior: probablemente aquí está el error.</summary>
    public bool SaltoDeSaldo { get; set; }
}

public class CorregirMovimientoDto
{
    public Guid MovimientoId { get; set; }
    public DateTime Fecha { get; set; }

    [StringLength(EstadoDeCuentaConsts.MaxConceptoLength)]
    public string Concepto { get; set; } = string.Empty;

    [Range(0, 999_999_999_999)]
    public decimal Cargo { get; set; }

    [Range(0, 999_999_999_999)]
    public decimal Abono { get; set; }
}

public class CorregirSaldosDto
{
    public decimal? SaldoInicial { get; set; }
    public decimal? SaldoFinal { get; set; }
}
