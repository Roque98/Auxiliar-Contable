namespace IgasConsole.RateLimiting;

/// <summary>
/// Límite de peticiones por IP para la API. Se configuran desde appsettings o variables
/// de entorno (sección <c>RateLimiting</c>), no desde AbpSettings: a diferencia del
/// token de Telegram, esto se lee en caliente en cada petición y no necesita ser
/// editable sin redeploy.
/// </summary>
public class RateLimitingOptions
{
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Peticiones permitidas por IP dentro de la ventana.
    /// <para>120 se quedó CORTO y rompió la aplicación en producción: entre la
    /// configuración de la app, el descubrimiento de OpenID, el token y las llamadas de
    /// las pantallas, un uso normal se acerca a ese número enseguida. Un límite pensado
    /// para frenar abuso no puede estar al alcance de un usuario legítimo.</para>
    /// </summary>
    public int PermitLimit { get; set; } = 600;

    public int WindowSeconds { get; set; } = 60;

    /// <summary>0 = rechazar de inmediato al superar el límite. Esto es protección
    /// contra abuso, no suavizado de picos: no tiene sentido poner en cola una petición
    /// que ya se identificó como parte de una ráfaga.</summary>
    public int QueueLimit { get; set; } = 0;

    /// <summary>
    /// Límite para las rutas donde se prueban contraseñas (login, recuperación, token).
    /// Mucho más bajo que <see cref="PermitLimit"/>: 120/min es razonable para navegar
    /// la API, pero serían 120 intentos de contraseña por minuto.
    /// <para>Cuenta por IP, así que detrás de un NAT compartido (una oficina) varias
    /// personas comparten cuota. 10/min deja sitio de sobra para un humano que se
    /// equivoca al teclear, no para un barrido automático.</para>
    /// </summary>
    public int LoginPermitLimit { get; set; } = 10;

    /// <summary>
    /// Límite de <c>/connect/token</c> (canje del código del login y refresco de sesión), en
    /// su propia partición.
    /// <para>⚠️ Antes compartía los 10/min del login, y el Angular pide token en CADA carga
    /// completa de página: unas pocas recargas en un minuto —o varias personas de una misma
    /// oficina abriendo la consola— daban 429 y la app mandaba de vuelta al login. Por aquí no
    /// viajan contraseñas (el cliente no tiene el grant «password»), los refresh tokens son
    /// aleatorios de 256 bits y el canje de códigos lleva PKCE: no hay nada que adivinar a
    /// fuerza de intentos, así que un techo holgado basta para frenar un abuso.</para>
    /// </summary>
    public int TokenPermitLimit { get; set; } = 120;

    /// <summary>
    /// Peticiones permitidas por USUARIO autenticado dentro de la ventana.
    ///
    /// <para>El límite por IP no cubre este caso: un script propio con un bucle roto sale
    /// de una dirección perfectamente legítima —a menudo la misma que usan compañeros— y
    /// bajar el límite de IP para atraparlo dejaría fuera a los demás. Esto acota por
    /// credencial, que es lo que de verdad se ha desbocado.</para>
    ///
    /// <para>Más alto que el de IP a propósito: quien ha iniciado sesión ya demostró algo,
    /// y una pantalla abierta con varios paneles hace muchas llamadas seguidas.</para>
    /// </summary>
    public int UserPermitLimit { get; set; } = 1200;

    /// <summary>
    /// Peticiones permitidas por CLAVE DE API dentro de la ventana.
    ///
    /// <para>Más bajo que el de un usuario: una clave la usa una máquina, y una máquina que
    /// pasa de este ritmo casi siempre está en un bucle, no trabajando. Además acota el
    /// daño de una clave filtrada sin tener que revocarla a mano en el momento.</para>
    /// </summary>
    public int ApiKeyPermitLimit { get; set; } = 600;

    /// <summary>
    /// Interruptor propio para los límites por identidad.
    /// <para>Aparte de <see cref="IsEnabled"/> porque son cosas distintas: el de IP protege
    /// de extraños y este de los propios. Poder apagar uno sin el otro evita quedarse sin
    /// ninguna protección al depurar un problema de cuotas.</para>
    /// </summary>
    public bool IdentityLimitsEnabled { get; set; } = true;
}
