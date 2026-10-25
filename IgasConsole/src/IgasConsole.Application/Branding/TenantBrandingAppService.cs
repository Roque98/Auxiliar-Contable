using System;
using System.Threading.Tasks;
using IgasConsole.Files;
using IgasConsole.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.SettingManagement;

namespace IgasConsole.Branding;

/// <summary>
/// Nombre, logo y color de acento del inquilino actual.
///
/// <para>El inquilino sale siempre de <c>ICurrentTenant</c>. Cuando quien edita es el host,
/// lo que se guarda es el valor <b>global</b>, es decir el que heredan todos los inquilinos
/// que no hayan puesto el suyo — de ahí <c>SetForTenantOrGlobalAsync</c>, que ya distingue
/// los dos casos.</para>
/// </summary>
[Authorize(IgasConsolePermissions.Branding.Default)]
public class TenantBrandingAppService : IgasConsoleAppService, ITenantBrandingAppService
{
    private readonly ISettingManager _ajustes;
    private readonly LectorDeMarca _lector;
    private readonly IBlobContainer<TenantLogoContainer> _logos;

    public TenantBrandingAppService(
        ISettingManager ajustes,
        LectorDeMarca lector,
        IBlobContainer<TenantLogoContainer> logos)
    {
        _ajustes = ajustes;
        _lector = lector;
        _logos = logos;
    }

    public async Task<BrandingDto> GetAsync()
    {
        return await DescribirAsync();
    }

    public async Task<BrandingDto> UpdateAsync(UpdateBrandingDto input)
    {
        var nombre = input.AppName?.Trim();

        if (nombre?.Length > BrandingConsts.MaxAppNameLength)
        {
            throw new UserFriendlyException(
                $"El nombre no puede pasar de {BrandingConsts.MaxAppNameLength} caracteres.");
        }

        // Vacío se guarda como null, que en ABP borra la fila del ajuste. Y borrar la fila
        // es exactamente "vuelve a heredar": el proveedor del inquilino no encuentra nada y
        // cae al global. Guardar la cadena vacía, en cambio, dejaría la aplicación sin
        // nombre en la barra lateral.
        await _ajustes.SetForTenantOrGlobalAsync(
            CurrentTenant.Id,
            BrandingSettingNames.AppName,
            nombre.IsNullOrWhiteSpace() ? null : nombre);

        // El color SÍ se valida y se rechaza, al revés que al leer: aquí hay un formulario
        // delante que puede corregirlo, y dejar entrar cualquier texto sería inyección de
        // CSS en la pantalla de todo el inquilino. Ver ColorDeMarca.
        if (!input.AccentColor.IsNullOrWhiteSpace())
        {
            var color = ColorDeMarca.Normalizar(input.AccentColor)
                        ?? throw new UserFriendlyException(
                            "El color tiene que ir en formato #RRGGBB, por ejemplo #1b6ac9.");

            await _ajustes.SetForTenantOrGlobalAsync(
                CurrentTenant.Id, BrandingSettingNames.AccentColor, color);
        }
        else
        {
            await _ajustes.SetForTenantOrGlobalAsync(
                CurrentTenant.Id, BrandingSettingNames.AccentColor, null);
        }

        return await DescribirAsync();
    }

    public async Task<BrandingDto> UploadLogoAsync(IRemoteStreamContent file)
    {
        if (file == null)
        {
            throw new UserFriendlyException("No llegó ningún archivo.");
        }

        // Atajo para cortar antes de leer un solo byte. El tope de verdad es el de abajo:
        // ContentLength lo declara quien sube y puede faltar o mentir.
        if (file.ContentLength > BrandingConsts.MaxLogoBytes)
        {
            throw new UserFriendlyException(MensajeDemasiadoGrande());
        }

        var bytes = await LectorConTope.LeerAsync(
            file.GetStream(), BrandingConsts.MaxLogoBytes, MensajeDemasiadoGrande());

        // El formato se mira en los BYTES, no en el Content-Type que declara el cliente.
        // Importa el doble aquí que en la foto de perfil: el logo se sirve desde un endpoint
        // ANÓNIMO, así que un HTML con <script> disfrazado de PNG sería XSS almacenado
        // servido a cualquiera que abra la pantalla de login. SVG queda fuera por lo mismo.
        if (DetectorDeImagen.Detectar(bytes) == FormatoDeImagen.Desconocido)
        {
            throw new UserFriendlyException(
                "El archivo no es una imagen JPEG, PNG o WebP. Elige otro logo.");
        }

        await _logos.SaveAsync(BrandingConsts.LogoBlobName, bytes, overrideExisting: true);

        // La versión se renueva DESPUÉS de guardar los bytes. Al revés, entre las dos
        // escrituras la URL nueva apuntaría al logo viejo y se quedaría cacheada así.
        await _ajustes.SetForTenantOrGlobalAsync(
            CurrentTenant.Id, BrandingSettingNames.LogoVersion, NuevaVersion());

        return await DescribirAsync();
    }

