namespace AuxiliarContable.Privacidad;

public static class AvisoDePrivacidadConsts
{
    /// <summary>
    /// Versión vigente del aviso de privacidad (la fecha en que cambió). ⚠️ Al cambiar el TEXTO
    /// del aviso (<c>angular/src/app/privacidad/aviso.ts</c>) hay que cambiar ESTO a la vez, y con
    /// el mismo valor allí: todos tendrán que aceptarlo de nuevo al entrar. Un cambio de redacción
    /// sin cambio de versión deja a la gente «aceptando» un texto que no leyó.
    /// </summary>
    public const string VersionVigente = "2026-10-25";

    public const int MaxVersionLength = 32;
}
