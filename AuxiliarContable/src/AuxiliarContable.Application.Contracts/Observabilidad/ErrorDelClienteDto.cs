using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Observabilidad;

/// <summary>Un error de JavaScript de la web (Angular) o de la app (Expo) que vio un usuario.</summary>
public class ErrorDelClienteDto
{
    /// <summary><c>web</c> o <c>app</c>.</summary>
    [Required]
    [StringLength(10)]
    public string Origen { get; set; } = "";

    [Required]
    [StringLength(ErroresDelClienteConsts.MaxMensaje)]
    public string Mensaje { get; set; } = "";

    [StringLength(ErroresDelClienteConsts.MaxPila)]
    public string? Pila { get; set; }

    /// <summary>La pantalla (ruta de la web o de Expo Router) donde pasó.</summary>
    [StringLength(300)]
    public string? Ruta { get; set; }

    /// <summary>Versión de la web (hash del despliegue) o de la app.</summary>
    [StringLength(60)]
    public string? Version { get; set; }

    /// <summary>Navegador o dispositivo.</summary>
    [StringLength(300)]
    public string? Agente { get; set; }
}

public static class ErroresDelClienteConsts
{
    public const int MaxMensaje = 1000;
    public const int MaxPila = 8000;

    /// <summary>Por persona y minuto: un error en un bucle no inunda los logs.</summary>
    public const int MaxPorMinuto = 20;
}

/// <summary>
/// Recibe los errores de la web y de la app y los deja en el log (y con él en OpenObserve), con el
/// usuario y la traza de la petición. Así no hace falta un SDK ni un servicio de terceros.
/// </summary>
public interface IErroresDelClienteAppService : IApplicationService
{
    Task RegistrarAsync(ErrorDelClienteDto input);
}
