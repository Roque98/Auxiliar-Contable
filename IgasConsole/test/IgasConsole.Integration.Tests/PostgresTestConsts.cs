using System;
using Npgsql;

namespace IgasConsole;

/// <summary>
/// Cadenas de conexión de los tests de integración, contra un **PostgreSQL local**
/// (sin Docker). Las bases son dedicadas y se crean/limpian en cada corrida.
///
/// Las credenciales NO viven en el repo (regla: nada de secretos versionados, y
/// gitleaks lo verifica): se aportan en la variable de entorno
/// <see cref="ConnectionEnvironmentVariable"/> con una cadena SIN base de datos, p. ej.
/// <c>Host=localhost;Port=5432;Username=igas_console;Password=...</c>
/// El nombre de la base lo pone cada propiedad de esta clase.
/// </summary>
public static class PostgresTestConsts
{
    public const string ConnectionEnvironmentVariable = "IGASCONSOLE_TEST_POSTGRES";

    public const string MainDatabaseName = "igas_console_integrationtests";

    /// <summary>Base de mantenimiento; solo para crear/eliminar las otras.</summary>
    public const string MaintenanceDatabaseName = "postgres";

    public static string MainConnectionString => BuildConnectionString(MainDatabaseName);

    public static string MaintenanceConnectionString => BuildConnectionString(MaintenanceDatabaseName);

    private static string BuildConnectionString(string databaseName)
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                $"Falta la variable de entorno '{ConnectionEnvironmentVariable}'. Los tests de " +
                "integración necesitan un PostgreSQL local y sus credenciales no se versionan. " +
                "Define, por ejemplo: " +
                $"setx {ConnectionEnvironmentVariable} \"Host=localhost;Port=5432;Username=igas_console;Password=TU_PASSWORD\" " +
                "(el rol necesita permiso CREATEDB). Ver .env.example.");
        }

        return new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName
        }.ConnectionString;
    }
}
