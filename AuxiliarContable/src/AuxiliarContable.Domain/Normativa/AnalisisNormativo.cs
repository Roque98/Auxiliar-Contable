using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Normativa;

/// <summary>
/// Un análisis de hipótesis normativa de un cliente: la pregunta u operación que planteó el despacho y lo que concluyó
/// el motor (índice de leyes + Claude) con su fundamento. Se pide desde la pantalla «Análisis normativo» y lo hace un
/// trabajo en segundo plano porque tarda minutos. Acordado el 19-nov-2026: núcleo fiscal siempre, leyes que activa el
/// perfil, chispazos y la cadena de cada artículo, sin aplicar normas de otro ejercicio.
/// </summary>
public class AnalisisNormativo : CreationAuditedAggregateRoot<Guid>
{
    public const int MaxPreguntaLength = 2000;
    public const int MaxCortoLength = 60;
    public const int MaxDisparadoresLength = 500;
    public const int MaxConclusionLength = 1500;
    public const int MaxErrorLength = 500;

    /// <summary>Si el motor o Claude no respondieron, se espera esto antes de reintentar.</summary>
    public static readonly TimeSpan EsperaTrasFalla = TimeSpan.FromMinutes(10);

    public const int MaxIntentos = 3;

    public Guid OrganizacionId { get; protected set; }
    public string Pregunta { get; protected set; } = string.Empty;

    /// <summary>Fecha de la operación: fija el ejercicio cuya normativa se aplica (art. 6 del CFF).</summary>
    public DateTime? FechaDeLaOperacion { get; protected set; }

    /// <summary>«fisica» o «moral».</summary>
    public string? ContraparteTipoDePersona { get; protected set; }

    /// <summary>País de residencia fiscal de la contraparte (MX, US…).</summary>
    public string? ContraparteResidencia { get; protected set; }

    /// <summary>Disparadores marcados a mano, separados por coma (los demás los detecta el motor).</summary>
    public string? Disparadores { get; protected set; }

    public EstadoDelAnalisis Estado { get; protected set; }
    public DateTime PedidoUtc { get; protected set; }
    public int Intentos { get; protected set; }

    /// <summary>El JSON completo que devolvió el motor (análisis + expediente).</summary>
    public string? Resultado { get; protected set; }

    public string? Conclusion { get; protected set; }
    public string? Confianza { get; protected set; }
    public DateTime? ListoUtc { get; protected set; }
    public string? Error { get; protected set; }

    protected AnalisisNormativo()
    {
    }

    public AnalisisNormativo(Guid id, Guid organizacionId, string pregunta, DateTime? fecha, string? contraparteTipo,
        string? contraparteResidencia, string? disparadores, DateTime utc) : base(id)
    {
        OrganizacionId = organizacionId;
        Pregunta = pregunta.Trim();
        FechaDeLaOperacion = fecha;
        ContraparteTipoDePersona = Corto(contraparteTipo, MaxCortoLength);
        ContraparteResidencia = Corto(contraparteResidencia, MaxCortoLength);
        Disparadores = Corto(disparadores, MaxDisparadoresLength);
        Estado = EstadoDelAnalisis.Pendiente;
        PedidoUtc = utc;
    }

    public void Terminar(string resultado, string? conclusion, string? confianza, DateTime utc)
    {
        Resultado = resultado;
        Conclusion = Corto(conclusion, MaxConclusionLength);
        Confianza = Corto(confianza, MaxCortoLength);
        Estado = EstadoDelAnalisis.Listo;
        ListoUtc = utc;
        Error = null;
    }

    /// <summary>El motor o Claude no respondieron: se reintenta más tarde, hasta <see cref="MaxIntentos"/>.</summary>
    public void Fallar(string error, DateTime utc)
    {
        Intentos++;
        Error = Corto(error, MaxErrorLength);
        if (Intentos >= MaxIntentos)
        {
            Estado = EstadoDelAnalisis.Error;
        }
        else
        {
            PedidoUtc = utc + EsperaTrasFalla;
        }
    }

    /// <summary>Algo que no se arregla solo (la pregunta no sirve, falta el perfil): sale de la fila.</summary>
    public void Detener(string error)
    {
        Error = Corto(error, MaxErrorLength);
        Estado = EstadoDelAnalisis.Error;
    }

    private static string? Corto(string? t, int max)
    {
        t = t?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length > max ? t[..max] : t;
    }
}

public enum EstadoDelAnalisis
{
    Pendiente = 0,
    Listo = 1,
    Error = 2,
}
