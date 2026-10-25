using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace IgasConsole.Actividad;

/// <summary>Qué tipo de cosa pasó (para filtrar y para el icono).</summary>
public static class TipoDeActividad
{
    public const string Sesion = "sesion";
    public const string Reporte = "reporte";
    public const string Organizacion = "organizacion";
    public const string Personas = "personas";
}

/// <summary>Una cosa que hizo alguien, contada en una frase.</summary>
public class ActividadDto
{
    public DateTime Fecha { get; set; }
    public Guid UsuarioId { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string? NombreCompleto { get; set; }

    /// <summary>Las organizaciones de la persona (si es cliente): «Carlos (Ferretería Norte)».</summary>
    public List<string> Organizaciones { get; set; } = [];

    public string Tipo { get; set; } = string.Empty;

    /// <summary>«Descargó el PDF para el cliente de EDS-0001 · julio 2026».</summary>
    public string Texto { get; set; } = string.Empty;

    /// <summary>Si la misma acción se repitió seguida (abrir el mismo reporte varias veces).</summary>
    public int Veces { get; set; } = 1;

    public string? Ip { get; set; }
}

public class GetActividadInput
{
    /// <summary>Por defecto, los últimos 30 días.</summary>
    public DateTime? Desde { get; set; }

    /// <summary>Paginación hacia atrás: lo anterior a esta fecha (la del último que se vio).</summary>
    public DateTime? AntesDe { get; set; }

    public Guid? UsuarioId { get; set; }
    public Guid? OrganizacionId { get; set; }

    /// <summary>Uno de <see cref="TipoDeActividad"/>, o vacío para todo.</summary>
    public string? Tipo { get; set; }
}

public class PaginaDeActividadDto
{
    public List<ActividadDto> Items { get; set; } = [];

    /// <summary>Lo que hay que mandar como AntesDe para seguir; null si no hay más.</summary>
    public DateTime? SiguienteAntesDe { get; set; }
}

public class PersonaDeActividadDto
{
    public Guid Id { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string? NombreCompleto { get; set; }
}

public class OpcionesDeActividadDto
{
    public List<PersonaDeActividadDto> Personas { get; set; } = [];
    public List<Organizaciones.OrganizacionResumenDto> Organizaciones { get; set; } = [];

    /// <summary>Hasta cuántos días atrás llega el registro de llamadas (vistas, descargas…).</summary>
    public int DiasDeRegistro { get; set; }
}

/// <summary>
/// Quién hizo qué en la consola, contado en frases. Sale de lo que ya registran ABP y las
/// tablas propias; respeta la jerarquía (nadie ve al Developer).
/// </summary>
public interface IActividadAppService : IApplicationService
{
    Task<PaginaDeActividadDto> GetListAsync(GetActividadInput input);

    Task<OpcionesDeActividadDto> GetOpcionesAsync();
}
