using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Users;

namespace IgasConsole.Files;

/// <summary>
/// Guarda, devuelve y borra la foto de perfil del usuario actual.
/// </summary>
[Authorize]
public class ProfilePictureAppService : IgasConsoleAppService, IProfilePictureAppService
{
    private readonly IBlobContainer<ProfilePictureContainer> _fotos;

    public ProfilePictureAppService(
        IBlobContainer<ProfilePictureContainer> fotos)
    {
        _fotos = fotos;
    }

    public async Task<ProfilePictureDto> GetAsync()
    {
        var bytes = await _fotos.GetAllBytesOrNullAsync(Clave());
        return Describir(bytes);
    }

    public async Task<ProfilePictureDto> UploadAsync(IRemoteStreamContent file)
    {
        if (file == null)
        {
            throw new UserFriendlyException("No llegó ningún archivo.");
        }

        // Dos comprobaciones de tamaño y no una. Esta es un atajo: si el cliente ya declara
        // que manda más de la cuenta, se corta antes de leer un solo byte del cuerpo. Pero
        // ContentLength lo pone quien sube y puede faltar o mentir, así que la que de verdad
        // protege es el tope al leer, más abajo.
        if (file.ContentLength > ProfilePictureConsts.MaxBytes)
        {
            throw new UserFriendlyException(MensajeDemasiadoGrande());
        }

        var bytes = await LectorConTope.LeerAsync(
            file.GetStream(), ProfilePictureConsts.MaxBytes, MensajeDemasiadoGrande());

        var formato = DetectorDeImagen.Detectar(bytes);
        if (formato == FormatoDeImagen.Desconocido)
        {
            // Se mira el contenido, no el Content-Type que declara el cliente. Ver
            // DetectorDeImagen para por qué esa diferencia importa.
            throw new UserFriendlyException(
                "El archivo no es una imagen JPEG, PNG o WebP. Elige otra foto.");
        }

        // overrideExisting: true — cambiar de foto es reemplazar la anterior, no un error.
        await _fotos.SaveAsync(Clave(), bytes, overrideExisting: true);

        return Describir(bytes);
    }

    public async Task DeleteAsync()
    {
        // DeleteAsync de ABP no lanza si no existe; devuelve false. Borrar una foto que ya
        // no está es exactamente lo que el usuario pidió, así que no es un error.
        await _fotos.DeleteAsync(Clave());
    }

    /// <summary>
    /// El id SIEMPRE de la sesión, nunca de la petición: si viniera por parámetro,
    /// cualquiera podría cambiarle la foto a otro.
    /// </summary>
    private string Clave()
    {
        return CurrentUser.GetId().ToString();
    }

    private static ProfilePictureDto Describir(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return new ProfilePictureDto { HasPicture = false };
        }

        // El tipo se vuelve a deducir de los bytes al LEER, no se guarda junto a la foto.
        // Así no hace falta una tabla aparte solo para recordar un dato que los propios
        // bytes ya llevan dentro, y no puede quedar desincronizado con el contenido real.
        var contentType = DetectorDeImagen.ContentType(DetectorDeImagen.Detectar(bytes));
        if (contentType == null)
        {
            // Solo puede pasar si alguien tocó el contenedor por fuera de este servicio.
            return new ProfilePictureDto { HasPicture = false };
        }

        return new ProfilePictureDto
        {
            HasPicture = true,
            DataUrl = $"data:{contentType};base64,{Convert.ToBase64String(bytes)}",
            SizeBytes = bytes.Length
        };
    }

    private static string MensajeDemasiadoGrande()
    {
        var kb = ProfilePictureConsts.MaxBytes / 1024;
        return $"La imagen pesa más de {kb} KB. Elige una más pequeña.";
    }
}
