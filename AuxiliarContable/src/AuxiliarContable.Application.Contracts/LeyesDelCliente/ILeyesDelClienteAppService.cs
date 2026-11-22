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

    /// <summary>Qué leyes estatales le tocan por su domicilio y su actividad (catálogo del motor, sin Claude), por qué, qué le
    /// piden y cuáles ya tenemos para usarlas sin subirlas.</summary>
    Task<LeyesQueTeAplicanDto> GetQueTeAplicanAsync(Guid organizacionId);

    /// <summary>Usa una ley que ya tenemos del estado del cliente (sin subir el PDF): entra a «Mis leyes» y se analiza igual.
    /// <paramref name="ley"/> es la clave del catálogo (salud, proteccion_civil…).</summary>
    Task<LeyDelClienteDto> UsarDeLaBibliotecaAsync(Guid organizacionId, string ley);
}

public class LeyesQueTeAplicanDto
{
    /// <summary>Clave del estado del domicilio fiscal (MOR, JAL…); null si no se sabe.</summary>
    public string? Estado { get; set; }

    public string? NombreDelEstado { get; set; }

    /// <summary>Cuando no se puede decir (sin domicilio, motor caído, estado sin ficha todavía).</summary>
    public string? Aviso { get; set; }

    /// <summary>Dónde están las versiones anteriores de las leyes del estado (para trabajar ejercicios pasados).</summary>
    public string? Historico { get; set; }

    public List<FuenteOficialDto> Fuentes { get; set; } = [];

    /// <summary>Lo que se tomó en cuenta del cliente: trabajadores, establecimiento, inmuebles…</summary>
    public List<string> Disparadores { get; set; } = [];

    /// <summary>Clave del bloque (A…G) -> título.</summary>
    public Dictionary<string, string> Bloques { get; set; } = [];

    public List<LeyQueTeAplicaDto> Leyes { get; set; } = [];
}

public class FuenteOficialDto
{
    public string Url { get; set; } = "";
    public string Que { get; set; } = "";
}

public class LeyQueTeAplicaDto
{
    /// <summary>Clave del catálogo (salud, proteccion_civil…).</summary>
    public string Id { get; set; } = "";

    /// <summary>Nombre genérico («Ley de salud»).</summary>
    public string Nombre { get; set; } = "";

    /// <summary>Nombre real en el estado («Ley de Salud del Estado de Morelos»).</summary>
    public string? NombreOficial { get; set; }

    public string Bloque { get; set; } = "";
    public List<string> Motivos { get; set; } = [];
    public string? Pide { get; set; }

    /// <summary>local (la tenemos), pendiente (existe y falta), no_existe, federal, municipal o sin_ficha.</summary>
    public string Estatus { get; set; } = "";

    public string? UltimaReforma { get; set; }
    public string? Url { get; set; }
    public string? Nota { get; set; }

    /// <summary>¿La tenemos para usarla sin subirla?</summary>
    public bool Disponible { get; set; }

    /// <summary>Cambia cada año (ley de ingresos).</summary>
    public bool Anual { get; set; }

    /// <summary>Si ya está en «Mis leyes», su id.</summary>
    public Guid? EnMisLeyes { get; set; }
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

    /// <summary>Si salió de las leyes que ya tenemos del estado («MOR/salud») y no la subió el cliente.</summary>
    public string? Biblioteca { get; set; }
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
