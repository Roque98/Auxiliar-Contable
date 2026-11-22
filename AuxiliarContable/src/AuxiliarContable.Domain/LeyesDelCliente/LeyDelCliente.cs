using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.LeyesDelCliente;

/// <summary>
/// Una ley estatal o municipal que subió el cliente porque el sistema no la tiene o no calcula como dice (apartado «Mis
/// leyes», pedido del 22-nov-2026). El PDF vive en la bóveda (<see cref="DocumentoId"/>, cifrado); aquí va en qué va su
/// lectura en el motor y su análisis contra el perfil del cliente.
///
/// <para>El análisis (claude -p) se repite solo cuando cambia la huella de lo que se le preguntó: el perfil del cliente,
/// el ejercicio o los valores del sistema. Así «en tiempo real»: si el cliente contrata trabajadores o cambia de giro, la
/// siguiente vuelta del trabajador vuelve a buscar en sus leyes lo que ahora le toca.</para>
/// </summary>
public class LeyDelCliente : CreationAuditedAggregateRoot<Guid>
{
    public const int MaxIntentos = 3;
    public const int MaxIntentosDeLectura = 10;

    public Guid OrganizacionId { get; protected set; }
    public Guid DocumentoId { get; protected set; }

    /// <summary>SHA-256 del PDF: es también la llave del documento en el motor.</summary>
    public string Sha256 { get; protected set; } = string.Empty;

    public string NombreDelArchivo { get; protected set; } = string.Empty;
    public AmbitoDeLey Ambito { get; protected set; }

    /// <summary>Clave del estado (JAL, NLE, CMX…).</summary>
    public string? Estado { get; protected set; }

    public string? Municipio { get; protected set; }

    /// <summary>Lo que dijo quien la subió: «Ley de ingresos 2026», «Reglamento de protección civil»…</summary>
    public string? Materia { get; protected set; }

    /// <summary>El título que leyó el motor del propio documento.</summary>
    public string? Titulo { get; protected set; }

    /// <summary>Si es una ley que ya teníamos del estado («MOR/tabaco», catálogo del motor): no hay PDF en la bóveda
    /// (<see cref="DocumentoId"/> vacío) y el motor la lee de su propia copia.</summary>
    public string? Biblioteca { get; protected set; }

    public LecturaDeLey Lectura { get; protected set; }
    public int? Paginas { get; protected set; }
    public int? PaginasConOcr { get; protected set; }
    public int? PaginasLeidas { get; protected set; }
    public int? Articulos { get; protected set; }
    public int IntentosDeLectura { get; protected set; }

    /// <summary>Huella (perfil + sistema + ejercicio) del último análisis que salió bien.</summary>
    public string? HuellaAnalizada { get; protected set; }

    /// <summary>Huella que se está intentando; si cambia, los intentos vuelven a cero.</summary>
    public string? HuellaEnCurso { get; protected set; }

    public int Intentos { get; protected set; }
    public DateTime ProximoIntentoUtc { get; protected set; }
    public DateTime? AnalizadoUtc { get; protected set; }
    public string? Resumen { get; protected set; }

    /// <summary>El JSON completo del motor (temas, artículos revisados, no encontrado).</summary>
    public string? Resultado { get; protected set; }

    public string? Error { get; protected set; }

    protected LeyDelCliente()
    {
    }

    public LeyDelCliente(Guid id, Guid organizacionId, Guid documentoId, string sha256, string nombre, AmbitoDeLey ambito,
        string? estado, string? municipio, string? materia, DateTime utc) : base(id)
    {
        OrganizacionId = organizacionId;
        DocumentoId = documentoId;
        Sha256 = sha256;
        NombreDelArchivo = Corto(nombre, 255) ?? "ley.pdf";
        Ambito = ambito;
        Estado = Corto(estado?.ToUpperInvariant(), LeyesDelClienteConsts.MaxEstadoLength);
        Municipio = Corto(municipio, LeyesDelClienteConsts.MaxMunicipioLength);
        Materia = Corto(materia, LeyesDelClienteConsts.MaxMateriaLength);
        Lectura = LecturaDeLey.Pendiente;
        ProximoIntentoUtc = utc;
    }

