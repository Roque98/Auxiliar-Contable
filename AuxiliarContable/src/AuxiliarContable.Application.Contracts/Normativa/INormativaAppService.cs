using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Normativa;

/// <summary>
/// Análisis normativo del despacho: se plantea una operación o pregunta de un cliente y el motor arma el expediente
/// (núcleo fiscal + leyes que activa el perfil, chispazos, cadena de cada artículo, normativa del ejercicio) y Claude
/// lo razona con su fundamento. Sólo el equipo del despacho. Tarda: se pide y se consulta después.
/// </summary>
public interface INormativaAppService : IApplicationService
{
    Task<AnalisisNormativoDto> PedirAsync(Guid organizacionId, PedirAnalisisNormativoDto input);

    /// <summary>Los análisis del cliente, del más reciente al más viejo (sin el resultado completo).</summary>
    Task<List<AnalisisNormativoDto>> GetConsultasAsync(Guid organizacionId);

    /// <summary>Un análisis con el resultado completo (JSON del motor).</summary>
    Task<AnalisisNormativoDto> GetAnalisisAsync(Guid organizacionId, Guid id);

    /// <summary>Lo que se puede marcar a mano (además de lo que el motor detecta solo).</summary>
    List<DisparadorDto> GetDisparadores();
}

public class PedirAnalisisNormativoDto
{
    /// <summary>La operación o la pregunta, en palabras del despacho.</summary>
    [Required]
    [StringLength(2000)]
    public string Pregunta { get; set; } = "";

    /// <summary>Fecha de la operación: si es de otro año se usa la normativa de ese ejercicio.</summary>
    public DateTime? FechaDeLaOperacion { get; set; }

    /// <summary>«fisica» o «moral».</summary>
    [StringLength(20)]
    public string? ContraparteTipoDePersona { get; set; }

    /// <summary>País de residencia de la contraparte (MX, US…).</summary>
    [StringLength(60)]
    public string? ContraparteResidencia { get; set; }

    public List<string> Disparadores { get; set; } = [];
}

public class AnalisisNormativoDto
{
    public Guid Id { get; set; }
    public string Pregunta { get; set; } = "";
    public DateTime? FechaDeLaOperacion { get; set; }
    public string? ContraparteTipoDePersona { get; set; }
    public string? ContraparteResidencia { get; set; }
    public List<string> Disparadores { get; set; } = [];

    /// <summary>Pendiente, Listo o Error.</summary>
    public string Estado { get; set; } = "";

    public string? Conclusion { get; set; }
    public string? Confianza { get; set; }
    public string? Error { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? ListoUtc { get; set; }

    /// <summary>El JSON del motor (sólo en <see cref="INormativaAppService.GetAnalisisAsync"/>).</summary>
    public string? Resultado { get; set; }
}

public class DisparadorDto
{
    public string Clave { get; set; } = "";
    public string Descripcion { get; set; } = "";
}
