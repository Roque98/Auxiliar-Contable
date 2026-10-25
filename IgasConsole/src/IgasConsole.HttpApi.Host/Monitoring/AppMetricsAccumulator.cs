using System.Threading;
using IgasConsole.Monitoring;
using Volo.Abp.DependencyInjection;

namespace IgasConsole.HealthChecks;

/// <summary>
/// Contadores en memoria de las peticiones del intervalo en curso. Singleton y sin
/// bloqueos en el camino caliente: cada petición solo hace operaciones Interlocked, así
/// que medir no cuesta latencia.
/// </summary>
public class AppMetricsAccumulator : IAppMetricsAccumulator, ISingletonDependency
{
    private long _peticiones;
    private long _errores;
    private long _erroresCliente;
    private long _milisegundosTotales;   // acumulado en centésimas para no perder precisión
    private long _maximoCentesimas;

    public void Record(int statusCode, double elapsedMs)
    {
        Interlocked.Increment(ref _peticiones);

        if (statusCode >= 500)
        {
            Interlocked.Increment(ref _errores);
        }
        else if (statusCode >= 400)
        {
            Interlocked.Increment(ref _erroresCliente);
        }

        var centesimas = (long)(elapsedMs * 100);
        Interlocked.Add(ref _milisegundosTotales, centesimas);

        // Máximo sin cerrojo: se reintenta mientras otro hilo gane la carrera.
        long maximoActual;
        while (centesimas > (maximoActual = Interlocked.Read(ref _maximoCentesimas)))
        {
            if (Interlocked.CompareExchange(ref _maximoCentesimas, centesimas, maximoActual) == maximoActual)
            {
                break;
            }
        }
    }

    public RequestSample DrainAndReset()
    {
        // Exchange en vez de leer-y-poner-a-cero: entre ambas operaciones cabrían
        // peticiones que se perderían de la cuenta.
        var peticiones = Interlocked.Exchange(ref _peticiones, 0);
        var errores = Interlocked.Exchange(ref _errores, 0);
        var erroresCliente = Interlocked.Exchange(ref _erroresCliente, 0);
        var totales = Interlocked.Exchange(ref _milisegundosTotales, 0);
        var maximo = Interlocked.Exchange(ref _maximoCentesimas, 0);

        return new RequestSample
        {
            RequestCount = (int)peticiones,
            ErrorCount = (int)errores,
            ClientErrorCount = (int)erroresCliente,
            AvgResponseMs = peticiones > 0 ? totales / 100.0 / peticiones : null,
            MaxResponseMs = peticiones > 0 ? maximo / 100.0 : null
        };
    }
}
