using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Security;

/// <summary>
/// Una IP vetada a mano desde el panel.
///
/// <para>Es distinta de <see cref="RateLimitEvent"/>, que solo REGISTRA lo que el rate
/// limiter rechazó. El rate limiting cuenta peticiones por ventana y vive en memoria: se
/// reinicia en cada despliegue y olvida a quien lleva días insistiendo. Esto es una
/// decisión humana que persiste.</para>
///
/// <para>NO es multi-tenant —una IP maliciosa lo es para toda la instalación— pero SÍ es
/// auditada (<c>CreationAuditedEntity</c>): interesa saber quién bloqueó y cuándo, porque
/// vetar a alguien por error deja a una persona fuera y hay que poder reconstruir qué pasó.
/// Sin soft delete: desbloquear es borrar de verdad, no dejar una fila oculta que el
/// middleware tendría que aprender a ignorar.</para>
/// </summary>
public class BlockedIp : CreationAuditedEntity<Guid>
{
    public string IpAddress { get; protected set; }

    /// <summary>Por qué se bloqueó. Obligatorio a propósito: dentro de tres meses, una
    /// lista de IPs sin motivo no se puede revisar — nadie se atreve a quitar nada.</summary>
    public string Reason { get; protected set; }

    /// <summary>Cuándo deja de aplicar. Null = indefinido.</summary>
    public DateTime? ExpiresUtc { get; protected set; }

    /// <summary>
    /// True si lo puso el evaluador automático, false si lo puso una persona.
    /// <para>Se distingue porque cambia cómo se lee la lista: un bloqueo automático es una
    /// consecuencia de un umbral y caduca solo, mientras que uno manual es una decisión que
    /// alguien tomó y que probablemente no conviene deshacer sin preguntarle. También evita
    /// que el evaluador vuelva a poner lo que un administrador acaba de quitar a mano.</para>
    /// </summary>
    public bool IsAutomatic { get; protected set; }

    protected BlockedIp()
    {
        IpAddress = string.Empty;
        Reason = string.Empty;
    }

    public BlockedIp(
        Guid id,
        string ipAddress,
        string reason,
        DateTime? expiresUtc,
        bool isAutomatic = false)
        : base(id)
    {
        if (expiresUtc.HasValue && expiresUtc.Value.Kind != DateTimeKind.Utc)
        {
            throw new AbpException(
                "BlockedIp.ExpiresUtc debe ser UTC: la columna es timestamptz y Npgsql " +
                "lanza en tiempo de ejecución si el Kind no es Utc.");
        }

        IpAddress = Check.NotNullOrWhiteSpace(
            ipAddress, nameof(ipAddress), SecurityConsts.MaxIpAddressLength);
        Reason = Check.NotNullOrWhiteSpace(
            reason, nameof(reason), SecurityConsts.MaxBlockReasonLength);
        ExpiresUtc = expiresUtc;
        IsAutomatic = isAutomatic;
    }

    /// <summary>¿Sigue vigente en el instante dado?</summary>
    public bool EstaVigente(DateTime ahoraUtc)
    {
        return ExpiresUtc == null || ExpiresUtc.Value > ahoraUtc;
    }
}
