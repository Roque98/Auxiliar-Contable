using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace IgasConsole.ApiKeys;

public interface IApiKeyRepository : IRepository<ApiKey, Guid>
{
    /// <summary>Las de un usuario, las más recientes primero. Incluye revocadas y
    /// caducadas: saber que una clave existió es parte de poder auditarla.</summary>
    Task<List<ApiKey>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca por el hash. Es la consulta del camino de autenticación, y por eso la columna
    /// va indexada.
    /// </summary>
    Task<ApiKey?> FindByHashAsync(string keyHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claves que caducan entre ahora y <paramref name="hastaUtc"/>, sin revocar y sin
    /// caducar todavía.
    /// <para>Deja fuera las ya caducadas a propósito: avisar de algo que ya pasó no evita
    /// nada, y llenaría la campana cada vez que corre el vigilante.</para>
    /// </summary>
    Task<List<ApiKey>> GetExpiringAsync(
        DateTime ahoraUtc,
        DateTime hastaUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cuántas claves <b>utilizables</b> tiene un inquilino ahora mismo: ni revocadas ni
    /// caducadas. Es el número contra el que se mide la cuota.
    ///
    /// <para>⚠️ Cuenta por INQUILINO y no por usuario, aunque una clave pertenezca a una
    /// persona. Lo que se está limitando es lo que consume la organización; si fuera por
    /// usuario, un inquilino con veinte personas tendría veinte veces la cuota sin que nadie
    /// lo decidiera.</para>
    ///
    /// <para>⚠️ Y deja fuera las revocadas y caducadas <b>a propósito</b>: la fila se conserva
    /// para poder auditarla (§6.05), pero una clave que ya no autentica no consume nada. Si
    /// contaran, un inquilino ordenado —que revoca lo que deja de usar— se quedaría sin cuota
    /// antes que uno que las abandona.</para>
    /// </summary>
    Task<int> ContarUtilizablesAsync(
        Guid? tenantId,
        DateTime ahoraUtc,
        CancellationToken cancellationToken = default);
}
