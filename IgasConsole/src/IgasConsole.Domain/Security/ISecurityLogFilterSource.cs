using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IgasConsole.Security;

/// <summary>
/// Las acciones que de verdad aparecen en el registro de seguridad.
///
/// <para>Hace falta una consulta propia porque <c>IIdentitySecurityLogRepository</c> filtra
/// por acción pero no sabe enumerarlas (verificado en <c>refs/abp</c>). Y se sacan de los
/// datos en vez de escribir una lista fija: los nombres los pone el módulo Identity de ABP
/// y pueden cambiar al actualizarlo — una lista fija se quedaría desfasada en silencio y el
/// desplegable ofrecería filtros que no encuentran nada.</para>
/// </summary>
public interface ISecurityLogFilterSource
{
    Task<List<string>> GetActionsAsync(CancellationToken cancellationToken = default);
}
