using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.IA;

namespace AuxiliarContable;

/// <summary>
/// El chat de los tests: cada llamada devuelve el siguiente turno programado y apunta la
/// solicitud (para comprobar qué historial y qué herramientas recibió el modelo).
/// </summary>
public class ModeloConversacionalDePrueba : IModeloConversacional
{
    private readonly Queue<Func<SolicitudDeConversacion, RespuestaDeConversacion>> _turnos = new();
    private int _usos;

    public List<SolicitudDeConversacion> Solicitudes { get; } = [];

    public bool EstaConfigurado { get; set; } = true;

    public void Contestar(string texto) =>
        _turnos.Enqueue(_ => new RespuestaDeConversacion([new BloqueDeTexto(texto)], "end_turn", Uso, "chat-de-prueba"));

    public void UsarHerramienta(string nombre, object entrada) =>
        _turnos.Enqueue(_ => new RespuestaDeConversacion(
            [new BloqueDeUsoDeHerramienta($"uso_{++_usos}", nombre, JsonSerializer.SerializeToElement(entrada))],
            "tool_use", Uso, "chat-de-prueba"));

    public void Fallar(MotivoDeErrorDeIA motivo) =>
        _turnos.Enqueue(_ => throw new ErrorDeIAException(motivo, "falla programada"));

    public void Reiniciar()
    {
        _turnos.Clear();
        Solicitudes.Clear();
        EstaConfigurado = true;
    }

    public Task<RespuestaDeConversacion> ConversarAsync(SolicitudDeConversacion solicitud, CancellationToken cancellationToken = default)
    {
        Solicitudes.Add(solicitud);
        if (_turnos.Count == 0)
        {
            throw new InvalidOperationException("El chat de prueba no tiene turno programado.");
        }

        return Task.FromResult(_turnos.Dequeue()(solicitud));
    }

    private static UsoDeIA Uso => new(100, 20, 0, 0);
}
