using System;
using System.Threading.Tasks;
using AuxiliarContable.Localization;
using Microsoft.Extensions.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Settings;

namespace AuxiliarContable.Branding;

/// <summary>
/// La marca ya resuelta para el inquilino actual: sin nulos, sin valores inválidos y con
/// las herencias aplicadas. Es lo que se pinta.
/// </summary>
public record MarcaResuelta(
    string AppName,
    string AccentColor,
    string? LogoVersion,
    string? LogoUrl)
{
    public bool HasLogo => !string.IsNullOrEmpty(LogoVersion);
}

/// <summary>
/// Lee la marca del inquilino actual y le aplica las reglas de herencia y de saneado.
///
/// <para>Existe como pieza aparte porque tiene <b>dos consumidores en capas distintas</b>:
/// la pantalla de administración (application service) y la página de login del Host
/// (<c>IBrandingProvider</c>). Escrito dos veces, el día que cambie una regla —el color por
/// defecto, la forma de la URL del logo— cambiaría en un sitio y no en el otro, y el
/// resultado sería una aplicación que se ve de un color y una pantalla de login que se ve
/// de otro, sin que nada falle.</para>
///
/// <para>No hace falta filtrar por inquilino a mano: <c>ISettingProvider</c> ya consulta
/// <c>TenantSettingValueProvider</c> con <c>ICurrentTenant.Id</c> y cae al valor global
/// cuando el inquilino no ha puesto nada.</para>
/// </summary>
public class LectorDeMarca : ITransientDependency
{
    private readonly ISettingProvider _ajustes;
    private readonly ICurrentTenant _inquilinoActual;
    private readonly IStringLocalizer<AuxiliarContableResource> _textos;

    public LectorDeMarca(
        ISettingProvider ajustes,
        ICurrentTenant inquilinoActual,
        IStringLocalizer<AuxiliarContableResource> textos)
    {
        _ajustes = ajustes;
        _inquilinoActual = inquilinoActual;
        _textos = textos;
    }

    /// <summary>
    /// El nombre que se ve cuando nadie ha configurado ninguno. Es el texto localizado que
    /// ya usaba el proveedor de marca original, y no una constante nueva: así una
    /// instalación que nunca abra esta pantalla se sigue viendo exactamente igual que antes.
    /// </summary>
    public string NombrePorDefecto => _textos["AppName"];

    public async Task<MarcaResuelta> LeerAsync()
    {
        var nombre = await _ajustes.GetOrNullAsync(BrandingSettingNames.AppName);
        var color = await _ajustes.GetOrNullAsync(BrandingSettingNames.AccentColor);
        var version = await _ajustes.GetOrNullAsync(BrandingSettingNames.LogoVersion);

        // ⚠️ Lo que devuelve `ISettingProvider` es el valor RESUELTO, no el que puso este
        // inquilino: si no tiene el suyo, aquí ya llega el del host. Eso está bien para
        // pintar —que es a lo que se dedica esta clase— pero no sirve para responder «¿lo
        // puso él?», y confundir las dos cosas hace que un inquilino que hereda parezca
        // tener nombre propio. Esa pregunta se contesta en el application service, pidiendo
        // el ajuste a un NIVEL concreto.
        return new MarcaResuelta(
            AppName: string.IsNullOrWhiteSpace(nombre) ? NombrePorDefecto : nombre!.Trim(),
            AccentColor: ColorDeMarca.OPorDefecto(color),
            LogoVersion: string.IsNullOrWhiteSpace(version) ? null : version,
            LogoUrl: ConstruirUrlDelLogo(version));
    }

    /// <summary>
    /// URL pública del logo, o <c>null</c> si este inquilino no tiene.
    ///
    /// <para>⚠️ El inquilino va SIEMPRE explícito en la consulta, aunque en el SPA la
    /// sesión ya lo diga y en la página de login lo diga la cookie. El motivo es la caché:
    /// la respuesta se marca cacheable para siempre (la versión ya distingue una imagen de
    /// otra), y si la URL fuera la misma para todos, cualquier caché compartida por el
    /// camino podría servirle a un inquilino el logo de otro. Con el id dentro, la URL
    /// determina por completo la respuesta.</para>
    /// </summary>
    private string? ConstruirUrlDelLogo(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var v = Uri.EscapeDataString(version!);

        // Sin __tenant significa "el host", que también es una URL distinta de las demás.
        return _inquilinoActual.Id.HasValue
            ? $"/api/branding/logo?__tenant={_inquilinoActual.Id.Value:D}&v={v}"
            : $"/api/branding/logo?v={v}";
    }
}
