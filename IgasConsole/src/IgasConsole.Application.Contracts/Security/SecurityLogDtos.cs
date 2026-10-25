using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace IgasConsole.Security;

public class SecurityLogDto
{
    public Guid Id { get; set; }

    public DateTime CreationTime { get; set; }

    /// <summary>
    /// Qué pasó: <c>LoginSucceeded</c>, <c>LoginFailed</c>, <c>LoginRequiresTwoFactor</c>,
    /// <c>ChangePassword</c>… Lo escribe ABP, no nosotros.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Quién lo registró: <c>Identity</c> o <c>OpenIddict</c>.</summary>
    public string Identity { get; set; } = string.Empty;

    public string? UserName { get; set; }

    public string? ClientIpAddress { get; set; }

    public string? BrowserInfo { get; set; }

    public string? ClientId { get; set; }

    /// <summary>True si la acción representa un intento fallido. Se deriva en el servidor
    /// para que la pantalla no tenga que conocer los nombres que usa ABP.</summary>
    public bool IsFailure { get; set; }

    /// <summary>
    /// Lo que la entrada guardó aparte de las columnas fijas, ya aplanado a texto.
    /// <para>Vacío en casi todas: lo usan las acciones cuyo dato clave no cabe en ninguna
    /// columna, como la suplantación —donde lo que se quiere leer es en qué espacio se
    /// entró.</para>
    /// </summary>
    public string? Detalle { get; set; }
}

public class GetSecurityLogsInput : PagedAndSortedResultRequestDto
{
    /// <summary>Cuántos días hacia atrás. Acotado para no permitir barridos completos.</summary>
    [Range(1, 90)]
    public int Days { get; set; } = 7;

    /// <summary>
    /// Filtra por acción exacta (las que escribe ABP). Vacío = todas.
    ///
    /// <para>⚠️ SE LLAMA <c>ActionName</c> Y NO <c>Action</c> A PROPÓSITO. En MVC,
    /// <c>action</c> es un valor de RUTA —el nombre del método— y el enlazador de modelos
    /// rellena con él cualquier propiedad que se llame así, aunque nadie la mande en la
    /// petición. El efecto era una consulta filtrando por <c>Action = 'GetList'</c>: cero
    /// filas, sin ningún error, con la tabla llena. Solo se ve mirando el SQL generado.</para>
    /// </summary>
    public string? ActionName { get; set; }

    /// <summary>Parcial y sin distinguir mayúsculas: aquí se busca a una persona.</summary>
    public string? UserName { get; set; }

    /// <summary>Exacta: se pega una IP del panel de bloqueos para ver qué hizo.</summary>
    public string? ClientIpAddress { get; set; }
}

/// <summary>
/// Las acciones que existen de verdad en el registro, para que la pantalla pueda ofrecer
/// un desplegable en vez de pedir que alguien escriba «LoginFailed» sin equivocarse.
/// <para>Se calculan de los datos y no de una lista fija: los nombres los pone ABP y
/// pueden cambiar al actualizar el módulo.</para>
/// </summary>
public class SecurityLogFiltersDto
{
    public List<string> Actions { get; set; } = new();
}

/// <summary>
/// Registro de seguridad: quién intentó entrar, desde dónde y si lo consiguió.
///
/// <para>La tabla la llena ABP solo desde el primer día —inicios de sesión correctos y
/// fallidos, bloqueos, cambios de contraseña, segundo factor— y hasta ahora <b>no había
/// forma de verla</b>. Complementa al registro de peticiones: aquel dice qué endpoint se
/// llamó, esto dice quién intentó autenticarse.</para>
/// </summary>
public interface ISecurityLogAppService : IApplicationService
{
    Task<PagedResultDto<SecurityLogDto>> GetListAsync(GetSecurityLogsInput input);

    /// <summary>Valores disponibles para el desplegable de acciones.</summary>
    Task<SecurityLogFiltersDto> GetFiltersAsync();
}
