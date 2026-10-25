using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Volo.Abp.AspNetCore.Mvc;

namespace IgasConsole.Acceso;

/// <summary>
/// Contesta a una sola pregunta: <b>¿este primer segmento de la URL es el espacio del host?</b>
///
/// <para>Hace falta porque el Angular tiene que saber, <b>antes de arrancar</b>, si
/// <c>/g7k2p/metrics</c> es «el panel del host, sección métricas» o una ruta que no existe. Y
/// no puede saberlo por su cuenta: <b>el segmento no está en el bundle a propósito</b>. Si
/// estuviera, cualquiera podría bajarse el JavaScript —que se sirve público, porque lo
/// necesitan los espacios de inquilino— y leerlo de ahí, con lo que esconder la ruta no habría
/// servido de nada. El secreto vive solo en la configuración del servidor.</para>
///
/// <para><b>Anónimo, y no puede ser otra cosa</b>: esto corre antes de que exista sesión — de
/// hecho corre antes de que exista la pantalla desde la que iniciarla.</para>
///
/// <para>Sí, es un oráculo: se puede preguntar a ciegas. Pero preguntar aquí no es más barato
/// que probar la URL directamente, y sale <b>más caro</b>, porque cada fallo es un 404 sobre
/// una ruta distinta y eso es exactamente lo que buscan las dos defensas que ya hay puestas: el
/// límite por IP y el detector de sondeo de rutas del bloqueo automático, que se dispara con 40
/// fallos y ≥80 % de error en 10 minutos. Un barrido serio se auto-bloquea antes de haber
/// probado nada.</para>
///
/// <para>La comparación es ordinal y no de tiempo constante. Un ataque de temporización sobre
/// una comparación de cadenas cortas necesita medir nanosegundos, y por delante hay una red,
/// nginx, el pipeline entero de ASP.NET y un límite de peticiones: la varianza del transporte
/// es varios órdenes de magnitud mayor que la señal. Poner aquí una comparación «segura» daría
/// una sensación de rigor que no corresponde al canal.</para>
/// </summary>
[AllowAnonymous]
[Route("api/acceso")]
[ApiExplorerSettings(IgnoreApi = true)]
public class AccesoController : AbpControllerBase
{
    private readonly AccesoOptions _opciones;

    public AccesoController(IOptions<AccesoOptions> opciones)
    {
        _opciones = opciones.Value;
    }

    /// <summary><c>GET /api/acceso/espacio</c> — la ruta es la raíz del sitio.</summary>
    [HttpGet("espacio")]
    public IActionResult EstaEnLaRaiz()
    {
        return Resolver(string.Empty);
    }

    /// <summary><c>GET /api/acceso/espacio/{segmento}</c> — el primer segmento de la ruta.</summary>
    [HttpGet("espacio/{segmento}")]
    public IActionResult EsElSegmentoDelHost(string segmento)
    {
        return Resolver(segmento);
    }

    /// <summary>
    /// ¿Esta ruta es el panel del host? Y si lo es, ¿bajo qué prefijo?
    ///
    /// <para>⚠️ <b>Contesta las DOS cosas de una vez, y no es un capricho de diseño.</b> Antes
    /// eran dos preguntas —«¿está en la raíz?» y «¿es este el segmento?»— y el navegador tenía
    /// que hacer <b>dos peticiones seguidas</b> antes de arrancar Angular, con la pantalla en
    /// blanco todo ese rato. Medido contra producción: unos 0,4 s con buena red, y el doble
    /// desde un móvil. Con una sola pregunta se queda en la mitad, que es lo mismo que ya
    /// pagaban los espacios de inquilino.</para>
    ///
    /// <para>Cuando el acceso <b>no está oculto</b>, cualquier primer segmento es una ruta
    /// normal del panel (que vive en la raíz), así que se contesta que sí con prefijo vacío.
    /// Cuando <b>sí lo está</b>, solo acierta el segmento configurado.</para>
    /// </summary>
    private IActionResult Resolver(string segmento)
    {
        var esperado = _opciones.SegmentoNormalizado;

        // Sin ocultar: el panel está en la raíz y el primer segmento es una ruta, no un
        // prefijo. Da igual lo que venga.
        if (esperado.Length == 0)
        {
            return Responder(string.Empty);
        }

        return string.Equals(segmento, esperado, StringComparison.Ordinal)
            ? Responder(esperado)
            : NoEsElPanel();
    }

    /// <summary>Sí: es el panel, y este es el prefijo bajo el que vive («» = la raíz).</summary>
    private IActionResult Responder(string prefijo)
    {
        Sin.Cache(Response);

        return new ContentResult
        {
            StatusCode = StatusCodes.Status200OK,
            ContentType = "application/json; charset=utf-8",
            Content = "{\"prefijo\":\"" + prefijo + "\"}"
        };
    }

    /// <summary>
    /// No es el panel: <b>404</b>.
    ///
    /// <para>Se responde con el <b>código de estado</b> y no con un 200 que lleve la negativa
    /// dentro, que era lo primero que se escribió. Un 200 deja el fallo invisible para las
    /// defensas: el detector de sondeo de rutas del bloqueo automático se guía por la
    /// <b>proporción de peticiones fallidas</b>, así que un barrido de miles de intentos le
    /// parecería tráfico perfectamente sano. Con el 404, cada intento cuenta como lo que es.</para>
    ///
    /// <para>⚠️ Lleva cuerpo y <c>ContentType</c>, como todos los 4xx propios del proyecto:
    /// fuera de Development, <c>UseErrorPage()</c> convierte en un 302 cualquier respuesta 4xx
    /// que vaya vacía (CLAUDE.md §5).</para>
    /// </summary>
    private IActionResult NoEsElPanel()
    {
        Sin.Cache(Response);

        return new ContentResult
        {
            StatusCode = StatusCodes.Status404NotFound,
            ContentType = "application/json; charset=utf-8",
            Content = "{\"prefijo\":null}"
        };
    }

    private static class Sin
    {
        /// <summary>
        /// El navegador pide esto en cada carga y la respuesta depende de la configuración del
        /// servidor: una caché por el medio que guardara el 404 dejaría el panel inalcanzable
        /// hasta que expirase.
        /// </summary>
        public static void Cache(HttpResponse respuesta)
        {
            respuesta.Headers.CacheControl = "no-store";
        }
    }
}
