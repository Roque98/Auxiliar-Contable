using System;
using System.Collections.Generic;
using AuxiliarContable.IA;
using Volo.Abp.Domain.Entities;

namespace AuxiliarContable.Conversaciones;

/// <summary>
/// Un turno de la conversación. Solo se añaden; no se editan.
///
/// <para><see cref="Bloques"/> es el turno tal como se le manda al modelo (texto, usos y
/// resultados de herramientas); <see cref="Texto"/>, lo que ve la persona. Los resultados de
/// herramientas (<see cref="RolDelMensaje.Herramientas"/>) no tienen texto visible.</para>
/// </summary>
public class MensajeDeConversacion : Entity<Guid>
{
    public Guid ConversacionId { get; protected set; }

    /// <summary>Orden dentro de la conversación (1, 2, 3…): la hora no basta, un turno con herramientas guarda varios en el mismo instante.</summary>
    public int Numero { get; protected set; }

    public RolDelMensaje Rol { get; protected set; }

    public string Texto { get; protected set; } = string.Empty;

    public string Bloques { get; protected set; } = "[]";

    public DateTime CreadoUtc { get; protected set; }

    /// <summary>Tokens que costó este turno del asistente (entrada + salida). 0 en los demás.</summary>
    public int Tokens { get; protected set; }

    protected MensajeDeConversacion()
    {
    }

    public MensajeDeConversacion(
        Guid id, Guid conversacionId, int numero, RolDelMensaje rol, string texto, IReadOnlyList<BloqueDeTurno> bloques, DateTime ahora, int tokens = 0)
        : base(id)
    {
        ConversacionId = conversacionId;
        Numero = numero;
        Rol = rol;
        Texto = texto.Length <= ConversacionConsts.MaxTextoLength ? texto : texto[..ConversacionConsts.MaxTextoLength];
        Bloques = BloqueDeTurno.Serializar(bloques);
        CreadoUtc = ahora;
        Tokens = tokens;
    }

    public TurnoDeConversacion ATurno() =>
        new(Rol == RolDelMensaje.Asistente ? RolDelTurno.Asistente : RolDelTurno.Usuario, BloqueDeTurno.Deserializar(Bloques));
}
