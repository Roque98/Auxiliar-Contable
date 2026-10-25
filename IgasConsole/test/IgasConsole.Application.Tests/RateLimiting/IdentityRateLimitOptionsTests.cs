using IgasConsole.RateLimiting;
using Shouldly;
using Xunit;

namespace IgasConsole;

/// <summary>
/// Las cuotas por defecto del límite por identidad.
///
/// <para>Parecen constantes tontas de probar, pero la relación ENTRE ellas es una decisión
/// de diseño y romperla no falla en ningún sitio: simplemente empieza a cortarle el paso a
/// quien no debía. Ya pasó una vez con el límite por IP —120/min tumbó la aplicación en
/// producción porque un uso normal lo rozaba— y por eso hoy son 600.</para>
///
/// <para>El comportamiento en sí (que corte, que cada clave tenga su cuota, que el 429
/// lleve cuerpo) se verificó contra la API real: no se puede montar aquí sin levantar el
/// pipeline HTTP entero, que es justo donde vive la lógica — el middleware va DESPUÉS de
/// la autenticación, y sin ella no hay identidad que limitar.</para>
/// </summary>
public class IdentityRateLimitOptionsTests
{
    private readonly RateLimitingOptions _opciones = new();

    [Fact]
    public void Un_usuario_tiene_mas_cuota_que_una_IP_anonima()
    {
        // Quien ha iniciado sesion ya demostro algo, y una pantalla con varios paneles hace
        // muchas llamadas seguidas. Si la cuota de usuario fuera MENOR que la de IP, el
        // limite por identidad se dispararia antes que el de abuso y castigaria justo a
        // quien ha entrado con sus credenciales.
        _opciones.UserPermitLimit.ShouldBeGreaterThan(_opciones.PermitLimit);
    }

    [Fact]
    public void Una_clave_de_API_tiene_menos_cuota_que_una_persona()
    {
        // Una clave la usa una maquina, y una maquina que se pasa de este ritmo casi
        // siempre esta en un bucle, no trabajando.
        _opciones.ApiKeyPermitLimit.ShouldBeLessThan(_opciones.UserPermitLimit);
    }

    [Fact]
    public void El_limite_de_login_sigue_siendo_el_mas_bajo_de_todos()
    {
        // Ahi se prueban contrasenas: cualquier otra cuota aplicada a esas rutas serian
        // cientos de intentos por minuto.
        _opciones.LoginPermitLimit.ShouldBeLessThan(_opciones.PermitLimit);
        _opciones.LoginPermitLimit.ShouldBeLessThan(_opciones.ApiKeyPermitLimit);
    }

    [Fact]
    public void El_token_tiene_holgura_para_recargar_la_pagina()
    {
        // El Angular pide token en cada carga completa de página. Con la cuota del login
        // (10/min) unas pocas recargas —o una oficina entera detrás de una IP— daban 429 y la
        // app mandaba al login. Por /connect/token no viajan contraseñas.
        _opciones.TokenPermitLimit.ShouldBeGreaterThanOrEqualTo(10 * _opciones.LoginPermitLimit);
    }

    [Fact]
    public void Los_limites_por_identidad_vienen_encendidos_pero_con_interruptor_propio()
    {
        // Aparte del general: el de IP protege de extranos y este de los propios. Poder
        // apagar uno sin el otro evita quedarse sin ninguna proteccion al depurar cuotas.
        _opciones.IdentityLimitsEnabled.ShouldBeTrue();
        _opciones.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Ninguna_cuota_pone_en_cola_las_peticiones()
    {
        // Esto es proteccion contra abuso, no suavizado de picos: encolar una peticion ya
        // identificada como parte de una rafaga solo retrasa el rechazo y ocupa memoria.
        _opciones.QueueLimit.ShouldBe(0);
    }
}
