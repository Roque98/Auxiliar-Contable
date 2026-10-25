using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AuxiliarContable.Actividad;

/// <summary>
/// Lectura de lo que ya registra ABP, para contarlo como actividad: las llamadas a servicios
/// (<c>AbpAuditLogActions</c>, con sus parámetros) y los inicios de sesión
/// (<c>AbpSecurityLogs</c>). No escribe nada.
///
/// <para>⚠️ El registro de llamadas se purga a los 30 días (MonitoringConsts.RetentionDays):
/// lo que sale de aquí no va más atrás. Lo que vive en tablas propias (reportes subidos,
/// invitaciones) sí conserva todo su historial.</para>
/// </summary>
public interface IRegistroDeActividad
{
    /// <summary>
    /// Llamadas CORRECTAS (sin excepción ni código de error) a los métodos pedidos, de
    /// usuarios con sesión, de la más reciente a la más antigua.
    /// </summary>
    Task<List<AccionRegistrada>> AccionesAsync(
        DateTime desde,
        DateTime antesDe,
        IReadOnlyCollection<string> servicios,
        IReadOnlyCollection<string> metodos,
        IReadOnlyCollection<Guid>? usuarios,
        int maximo,
        CancellationToken cancellationToken = default);

    /// <summary>Entradas del registro de seguridad con esas acciones y un usuario conocido.</summary>
    Task<List<SesionRegistrada>> SesionesAsync(
        DateTime desde,
        DateTime antesDe,
        IReadOnlyCollection<string> acciones,
        IReadOnlyCollection<Guid>? usuarios,
        int maximo,
        CancellationToken cancellationToken = default);
}

public class AccionRegistrada
{
    public DateTime Fecha { get; set; }
    public Guid UserId { get; set; }
    public string? Ip { get; set; }
    public string Servicio { get; set; } = string.Empty;
    public string Metodo { get; set; } = string.Empty;

    /// <summary>Los parámetros en JSON, tal como los guarda ABP (sin los marcados [DisableAuditing]).</summary>
    public string? Parametros { get; set; }
}

public class SesionRegistrada
{
    public DateTime Fecha { get; set; }
    public Guid UserId { get; set; }
    public string Accion { get; set; } = string.Empty;
    public string? Ip { get; set; }
    public string? Navegador { get; set; }
}
