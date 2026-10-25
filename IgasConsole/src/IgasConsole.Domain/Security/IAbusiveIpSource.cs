using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IgasConsole.Security;

/// <summary>Una IP que ha acumulado rechazos del rate limiter suficientes para ser sospechosa.</summary>
public record IpAbusiva(string IpAddress, int Rechazos);

/// <summary>
/// Una IP que está SONDEANDO rutas: muchas peticiones fallidas y casi todas distintas.
///
/// <para>Es una señal distinta de la del rate limiter, y hace falta porque un escaneador
/// típico <b>no supera el límite de peticiones</b>: va a unos cientos por minuto, por
/// debajo de la cuota, y aun así prueba <c>/.env</c>, <c>/credentials/aws.env</c>,
/// <c>/.ssh/id_ecdsa.pub</c>… buscando credenciales. Medido en producción: el peor iba a
/// 378 peticiones por minuto con la cuota en 600, así que jamás llegó a ser rechazado ni
/// contado.</para>
///
/// <para><see cref="PorcentajeFallo"/> es lo que separa a un escaneador de alguien con un
/// enlace roto. En los datos reales la diferencia no es sutil: los escaneadores fallaban
/// entre el 92 % y el 100 % de las veces; los usuarios de verdad, el 0-2 %.</para>
/// </summary>
public record IpEscaneando(
    string IpAddress, int Fallos, int RutasDistintas, int PorcentajeFallo);

/// <summary>
/// De dónde salen las IPs candidatas a bloqueo automático.
///
/// <para>Existe como interfaz para que el módulo de seguridad NO dependa del de monitoreo.
/// Hoy la señal son los rechazos del rate limiter, que registra el recolector de métricas;
/// mañana podría ser otra cosa (fallos de login, respuestas 404 en ráfaga) sin tocar nada
/// del bloqueo. La implementación vive en la capa de infraestructura, que es la única que
/// conoce ambas tablas.</para>
/// </summary>
public interface IAbusiveIpSource
{
    /// <summary>
    /// IPs con al menos <paramref name="minimoRechazos"/> desde <paramref name="desdeUtc"/>,
    /// las peores primero.
    /// </summary>
    Task<List<IpAbusiva>> GetAsync(
        DateTime desdeUtc,
        int minimoRechazos,
        int maxResultados,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// IPs que están sondeando rutas desde <paramref name="desdeUtc"/>: al menos
    /// <paramref name="minimoFallos"/> peticiones fallidas y un porcentaje de fallo de al
    /// menos <paramref name="minimoPorcentajeFallo"/>.
    /// <para>Las dos condiciones van juntas a propósito. Solo con el recuento se
    /// bloquearía a un cliente muy activo que se equivoca de vez en cuando; solo con el
    /// porcentaje, a quien entró una vez a una URL mala y se fue.</para>
    /// </summary>
    Task<List<IpEscaneando>> GetScannersAsync(
        DateTime desdeUtc,
        int minimoFallos,
        int minimoPorcentajeFallo,
        int maxResultados,
        CancellationToken cancellationToken = default);
}
