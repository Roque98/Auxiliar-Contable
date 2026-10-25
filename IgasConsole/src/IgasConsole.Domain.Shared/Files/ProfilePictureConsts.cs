namespace IgasConsole.Files;

public static class ProfilePictureConsts
{
    /// <summary>
    /// Nombre del contenedor de BLOBs. Va en una constante y NO se deduce del nombre de la
    /// clase contenedora a propósito: si mañana se renombra <c>ProfilePictureContainer</c>,
    /// las fotos ya guardadas seguirían estando bajo el nombre viejo y dejarían de
    /// encontrarse.
    /// </summary>
    public const string ContainerName = "profile-pictures";

    /// <summary>
    /// Tope real de lo que se acepta guardar, en bytes.
    /// <para>El navegador ya reescala antes de subir (ver el componente Angular), así que
    /// una foto normal llega en torno a 50 KB. Este límite es para quien llame a la API
    /// directamente, sin pasar por esa pantalla: sin él, cualquiera con sesión podría
    /// meter archivos de cientos de megas en la base de datos.</para>
    /// </summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>Lado máximo, en píxeles, al que el navegador reescala antes de subir.</summary>
    public const int MaxLadoPixeles = 512;
}
