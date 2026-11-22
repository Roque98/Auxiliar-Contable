using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.LeyesDelCliente;

/// <summary>
/// Una pregunta libre sobre las leyes que subió el cliente («¿cada cuándo renuevo el dictamen de protección civil?»). La
/// contesta el motor solo con esas leyes y con citas verificadas; tarda un par de minutos, así que va por el trabajador.
/// </summary>
public class PreguntaSobreLeyes : CreationAuditedAggregateRoot<Guid>
{
    public const int MaxIntentos = 3;

    public Guid OrganizacionId { get; protected set; }
    public string Pregunta { get; protected set; } = string.Empty;
    public EstadoDePregunta Estado { get; protected set; }
    public DateTime PedidoUtc { get; protected set; }
    public int Intentos { get; protected set; }

    /// <summary>JSON del motor: encontrado, respuesta, citas (verificadas), falta.</summary>
    public string? Respuesta { get; protected set; }

    public DateTime? ListaUtc { get; protected set; }
    public string? Error { get; protected set; }

    protected PreguntaSobreLeyes()
    {
    }

    public PreguntaSobreLeyes(Guid id, Guid organizacionId, string pregunta, DateTime utc) : base(id)
    {
        OrganizacionId = organizacionId;
        Pregunta = LeyDelCliente.Corto(pregunta, LeyesDelClienteConsts.MaxPreguntaLength) ?? "";
        Estado = EstadoDePregunta.Pendiente;
        PedidoUtc = utc;
    }

    public void Contestar(string json, DateTime utc)
    {
        Respuesta = json;
        Estado = EstadoDePregunta.Lista;
        ListaUtc = utc;
        Error = null;
    }

    public void Fallar(string error, DateTime proxima)
    {
        Intentos++;
        Error = LeyDelCliente.Corto(error, LeyesDelClienteConsts.MaxErrorLength);
        if (Intentos >= MaxIntentos)
        {
            Estado = EstadoDePregunta.Error;
        }
        else
        {
            PedidoUtc = proxima;
        }
    }

    public void Esperar(DateTime proxima) => PedidoUtc = proxima;

    public void Detener(string error)
    {
        Error = LeyDelCliente.Corto(error, LeyesDelClienteConsts.MaxErrorLength);
        Estado = EstadoDePregunta.Error;
    }
}
