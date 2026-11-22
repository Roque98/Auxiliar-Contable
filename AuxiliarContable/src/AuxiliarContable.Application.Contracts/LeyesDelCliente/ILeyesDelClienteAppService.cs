using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AuxiliarContable.LeyesDelCliente;

/// <summary>
/// «Mis leyes»: el cliente sube las leyes estatales y municipales que le afectan (ley de ingresos, hacienda, código fiscal,
/// protección civil, comercio…); el motor las lee, busca lo que le toca según su perfil y propone hallazgos con su cita
/// literal. El despacho acepta o rechaza cada uno: un valor aceptado reemplaza al del sistema solo para ese cliente.
/// </summary>
public interface ILeyesDelClienteAppService : IApplicationService
{
    Task<MisLeyesDto> GetMisLeyesAsync(Guid organizacionId);

    /// <summary>Sube una ley en PDF. <paramref name="ambito"/>: estatal o municipal.</summary>
    Task<LeyDelClienteDto> SubirAsync(Guid organizacionId, IRemoteStreamContent archivo, string ambito, string? estado = null,
        string? municipio = null, string? materia = null);

    /// <summary>Quita la ley y sus hallazgos (los aceptados dejan de contar). El PDF se queda en la bóveda.</summary>
    Task EliminarAsync(Guid organizacionId, Guid id);

    /// <summary>Solo el despacho: vuelve a analizar aunque nada haya cambiado.</summary>
    Task ReanalizarAsync(Guid organizacionId, Guid id);

    /// <summary>Solo el despacho: acepta o rechaza un hallazgo.</summary>
    Task<HallazgoDeLeyDto> DecidirAsync(Guid organizacionId, Guid id, DecidirHallazgoDto input);

    Task<PreguntaSobreLeyesDto> PreguntarAsync(Guid organizacionId, PreguntarSobreLeyesDto input);
}

public class MisLeyesDto
{
    /// <summary>El mensaje que se le dice a quien entra al apartado.</summary>
    public string Mensaje { get; set; } = LeyesDelClienteConsts.Mensaje;

    /// <summary>¿Quien mira es del despacho? (solo el despacho decide los hallazgos)</summary>
    public bool EsDelDespacho { get; set; }

    /// <summary>El estado del domicilio fiscal (clave), para proponerlo al subir.</summary>
    public string? EstadoDelCliente { get; set; }

    public List<LeyDelClienteDto> Leyes { get; set; } = [];
    public List<HallazgoDeLeyDto> Hallazgos { get; set; } = [];
    public List<PreguntaSobreLeyesDto> Preguntas { get; set; } = [];

    /// <summary>¿Hay algo leyéndose, analizándose o por contestar? (la pantalla vuelve a preguntar)</summary>
    public bool EnCurso { get; set; }
}

public class LeyDelClienteDto
{
    public Guid Id { get; set; }
    public Guid DocumentoId { get; set; }
    public string NombreDelArchivo { get; set; } = "";
    public string Ambito { get; set; } = "";
    public string? Estado { get; set; }
    public string? Municipio { get; set; }
    public string? Materia { get; set; }
    public string? Titulo { get; set; }

    /// <summary>Pendiente, Leyendo, Lista o Error.</summary>
    public string Lectura { get; set; } = "";

    public int? Paginas { get; set; }
    public int? PaginasConOcr { get; set; }
    public int? PaginasLeidas { get; set; }
    public int? Articulos { get; set; }

    /// <summary>sin_leer, por_analizar, analizada o error.</summary>
    public string Analisis { get; set; } = "";

    public string? Resumen { get; set; }
    public List<string> NoEncontrado { get; set; } = [];
    public List<string> Temas { get; set; } = [];
    public DateTime? AnalizadoUtc { get; set; }
    public string? Error { get; set; }
    public DateTime CreationTime { get; set; }
}

public class HallazgoDeLeyDto
{
    public Guid Id { get; set; }
    public Guid LeyId { get; set; }
    public string Tipo { get; set; } = "";
    public string? Tema { get; set; }
    public string Titulo { get; set; } = "";
    public string? Clave { get; set; }
    public string? Valor { get; set; }
    public decimal? ValorNumerico { get; set; }
    public string? ValorDelSistema { get; set; }
    public string? Condicion { get; set; }
    public string? Periodicidad { get; set; }
    public string? FechaLimite { get; set; }
    public string Articulo { get; set; } = "";
    public string? Etiqueta { get; set; }
    public int? Pagina { get; set; }
    public string Cita { get; set; } = "";
    public string? Explicacion { get; set; }

    /// <summary>Propuesto, Aceptado, Rechazado o Reemplazado.</summary>
    public string Decision { get; set; } = "";

    public DateTime? DecididoUtc { get; set; }

    /// <summary>De qué ley salió (título o nombre del archivo).</summary>
    public string? Ley { get; set; }
}

public class DecidirHallazgoDto
{
    public bool Aceptar { get; set; }
}

public class PreguntarSobreLeyesDto
{
    public string Pregunta { get; set; } = "";
}

public class PreguntaSobreLeyesDto
{
    public Guid Id { get; set; }
    public string Pregunta { get; set; } = "";

    /// <summary>Pendiente, Lista o Error.</summary>
    public string Estado { get; set; } = "";

    /// <summary>JSON del motor: encontrado, respuesta, citas, falta.</summary>
    public string? Respuesta { get; set; }

    public string? Error { get; set; }
    public DateTime CreationTime { get; set; }
}
