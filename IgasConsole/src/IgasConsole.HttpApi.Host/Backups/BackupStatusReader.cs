using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IgasConsole.Backups;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace IgasConsole.HealthChecks;

/// <summary>
/// Lee los archivos de estado que dejan los scripts de copia.
///
/// <para>Vive en el Host porque toca el sistema de archivos de la máquina, igual que la
/// sonda de métricas. El dominio solo conoce la interfaz.</para>
/// </summary>
public class BackupStatusReader : IBackupStatusReader, ITransientDependency
{
    private static readonly JsonSerializerOptions Opciones = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly BackupOptions _rutas;
    private readonly ILogger<BackupStatusReader> _logger;

    public BackupStatusReader(IOptions<BackupOptions> rutas, ILogger<BackupStatusReader> logger)
    {
        _rutas = rutas.Value;
        _logger = logger;
    }

    public Task<BackupStatus?> LeerCopiaAsync(CancellationToken cancellationToken = default)
        => LeerAsync<BackupStatus>(_rutas.StatusFilePath, cancellationToken);

    public Task<RestoreCheckStatus?> LeerVerificacionAsync(
        CancellationToken cancellationToken = default)
        => LeerAsync<RestoreCheckStatus>(_rutas.RestoreCheckFilePath, cancellationToken);

    private async Task<T?> LeerAsync<T>(string ruta, CancellationToken ct) where T : class
    {
        // Ausente = no hay información, que NO es lo mismo que "falló". En desarrollo es
        // lo normal, y confundir ambas cosas llenaría la campana de avisos falsos.
        if (string.IsNullOrWhiteSpace(ruta) || !File.Exists(ruta))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(ruta);
            return await JsonSerializer.DeserializeAsync<T>(stream, Opciones, ct);
        }
        catch (Exception ex)
        {
            // Un archivo ilegible o a medio escribir no puede tumbar ni el health check ni
            // el vigilante. El script lo escribe con un mv atómico justo para que esto no
            // pase, pero si pasara, se trata como "sin información".
            _logger.LogWarning(ex, "No se pudo leer el estado de copias en {Ruta}", ruta);
            return null;
        }
    }
}
