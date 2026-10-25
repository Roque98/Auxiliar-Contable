using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.IA;

namespace AuxiliarContable;

/// <summary>
/// La IA de los tests: devuelve lo que el test le programe (como JSON, igual que la API real)
/// y apunta lo que se le pidió. Sin programar, se comporta como «no configurado».
/// </summary>
public class ModeloDeIADePrueba : IModeloDeIA
{
    private readonly Queue<Func<SolicitudDeIA, string>> _respuestas = new();

    public List<SolicitudDeIA> Solicitudes { get; } = [];

    public bool EstaConfigurado { get; set; } = true;

    /// <summary>La próxima llamada devolverá este objeto (serializado en camelCase, como el modelo).</summary>
    public void Responder(object datos) =>
        _respuestas.Enqueue(_ => JsonSerializer.Serialize(datos, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    public void Fallar(MotivoDeErrorDeIA motivo) =>
        _respuestas.Enqueue(_ => throw new ErrorDeIAException(motivo, "falla programada"));

    public void Reiniciar()
    {
        _respuestas.Clear();
        Solicitudes.Clear();
        EstaConfigurado = true;
    }

    public Task<ResultadoDeIA<T>> ExtraerAsync<T>(SolicitudDeIA solicitud, CancellationToken cancellationToken = default)
        where T : class
    {
        if (!EstaConfigurado)
        {
            throw new ErrorDeIAException(MotivoDeErrorDeIA.NoConfigurado, "IA de prueba apagada");
        }

        Solicitudes.Add(solicitud);
        if (_respuestas.Count == 0)
        {
            throw new InvalidOperationException("La IA de prueba no tiene respuesta programada.");
        }

        var json = _respuestas.Dequeue()(solicitud);
        var datos = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return Task.FromResult(new ResultadoDeIA<T>(datos, "modelo-de-prueba", new UsoDeIA(10, 5, 0, 0), TimeSpan.Zero));
    }
}
