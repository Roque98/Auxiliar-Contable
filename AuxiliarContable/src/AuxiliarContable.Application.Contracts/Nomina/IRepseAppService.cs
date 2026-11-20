using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Nomina;

/// <summary>
/// Servicios especializados (REPSE) de un cliente: sus contratos, la revisión de cada mes (los documentos que el
/// beneficiario debe recabar del contratista para deducir y acreditar, art. 27-V LISR y 5-II LIVA) y el informe
/// cuatrimestral del prestador al IMSS (ICSOE, art. 15-A LSS) y al INFONAVIT (SISUB, art. 29 Bis LINFONAVIT). Solo el
/// equipo del despacho.
/// </summary>
public interface IRepseAppService : IApplicationService
{
    Task<RepseDelMesDto> GetDelMesAsync(Guid organizacionId, int anio, int mes);

    Task<ContratoEspecializadoDto> GuardarContratoAsync(Guid organizacionId, GuardarContratoEspecializadoDto dto);

    Task BorrarContratoAsync(Guid organizacionId, Guid contratoId);

    /// <summary>Marca o desmarca una casilla del mes de un contrato, o el informe cuatrimestral (sin contrato).</summary>
    Task MarcarAsync(Guid organizacionId, MarcarRepseDto dto);

    /// <summary>Hoja (CSV) con lo que pide el informe cuatrimestral: partes, contratos y trabajadores con su SBC.</summary>
    Task<InformeCuatrimestralDto> InformeCuatrimestralAsync(Guid organizacionId, int anio, int cuatrimestre);
}

public class GuardarContratoEspecializadoDto
{
    /// <summary>Null: uno nuevo.</summary>
    public Guid? Id { get; set; }

    /// <summary>presta o recibe.</summary>
    public string Rol { get; set; } = "presta";

    public string ContraparteRfc { get; set; } = "";
    public string ContraparteNombre { get; set; } = "";
    public string Objeto { get; set; } = "";
    public DateTime Inicio { get; set; }
    public DateTime? Fin { get; set; }
    public int TrabajadoresAproximados { get; set; }
    public string? RegistroRepse { get; set; }
    public DateTime? RegistroVigenteHasta { get; set; }

    /// <summary>Solo si presta: los trabajadores del cliente asignados al contrato.</summary>
    public List<Guid> Trabajadores { get; set; } = [];
}

public class ContratoEspecializadoDto : GuardarContratoEspecializadoDto
{
}

public class RepseDelMesDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public bool PuedeEditar { get; set; }
    public List<ContratoEspecializadoDto> Contratos { get; set; } = [];

    /// <summary>Los contratos vigentes en el mes, con lo que hay que juntar o entregar.</summary>
    public List<ContratoDelMesDto> DelMes { get; set; } = [];

    /// <summary>Si el cliente presta servicios: el informe cuatrimestral que vence a partir de este mes.</summary>
    public InformeRepseDto? Informe { get; set; }

    /// <summary>Registros por vencer, contratos sin registro o sin trabajadores.</summary>
    public List<string> Avisos { get; set; } = [];
}

public class ContratoDelMesDto
{
    public Guid ContratoId { get; set; }
    public string Rol { get; set; } = "";
    public string Contraparte { get; set; } = "";

    /// <summary>Facturas del mes con la otra parte (recibidas si el cliente recibe el servicio; emitidas si lo presta).</summary>
    public int Facturas { get; set; }

    public decimal Importe { get; set; }

    /// <summary>Último día para juntar o entregar los documentos: el último del mes siguiente (art. 5-II LIVA).</summary>
    public DateTime Limite { get; set; }

    public List<CasillaRepseDto> Casillas { get; set; } = [];

    /// <summary>¿Hay facturas, faltan casillas y ya pasó el límite?</summary>
    public bool Vencido { get; set; }
}

public class CasillaRepseDto
{
    public string Clave { get; set; } = "";
    public string Texto { get; set; } = "";
    public string Fundamento { get; set; } = "";
    public bool Cumplida { get; set; }
}

public class InformeRepseDto
{
    public int Anio { get; set; }
    public int Cuatrimestre { get; set; }

    /// <summary>«enero a abril de 2026».</summary>
    public string Meses { get; set; } = "";

    /// <summary>El día 17 de enero, mayo o septiembre.</summary>
    public DateTime Limite { get; set; }

    public List<CasillaRepseDto> Casillas { get; set; } = [];
}

public class MarcarRepseDto
{
    /// <summary>Null: el informe cuatrimestral.</summary>
    public Guid? ContratoId { get; set; }

    public int Anio { get; set; }

    /// <summary>Mes (con contrato) o cuatrimestre (sin contrato).</summary>
    public int Periodo { get; set; }

    public string Clave { get; set; } = "";
    public bool Cumplida { get; set; }
}

public class InformeCuatrimestralDto
{
    public string Nombre { get; set; } = "";
    public string ContenidoB64 { get; set; } = "";
    public List<string> Avisos { get; set; } = [];
}
