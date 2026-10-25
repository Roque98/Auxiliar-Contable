using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AuxiliarContable.Monitoring;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Lee CPU, memoria y disco de /proc. Es SINGLETON a propósito: el porcentaje de CPU no
/// se puede leer de una muestra suelta —/proc/stat da contadores acumulados desde el
/// arranque—, hay que guardar la lectura anterior y calcular la diferencia.
/// <para>Fuera de Linux devuelve todo null en vez de lanzar: en la máquina de desarrollo
/// (Windows) no existe /proc, y que el recolector reviente ahí sería peor que no tener
/// métricas de sistema.</para>
/// </summary>
public class SystemMetricsProbe : ISystemMetricsProbe, ISingletonDependency
{
    private readonly ILogger<SystemMetricsProbe> _logger;
    private readonly object _cerrojo = new();

    private ulong _totalAnterior;
    private ulong _ociosoAnterior;

    public SystemMetricsProbe(ILogger<SystemMetricsProbe> logger)
    {
        _logger = logger;
    }

    public SystemSample Read()
    {
        var muestra = new SystemSample();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return muestra;
        }

        try
        {
            muestra.CpuPercent = LeerCpu();
            LeerMemoria(muestra);
            muestra.DiskUsedPercent = LeerDisco();
        }
        catch (Exception ex)
        {
            // Una muestra perdida no justifica tumbar el worker: se registra y se sigue.
            _logger.LogWarning(ex, "No se pudieron leer las métricas de sistema");
        }

        return muestra;
    }

    /// <summary>
    /// /proc/stat da tiempos ACUMULADOS por estado. El porcentaje es la fracción no
    /// ociosa del tiempo transcurrido desde la lectura anterior.
    /// </summary>
    private double? LeerCpu()
    {
        var linea = File.ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu "));
        if (linea == null)
        {
            return null;
        }

        var campos = linea.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        ulong total = 0;
        for (var i = 1; i < campos.Length; i++)
        {
            if (ulong.TryParse(campos[i], out var v))
            {
                total += v;
            }
        }

        // campos[4] = idle, campos[5] = iowait: la CPU tampoco trabaja esperando al disco.
        var ocioso = Parsear(campos, 4) + Parsear(campos, 5);

        lock (_cerrojo)
        {
            var totalAnterior = _totalAnterior;
            var ociosoAnterior = _ociosoAnterior;
            _totalAnterior = total;
            _ociosoAnterior = ocioso;

            // Primera lectura: no hay con qué comparar todavía.
            if (totalAnterior == 0 || total <= totalAnterior)
            {
                return null;
            }

            var deltaTotal = (double)(total - totalAnterior);
            var deltaOcioso = (double)(ocioso - ociosoAnterior);

            return Math.Round((1 - deltaOcioso / deltaTotal) * 100, 2);
        }
    }

    private static ulong Parsear(string[] campos, int indice)
    {
        return indice < campos.Length && ulong.TryParse(campos[indice], out var v) ? v : 0UL;
    }

    private static void LeerMemoria(SystemSample muestra)
    {
        var valores = new System.Collections.Generic.Dictionary<string, long>();
        foreach (var linea in File.ReadLines("/proc/meminfo"))
        {
            var partes = linea.Split(':', 2);
            if (partes.Length != 2)
            {
                continue;
            }

            // El valor viene como "  8123456 kB".
            var texto = partes[1].Trim().Split(' ')[0];
            if (long.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb))
            {
                valores[partes[0]] = kb;
            }
        }

        if (valores.TryGetValue("MemTotal", out var total))
        {
            muestra.MemoryTotalMb = (int)(total / 1024);

            // MemAvailable, no MemFree: MemFree ignora la caché reclamable y haría creer
            // que el servidor está sin memoria cuando en realidad está sana.
            if (valores.TryGetValue("MemAvailable", out var disponible))
            {
                muestra.MemoryUsedMb = (int)((total - disponible) / 1024);
            }
        }

        if (valores.TryGetValue("SwapTotal", out var swapTotal) &&
            valores.TryGetValue("SwapFree", out var swapLibre))
        {
            muestra.SwapUsedMb = (int)((swapTotal - swapLibre) / 1024);
            muestra.SwapTotalMb = (int)(swapTotal / 1024);
        }
    }

    private static double? LeerDisco()
    {
        var raiz = new DriveInfo("/");
        if (!raiz.IsReady || raiz.TotalSize == 0)
        {
            return null;
        }

        var usado = raiz.TotalSize - raiz.AvailableFreeSpace;
        return Math.Round((double)usado / raiz.TotalSize * 100, 2);
    }
}
