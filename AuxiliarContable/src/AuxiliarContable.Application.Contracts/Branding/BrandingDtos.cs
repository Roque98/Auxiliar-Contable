using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AuxiliarContable.Branding;

public class BrandingDto
{
    /// <summary>
    /// Nombre elegido, o <c>null</c> si este inquilino no ha puesto ninguno y hereda el de
    /// la instalación. Se distingue de la cadena vacía a propósito: la pantalla enseña el
    /// heredado como marcador de posición, y así se ve de dónde sale lo que hay puesto.
    /// </summary>
    public string? AppName { get; set; }

    /// <summary>Lo que se pinta hoy: el propio si lo hay, y si no, el heredado.</summary>
    public string EffectiveAppName { get; set; } = string.Empty;

    /// <summary>
    /// El nombre que quedaría al vaciar el campo — el de la instalación, o el de serie si
    /// tampoco lo han puesto. Va aparte de <see cref="EffectiveAppName"/> porque cuando el
    /// inquilino SÍ tiene nombre propio los dos son distintos, y es este el que la pantalla
    /// necesita para poder decir «si lo borras, se llamará así».
    /// </summary>
    public string InheritedAppName { get; set; } = string.Empty;

    /// <summary>Color de acento en <c>#rrggbb</c>. Nunca viene vacío ni inválido.</summary>
    public string AccentColor { get; set; } = BrandingConsts.DefaultAccentColor;

    public bool HasLogo { get; set; }

    /// <summary>
    /// Marca de versión del logo. Va en la URL como <c>?v=</c> para que el navegador pueda
    /// cachear la imagen indefinidamente y aun así ver el cambio al instante.
    /// </summary>
    public string? LogoVersion { get; set; }

    /// <summary>
    /// Ruta relativa desde la que se sirve el logo, con el inquilino y la versión ya
    /// puestos — o <c>null</c> si no hay logo.
    /// <para>⚠️ El Angular <b>no</b> usa esta propiedad para pintar el logo, y no puede: la
    /// arma él a partir de los ajustes visibles (ver <c>logo-url.ts</c>), porque este
    /// servicio exige el permiso <c>AuxiliarContable.Branding</c> y el logo tiene que verlo todo el
    /// mundo. Hay, pues, dos construcciones de la misma URL, una por lado de la frontera —
    /// inevitable, porque la página de login es HTML del servidor sin JavaScript nuestro.
    /// Un test de integración fija la forma exacta para que no se separen.</para>
    /// <para>Aquí sirve para la vista previa de la pantalla de administración.</para>
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>Tamaño en bytes del logo guardado. 0 si no hay.</summary>
    public int LogoSizeBytes { get; set; }

    /// <summary>
    /// <c>true</c> cuando quien edita es el host. Lo que guarde entonces es el valor por
    /// defecto de TODA la instalación, no el de un inquilino — y la pantalla tiene que
    /// decirlo, porque el formulario es exactamente el mismo.
    /// </summary>
    public bool IsHost { get; set; }
}

public class UpdateBrandingDto
{
    /// <summary>
    /// Vacío o <c>null</c> significa «vuelve a heredar», no «pon una cadena vacía». Un
    /// inquilino sin nombre propio enseña el de la instalación, que es lo que quiere quien
    /// borra el campo.
    /// </summary>
    public string? AppName { get; set; }

    /// <summary>
    /// <c>#RRGGBB</c>. Otra cosa se rechaza — ver <c>ColorDeMarca</c>: este valor acaba
    /// dentro de un bloque de CSS.
    /// </summary>
    public string? AccentColor { get; set; }
}

/// <summary>
/// Marca del inquilino actual: nombre, logo y color de acento.
///
/// <para>El inquilino sale SIEMPRE de la sesión (<c>ICurrentTenant</c>) y nunca de la
/// petición. Si viniera por parámetro, quien tuviera el permiso en su propio inquilino
/// podría repintarle la pantalla de login a otro.</para>
/// </summary>
public interface ITenantBrandingAppService : IApplicationService
{
    Task<BrandingDto> GetAsync();

    Task<BrandingDto> UpdateAsync(UpdateBrandingDto input);

    /// <summary>
    /// Sube o reemplaza el logo. Va como <c>multipart/form-data</c> con el archivo en el
    /// campo <c>file</c> — el nombre del campo tiene que coincidir con el del parámetro,
    /// que es como lo empareja el model binder de ABP.
    /// </summary>
    Task<BrandingDto> UploadLogoAsync(IRemoteStreamContent file);

    Task<BrandingDto> DeleteLogoAsync();
}
