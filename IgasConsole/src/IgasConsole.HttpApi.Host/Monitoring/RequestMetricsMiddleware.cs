using System.Diagnostics;
using System.Threading.Tasks;
using IgasConsole.Monitoring;
using Microsoft.AspNetCore.Http;
using Volo.Abp.DependencyInjection;

namespace IgasConsole.HealthChecks;

/// <summary>
/// Cronometra cada petición y se la entrega al acumulador. Se registra al principio del
/// pipeline para que el tiempo medido incluya todo lo que viene después.
/// </summary>
public class RequestMetricsMiddleware : IMiddleware, ITransientDependency
{
    private readonly IAppMetricsAccumulator _acumulador;

    public RequestMetricsMiddleware(IAppMetricsAccumulator acumulador)
    {
        _acumulador = acumulador;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // El propio endpoint de salud lo consulta el recolector cada minuto y el
        // despliegue en bucle: contarlo inflaría las peticiones con tráfico interno.
        if (context.Request.Path.StartsWithSegments("/health-status"))
        {
            await next(context);
            return;
        }

        var reloj = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            reloj.Stop();
            _acumulador.Record(context.Response.StatusCode, reloj.Elapsed.TotalMilliseconds);
        }
    }
}
