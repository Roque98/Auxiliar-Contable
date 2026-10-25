using AuxiliarContable.Branding;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Threading;
using Volo.Abp.Ui.Branding;

namespace AuxiliarContable;

/// <summary>
/// Marca de las páginas MVC del Host — en la práctica, la <b>pantalla de login</b>, que es
/// la única que ve un usuario del SPA.
///
/// <para>Lee el nombre y el logo del inquilino a través de <see cref="LectorDeMarca"/>, el
/// mismo que usa la pantalla de administración, para que las dos no puedan discrepar.</para>
///
/// <para>⚠️ <b>El inquilino solo se conoce después de elegirlo.</b> La página arranca sin
/// inquilino y enseña la marca de la instalación; al elegir uno en el selector, ABP deja la
/// cookie <c>__tenant</c>, <c>CookieTenantResolveContributor</c> la lee en la siguiente
/// petición y a partir de ahí la marca es la suya. No hay forma de adivinarlo antes: la
/// primera petición no trae nada que diga quién eres.</para>
///
/// <para>⚠️ <b>Y por eso hay un <c>AsyncHelper.RunSync</c> aquí</b>, que normalmente sería
/// un error grave. <c>IBrandingProvider</c> declara propiedades <b>síncronas</b> y las
/// vistas de los temas lo inyectan con <c>@inject</c>; leer un ajuste, en cambio, es
/// asíncrono. No hay ningún punto asíncrono por el que colar la lectura antes de que la
/// vista pregunte. El riesgo habitual —bloquear un hilo esperando E/S— está acotado porque
/// ABP ya cachea los ajustes (<c>SettingManagementStore</c> sobre <c>IDistributedCache</c>,
/// verificado en <c>refs/abp</c>): esto es un acceso a caché, no una consulta a la base. Es
/// además el mismo recurso que usa ABP en su propio
/// <c>MvcCachedApplicationConfigurationClient</c>.</para>
/// </summary>
[Dependency(ReplaceServices = true)]
public class AuxiliarContableBrandingProvider : DefaultBrandingProvider
{
    private readonly LectorDeMarca _lector;

    public AuxiliarContableBrandingProvider(LectorDeMarca lector)
    {
        _lector = lector;
    }

    public override string AppName => Marca().AppName;

    public override string? LogoUrl => Marca().LogoUrl;

    /// <summary>
    /// El mismo logo sobre fondo oscuro. Se guarda UNO solo: pedir dos versiones a cada
    /// inquilino es pedirle que resuelva un problema de diseño para estrenar la pantalla, y
    /// casi todos subirían el mismo archivo dos veces.
    /// </summary>
    public override string? LogoReverseUrl => Marca().LogoUrl;

    private MarcaResuelta Marca()
    {
        return AsyncHelper.RunSync(() => _lector.LeerAsync());
    }
}
