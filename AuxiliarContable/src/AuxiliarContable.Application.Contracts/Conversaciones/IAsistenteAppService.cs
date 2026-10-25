using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Conversaciones;

/// <summary>
/// El chat: «platicar con tu contador». Siempre sobre UN cliente explícito
/// (<c>organizacionId</c>), con los permisos de quien escribe: lo que el asistente lee o guarda
/// pasa por los mismos servicios que la pantalla.
/// </summary>
public interface IAsistenteAppService : IApplicationService
{
    /// <summary>La conversación abierta con ese cliente (vacía si no hay).</summary>
    Task<ConversacionDto> GetAsync(Guid organizacionId);

    /// <summary>Manda un mensaje y espera la respuesta. Devuelve la conversación entera, ya con la respuesta.</summary>
    Task<ConversacionDto> EnviarAsync(Guid organizacionId, EnviarMensajeDto input);

    /// <summary>Cierra la conversación abierta; la siguiente empieza de cero (el perfil se queda).</summary>
    Task<ConversacionDto> EmpezarDeNuevoAsync(Guid organizacionId);
}

public class EnviarMensajeDto
{
    [Required]
    [StringLength(ConversacionConsts.MaxTextoDelUsuario)]
    public string Texto { get; set; } = string.Empty;
}

public class ConversacionDto
{
    public Guid OrganizacionId { get; set; }

    public string NombreDelCliente { get; set; } = string.Empty;

    /// <summary>¿Hay modelo configurado? Si no, la pantalla lo dice en vez de dejar escribir.</summary>
    public bool Disponible { get; set; }

    /// <summary>¿Puede guardar datos del cliente? Si no, el asistente solo consulta.</summary>
    public bool PuedeEditar { get; set; }

    /// <summary>¿Habla alguien del despacho (y no el cliente)? Cambia el tono y lo que se le ofrece.</summary>
    public bool EsDelDespacho { get; set; }

    public List<MensajeDto> Mensajes { get; set; } = [];
}

public class MensajeDto
{
    public Guid Id { get; set; }

    public RolDelMensaje Rol { get; set; }

    public string Texto { get; set; } = string.Empty;

    public DateTime CreadoUtc { get; set; }

    /// <summary>Qué hizo el asistente en este turno («Guardó: Giro»), para que se vea sin leer la respuesta.</summary>
    public List<string> Acciones { get; set; } = [];
}
