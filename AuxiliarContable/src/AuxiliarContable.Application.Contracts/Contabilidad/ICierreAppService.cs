using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AuxiliarContable.Bancos;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// El cierre mensual de un cliente, como en un sistema contable: llegan los documentos del periodo,
/// se concilian los bancos (<see cref="IBancosAppService.ConciliarAsync"/> la guarda), se depuran las
/// partidas sin factura y se cierra. Cerrar, reabrir y resolver partidas es del despacho; el cliente
/// ve su cierre y contesta lo que se le pregunta.
/// </summary>
public interface ICierreAppService : IApplicationService
{
    /// <summary>Cómo va el mes: lista de lo que falta, la última conciliación y sus partidas.</summary>
    Task<CierreDelMesDto> GetAsync(Guid organizacionId, int anio, int mes);

    /// <summary>Lo que el despacho le preguntó al cliente y sigue sin respuesta, de todos sus meses.</summary>
    Task<List<PartidaDto>> GetPreguntasAsync(Guid organizacionId);

    /// <summary>El despacho decide qué es un movimiento sin factura.</summary>
    Task<PartidaDto> ResolverAsync(Guid organizacionId, Guid partidaId, ResolverPartidaDto input);

    /// <summary>El cliente contesta qué es un movimiento por el que se le preguntó.</summary>
    Task<PartidaDto> ResponderAsync(Guid organizacionId, Guid partidaId, ResponderPartidaDto input);

    /// <summary>Solo si está listo: conciliado con los archivos de hoy y sin partidas pendientes.</summary>
    Task<CierreDelMesDto> CerrarAsync(Guid organizacionId, int anio, int mes);

    Task<CierreDelMesDto> ReabrirAsync(Guid organizacionId, int anio, int mes);
}

public class CierreDelMesDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public EtapaDelPeriodo Etapa { get; set; }

    /// <summary>Lo que se revisa para cerrar, en orden. Lo que tiene <c>Bloquea</c> impide cerrar.</summary>
    public List<RenglonDelCierreDto> Revision { get; set; } = [];

    public int EstadosDeCuenta { get; set; }
    public int Facturas { get; set; }

    /// <summary>La última conciliación guardada, o null si nunca se concilió.</summary>
    public ConciliacionDelMesDto? Conciliacion { get; set; }

    /// <summary>Se subió, borró o movió un documento del mes después de conciliar: hay que volver a conciliar.</summary>
    public bool ConciliacionDesactualizada { get; set; }

    public List<PartidaDto> Partidas { get; set; } = [];

    public DateTime? CerradoUtc { get; set; }

    /// <summary>¿Quien mira puede resolver partidas, cerrar y reabrir? (el despacho).</summary>
    public bool PuedeCerrar { get; set; }
}

public class RenglonDelCierreDto
{
    public string Texto { get; set; } = string.Empty;
    public bool Hecho { get; set; }

    /// <summary>Sin esto no se puede cerrar. Lo demás es aviso.</summary>
    public bool Bloquea { get; set; }
}

public class PartidaDto
{
    public Guid Id { get; set; }
    public int Anio { get; set; }
    public int Mes { get; set; }
    public string Clave { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public ResolucionDePartida Resolucion { get; set; }
    public string? Nota { get; set; }
    public string? Respuesta { get; set; }
    public DateTime? RespondidaUtc { get; set; }

    /// <summary>¿Detiene el cierre?</summary>
    public bool Pendiente { get; set; }
}

public class ResolverPartidaDto
{
    public ResolucionDePartida Resolucion { get; set; }

    /// <summary>La pregunta para el cliente, o por qué se resolvió así.</summary>
    public string? Nota { get; set; }
}

public class ResponderPartidaDto
{
    public string Respuesta { get; set; } = string.Empty;
}
