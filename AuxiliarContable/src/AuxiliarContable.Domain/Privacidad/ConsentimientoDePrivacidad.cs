using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace AuxiliarContable.Privacidad;

/// <summary>
/// Constancia de que una persona aceptó una versión del aviso de privacidad. Solo se
/// AÑADEN filas: aceptar la versión nueva no borra la vieja, porque lo que hay que poder
/// demostrar (LFPDPPP) es qué texto aceptó cada quien y cuándo — incluido el consentimiento
/// expreso para datos financieros y patrimoniales.
/// </summary>
public class ConsentimientoDePrivacidad : Entity<Guid>
{
    public Guid UserId { get; protected set; }

    public string Version { get; protected set; } = string.Empty;

    public DateTime AceptadoUtc { get; protected set; }

    protected ConsentimientoDePrivacidad()
    {
    }

    public ConsentimientoDePrivacidad(Guid id, Guid userId, string version, DateTime aceptadoUtc) : base(id)
    {
        if (aceptadoUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("La fecha tiene que ir en UTC.", nameof(aceptadoUtc));
        }

        UserId = userId;
        Version = Check.NotNullOrWhiteSpace(version, nameof(version), AvisoDePrivacidadConsts.MaxVersionLength);
        AceptadoUtc = aceptadoUtc;
    }
}
