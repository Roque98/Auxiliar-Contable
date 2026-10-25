using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Conversaciones;

/// <summary>
/// La plática de UNA persona sobre UN cliente. El cliente activo va en la conversación y no en
/// lo que diga el modelo: así nunca se cruzan datos de dos clientes (visión, «chat del equipo»).
///
/// <para>Una abierta por persona y cliente; «empezar de nuevo» la cierra y abre otra. Las
/// cerradas se quedan: son el rastro de qué se le dijo a quién.</para>
/// </summary>
public class Conversacion : CreationAuditedAggregateRoot<Guid>
{
    public Guid UsuarioId { get; protected set; }

    public Guid OrganizacionId { get; protected set; }

    public bool Cerrada { get; protected set; }

    public DateTime UltimoMensajeUtc { get; protected set; }

    protected Conversacion()
    {
    }

    public Conversacion(Guid id, Guid usuarioId, Guid organizacionId, DateTime ahora) : base(id)
    {
        UsuarioId = usuarioId;
        OrganizacionId = organizacionId;
        UltimoMensajeUtc = ahora;
    }

    public void Tocar(DateTime ahora) => UltimoMensajeUtc = ahora;

    public void Cerrar() => Cerrada = true;
}
