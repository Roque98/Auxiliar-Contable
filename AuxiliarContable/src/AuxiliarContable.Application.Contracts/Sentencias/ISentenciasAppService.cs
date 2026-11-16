using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using AuxiliarContable.MotorFiscal;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Sentencias;

/// <summary>
/// La librería de sentencias del motor fiscal: tesis, precedentes y jurisprudencias fiscales del TFJA y de la
/// SCJN, cada una con su conclusión, a quién favorece, su fundamento y qué hacer. Buscar: cualquiera con sesión
/// (es información pública). Actualizar (bajar lo nuevo de las fuentes y estudiarlo): solo el equipo.
/// </summary>
public interface ISentenciasAppService : IApplicationService
{
    Task<List<SentenciaDelMotor>> BuscarAsync(BuscarSentenciasDto input);

    /// <summary>Cuántas hay, cuántas están estudiadas y cómo va la actualización; y si quien pregunta puede actualizar.</summary>
    Task<EstadoDeLaLibreriaDto> GetEstadoAsync();

    /// <summary>Arranca la actualización en el motor y devuelve el estado. Si ya hay una en curso, no arranca otra.</summary>
    Task<EstadoDeLaLibreriaDto> ActualizarAsync();
}

public class BuscarSentenciasDto
{
    /// <summary>La situación o el tema con palabras («deducción de servicios sin evidencia de que se prestaron»).</summary>
    [Required]
    [StringLength(300, MinimumLength = 2)]
    public string Consulta { get; set; } = "";

    /// <summary>TFJA, SCJN o null para las dos.</summary>
    [StringLength(4)]
    public string? Fuente { get; set; }
}

public class EstadoDeLaLibreriaDto
{
    public EstadoDeSentencias Estado { get; set; } = new();
    public bool PuedeActualizar { get; set; }
}
