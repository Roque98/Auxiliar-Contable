using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp.Auditing;

namespace AuxiliarContable.Observabilidad;

/// <summary>
/// Los errores de JavaScript de la web y de la app, al log estructurado. Solo con sesión (sin ella
/// cualquiera podría escribir en nuestros logs) y fuera de la auditoría: un error no es actividad
/// de la persona, y sus textos no deben acabar en AbpAuditLogActions.
/// </summary>
[Authorize]
[DisableAuditing]
public class ErroresDelClienteAppService : AuxiliarContableAppService, IErroresDelClienteAppService
{
    /// <summary>Envíos por persona en el minuto en curso. Vive en memoria: tras reiniciar, empieza de cero.</summary>
    private static readonly ConcurrentDictionary<Guid, (long Minuto, int Envios)> Cuentas = new();

    public virtual Task RegistrarAsync(ErrorDelClienteDto input)
    {
        var usuario = CurrentUser.Id ?? Guid.Empty;
        var minuto = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;
        var cuenta = Cuentas.AddOrUpdate(usuario, _ => (minuto, 1), (_, c) => c.Minuto == minuto ? (minuto, c.Envios + 1) : (minuto, 1));
        if (cuenta.Envios > ErroresDelClienteConsts.MaxPorMinuto)
        {
            return Task.CompletedTask;
        }

        if (Cuentas.Count > 10_000)
        {
            // Nunca debería pasar con el número de usuarios de la consola; por si acaso, no crece sin techo.
            Cuentas.Clear();
        }

        Logger.LogWarning(
            "Error en el cliente ({Origen}): {Mensaje} | pantalla {Ruta} | versión {VersionDelCliente} | {Agente} | usuario {UsuarioId}\n{Pila}",
            input.Origen == "app" ? "app" : "web",
            Recortar(input.Mensaje, ErroresDelClienteConsts.MaxMensaje),
            Recortar(input.Ruta, 300),
            Recortar(input.Version, 60),
            Recortar(input.Agente, 300),
            usuario,
            Recortar(input.Pila, ErroresDelClienteConsts.MaxPila));
        return Task.CompletedTask;
    }

    private static string Recortar(string? texto, int max)
    {
        var t = (texto ?? "").Replace("\r", "");
        return t.Length > max ? t[..max] : t;
    }
}
