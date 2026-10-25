using System;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.ApiKeys;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace IgasConsole;

/// <summary>
/// Claves de API contra PostgreSQL real.
/// <para>Lo que se prueba son las propiedades de las que depende que una clave sea una
/// credencial y no un adorno: no se guarda en claro, solo se enseña una vez, revocarla la
/// invalida de verdad, y no se pueden tocar las de otro.</para>
/// </summary>
public class ApiKeysIntegrationTests : IntegrationTestBase
{
    private readonly IApiKeyAppService _claves;
    private readonly IApiKeyRepository _repositorio;

    /// <summary>El mismo id que finge FakeCurrentPrincipalAccessor.</summary>
    private static readonly Guid UsuarioActual = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    public ApiKeysIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _claves = GetRequiredService<IApiKeyAppService>();
        _repositorio = GetRequiredService<IApiKeyRepository>();
    }

    [Fact]
    public async Task La_clave_NO_se_guarda_en_claro()
    {
        // LA PROPIEDAD QUE SOSTIENE TODO. Quien lea la base —o una copia de seguridad— no
        // puede sacar de ahi una clave utilizable.
        var creada = await _claves.CreateAsync(new CreateApiKeyInput { Name = "Script nocturno" });

        var guardada = await WithUnitOfWorkAsync(
            () => _repositorio.FindByHashAsync(ApiKeyAppService.Hash(creada.PlainKey)));

        guardada.ShouldNotBeNull();
        guardada!.KeyHash.ShouldNotBe(creada.PlainKey);
        guardada.KeyHash.Length.ShouldBe(ApiKeyConsts.HashLength);
        // Y la pista no basta para reconstruirla.
        guardada.Pista.Length.ShouldBe(ApiKeyConsts.PistaLength);
        creada.PlainKey.Length.ShouldBeGreaterThan(guardada.Pista.Length * 2);
    }

    [Fact]
    public async Task La_clave_se_devuelve_UNA_sola_vez()
    {
        var creada = await _claves.CreateAsync(new CreateApiKeyInput { Name = "Una vez" });
        creada.PlainKey.ShouldNotBeNullOrWhiteSpace();

        // Al listar ya no aparece por ningun lado: solo la pista.
        var lista = await _claves.GetListAsync();
        var fila = lista.ShouldHaveSingleItem();

        fila.Pista.ShouldBe(creada.PlainKey[..ApiKeyConsts.PistaLength]);
        // El DTO del listado ni siquiera tiene donde meterla, pero se comprueba que la
        // pista no es la clave.
        fila.Pista.ShouldNotBe(creada.PlainKey);
    }

    [Fact]
    public async Task La_clave_lleva_prefijo_reconocible()
    {
        var creada = await _claves.CreateAsync(new CreateApiKeyInput { Name = "Con prefijo" });

        // Para que al aparecer en un log o un repositorio se sepa QUE es lo que se filtro.
        creada.PlainKey.ShouldStartWith(ApiKeyConsts.Prefijo);
    }

    [Fact]
    public async Task Dos_claves_seguidas_son_distintas()
    {
        var a = await _claves.CreateAsync(new CreateApiKeyInput { Name = "A" });
        var b = await _claves.CreateAsync(new CreateApiKeyInput { Name = "B" });

        a.PlainKey.ShouldNotBe(b.PlainKey);
    }

    [Fact]
    public async Task Revocar_la_deja_inutilizable_pero_a_la_vista()
    {
        var creada = await _claves.CreateAsync(new CreateApiKeyInput { Name = "A revocar" });

        await _claves.RevokeAsync(creada.Key.Id);

        var fila = (await _claves.GetListAsync()).ShouldHaveSingleItem();
        fila.IsUsable.ShouldBeFalse();
        fila.RevokedUtc.ShouldNotBeNull();

        // Sigue en la tabla: saber que existio y cuando dejo de valer es parte de poder
        // auditarla.
        var guardada = await WithUnitOfWorkAsync(
            () => _repositorio.FindByHashAsync(ApiKeyAppService.Hash(creada.PlainKey)));
        guardada.ShouldNotBeNull();
        guardada!.EsUtilizable(DateTime.UtcNow).ShouldBeFalse();
    }

    [Fact]
    public async Task Revocar_dos_veces_no_mueve_la_fecha()
    {
        var creada = await _claves.CreateAsync(new CreateApiKeyInput { Name = "Doble" });

        await _claves.RevokeAsync(creada.Key.Id);
        var primera = (await _claves.GetListAsync()).Single().RevokedUtc;

        await _claves.RevokeAsync(creada.Key.Id);
        var segunda = (await _claves.GetListAsync()).Single().RevokedUtc;

        // La fecha que cuenta para reconstruir que paso es la de la PRIMERA vez.
        segunda.ShouldBe(primera);
    }

    [Fact]
    public async Task No_se_puede_revocar_la_clave_de_otro()
    {
        // Clave de otra persona, insertada directamente.
        var ajena = new ApiKey(
            Guid.NewGuid(), Guid.NewGuid(), null, "De otro",
            ApiKeyAppService.Hash("tpl_deotro"), "tpl_deotro12", null);

        await WithUnitOfWorkAsync(() => _repositorio.InsertAsync(ajena, autoSave: true));

        // Sin esta comprobacion, cualquiera con el permiso podria revocar las de otro con
        // solo adivinar un id.
        var ex = await Should.ThrowAsync<UserFriendlyException>(() => _claves.RevokeAsync(ajena.Id));
        ex.Message.ShouldContain("no es tuya");
    }

    [Fact]
    public async Task Solo_se_listan_las_propias()
    {
        await _claves.CreateAsync(new CreateApiKeyInput { Name = "Mia" });

        await WithUnitOfWorkAsync(() => _repositorio.InsertAsync(
            new ApiKey(Guid.NewGuid(), Guid.NewGuid(), null, "De otro",
                ApiKeyAppService.Hash("tpl_otra"), "tpl_otra123", null),
            autoSave: true));

        var lista = await _claves.GetListAsync();

        lista.ShouldHaveSingleItem().Name.ShouldBe("Mia");
    }

    [Fact]
    public async Task Una_clave_caducada_deja_de_servir()
    {
        // Nacida caducada, como la que se creo hace un ano por 30 dias.
        var vencida = new ApiKey(
            Guid.NewGuid(), UsuarioActual, null, "Vencida",
            ApiKeyAppService.Hash("tpl_vencida"), "tpl_vencida", DateTime.UtcNow.AddHours(-1));

        await WithUnitOfWorkAsync(() => _repositorio.InsertAsync(vencida, autoSave: true));

        vencida.EsUtilizable(DateTime.UtcNow).ShouldBeFalse();
        (await _claves.GetListAsync()).ShouldHaveSingleItem().IsUsable.ShouldBeFalse();
    }

    [Fact]
    public void La_marca_de_uso_no_se_reescribe_en_cada_llamada()
    {
        // Escribirla en cada peticion convertiria cada llamada de lectura en una escritura.
        var clave = new ApiKey(
            Guid.NewGuid(), UsuarioActual, null, "Uso",
            ApiKeyAppService.Hash("tpl_uso"), "tpl_uso12345", null);

        var ahora = DateTime.UtcNow;

        clave.AnotarUso(ahora).ShouldBeTrue();          // primera vez: si
        clave.AnotarUso(ahora.AddMinutes(1)).ShouldBeFalse(); // dentro del margen: no
        clave.AnotarUso(ahora.AddMinutes(10)).ShouldBeTrue(); // pasado el margen: si
    }
}
