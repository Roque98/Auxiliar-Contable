using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Auditing;
using Volo.Abp.Identity;

namespace IgasConsole.Security;

/// <summary>
/// Lee el registro de seguridad que ABP ya escribe.
///
/// <para>⚠️ Aquí SÍ basta el repositorio de ABP: <c>IIdentitySecurityLogRepository</c> trae
/// <c>GetListAsync</c> con todos los filtros y <c>GetCountAsync</c> para el total
/// (verificado en <c>refs/abp</c>). Al revés que con los trabajos en segundo plano, donde
/// hubo que escribir uno propio porque el de ABP solo sabía listar lo pendiente.</para>
/// </summary>
[Authorize(IgasConsolePermissions.Security.Logs)]
// Sin auditar: cada consulta a esta pantalla generaría una entrada en el registro de
// peticiones, y mirar el registro no es un evento que merezca registrarse.
[DisableAuditing]
public class SecurityLogAppService : IgasConsoleAppService, ISecurityLogAppService
{
    /// <summary>
    /// Acciones que representan un intento que NO salió bien.
    /// <para>Los nombres los escribe ABP; se traducen aquí a un booleano para que la
    /// pantalla no tenga que conocerlos ni acertar con las mayúsculas.</para>
    /// </summary>
    private static readonly HashSet<string> Fallidas = new(StringComparer.OrdinalIgnoreCase)
    {
        "LoginFailed",
        "LoginNotAllowed",
        "LoginLockedout",
        "ChangePasswordFailed",
        "ChangeProfileFailed"
    };

    private readonly IIdentitySecurityLogRepository _registro;
    private readonly ISecurityLogFilterSource _filtros;

    public SecurityLogAppService(
        IIdentitySecurityLogRepository registro, ISecurityLogFilterSource filtros)
    {
        _registro = registro;
        _filtros = filtros;
    }

    public async Task<SecurityLogFiltersDto> GetFiltersAsync()
    {
        return new SecurityLogFiltersDto { Actions = await _filtros.GetActionsAsync() };
    }

    public async Task<PagedResultDto<SecurityLogDto>> GetListAsync(GetSecurityLogsInput input)
    {
        var desde = DateTime.UtcNow.AddDays(-input.Days);

        // El filtro va por acción EXACTA, que es lo que el repositorio sabe hacer y contar.
        // La alternativa —un "solo fallidos" que agrupara varias acciones— habría que
        // aplicarla en memoria DESPUÉS de paginar, y entonces el total seguiría siendo el de
        // todas las entradas: la pantalla diría "3 de 40" y ese 40 no significaría nada.
        var accion = input.ActionName;

        var filas = await _registro.GetListAsync(
            sorting: "CreationTime DESC",
            maxResultCount: input.MaxResultCount,
            skipCount: input.SkipCount,
            startTime: desde,
            endTime: null,
            applicationName: null,
            identity: null,
            action: accion,
            userId: null,
            userName: input.UserName,
            clientId: null,
            correlationId: null,
            clientIpAddress: input.ClientIpAddress);

        var total = await _registro.GetCountAsync(
            startTime: desde,
            action: accion,
            userName: input.UserName,
            clientIpAddress: input.ClientIpAddress);

        return new PagedResultDto<SecurityLogDto>(total, filas.Select(Mapear).ToList());
    }

    private static SecurityLogDto Mapear(IdentitySecurityLog log)
    {
        return new SecurityLogDto
        {
            Id = log.Id,
            CreationTime = log.CreationTime,
            Action = log.Action,
            Identity = log.Identity,
            UserName = log.UserName,
            ClientIpAddress = log.ClientIpAddress,
            BrowserInfo = log.BrowserInfo,
            ClientId = log.ClientId,
            IsFailure = Fallidas.Contains(log.Action ?? string.Empty),
            Detalle = Detallar(log)
        };
    }

    /// <summary>
    /// Lo que la entrada guardó aparte, en una línea legible.
    ///
    /// <para>Hace falta porque hay acciones cuyo dato importante no cabe en ninguna columna.
    /// La que lo estrenó es la suplantación: la fila dice «alguien hizo ImpersonateTenant» y
    /// lo que uno viene a saber es <b>en qué espacio entró</b>. Sin esto, la entrada
    /// registraba el hecho y se callaba lo único que se pregunta al leerla.</para>
    ///
    /// <para>Se aplana a texto en el servidor y no se manda el diccionario en crudo: son
    /// pares que pone quien escribe la entrada —hoy nosotros, mañana un módulo de ABP— y la
    /// pantalla no puede saber cómo presentar algo que no conoce.</para>
    /// </summary>
    private static string? Detallar(IdentitySecurityLog log)
    {
        if (log.ExtraProperties.Count == 0)
        {
            return null;
        }

        return string.Join(
            " · ",
            log.ExtraProperties
                .Where(p => p.Value != null)
                .Select(p => $"{p.Key}: {p.Value}"));
    }
}