    /// <summary>Una ley del catálogo del estado que el cliente decidió usar sin subirla.</summary>
    public static LeyDelCliente DeBiblioteca(Guid id, Guid organizacionId, string estado, string ley, string sha256, string nombre,
        DateTime utc)
    {
        return new LeyDelCliente(id, organizacionId, Guid.Empty, sha256, nombre, AmbitoDeLey.Estatal, estado, null, nombre, utc)
        {
            Biblioteca = Corto($"{estado}/{ley}", LeyesDelClienteConsts.MaxBibliotecaLength),
        };
    }

    public void Leyendo(int? paginas, int? conOcr, int? leidas, DateTime proxima)
    {
        Lectura = LecturaDeLey.Leyendo;
        Paginas = paginas;
        PaginasConOcr = conOcr;
        PaginasLeidas = leidas;
        ProximoIntentoUtc = proxima;
        Error = null;
    }

    public void Leida(int? paginas, int? conOcr, int? articulos, string? titulo, DateTime utc)
    {
        Lectura = LecturaDeLey.Lista;
        Paginas = paginas;
        PaginasConOcr = conOcr;
        PaginasLeidas = conOcr;
        Articulos = articulos;
        Titulo = Corto(titulo, LeyesDelClienteConsts.MaxTituloLength);
        IntentosDeLectura = 0;
        ProximoIntentoUtc = utc;
        Error = null;
    }

    /// <summary>El motor perdió su lectura (se reinició o se borró su estado): se le vuelve a mandar el PDF.</summary>
    public void VolverALeer(DateTime utc)
    {
        Lectura = LecturaDeLey.Pendiente;
        ProximoIntentoUtc = utc;
    }

    /// <summary>El PDF no sirve (no abre, no trae texto): no se reintenta.</summary>
    public void NoSePudoLeer(string error)
    {
        Lectura = LecturaDeLey.Error;
        Error = Corto(error, LeyesDelClienteConsts.MaxErrorLength);
    }

    /// <summary>El motor no respondió: se reintenta más tarde.</summary>
    public void LecturaFallida(string error, DateTime proxima)
    {
        IntentosDeLectura++;
        Error = Corto(error, LeyesDelClienteConsts.MaxErrorLength);
        ProximoIntentoUtc = proxima;
        if (IntentosDeLectura >= MaxIntentosDeLectura)
        {
            Lectura = LecturaDeLey.Error;
        }
    }

    /// <summary>¿Hay que (re)analizarla con esta huella?</summary>
    public bool TocaAnalizar(string huella, DateTime ahora) =>
        Lectura == LecturaDeLey.Lista && HuellaAnalizada != huella && ProximoIntentoUtc <= ahora
        && (HuellaEnCurso != huella || Intentos < MaxIntentos);

    public void Analizando(string huella)
    {
        if (HuellaEnCurso != huella)
        {
            HuellaEnCurso = huella;
            Intentos = 0;
        }
    }

    public void Analizada(string huella, string? resumen, string resultado, DateTime utc)
    {
        HuellaAnalizada = huella;
        HuellaEnCurso = huella;
        Intentos = 0;
        Resumen = Corto(resumen, LeyesDelClienteConsts.MaxResumenLength);
        Resultado = resultado;
        AnalizadoUtc = utc;
        Error = null;
    }

    public void AnalisisFallido(string error, DateTime proxima)
    {
        Intentos++;
        Error = Corto(error, LeyesDelClienteConsts.MaxErrorLength);
        ProximoIntentoUtc = proxima;
    }

    /// <summary>El despacho pide que se vuelva a analizar aunque nada haya cambiado.</summary>
    public void Reanalizar(DateTime utc)
    {
        HuellaAnalizada = null;
        HuellaEnCurso = null;
        Intentos = 0;
        ProximoIntentoUtc = utc;
        if (Lectura == LecturaDeLey.Error)
        {
            Lectura = LecturaDeLey.Pendiente;
            IntentosDeLectura = 0;
        }
    }

    public static string? Corto(string? t, int max)
    {
        t = t?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length > max ? t[..max] : t;
    }
}
