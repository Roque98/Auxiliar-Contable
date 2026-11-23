using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.LeyesDelCliente;

/// <summary>
/// Reporte para el despacho (pedido del 22-nov-2026): qué municipios cubre la biblioteca de reglamentos municipales, cuáles
/// están en proceso y cuáles no, con el criterio; y qué clientes están en un municipio que no cubrimos (de ellos solo
/// sabemos lo federal y lo estatal).
/// </summary>
public interface ICoberturaMunicipalAppService : IApplicationService
{
    Task<CoberturaMunicipalDto> GetReporteAsync();
}

public class CoberturaMunicipalDto
{
    public string? Criterio { get; set; }
    public int Cubiertos { get; set; }

    /// <summary>Con algunos reglamentos (ley de ingresos, bando…), no todos.</summary>
    public int Parciales { get; set; }

    /// <summary>Se revisaron y no publican nada en línea (portal caído, bloqueado o sin reglamentos).</summary>
    public int SinFuente { get; set; }

    /// <summary>Mínimo de reglamentos propios para contar un municipio como cubierto.</summary>
    public int MinimoCubierto { get; set; }

    public int EnProceso { get; set; }
    public int NoCubiertos { get; set; }

    /// <summary>Clientes cuyo municipio no está cubierto (o no se supo): primero lo que el despacho debe atender.</summary>
    public List<ClienteSinCoberturaDto> Clientes { get; set; } = [];

    public List<CoberturaDeEstadoDto> Estados { get; set; } = [];

    /// <summary>Si el motor no respondió.</summary>
    public string? Aviso { get; set; }
}

public class ClienteSinCoberturaDto
{
    public Guid OrganizacionId { get; set; }
    public string Nombre { get; set; } = "";
    public string? Estado { get; set; }
    public string? Municipio { get; set; }

    /// <summary>parcial, sin_fuente, seleccionado (en proceso), no_cubierto o sin_municipio (no se supo por el domicilio).</summary>
    public string Cobertura { get; set; } = "";
}

public class CoberturaDeEstadoDto
{
    public string Estado { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int Municipios { get; set; }
    public double? PoblacionCubiertaPct { get; set; }
    public double? PoblacionSeleccionadaPct { get; set; }
    public List<string> Cubiertos { get; set; } = [];
    public List<string> Parciales { get; set; } = [];
    public List<string> SinFuente { get; set; } = [];
    public List<string> EnProceso { get; set; } = [];
    public List<string> NoCubiertos { get; set; } = [];
}
