using IgasConsole.Organizaciones;
using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace IgasConsole.Invitaciones;

/// <summary>
/// Una invitación por correo para darse de alta con un rol ya decidido.
///
/// <para>⚠️ Del enlace solo se guarda el HASH (<see cref="TokenHash"/>). El token en claro
/// existe únicamente en el correo: quien lea la base no puede usar una invitación pendiente.
/// Por lo mismo, el token lo genera el trabajo que manda el correo, no la petición que lo
/// encola (los argumentos de los trabajos se guardan en una tabla).</para>
///
/// <para>Aceptarla confirma el correo: el enlace solo llega a quien tiene acceso a ese
/// buzón. Por eso la cuenta nace con el correo verificado y no hace falta otro paso.</para>
///
/// <para>Quien invita es <c>CreatorId</c> (auditoría de ABP), no un dato de la petición.</para>
/// </summary>
public class Invitacion : FullAuditedAggregateRoot<Guid>
{
    /// <summary>En minúsculas y sin espacios: la unicidad de las pendientes se mira así.</summary>
    public string Email { get; private set; }

    /// <summary>El nombre del rol de ABP que tendrá la cuenta al aceptarla.</summary>
    public string Rol { get; private set; }

    /// <summary>
    /// Dónde entra la cuenta al aceptarla. Obligatoria si el rol es de cliente (nivel
    /// Usuario), vacía para el equipo: ver <c>MembresiasDeOrganizacion</c>.
    /// </summary>
    public Guid? OrganizacionId { get; private set; }

    /// <summary>Null hasta que sale el primer correo.</summary>
    public string? TokenHash { get; private set; }

    public DateTime? ExpiraEn { get; private set; }

    public DateTime? UltimoEnvio { get; private set; }

    public int Envios { get; private set; }

    public DateTime? AceptadaEn { get; private set; }

    /// <summary>La cuenta que se creó al aceptarla.</summary>
    public Guid? UsuarioId { get; private set; }

    public DateTime? RevocadaEn { get; private set; }

    protected Invitacion()
    {
        Email = string.Empty;
        Rol = string.Empty;
    }

    /// <summary>Su papel en la organización al aceptar (solo con <see cref="OrganizacionId"/>).</summary>
    public RolEnOrganizacion RolEnOrganizacion { get; private set; } = RolEnOrganizacion.Miembro;

    public Invitacion(
        Guid id, string email, string rol, Guid? organizacionId = null,
        RolEnOrganizacion rolEnOrganizacion = RolEnOrganizacion.Miembro)
        : base(id)
    {
        Email = NormalizarEmail(email);
        Rol = Check.NotNullOrWhiteSpace(rol, nameof(rol), InvitacionConsts.MaxRolLength);
        OrganizacionId = organizacionId;
        RolEnOrganizacion = rolEnOrganizacion;
    }

    public static string NormalizarEmail(string email)
    {
        return Check.NotNullOrWhiteSpace(email, nameof(email), InvitacionConsts.MaxEmailLength)
            .Trim()
            .ToLowerInvariant();
    }

    public EstadoInvitacion Estado(DateTime ahora)
    {
        if (AceptadaEn.HasValue)
        {
            return EstadoInvitacion.Aceptada;
        }

        if (RevocadaEn.HasValue)
        {
            return EstadoInvitacion.Revocada;
        }

        if (ExpiraEn.HasValue && ExpiraEn.Value <= ahora)
        {
            return EstadoInvitacion.Vencida;
        }

        return EstadoInvitacion.Pendiente;
    }

    /// <summary>¿Se le puede mandar (otra vez) el correo? Las vencidas sí: reenviar las revive.</summary>
    public bool AdmiteEnvio(DateTime ahora)
    {
        var estado = Estado(ahora);
        return estado is EstadoInvitacion.Pendiente or EstadoInvitacion.Vencida;
    }

    /// <summary>
    /// Registra un enlace nuevo. El anterior deja de valer en el acto: solo se guarda un hash.
    /// </summary>
    public void RegistrarEnvio(string tokenHash, DateTime ahora)
    {
        if (!AdmiteEnvio(ahora))
        {
            throw new BusinessException("IgasConsole:InvitacionCerrada");
        }

        TokenHash = Check.NotNullOrWhiteSpace(tokenHash, nameof(tokenHash), InvitacionConsts.TokenHashLength);
        ExpiraEn = ahora.AddDays(InvitacionConsts.DiasDeVigencia);
        UltimoEnvio = ahora;
        Envios++;
    }

    public void Aceptar(Guid usuarioId, DateTime ahora)
    {
        if (Estado(ahora) != EstadoInvitacion.Pendiente || TokenHash == null)
        {
            throw new BusinessException("IgasConsole:InvitacionCerrada");
        }

        AceptadaEn = ahora;
        UsuarioId = usuarioId;
        // El enlace ya no sirve para nada: fuera el hash.
        TokenHash = null;
    }

    public void Revocar(DateTime ahora)
    {
        if (AceptadaEn.HasValue)
        {
            throw new BusinessException("IgasConsole:InvitacionCerrada");
        }

        RevocadaEn ??= ahora;
        TokenHash = null;
    }
}
