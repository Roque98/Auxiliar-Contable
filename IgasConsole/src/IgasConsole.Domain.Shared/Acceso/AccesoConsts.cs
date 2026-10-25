using System;
using System.Linq;

namespace IgasConsole.Acceso;

/// <summary>
/// Dónde vive la puerta del superadministrador.
///
/// <para>Por defecto el panel del host está en la raíz del sitio (<c>/</c>) y el formulario de
/// login en <c>/Account/Login</c> — las dos rutas por defecto de cualquier instalación de ABP,
/// y por tanto las dos primeras que prueba cualquier escáner. Configurando un <b>segmento</b>
/// las dos se mueven a la vez:</para>
///
/// <code>
/// Acceso:SegmentoDelHost = "g7k2p"
///
///   https://sitio/                  -> no hay nada (espacio no disponible)
///   https://sitio/g7k2p             -> panel del host
///   https://api.sitio/Account/Login -> 404
///   https://api.sitio/g7k2p         -> formulario de login
/// </code>
///
/// <para>Los espacios de inquilino (<c>/app/{nombre}</c>) <b>no se tocan</b>: son direcciones
/// que sus usuarios tienen que poder recibir y escribir.</para>
///
/// <para>⚠️⚠️ <b>ESTO REDUCE RUIDO, NO ES UNA CERRADURA.</b> Frena el escaneo automático —que
/// es la inmensa mayoría del tráfico hostil: los registros de esta instalación están llenos de
/// sondas a <c>/.env</c> y <c>/.ssh/id_ecdsa.pub</c>— y evita que quien pase por el dominio se
/// encuentre un panel de administración. No frena a quien vaya a por ti: el segmento se puede
/// adivinar a fuerza de intentos, y quien complete un <c>/connect/authorize</c> válido (los
/// parámetros que hacen falta están en el JavaScript del SPA, que es público) recibe una
/// redirección al formulario y con ella el segmento. Lo que protege de verdad la cuenta del
/// superadministrador es el <b>segundo factor</b>, no esta ruta.</para>
///
/// <para><b>Vacío = desactivado</b>, y ese es el interruptor de emergencia: si algo sale mal en
/// producción se borra la variable de entorno y se reinicia el servicio, sin desplegar nada. En
/// Development se deja vacío a propósito, para que el proyecto se levante y se pruebe en la
/// raíz de siempre.</para>
/// </summary>
public static class AccesoConsts
{
    /// <summary>Clave de configuración. En producción llega como <c>Acceso__SegmentoDelHost</c>.</summary>
    public const string ClaveSegmentoDelHost = "Acceso:SegmentoDelHost";

    /// <summary>
    /// Lo bastante largo como para que adivinarlo no sea realista con el límite de intentos
    /// que ya hay puesto (10/min por IP sobre las rutas de credenciales).
    /// </summary>
    public const int MinimoDeCaracteres = 4;

    public const int MaximoDeCaracteres = 64;

    /// <summary>
    /// Deja el segmento en su forma canónica, o cadena vacía si no sirve.
    ///
    /// <para>⚠️ La lista de caracteres permitidos es una <b>lista blanca</b>, no una limpieza de
    /// caracteres peligrosos. Este valor acaba siendo una plantilla de ruta de Razor Pages y un
    /// prefijo de URL: un <c>/</c>, un <c>{</c> o un <c>?</c> por medio no darían un error
    /// claro, darían una ruta que no es la que nadie quiso. Mismo criterio que
    /// <c>ColorDeMarca</c>.</para>
    ///
    /// <para>Devolver cadena vacía ante algo inválido —en vez de lanzar— es deliberado: un
    /// segmento mal escrito en la configuración dejaría el servicio sin arrancar, y la avería
    /// sería peor que el problema. Se cae al comportamiento por defecto, que funciona.</para>
    /// </summary>
    public static string Normalizar(string? valor)
    {
        var limpio = valor?.Trim().Trim('/');

        if (string.IsNullOrEmpty(limpio) ||
            limpio.Length < MinimoDeCaracteres ||
            limpio.Length > MaximoDeCaracteres)
        {
            return string.Empty;
        }

        return limpio.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_')
            ? limpio
            : string.Empty;
    }

    /// <summary>
    /// La URL de la raíz del espacio del host — la <c>redirect_uri</c> del superadministrador.
    ///
    /// <para>⚠️ Sin barra final, y tiene que coincidir <b>carácter a carácter</b> con lo que
    /// construye el Angular: OpenIddict compara la <c>redirect_uri</c> de forma exacta. Es el
    /// mismo acoplamiento entre dos lenguajes que ya tiene
    /// <c>SincronizadorDeRedirecciones.RaizDelEspacio</c>, y está cubierto por el mismo test.</para>
    /// </summary>
    public static string RaizDelHost(string raiz, string? segmento)
    {
        var limpio = Normalizar(segmento);
        var sinBarra = raiz.TrimEnd('/');

        return limpio.Length == 0 ? sinBarra : $"{sinBarra}/{Uri.EscapeDataString(limpio)}";
    }
}
