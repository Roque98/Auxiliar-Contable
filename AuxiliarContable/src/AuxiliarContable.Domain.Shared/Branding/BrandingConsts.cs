namespace AuxiliarContable.Branding;

/// <summary>
/// Personalización de la marca por inquilino: nombre, logo y color de acento.
/// </summary>
public static class BrandingConsts
{
    /// <summary>
    /// Nombre del contenedor de BLOBs donde vive el logo. Constante y NO deducido del
    /// nombre de la clase, por lo mismo que en las fotos de perfil: renombrar la clase
    /// dejaría los logos ya guardados bajo el nombre viejo.
    /// </summary>
    public const string ContainerName = "tenant-logos";

    /// <summary>
    /// Clave del BLOB dentro del contenedor. Es una CONSTANTE y no lleva el id del
    /// inquilino: los contenedores de ABP ya son multi-tenant por defecto
    /// (<c>BlobContainerConfiguration.IsMultiTenant</c>, verificado en <c>refs/abp</c>),
    /// así que el propio contenedor resuelve <c>ICurrentTenant.Id</c> y guarda cada logo
    /// por separado. Meter el id aquí además sería guardarlo dos veces.
    /// </summary>
    public const string LogoBlobName = "logo";

    /// <summary>
    /// Tope real de lo que se acepta guardar, en bytes.
    /// <para>El navegador reescala antes de subir, así que un logo normal llega en unos
    /// pocos KB. Este límite es para quien llame a la API sin pasar por esa pantalla.</para>
    /// </summary>
    public const int MaxLogoBytes = 256 * 1024;

    /// <summary>Lado máximo, en píxeles, al que el navegador reescala antes de subir.</summary>
    public const int MaxLadoPixeles = 256;

    /// <summary>
    /// El nombre sale en la barra lateral y en la pestaña del navegador, donde el espacio
    /// es el que es. Además hay un techo duro por encima: la columna <c>Value</c> de
    /// <c>AbpSettings</c> es <c>varchar(2048)</c>.
    /// </summary>
    public const int MaxAppNameLength = 64;

    /// <summary>
    /// El azul marino del isotipo, que es el acento del design system (<c>--ig-acento</c> en
    /// <c>angular/src/styles/auxiliar/_tokens.scss</c>). Tienen que coincidir: es el valor del
    /// ajuste cuando nadie ha elegido color, y la pantalla de marca parte de él.
    /// </summary>
    public const string DefaultAccentColor = "#2c5282";
}

/// <summary>
/// Nombres de los ajustes de marca.
///
/// <para>Los tres van <b>visibles para clientes</b> (<c>isVisibleToClients: true</c>), que
/// es lo que hace que el Angular los reciba dentro de
/// <c>/api/abp/application-configuration</c> <b>ya resueltos para el inquilino actual</b>:
/// ABP registra <c>TenantSettingValueProvider</c> entre el global y el de usuario, así que
/// no hace falta ninguna resolución propia ni tabla nueva.</para>
///
/// <para>⚠️ Por eso mismo el <b>logo no es un ajuste</b>: <c>AbpSettings.Value</c> es
/// <c>varchar(2048)</c> (<c>SettingConsts.MaxValueLength</c>, verificado en
/// <c>refs/abp</c>) y un <c>data:</c> URL de una imagen no cabe ni de lejos. El ajuste solo
/// guarda una <b>marca de versión</b>; los bytes viven en un BLOB.</para>
/// </summary>
public static class BrandingSettingNames
{
    private const string Prefijo = "AuxiliarContable.Branding.";

    public const string AppName = Prefijo + "AppName";

    public const string AccentColor = Prefijo + "AccentColor";

    /// <summary>
    /// Cadena corta que cambia cada vez que se sustituye el logo, y vacía cuando no hay.
    /// Hace dos cosas de una vez: dice si existe logo (sin tener que pedirlo para
    /// averiguarlo) y va en la URL como <c>?v=</c>, de modo que la respuesta se puede
    /// cachear para siempre y aun así un logo nuevo se ve al instante.
    /// </summary>
    public const string LogoVersion = Prefijo + "LogoVersion";
}
