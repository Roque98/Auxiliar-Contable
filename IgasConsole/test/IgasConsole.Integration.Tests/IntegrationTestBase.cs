using System;
using System.Threading.Tasks;
using Volo.Abp.Features;
using Volo.Abp.FeatureManagement;
using Xunit;

namespace IgasConsole;

/// <summary>
/// Clase base reutilizable para tests de integración contra PostgreSQL.
/// Cada test arranca con la base principal LIMPIA (Respawn) y una app ABP real.
/// </summary>
[Collection(PostgresCollection.Name)]
public abstract class IntegrationTestBase : IgasConsoleTestBase<IgasConsolePostgreSqlTestModule>, IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    protected IntegrationTestBase(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public virtual Task InitializeAsync() => _fixture.ResetMainDatabaseAsync();

    /// <summary>
    /// Se limpia TAMBIÉN al terminar, no solo al empezar. Parece redundante y no lo es.
    ///
    /// <para>⚠️ xUnit construye la clase de test —y con ella la aplicación ABP entera—
    /// <b>antes</b> de llamar a <see cref="InitializeAsync"/>. O sea que la aplicación
    /// arranca viendo los datos que dejó el test ANTERIOR, y lo que lea durante el arranque
    /// se le queda cacheado; el <c>Respawn</c> de un instante después borra las filas pero
    /// no la caché. Con datos normales da igual, porque cada test escribe los suyos; con los
    /// AJUSTES no, porque ABP los cachea en bloque
    /// (<c>SettingManagementStore</c> sobre <c>IDistributedCache</c>) y un ajuste
    /// «heredado» de otro test se cuela sin que nada falle.</para>
    ///
    /// <para>Limpiando también al final, la aplicación del test siguiente siempre nace
    /// sobre una base vacía y no hay nada viejo que cachear. Cuesta unos milisegundos.</para>
    /// </summary>
    public virtual Task DisposeAsync() => _fixture.ResetMainDatabaseAsync();
}
