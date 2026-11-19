using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// El botón «Conciliar» del cierre del mes: todo lo de la plataforma en una sola vuelta, de los meses abiertos del año
/// hasta el elegido. Sólo el equipo del despacho. Tarda (Claude, el SAT, el motor): se pide y se consulta el avance.
/// </summary>
public interface IConciliacionIntegralAppService : IApplicationService
{
    Task<ConciliacionIntegralDto> PedirAsync(Guid organizacionId, int anio, int mes);

    /// <summary>La última vuelta del cliente (la que está corriendo o la más reciente); null si nunca se ha pedido.</summary>
    Task<ConciliacionIntegralDto?> GetUltimaAsync(Guid organizacionId);
}

public class ConciliacionIntegralDto
{
    public Guid Id { get; set; }
    public int Anio { get; set; }
    public int Mes { get; set; }

    /// <summary>Pendiente, Trabajando, Lista o Error.</summary>
    public string Estado { get; set; } = "";

    public DateTime PedidaUtc { get; set; }
    public DateTime? IniciadaUtc { get; set; }
    public DateTime? TerminadaUtc { get; set; }
    public List<PasoDeConciliacionDto> Pasos { get; set; } = [];
    public List<MesConciliadoDto> Meses { get; set; } = [];
    public string? Error { get; set; }
}

public class PasoDeConciliacionDto
{
    /// <summary>AAAA-MM; vacío si el paso es de toda la vuelta.</summary>
    public string Periodo { get; set; } = "";

    public string Clave { get; set; } = "";
    public string Titulo { get; set; } = "";

    /// <summary>pendiente, trabajando, hecho, aviso, error, omitido.</summary>
    public string Estado { get; set; } = "pendiente";

    public string? Resumen { get; set; }
}

public class MesConciliadoDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public int Polizas { get; set; }
    public int Detenidas { get; set; }
    public decimal? IsrAPagar { get; set; }
    public decimal? IvaAPagar { get; set; }
    public decimal? IvaAFavor { get; set; }
    public decimal? UtilidadDelEjercicio { get; set; }
    public bool EstadosPreliminares { get; set; }
    public int Notas { get; set; }
    public List<string> Avisos { get; set; } = [];
    public List<AlertaNormativaDto> AlertasNormativas { get; set; } = [];
}

public class AlertaNormativaDto
{
    public string Disparador { get; set; } = "";
    public string Pregunta { get; set; } = "";

    /// <summary>«LIVA 29», «Reg_LIVA_250914 58»…</summary>
    public List<string> Fuentes { get; set; } = [];
}
