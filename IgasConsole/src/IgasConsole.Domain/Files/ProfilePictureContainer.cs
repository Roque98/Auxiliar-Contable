using Volo.Abp.BlobStoring;

namespace IgasConsole.Files;

/// <summary>
/// Contenedor tipado donde viven las fotos de perfil. La clase está vacía a propósito:
/// para el sistema de BLOBs de ABP es solo una etiqueta que permite inyectar
/// <c>IBlobContainer&lt;ProfilePictureContainer&gt;</c> y configurar ESTE contenedor
/// aparte de los demás (ver <c>IgasConsoleDomainModule</c>).
/// </summary>
[BlobContainerName(ProfilePictureConsts.ContainerName)]
public class ProfilePictureContainer
{
}
