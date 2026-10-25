using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace IgasConsole.Files;

public class ProfilePictureDto
{
    public bool HasPicture { get; set; }

    /// <summary>
    /// La imagen como <c>data:</c> URL (<c>data:image/jpeg;base64,...</c>), o null si el
    /// usuario no tiene foto.
    /// <para>Va embebida en el JSON y no como respuesta binaria aparte porque el destino
    /// es un <c>&lt;img src&gt;</c> y el avatar de la barra superior, que esperan una
    /// cadena. Devolver bytes obligaría al navegador a pedir la imagen en una segunda
    /// petición que también tendría que ir autenticada — es decir, o un endpoint anónimo,
    /// o el token en la URL. Ninguna de las dos hace falta para una imagen que, ya
    /// reescalada, pesa unas decenas de KB.</para>
    /// </summary>
    public string? DataUrl { get; set; }

    /// <summary>Tamaño en bytes de lo que hay guardado. 0 si no hay foto.</summary>
    public int SizeBytes { get; set; }
}

/// <summary>
/// Foto de perfil del USUARIO ACTUAL. Sin permiso propio, igual que la vinculación de
/// Telegram: cualquiera con sesión gestiona la suya, y solo la suya — el id de usuario
/// sale siempre de la sesión y nunca de la petición.
/// </summary>
public interface IProfilePictureAppService : IApplicationService
{
    Task<ProfilePictureDto> GetAsync();

    /// <summary>
    /// Sube o reemplaza la foto. Se envía como <c>multipart/form-data</c> con el archivo
    /// en el campo <c>file</c> (el nombre del campo tiene que coincidir con el del
    /// parámetro: así lo resuelve el model binder de ABP).
    /// </summary>
    Task<ProfilePictureDto> UploadAsync(IRemoteStreamContent file);

    Task DeleteAsync();
}
