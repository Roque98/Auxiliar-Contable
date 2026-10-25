using System;
using Volo.Abp.BackgroundJobs;

namespace AuxiliarContable.BackgroundJobs;

/*
 * Los ARGUMENTOS de los trabajos en segundo plano viven aquí y no junto a su
 * implementación (que está en el Host, porque necesita el cliente HTTP y el token del bot).
 *
 * Motivo: son un contrato, no lógica. Quien encola y quien ejecuta tienen que estar de
 * acuerdo en la forma exacta de estos datos, y además así se pueden probar — el proyecto
 * de tests de integración carga Application + EntityFrameworkCore, pero NO el Host.
 *
 * ⚠️ El nombre del job va FIJADO con [BackgroundJobName] en todos. ABP guarda ese nombre en
 * la fila de AbpBackgroundJobs; si se dedujera del nombre del tipo, mover o renombrar la
 * clase dejaría huérfanos los trabajos que estuvieran encolados en ese momento: seguirían
 * en la tabla y ya nadie sabría qué código los ejecuta.
 */

/// <summary>Aviso de monitoreo pendiente de entregar.</summary>
[BackgroundJobName("template:aviso-monitoreo")]
public class EnviarAvisoDeMonitoreoArgs
{
    public string Titulo { get; set; } = string.Empty;

    public string Cuerpo { get; set; } = string.Empty;
}

/// <summary>
/// Petición de enlace de recuperación de contraseña pendiente de entregar.
///
/// <para>⚠️ Fíjate en lo que NO hay aquí: <b>el token de restablecimiento</b>. Estos
/// argumentos se guardan como JSON en la tabla <c>AbpBackgroundJobs</c>, y ese token es una
/// credencial — quien lo tenga cambia la contraseña de la cuenta. Dejarlo escrito en una
/// tabla lo pondría al alcance de cualquiera con acceso a la base y, si el trabajo fallara,
/// ahí seguiría hasta dos días. El token se genera DENTRO del job, justo antes de enviarlo,
/// y no llega a escribirse en ninguna parte.</para>
/// </summary>
[BackgroundJobName("template:enlace-recuperacion")]
public class EnviarEnlaceRecuperacionArgs
{
    public Guid UserId { get; set; }

    /// <summary>
    /// Tenant del usuario. Va explícito porque un trabajo en segundo plano NO hereda el
    /// tenant de la petición que lo encoló: sin esto, buscar al usuario no lo encontraría.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>"MVC" o "Angular": decide a qué front apunta el enlace.</summary>
    public string AppName { get; set; } = "MVC";

    public string? ReturnUrl { get; set; }

    public string? ReturnUrlHash { get; set; }
}

/// <summary>
/// Correo de invitación pendiente de entregar. Como en la recuperación de contraseña, el
/// enlace NO va aquí: lo genera el trabajo al enviar y solo se guarda su hash.
/// </summary>
[BackgroundJobName("auxiliar:invitacion")]
public class EnviarInvitacionArgs
{
    public Guid InvitacionId { get; set; }
}

/// <summary>
/// Leer una Constancia de Situación Fiscal recién subida y proponer sus datos al perfil. En
/// segundo plano porque llama al modelo de IA (segundos y dinero). Solo el id: el contenido
/// se lee cifrado del almacén, nunca viaja en los argumentos del trabajo.
/// </summary>
[BackgroundJobName("auxiliar:leer-csf")]
public class LeerCsfArgs
{
    public Guid DocumentoId { get; set; }
}
