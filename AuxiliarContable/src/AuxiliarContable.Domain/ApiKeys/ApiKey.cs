using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.ApiKeys;

/// <summary>
/// Una clave de API: permite llamar a la API sin pasar por el login interactivo.
///
/// <para>La clave <b>autentica COMO un usuario</b> y hereda sus permisos. Se eligió así en
/// vez de darle permisos propios porque un segundo sistema de autorización en paralelo
/// acaba divergiendo del primero: se concede un permiso a un rol y nadie se acuerda de las
/// claves. Con esto, revisar qué puede hacer una clave es revisar qué puede hacer su dueño.</para>
///
/// <para>⚠️ Solo se guarda el HASH. Ni el dueño puede volver a ver la clave después de
/// crearla — si se pierde, se revoca y se hace otra.</para>
/// </summary>
public class ApiKey : CreationAuditedEntity<Guid>
{
    /// <summary>Usuario como el que actúa esta clave.</summary>
    public Guid UserId { get; protected set; }

    /// <summary>Tenant del usuario, para poder restaurarlo sin consultar al usuario.</summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>Para qué es. Lo escribe quien la crea: "script de facturación nocturno".</summary>
    public string Name { get; protected set; }

    /// <summary>SHA-256 de la clave, en hexadecimal.</summary>
    public string KeyHash { get; protected set; }

    /// <summary>Primeros caracteres en claro, para reconocerla en la lista.</summary>
    public string Pista { get; protected set; }

    public DateTime? ExpiresUtc { get; protected set; }

    /// <summary>Última vez que se usó, con la precisión que permite el refresco. Null =
    /// nunca. Es el dato que se mira antes de revocar una clave que ya no se sabe de quién es.</summary>
    public DateTime? LastUsedUtc { get; protected set; }

    /// <summary>Revocada a mano. Se conserva la fila en vez de borrarla: interesa saber que
    /// existió y cuándo dejó de valer.</summary>
    public DateTime? RevokedUtc { get; protected set; }

    protected ApiKey()
    {
        Name = string.Empty;
        KeyHash = string.Empty;
        Pista = string.Empty;
    }

    public ApiKey(
        Guid id,
        Guid userId,
        Guid? tenantId,
        string name,
        string keyHash,
        string pista,
        DateTime? expiresUtc)
        : base(id)
    {
        if (expiresUtc.HasValue && expiresUtc.Value.Kind != DateTimeKind.Utc)
        {
            throw new AbpException(
                "ApiKey.ExpiresUtc debe ser UTC: la columna es timestamptz y Npgsql lanza " +
                "en tiempo de ejecución si el Kind no es Utc.");
        }

        UserId = userId;
        TenantId = tenantId;
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), ApiKeyConsts.MaxNameLength);
        KeyHash = Check.NotNullOrWhiteSpace(keyHash, nameof(keyHash), ApiKeyConsts.HashLength);
        Pista = Check.NotNullOrWhiteSpace(pista, nameof(pista), ApiKeyConsts.PistaLength);
        ExpiresUtc = expiresUtc;
    }

    /// <summary>¿Sirve para autenticar ahora mismo?</summary>
    public bool EsUtilizable(DateTime ahoraUtc)
    {
        return RevokedUtc == null && (ExpiresUtc == null || ExpiresUtc.Value > ahoraUtc);
    }

    public void Revocar(DateTime ahoraUtc)
    {
        // Idempotente: revocar dos veces no debe mover la fecha de la primera vez, que es
        // la que cuenta para reconstruir qué pasó.
        RevokedUtc ??= ahoraUtc;
    }

    /// <summary>
    /// Anota el uso, pero solo si ha pasado el margen de refresco.
    /// </summary>
    /// <returns>True si de verdad cambió y hay que guardar.</returns>
    public bool AnotarUso(DateTime ahoraUtc)
    {
        if (LastUsedUtc.HasValue &&
            ahoraUtc - LastUsedUtc.Value < TimeSpan.FromMinutes(ApiKeyConsts.UltimoUsoRefrescoMinutos))
        {
            return false;
        }

        LastUsedUtc = ahoraUtc;
        return true;
    }
}
