using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Leyes;

/// <summary>
/// Buscar en leyes fiscales, reglamentos, la Resolución Miscelánea y las NIF. Lo hace el índice
/// del motor fiscal (motores-fiscales); no hay IA de por medio: devuelve los artículos tal cual,
/// con su cita. Cualquiera con sesión: es información pública, no de ningún cliente.
/// </summary>
public interface ILeyesAppService : IApplicationService
{
    /// <summary>De página en página (<see cref="BuscarEnLeyesDto.Pagina"/>): «Ver más» pide la siguiente.</summary>
    Task<PaginaDeLeyesDto> BuscarAsync(BuscarEnLeyesDto input);
}

public class BuscarEnLeyesDto
{
    /// <summary>Palabras o la cita («artículo 27 LISR», «deducción de automóviles»).</summary>
    [Required]
    [StringLength(300, MinimumLength = 2)]
    public string Consulta { get; set; } = "";

    /// <summary>Siglas del ordenamiento (LISR, CFF, RMF…) o null para todos.</summary>
    [StringLength(20)]
    public string? Ley { get; set; }

    /// <summary>1 la primera; cada una trae <c>LeyesAppService.PorPagina</c> artículos.</summary>
    [Range(1, 40)]
    public int Pagina { get; set; } = 1;
}

public class PaginaDeLeyesDto
{
    public List<ArticuloDeLeyDto> Resultados { get; set; } = [];

    /// <summary>Hay más artículos sobre el tema: la pantalla ofrece «Ver más».</summary>
    public bool HayMas { get; set; }
}

public class ArticuloDeLeyDto
{
    /// <summary>Siglas: LISR, CFF, RMF…</summary>
    public string Ley { get; set; } = "";

    /// <summary>Nombre completo del ordenamiento.</summary>
    public string Ordenamiento { get; set; } = "";

    public string? Articulo { get; set; }

    /// <summary>Título, capítulo o sección donde está.</summary>
    public string? Ubicacion { get; set; }

    public string? UltimaReformaDof { get; set; }
    public string? NotaDeVigencia { get; set; }

    /// <summary>El documento oficial (Cámara de Diputados, SAT, DOF…).</summary>
    public string? Url { get; set; }

    public string Texto { get; set; } = "";
}
