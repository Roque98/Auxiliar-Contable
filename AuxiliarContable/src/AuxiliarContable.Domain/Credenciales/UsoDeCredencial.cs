using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Credenciales;

/// <summary>
/// Una línea de la bitácora de credenciales: quién (CreatorId) vio, descargó, cambió o borró qué credencial de qué
/// cliente y cuándo (CreationTime). Nunca lleva el dato en sí.
/// </summary>
public class UsoDeCredencial : CreationAuditedEntity<Guid>
{
    public Guid OrganizacionId { get; protected set; }
    public TipoDeCredencial Tipo { get; protected set; }
    public AccionSobreCredencial Accion { get; protected set; }

    protected UsoDeCredencial()
    {
    }

    public UsoDeCredencial(Guid id, Guid organizacionId, TipoDeCredencial tipo, AccionSobreCredencial accion) : base(id)
    {
        OrganizacionId = organizacionId;
        Tipo = tipo;
        Accion = accion;
    }
}
