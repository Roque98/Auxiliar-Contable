using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Npgsql;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;

namespace IgasConsole.Data;

/// <summary>
/// Implementación (infraestructura) de <see cref="IExternalDataSourceConnectionFactory"/>.
/// Es el ÚNICO lugar autorizado a instanciar conexiones a fuentes externas
/// (ver test de arquitectura). No reimplementa la resolución de cadenas: delega
/// en <see cref="IConnectionStringResolver"/> de ABP, que respeta multi-tenancy
/// y la configuración por capas.
/// El driver se elige por <c>DataSources:{Nombre}:Provider</c>, así que una fuente
/// externa puede estar en un motor distinto al de la base principal (PostgreSQL).
/// </summary>
public class ExternalDataSourceConnectionFactory : IExternalDataSourceConnectionFactory, ITransientDependency
{
    private readonly IConnectionStringResolver _connectionStringResolver;
    private readonly ExternalDataSourceOptions _options;

    public ExternalDataSourceConnectionFactory(
        IConnectionStringResolver connectionStringResolver,
        IOptions<ExternalDataSourceOptions> options)
    {
        _connectionStringResolver = connectionStringResolver;
        _options = options.Value;
    }

    public async Task<DbConnection> CreateOpenConnectionAsync(
        string dataSourceName,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Sources.TryGetValue(dataSourceName, out var descriptor))
        {
            throw new AbpException(
                $"La fuente de datos '{dataSourceName}' no está registrada en la sección " +
                "'DataSources'. Regístrala ahí (metadatos) y en 'ConnectionStrings' (cadena).");
        }

        var connectionString = await _connectionStringResolver.ResolveAsync(dataSourceName);
        if (connectionString.IsNullOrWhiteSpace())
        {
            throw new AbpException(
                $"No hay cadena de conexión para '{dataSourceName}'. Añádela en " +
                $"'ConnectionStrings:{dataSourceName}' (user-secrets en dev, variables de entorno en prod).");
        }

        var connection = CreateConnection(dataSourceName, descriptor.Provider, connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static DbConnection CreateConnection(string dataSourceName, string provider, string connectionString)
    {
        if (string.Equals(provider, ExternalDataSourceProviders.PostgreSql, StringComparison.OrdinalIgnoreCase))
        {
            return new NpgsqlConnection(connectionString);
        }

        if (string.Equals(provider, ExternalDataSourceProviders.SqlServer, StringComparison.OrdinalIgnoreCase))
        {
            return new SqlConnection(connectionString);
        }

        throw new AbpException(
            $"Proveedor '{provider}' no soportado para la fuente '{dataSourceName}'. " +
            $"Valores válidos: '{ExternalDataSourceProviders.PostgreSql}', '{ExternalDataSourceProviders.SqlServer}'. " +
            $"Se configura en 'DataSources:{dataSourceName}:Provider'.");
    }
}
