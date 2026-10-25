using System;
using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using AuxiliarContable.RateLimiting;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.RateLimiting;

/// <summary>
/// Guarda un contador por identidad y lo va olvidando.
///
/// <para>Es singleton y vive en memoria, igual que el límite por IP: se reinicia en cada
/// despliegue, y está bien. Esto frena ráfagas de minutos, no castiga a nadie durante días
/// — para eso está el bloqueo de IPs, que sí persiste.</para>
///
/// <para>⚠️ Sin la limpieza, el diccionario crecería con cada identidad que aparezca y no
/// se vaciaría nunca: una clave de API que se usó una vez hace un mes seguiría ocupando
/// sitio. Se purgan las entradas cuya ventana ya expiró; como mucho se recrean.</para>
/// </summary>
public class IdentityRateLimiterStore : ISingletonDependency, IDisposable
{
    private sealed class Entrada
    {
        public required RateLimiter Limitador { get; init; }
        public DateTime UltimoUsoUtc { get; set; }
    }

    /// <summary>Cada cuánto se barre el diccionario.</summary>
    private static readonly TimeSpan IntervaloLimpieza = TimeSpan.FromMinutes(10);

    /// <summary>Cuánto sin usarse antes de olvidar una identidad.</summary>
    private static readonly TimeSpan Caducidad = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, Entrada> _porIdentidad = new();
    private readonly RateLimitingOptions _opciones;
    private DateTime _ultimaLimpiezaUtc = DateTime.UtcNow;

    public IdentityRateLimiterStore(IOptions<RateLimitingOptions> opciones)
    {
        _opciones = opciones.Value;
    }

    public ValueTask<RateLimitLease> AdquirirAsync(string clave, int limite)
    {
        LimpiarSiToca();

        var entrada = _porIdentidad.GetOrAdd(clave, _ => new Entrada
        {
            Limitador = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = limite,
                Window = TimeSpan.FromSeconds(_opciones.WindowSeconds),
                // Sin cola, igual que el límite por IP: esto es protección contra abuso, no
                // suavizado de picos. Poner en cola una petición ya identificada como parte
                // de una ráfaga solo retrasa el rechazo y ocupa memoria.
                QueueLimit = 0,
                AutoReplenishment = true
            }),
            UltimoUsoUtc = DateTime.UtcNow
        });

        entrada.UltimoUsoUtc = DateTime.UtcNow;

        // AttemptAcquire y no AcquireAsync: no queremos esperar a que haya hueco, queremos
        // saber ya si lo hay.
        return ValueTask.FromResult(entrada.Limitador.AttemptAcquire());
    }

    private void LimpiarSiToca()
    {
        var ahora = DateTime.UtcNow;
        if (ahora - _ultimaLimpiezaUtc < IntervaloLimpieza)
        {
            return;
        }

        _ultimaLimpiezaUtc = ahora;

        foreach (var par in _porIdentidad)
        {
            if (ahora - par.Value.UltimoUsoUtc > Caducidad &&
                _porIdentidad.TryRemove(par.Key, out var quitada))
            {
                quitada.Limitador.Dispose();
            }
        }
    }

    public void Dispose()
    {
        foreach (var par in _porIdentidad)
        {
            par.Value.Limitador.Dispose();
        }

        _porIdentidad.Clear();
        GC.SuppressFinalize(this);
    }
}
