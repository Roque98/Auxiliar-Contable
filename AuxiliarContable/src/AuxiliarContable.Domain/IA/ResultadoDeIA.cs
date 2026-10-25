using System;
using Volo.Abp;

namespace AuxiliarContable.IA;

/// <summary>Los datos que devolvió el modelo y lo que costó obtenerlos.</summary>
public sealed class ResultadoDeIA<T>
    where T : class
{
    public ResultadoDeIA(T datos, string modelo, UsoDeIA uso, TimeSpan duracion)
    {
        Datos = datos;
        Modelo = modelo;
        Uso = uso;
        Duracion = duracion;
    }

    public T Datos { get; }

    /// <summary>El modelo que contestó de verdad (el alias de la configuración puede resolver a una versión concreta).</summary>
    public string Modelo { get; }

    public UsoDeIA Uso { get; }

    public TimeSpan Duracion { get; }
}

/// <summary>
/// Tokens de una llamada, tal como los cuenta Anthropic. Es lo que se factura: la entrada
/// leída de caché cuesta ~10 % y la escrita en caché ~125 % del precio normal de entrada.
/// </summary>
public sealed record UsoDeIA(int TokensDeEntrada, int TokensDeSalida, int TokensLeidosDeCache, int TokensEscritosEnCache);

public enum MotivoDeErrorDeIA
{
    /// <summary>Falta la API key. No se llamó a nadie.</summary>
    NoConfigurado,

    /// <summary>La API rechazó la petición (400, 401, 403, 404, 413). Reintentar no sirve.</summary>
    Rechazada,

    /// <summary>Saturación, límite de uso o caída (429, 5xx, 529, tiempo agotado). Reintentar más tarde sí sirve.</summary>
    NoDisponible,

    /// <summary>La respuesta se cortó por el tope de tokens: hay que subir <c>MaxTokens</c> o partir el documento.</summary>
    Truncada,

    /// <summary>El modelo contestó, pero no con datos que cumplan el esquema.</summary>
    RespuestaInvalida,
}

/// <summary>
/// Un fallo hablando con el modelo. El mensaje NUNCA lleva la API key ni el contenido de los
/// documentos (son datos financieros de los clientes): solo el código y el error de la API.
/// </summary>
public class ErrorDeIAException : AbpException
{
    public ErrorDeIAException(MotivoDeErrorDeIA motivo, string mensaje, Exception? interna = null)
        : base(mensaje, interna)
    {
        Motivo = motivo;
    }

    public MotivoDeErrorDeIA Motivo { get; }

    /// <summary>Solo los fallos del servicio; los de la petición fallarían igual otra vez.</summary>
    public bool EsReintentable => Motivo == MotivoDeErrorDeIA.NoDisponible;
}
