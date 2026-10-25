using Volo.Abp.Auditing;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace IgasConsole.Seguridad;

/// <summary>
/// La seguridad de la cuenta de QUIEN LLAMA: registrar y verificar su correo y su teléfono,
/// y activar la verificación en dos pasos. Nunca toca a otra persona: el usuario sale de la
/// sesión, no de la petición.
/// </summary>
public interface ISeguridadDeCuentaAppService : IApplicationService
{
    Task<EstadoDeSeguridadDto> GetAsync();

    /// <summary>Manda un código al correo nuevo. El correo no cambia hasta confirmarlo.</summary>
    Task EnviarCodigoCorreoAsync(CorreoDto input);

    Task<EstadoDeSeguridadDto> ConfirmarCorreoAsync(ConfirmarCorreoDto input);

    /// <summary>Manda un código por SMS. Solo si hay SMS (ver <see cref="EstadoDeSeguridadDto.SmsDisponible"/>).</summary>
    Task EnviarCodigoTelefonoAsync(TelefonoDto input);

    Task<EstadoDeSeguridadDto> ConfirmarTelefonoAsync(ConfirmarTelefonoDto input);

    /// <summary>
    /// Guarda el teléfono SIN verificar, cuando todavía no hay SMS. Sirve de contacto para
    /// recuperar la cuenta con ayuda de un administrador, no como segundo factor.
    /// </summary>
    Task<EstadoDeSeguridadDto> GuardarTelefonoAsync(TelefonoDto input);

    /// <summary>Activa o desactiva la verificación en dos pasos. Activarla exige un método verificado.</summary>
    Task<EstadoDeSeguridadDto> SetSegundoFactorAsync(SegundoFactorDto input);

    /// <summary>
    /// Diez códigos de recuperación nuevos (los anteriores dejan de valer). Sirven para entrar
    /// una vez cada uno si no llega ningún código. Se enseñan UNA sola vez. Exige tener la
    /// verificación en dos pasos activa.
    /// </summary>
    Task<CodigosDeRecuperacionDto> GenerarCodigosDeRecuperacionAsync();
}

/// <summary>
/// La seguridad de OTRA persona, para quien la gestiona (mismo permiso y misma jerarquía que
/// editarla). Sirve para rescatar a alguien que se quedó sin su segundo factor.
/// </summary>
public interface ISeguridadDeUsuariosAppService : IApplicationService
{
    Task<SeguridadDeUsuarioDto> GetAsync(System.Guid id);

    /// <summary>Le quita la verificación en dos pasos (vuelve a entrar solo con contraseña).</summary>
    Task<SeguridadDeUsuarioDto> QuitarSegundoFactorAsync(System.Guid id);
}

public class CodigosDeRecuperacionDto
{
    public System.Collections.Generic.List<string> Codigos { get; set; } = [];
}

public class SeguridadDeUsuarioDto
{
    public bool SegundoFactorActivo { get; set; }
    public bool CorreoConfirmado { get; set; }
    public bool SinCorreo { get; set; }
    public bool TelefonoConfirmado { get; set; }
    public int CodigosDeRecuperacionRestantes { get; set; }
}

public class EstadoDeSeguridadDto
{
    /// <summary>Vacío si el correo es el provisional de quien se dio de alta sin correo.</summary>
    public string? Correo { get; set; }

    public bool CorreoConfirmado { get; set; }

    public string? Telefono { get; set; }

    public bool TelefonoConfirmado { get; set; }

    public bool SegundoFactorActivo { get; set; }

    public bool TelegramVinculado { get; set; }

    /// <summary>¿Se pueden mandar correos? Sin Brevo configurado, no hay códigos por correo.</summary>
    public bool CorreoDisponible { get; set; }

    public bool SmsDisponible { get; set; }

    public int MinutosDeValidez { get; set; } = SeguridadConsts.MinutosDeValidez;

    /// <summary>Códigos de recuperación sin usar (0 = no tiene, o los gastó).</summary>
    public int CodigosDeRecuperacionRestantes { get; set; }

    /// <summary>Lo que le falta para tener la cuenta protegida (0 = todo en orden).</summary>
    public int Pendientes { get; set; }
}

public class CorreoDto
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Correo { get; set; } = string.Empty;
}

public class ConfirmarCorreoDto : CorreoDto
{
    [Required]
    [StringLength(10)]
    // Fuera del registro de auditoría: ABP guarda los parámetros de cada llamada.
    [DisableAuditing]
    public string Codigo { get; set; } = string.Empty;
}

public class TelefonoDto
{
    [Required]
    [StringLength(32)]
    public string Telefono { get; set; } = string.Empty;
}

public class ConfirmarTelefonoDto : TelefonoDto
{
    [Required]
    [StringLength(10)]
    // Fuera del registro de auditoría: ABP guarda los parámetros de cada llamada.
    [DisableAuditing]
    public string Codigo { get; set; } = string.Empty;
}

public class SegundoFactorDto
{
    public bool Activo { get; set; }
}
