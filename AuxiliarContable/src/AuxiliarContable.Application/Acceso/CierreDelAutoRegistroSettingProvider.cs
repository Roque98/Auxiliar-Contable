using Volo.Abp.Account.Settings;
using Volo.Abp.Settings;

namespace AuxiliarContable.Acceso;

/// <summary>
/// Cierra el alta de cuentas por cuenta propia.
///
/// <para>⚠️⚠️ <b>ABP trae esto ENCENDIDO por defecto</b>
/// (<c>AccountSettingDefinitionProvider</c> define <c>IsSelfRegistrationEnabled</c> con
/// <c>"true"</c>, verificado en <c>refs/abp</c>). O sea que no lo activó nadie: viene así, y
/// en este proyecto estuvo así en producción hasta que se miró la pantalla de login.</para>
///
/// <para><b>Por qué se cierra.</b> §6.98 se toma la molestia de esconder el panel y el
/// formulario de login del superadministrador detrás de un segmento secreto, y mientras tanto
/// <c>/Account/Register</c> respondía 200 en su dirección de siempre: cualquiera podía crearse
/// una cuenta con sesión válida en el host. No es una escalada —una cuenta nueva no tiene
/// permisos (§6.965.2 y la siembra de ABP solo conceden al rol <c>admin</c>)— pero sí es una
/// contradicción: se esconde la puerta principal y se deja la de servicio abierta.</para>
///
/// <para>Y encaja con cómo funciona ya todo lo demás: los inquilinos se dan de alta con un
/// enlace de activación (§6.965.1) y los usuarios los crea un administrador desde Identity.
/// El auto-registro no era una decisión del proyecto, era un valor por defecto heredado.</para>
///
/// <para><b>Efecto secundario deseable</b>: <c>Login.cshtml</c> pinta el enlace «Register»
/// <b>solo si</b> este ajuste está en true (verificado en <c>refs/abp</c>, línea 36), así que
/// el enlace desaparece solo — sin tocar la vista del tema, que es la regla de §5.2.</para>
///
/// <para>⚠️ Esto cambia el <b>valor por defecto</b>, no el guardado. Si alguien llegó a
/// escribir el ajuste a mano en <c>AbpSettings</c>, ese valor manda y esto no hace nada; hay
/// que comprobarlo contra la API después de desplegar, no darlo por hecho.</para>
///
/// <para>⚠️⚠️ <b>VIVE EN LA CAPA Application, y la capa importa.</b> Los proveedores de
/// definiciones se ejecutan <b>en orden de registro</b>
/// (<c>StaticSettingDefinitionStore.CreateSettingDefinitionsAsync</c>, verificado en
/// <c>refs/abp</c>), y el último que llama a <c>context.Add</c> con el mismo nombre
/// <b>reemplaza</b> la definición entera. Puesto en <c>Application.Contracts</c> este corría
/// ANTES que <c>AccountSettingDefinitionProvider</c>, que volvía a añadir el ajuste con
/// <c>"true"</c> encima: no fallaba nada, no había ningún aviso, y el auto-registro seguía
/// abierto. Se vio comprobando el ajuste contra la API, no leyendo el código.</para>
///
/// <para>La regla general: <b>para pisar un ajuste de un módulo de ABP, el proveedor propio
/// tiene que estar en una capa que cargue DESPUÉS de ese módulo.</b> Es lo mismo que ya hacía
/// <c>ChangeIdentityPasswordPolicySettingDefinitionProvider</c> desde <c>Domain</c>, que
/// carga después de <c>Volo.Abp.Identity.Domain</c> — allí funcionaba por casualidad de dónde
/// se puso.</para>
/// </summary>
public class CierreDelAutoRegistroSettingProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        var autoRegistro = context.GetOrNull(AccountSettingNames.IsSelfRegistrationEnabled);

        if (autoRegistro != null)
        {
            // En minúsculas, igual que lo define ABP. `IsTrueAsync` no distingue mayúsculas,
            // pero el valor viaja al cliente dentro de application-configuration y ahí lo lee
            // JavaScript, que sí compara cadenas.
            autoRegistro.DefaultValue = "false";
        }
    }
}
