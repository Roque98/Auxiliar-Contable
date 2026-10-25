using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.ApiKeys;
using AuxiliarContable.Notifications;
using AuxiliarContable.Telegram;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace AuxiliarContable.MiCuenta;

/// <summary>
/// El resumen que abre «Mi cuenta»: lo que ABP no cuenta de quien está mirando.
///
/// <para><b>Sin permiso propio</b>, igual que la foto de perfil y las sesiones: cada uno ve
/// lo suyo y solo lo suyo. El identificador sale <b>siempre de la sesión</b>
/// (<c>CurrentUser.GetId()</c>) y nunca de la petición — no hay parámetro que manipular,
/// que es lo que hace que no necesite permiso.</para>
///
/// <para>⚠️ <b>Este servicio LEE de sitios que ya tienen su propio dueño</b> (sesiones,
/// claves, avisos, Telegram) y no duplica ni una regla: pregunta a los mismos repositorios.
/// La razón de que exista es que la pantalla necesita <b>los cinco recuentos a la vez</b>, y
/// pedirlos por separado serían cinco llamadas para pintar una cabecera — justo lo que hacía
/// que <c>/metrics</c> tardara en abrirse (§6.99.3).</para>
/// </summary>
[Authorize]
public class MiCuentaAppService : AuxiliarContableAppService, IMiCuentaAppService
{
    private readonly IIdentityUserRepository _usuarios;
    private readonly IIdentitySessionRepository _sesiones;
    private readonly IIdentitySecurityLogRepository _registroDeSeguridad;
    private readonly IApiKeyRepository _claves;
    private readonly INotificationAppService _avisos;
    private readonly ITelegramLinkRepository _telegram;

    public MiCuentaAppService(
        IIdentityUserRepository usuarios,
        IIdentitySessionRepository sesiones,
        IIdentitySecurityLogRepository registroDeSeguridad,
        IApiKeyRepository claves,
        INotificationAppService avisos,
        ITelegramLinkRepository telegram)
    {
        _usuarios = usuarios;
        _sesiones = sesiones;
        _registroDeSeguridad = registroDeSeguridad;
        _claves = claves;
        _avisos = avisos;
        _telegram = telegram;
    }

    public async Task<MiCuentaDto> GetAsync()
    {
        var userId = CurrentUser.GetId();

        // ⚠️ `includeDetails: true` para que llegue la colección de roles. No basta con el
        // parámetro: `EfCoreRepository.GetAsync` solo carga las hijas si la entidad declara
        // `DefaultWithDetailsFunc`, y `IdentityUser` de ABP lo hace (§6.966.1 documenta el
        // caso contrario, que muerde en silencio).
        var usuario = await _usuarios.GetAsync(userId, includeDetails: true);
        var roles = await _usuarios.GetRoleNamesAsync(userId);

        return new MiCuentaDto
        {
            CreacionUtc = usuario.CreationTime,
            AccesoAnteriorUtc = await AccesoAnteriorAsync(userId),
            CorreoConfirmado = usuario.EmailConfirmed,
            Telefono = usuario.PhoneNumber,
            SegundoFactorActivo = usuario.TwoFactorEnabled,
            TelegramVinculado = await _telegram.FindByUserIdAsync(userId) is not null,
            SesionesActivas = (await _sesiones.GetListAsync(userId: userId)).Count,
            // ⚠️ «Utilizable» lo decide la ENTIDAD (`ApiKey.EsUtilizable`), no un filtro
            // escrito aquí: ni revocada ni caducada. Copiar esa condición la dejaría con dos
            // definiciones, y la de la cuota (§6.966.07) ya usa la misma.
            ClavesDeApi = (await _claves.GetByUserAsync(userId)).Count(c => c.EsUtilizable(Clock.Now.ToUniversalTime())),
            // ⚠️ Se pide al SERVICIO de avisos y no al repositorio: «sin leer para mí»
            // depende de los permisos concedidos, y esa resolución vive ahí. Preguntando al
            // repositorio habría que repetirla, y el día que cambie se corregiría en un lado.
            AvisosSinLeer = await _avisos.GetUnreadCountAsync(),
            Roles = roles.ToList(),
        };
    }

    /// <summary>
    /// El inicio de sesión <b>anterior al actual</b>.
    ///
    /// <para>⚠️ Se piden DOS y se devuelve el segundo. La entrada de la sesión en curso ya
    /// está escrita cuando esta pantalla se abre, así que quedarse con la primera enseñaría
    /// «último acceso: hace unos segundos» — el propio reflejo de quien mira. La pregunta que
    /// este dato contesta es otra: «la vez anterior, ¿fui yo?».</para>
    ///
    /// <para>Se filtra por acción exacta (<c>LoginSucceeded</c>) por lo mismo que el registro
    /// de seguridad de <c>/security</c> (§6.15.1): agrupar varias obligaría a filtrar en
    /// memoria después de recortar.</para>
    /// </summary>
    private async Task<DateTime?> AccesoAnteriorAsync(Guid userId)
    {
        var ultimos = await _registroDeSeguridad.GetListAsync(
            sorting: nameof(IdentitySecurityLog.CreationTime) + " DESC",
            maxResultCount: 2,
            userId: userId,
            action: "LoginSucceeded");

        return ultimos.Count >= 2 ? ultimos[1].CreationTime : null;
    }
}
