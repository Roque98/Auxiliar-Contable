using System.Threading.Tasks;
using IgasConsole.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Xunit;

namespace IgasConsole;

/// <summary>
/// Fixture reutilizable (una sola vez por corrida) que:
/// 1. Recrea el esquema de la base principal de test en PostgreSQL.
/// 2. Crea y siembra la base externa de reportes (ejemplo Dapper).
/// 3. Prepara un <see cref="Respawner"/> para limpiar los datos entre tests
///    de forma rápida (sin recrear el esquema, sin Docker).
/// Agregar un test nuevo es trivial: heredar de <see cref="IntegrationTestBase"/>.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private Respawner? _respawner;

    public async Task InitializeAsync()
    {
        await RecreateMainDatabaseSchemaAsync();

        await using var connection = new NpgsqlConnection(PostgresTestConsts.MainConnectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            // En PostgreSQL hay que acotar el barrido: sin esto Respawn miraría también
            // los catálogos del sistema.
            SchemasToInclude = new[] { "public" },
            TablesToIgnore = new Table[] { "__EFMigrationsHistory" }
        });
    }

    /// <summary>Limpia todos los datos de la base principal antes de cada test.</summary>
    public async Task ResetMainDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(PostgresTestConsts.MainConnectionString);
        await connection.OpenAsync();
        await _respawner!.ResetAsync(connection);
    }

    private static async Task RecreateMainDatabaseSchemaAsync()
    {
        // ⚠️⚠️ ESTO NO ES OPCIONAL, y cuesta un rato entender por qué.
        //
        // El esquema de los tests se crea con un `IgasConsoleDbContext` construido A MANO, fuera
        // de la aplicación ABP. Eso significa que **nada de lo que configuran los módulos al
        // arrancar está puesto** cuando EF construye el modelo — y entre esas cosas están las
        // PROPIEDADES EXTENDIDAS mapeadas a columna con `MapEfCoreProperty`
        // (`TipoDeUsuarioId` en `AbpUsers`, ver `IgasConsoleEfCoreEntityExtensionMappings`).
        //
        // Sin esta llamada, `EnsureCreated` crea la tabla SIN esa columna y la aplicación de
        // los tests revienta al arrancar con «42703: no existe la columna a.TipoDeUsuarioId».
        // El error no menciona ni los tests ni las extensiones, y como pasa en el constructor
        // de la clase de test, TODOS los tests del ensamblado fallan a la vez en 1 ms — lo que
        // parece un problema de base de datos y no de modelo.
        //
        // `Configure()` lleva un `OneTimeRunner` dentro, así que llamarlo aquí es inocuo
        // aunque la aplicación lo llame después.
        IgasConsoleEfCoreEntityExtensionMappings.Configure();

        await TirarLaBaseSiQuedoInvalidaAsync();

        var options = new DbContextOptionsBuilder<IgasConsoleDbContext>()
            .UseNpgsql(PostgresTestConsts.MainConnectionString)
            .Options;

        await using var dbContext = new IgasConsoleDbContext(options);
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
    }

    /// <summary>
    /// ⚠️⚠️ Una corrida interrumpida a mitad de un <c>DROP DATABASE</c> deja la base
    /// <b>marcada como inválida</b> en PostgreSQL (<c>pg_database.datconnlimit = -2</c>), y a
    /// partir de ahí nadie puede volver a conectarse a ella — ni siquiera para comprobar si
    /// existe.
    ///
    /// <para>Lo caro no es el fallo: es <b>cómo se presenta</b>. <c>EnsureDeletedAsync</c>
    /// intenta conectarse, se queda esperando y acaba lanzando un
    /// <c>TimeoutException: Timeout during reading attempt</c> envuelto en «likely due to a
    /// transient failure», con una pila llena de Npgsql y ni una palabra de la causa. Se lee
    /// como una base caída o como contención, y como los tests que se llevan por delante son
    /// los que tocaron esa base, se lee <b>como una regresión del código que se acaba de
    /// escribir</b>. Costó media hora perseguir eso; el mensaje de verdad
    /// (<c>55000: no se puede conectar a la base de datos no válida</c>) solo aparece si se
    /// mira el error de un test concreto en vez del resumen.</para>
    ///
    /// <para>Se arregla solo: <c>DROP DATABASE</c> <b>sí</b> funciona sobre una base
    /// inválida —es justo para lo que existe ese estado—, así que basta con mirar el catálogo
    /// y tirarla antes de empezar. La consulta va contra la base de mantenimiento, que es la
    /// única a la que se puede entrar cuando la otra está en ese estado.</para>
    ///
    /// <para>⚠️ <b>Verificado a medias, y conviene saberlo.</b> Las dos mitades se
    /// observaron por separado —la base apareció de verdad con <c>datconnlimit = -2</c> y el
    /// <c>DROP DATABASE</c> sobre ella funcionó—, pero el camino completo a través de este
    /// método no se ha podido ejecutar: marcar una base como inválida a mano exige
    /// superusuario, y la forma natural de provocarlo es matar un <c>DROP</c> a mitad. Lo que
    /// sí está comprobado es que con la base sana devuelve <c>-1</c>, no hace nada y la suite
    /// pasa igual.</para>
    /// </summary>
    private static async Task TirarLaBaseSiQuedoInvalidaAsync()
    {
        await using var mantenimiento = new NpgsqlConnection(PostgresTestConsts.MaintenanceConnectionString);
        await mantenimiento.OpenAsync();

        await using var consulta = new NpgsqlCommand(
            "SELECT datconnlimit FROM pg_database WHERE datname = @n;", mantenimiento);
        consulta.Parameters.AddWithValue("n", PostgresTestConsts.MainDatabaseName);

        // -2 es el valor que PostgreSQL usa para «invalidada»; no es un límite de conexiones.
        if (await consulta.ExecuteScalarAsync() is int limite && limite == -2)
        {
            // El nombre es una constante de compilación, no entrada de usuario: no hay
            // superficie de inyección (mismo criterio que el CREATE DATABASE de más abajo).
            await ExecuteAsync(mantenimiento, $"DROP DATABASE {PostgresTestConsts.MainDatabaseName};");
        }
    }

    private static async Task<bool> ExistsDatabaseAsync(NpgsqlConnection connection, string databaseName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name;";
        command.Parameters.AddWithValue("@name", databaseName);
        return await command.ExecuteScalarAsync() != null;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL integration tests";
}
