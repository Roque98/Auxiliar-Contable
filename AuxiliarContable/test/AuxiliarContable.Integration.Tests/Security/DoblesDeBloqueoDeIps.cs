using System.Collections.Generic;
using System.Threading.Tasks;
using AuxiliarContable.Security;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable;

/// <summary>
/// IP del llamante, controlable desde el test. La implementación real sale de
/// <c>IWebClientInfoProvider</c> y necesita una petición HTTP viva, que aquí no hay.
/// </summary>
/// <remarks>ExposeServices obligatorio: el nombre de la clase no termina en
/// "CallerIpProvider", así que el registro por convención de ABP no expone la interfaz.</remarks>
[ExposeServices(typeof(ICallerIpProvider))]
public class CallerIpProviderDePrueba : ICallerIpProvider, ISingletonDependency
{
    public string? Ip { get; set; }

    public string? Agente { get; set; }

    public string? IpActual => Ip;

    public string? AgenteActual => Agente;
}

/// <summary>
/// Caché de IPs bloqueadas de mentira. La real vive en HttpApi.Host, que estos tests no
/// cargan; sin este doble, <c>BlockedIpAppService</c> no se puede ni construir.
/// </summary>
[ExposeServices(typeof(IBlockedIpCache))]
public class BlockedIpCacheDePrueba : IBlockedIpCache, ISingletonDependency
{
    /// <summary>Cuántas veces se pidió refrescar, por si algún test quiere comprobarlo.</summary>
    public int Refrescos { get; private set; }

    public List<string> Bloqueadas { get; } = new();

    public bool EstaBloqueada(string? ipAddress)
    {
        return ipAddress != null && Bloqueadas.Contains(ipAddress);
    }

    public Task RefrescarAsync()
    {
        Refrescos++;
        return Task.CompletedTask;
    }
}