    public async Task<BrandingDto> DeleteLogoAsync()
    {
        // Se limpia la versión ANTES de borrar el archivo: así, si algo fallara en medio,
        // queda un archivo huérfano que nadie pide — en vez de una URL publicada que
        // apunta a un 404.
        await _ajustes.SetForTenantOrGlobalAsync(
            CurrentTenant.Id, BrandingSettingNames.LogoVersion, null);

        // DeleteAsync no lanza si no existe. Borrar un logo que ya no está es justo lo que
        // pidió quien pulsó el botón.
        await _logos.DeleteAsync(BrandingConsts.LogoBlobName);

        return await DescribirAsync();
    }

    private async Task<BrandingDto> DescribirAsync()
    {
        var marca = await _lector.LeerAsync();

        var bytes = marca.HasLogo
            ? await _logos.GetAllBytesOrNullAsync(BrandingConsts.LogoBlobName)
            : null;

        // ⚠️ El nombre PROPIO se pide a un nivel CONCRETO y con `fallback: false`.
        //
        // `ISettingProvider` —el que usa LectorDeMarca— devuelve el valor ya resuelto, y ahí
        // «lo puso este inquilino» y «lo heredó del host» se ven exactamente igual. Usarlo
        // aquí tenía dos consecuencias, las dos silenciosas: el formulario aparecía relleno
        // con el nombre heredado, de modo que guardar sin tocar nada lo convertía en propio
        // y el inquilino dejaba de seguir a la instalación; y vaciar el campo parecía no
        // hacer nada, porque al releer volvía a salir el heredado.
        var propio = CurrentTenant.Id.HasValue
            ? await _ajustes.GetOrNullForCurrentTenantAsync(
                BrandingSettingNames.AppName, fallback: false)
            : await _ajustes.GetOrNullGlobalAsync(BrandingSettingNames.AppName, fallback: false);

        // El heredado es el global — salvo cuando quien mira ES el host, que no hereda de
        // nadie: para él lo que quedaría al vaciar el campo es el nombre de serie.
        var global = CurrentTenant.Id.HasValue
            ? await _ajustes.GetOrNullGlobalAsync(BrandingSettingNames.AppName, fallback: false)
            : null;

        return new BrandingDto
        {
            AppName = propio.IsNullOrWhiteSpace() ? null : propio,
            EffectiveAppName = marca.AppName,
            InheritedAppName = global.IsNullOrWhiteSpace() ? _lector.NombrePorDefecto : global!,
            AccentColor = marca.AccentColor,
            HasLogo = marca.HasLogo,
            LogoVersion = marca.LogoVersion,
            LogoUrl = marca.LogoUrl,
            LogoSizeBytes = bytes?.Length ?? 0,
            IsHost = CurrentTenant.Id == null
        };
    }

    /// <summary>
    /// Ocho caracteres al azar. No es una cuenta ni una fecha a propósito: lo único que
    /// tiene que hacer es <b>ser distinta de la anterior</b> para romper la caché del
    /// navegador. Una marca de tiempo lo haría igual pero contaría cuándo se tocó el logo
    /// a cualquiera que mire la URL, sin ganar nada a cambio.
    /// </summary>
    private static string NuevaVersion()
    {
        return Guid.NewGuid().ToString("N")[..8];
    }

    private static string MensajeDemasiadoGrande()
    {
        var kb = BrandingConsts.MaxLogoBytes / 1024;
        return $"La imagen pesa más de {kb} KB. Elige una más pequeña.";
    }
}
