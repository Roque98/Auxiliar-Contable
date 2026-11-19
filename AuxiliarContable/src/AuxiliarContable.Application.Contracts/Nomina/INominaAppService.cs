using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AuxiliarContable.MotorFiscal;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Nomina;

/// <summary>
/// Nómina de un cliente (patrón): sus trabajadores y cada periodo con sus incidencias y su cálculo (ISR, IMSS,
/// INFONAVIT, FONACOT, ISN, neto y póliza). Solo el equipo del despacho.
/// </summary>
public interface INominaAppService : IApplicationService
{
    Task<NominaDelClienteDto> GetDelClienteAsync(Guid organizacionId, int anio);

    Task<TrabajadorDto> GuardarTrabajadorAsync(Guid organizacionId, GuardarTrabajadorDto dto);

    /// <summary>Fecha null: reingresa (alta al IMSS con la fecha de hoy). <paramref name="causa"/>: causa de la baja del IMSS.</summary>
    Task<TrabajadorDto> DarDeBajaAsync(Guid organizacionId, Guid trabajadorId, DateTime? fecha, string? causa);

    /// <summary>Los archivos del IDSE con los movimientos que faltan de presentar (o solo los de <paramref name="ids"/>).</summary>
    Task<ArchivosDeIdse> ArchivosIdseAsync(Guid organizacionId, List<Guid>? ids);

    /// <summary>Ya se presentaron en el IDSE: dejan de salir en los archivos.</summary>
    Task MarcarPresentadosAsync(Guid organizacionId, List<Guid> ids);

    /// <summary>Recibos del periodo (CFDI de nómina sin sello, validados contra el XSD del SAT) para timbrarlos.</summary>
    Task<RecibosDeNomina> RecibosAsync(Guid organizacionId, Guid periodoId);

    /// <summary>Cuotas IMSS e INFONAVIT de los periodos calculados que terminan en el mes, para cotejar con el SUA o el SIPARE.</summary>
    Task<CuotasDelMesDto> CuotasDelMesAsync(Guid organizacionId, int anio, int mes);

    Task<PeriodoDeNominaDto> CrearPeriodoAsync(Guid organizacionId, CrearPeriodoDto dto);

    Task<PeriodoDeNominaDto> GetPeriodoAsync(Guid organizacionId, Guid periodoId);

    Task<PeriodoDeNominaDto> GuardarIncidenciasAsync(Guid organizacionId, Guid periodoId, List<IncidenciasDeTrabajadorDto> incidencias);

    /// <summary>Calcula el periodo con el motor y lo guarda.</summary>
    Task<PeriodoDeNominaDto> CalcularAsync(Guid organizacionId, Guid periodoId);

    Task BorrarPeriodoAsync(Guid organizacionId, Guid periodoId);
}

public class NominaDelClienteDto
{
    public bool PuedeEditar { get; set; }

    /// <summary>Lo que falta en el perfil del patrón (registro patronal, prima de riesgo, domicilio).</summary>
    public List<string> FaltaEnElPerfil { get; set; } = [];

    public List<TrabajadorDto> Trabajadores { get; set; } = [];
    public List<PeriodoDeNominaDto> Periodos { get; set; } = [];

    /// <summary>Avisos al IMSS: primero los que faltan de presentar.</summary>
    public List<MovimientoAfiliatorioDto> Movimientos { get; set; } = [];
}

public class MovimientoAfiliatorioDto
{
    public Guid Id { get; set; }
    public Guid TrabajadorId { get; set; }
    public string Nombre { get; set; } = "";

    /// <summary>alta, modificacion, baja.</summary>
    public string Tipo { get; set; } = "";

    public DateTime Fecha { get; set; }
    public decimal SalarioDiario { get; set; }
    public string? CausaBaja { get; set; }
    public DateTime? PresentadoUtc { get; set; }
}

public class CuotasDelMesDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }

    /// <summary>Cuántos periodos calculados entraron (los que terminan en el mes).</summary>
    public int Periodos { get; set; }

    /// <summary>Periodos del mes sin calcular: faltan en la suma.</summary>
    public int SinCalcular { get; set; }

    public CuotasDeNomina Cuotas { get; set; } = new();
}

public class TrabajadorDto : GuardarTrabajadorDto
{
    public DateTime? FechaBaja { get; set; }
}

public class GuardarTrabajadorDto
{
    /// <summary>Null: uno nuevo.</summary>
    public Guid? Id { get; set; }

    /// <summary>Nombre(s), sin apellidos.</summary>
    public string Nombre { get; set; } = "";

    public string? PrimerApellido { get; set; }
    public string? SegundoApellido { get; set; }
    public string? CodigoPostal { get; set; }
    public string? NumeroEmpleado { get; set; }
    public string? Umf { get; set; }
    public string? Rfc { get; set; }
    public string? Curp { get; set; }
    public string? Nss { get; set; }
    public DateTime FechaIngreso { get; set; }
    public decimal SalarioDiario { get; set; }
    public decimal? SalarioDiarioIntegrado { get; set; }
    public int DiasAguinaldo { get; set; } = 15;
    public decimal PrimaVacacionalPct { get; set; } = 25;
    public string? CreditoInfonavitTipo { get; set; }
    public decimal? CreditoInfonavitValor { get; set; }
    public decimal? Fonacot { get; set; }
}

public class CrearPeriodoDto
{
    public DateTime Inicio { get; set; }
    public DateTime Fin { get; set; }

    /// <summary>semanal, catorcenal, quincenal, mensual.</summary>
    public string Periodicidad { get; set; } = "quincenal";
}

public class PeriodoDeNominaDto
{
    public Guid Id { get; set; }
    public DateTime Inicio { get; set; }
    public DateTime Fin { get; set; }
    public string Periodicidad { get; set; } = "";
    public int Dias { get; set; }
    public DateTime? CalculadoUtc { get; set; }

    /// <summary>Solo al pedir un periodo (no en la lista).</summary>
    public List<IncidenciasDeTrabajadorDto> Incidencias { get; set; } = [];

    public NominaDelMotor? Resultado { get; set; }

    /// <summary>En la lista: el neto del periodo, si está calculado.</summary>
    public decimal? Neto { get; set; }
}

public class IncidenciasDeTrabajadorDto
{
    public Guid TrabajadorId { get; set; }
    public string? Nombre { get; set; }
    public IncidenciasDeNomina Incidencias { get; set; } = new();
}
