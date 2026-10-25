namespace AuxiliarContable.Acceso;

/// <summary>
/// Sección <c>Acceso</c> de la configuración. Ver <see cref="AccesoConsts"/> para qué hace y,
/// sobre todo, para qué <b>no</b> hace.
/// </summary>
public class AccesoOptions
{
    /// <summary>
    /// Segmento bajo el que viven el panel del host y el formulario de login.
    /// <b>Vacío = comportamiento por defecto</b> (raíz y <c>/Account/Login</c>).
    /// </summary>
    public string SegmentoDelHost { get; set; } = string.Empty;

    /// <summary>El valor ya validado. Usa siempre este, nunca la propiedad en crudo.</summary>
    public string SegmentoNormalizado => AccesoConsts.Normalizar(SegmentoDelHost);

    public bool EstaOculto => SegmentoNormalizado.Length > 0;
}
