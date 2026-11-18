using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.Documentos;

namespace AuxiliarContable;

/// <summary>El SAT de los tests: contesta lo que el test programe por UUID (si no, «Vigente») y apunta qué se le preguntó.</summary>
public class VerificadorDeCfdiDePrueba : IVerificadorDeCfdi
{
    public bool EstaConfigurado => true;

    public bool Caido { get; set; }

    public Dictionary<string, EstatusDelSat> Respuestas { get; } = [];

    public List<(string Emisor, string Receptor, string Total, string Uuid)> Preguntas { get; } = [];

    public void Reiniciar()
    {
        Caido = false;
        Respuestas.Clear();
        Preguntas.Clear();
    }

    public Task<EstatusDelSat> ConsultarAsync(string rfcEmisor, string rfcReceptor, string total, string uuid, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new VerificadorNoDisponibleException("El SAT no responde.");
        }

        Preguntas.Add((rfcEmisor, rfcReceptor, total, uuid));
        return Task.FromResult(Respuestas.TryGetValue(uuid, out var r) ? r : new EstatusDelSat("Vigente", null, "Cancelable sin aceptación", "S - Comprobante obtenido satisfactoriamente."));
    }
}
