using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Control interno: el expediente de cada operación del mes (movimientos del banco de la última conciliación y
/// registros de «Efectivo y crédito») con la evidencia que la respalda, y qué le falta a cada salida de dinero
/// (<see cref="EstadoDeEvidencia"/>). Ver: quien ve al cliente. Agregar o quitar evidencia: permiso de subir, con el
/// mes abierto.
/// </summary>
public interface IControlInternoAppService : IApplicationService
{
    Task<ExpedienteDelMesDto> GetAsync(Guid organizacionId, int anio, int mes);

    Task<EvidenciaDto> AgregarAsync(Guid organizacionId, AgregarEvidenciaDto input);

    Task QuitarAsync(Guid organizacionId, Guid evidenciaId);
}

public class AgregarEvidenciaDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public OrigenDeOperacion Origen { get; set; }

    /// <summary>Movimiento del banco: su clave (la que trae <see cref="OperacionDto.Clave"/>).</summary>
    [StringLength(ContabilidadConsts.ClaveLength)]
    public string? Clave { get; set; }

    /// <summary>Registro de «Efectivo y crédito».</summary>
    public Guid? MovimientoFueraDelBancoId { get; set; }

    public TipoDeEvidencia Tipo { get; set; }

    /// <summary>Un documento de la bóveda del mismo cliente.</summary>
    public Guid? DocumentoId { get; set; }

    [StringLength(ControlInternoConsts.MaxNotaLength)]
    public string? Nota { get; set; }
}

public class EvidenciaDto
{
    public Guid Id { get; set; }
    public TipoDeEvidencia Tipo { get; set; }
    public Guid? DocumentoId { get; set; }
    public string? NombreDelDocumento { get; set; }
    public string? Nota { get; set; }
    public DateTime Cuando { get; set; }
}

public class OperacionDto
{
    public OrigenDeOperacion Origen { get; set; }
    public string? Clave { get; set; }
    public Guid? MovimientoFueraDelBancoId { get; set; }
    public string? Fecha { get; set; }
    public string Descripcion { get; set; } = "";
    public string? Contraparte { get; set; }
    public bool EsSalida { get; set; }
    public decimal Importe { get; set; }

    /// <summary>UUID de las facturas que la conciliación le encontró (o la factura ligada en efectivo y crédito).</summary>
    public List<string> Facturas { get; set; } = [];

    public EstadoDeEvidencia Estado { get; set; }

    /// <summary>Lo que ya la respalda: sus evidencias, y el comprobante propio de efectivo y crédito o la explicación
    /// de la conciliación cuando los hay.</summary>
    public List<EvidenciaDto> Evidencias { get; set; } = [];

    public string? Explicacion { get; set; }
}

public class ExpedienteDelMesDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public List<OperacionDto> Operaciones { get; set; } = [];

    /// <summary>Hay conciliación del mes: sin ella solo salen los registros de efectivo y crédito.</summary>
    public bool HayConciliacion { get; set; }

    public int Completas { get; set; }
    public int FaltaMaterialidad { get; set; }
    public int FaltaComprobante { get; set; }

    public bool PuedeAgregar { get; set; }
    public bool Cerrado { get; set; }
}
