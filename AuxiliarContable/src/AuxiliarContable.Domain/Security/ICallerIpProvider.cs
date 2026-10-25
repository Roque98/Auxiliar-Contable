namespace AuxiliarContable.Security;

/// <summary>
/// La IP desde la que llega la petición actual, o null si no hay ninguna en curso.
///
/// <para>Existe para que la capa de aplicación pueda comprobar «¿me estoy bloqueando a mí
/// mismo?» sin depender de ASP.NET Core. ABP ya ofrece <c>IWebClientInfoProvider</c>, pero
/// vive en la capa web; esto es una fachada de una sola propiedad implementada en el host,
/// igual que <c>ITelegramMessenger</c>.</para>
/// </summary>
public interface ICallerIpProvider
{
    string? IpActual { get; }

    /// <summary>User-Agent de la petición, o null si no hay ninguna en curso.</summary>
    string? AgenteActual { get; }
}
