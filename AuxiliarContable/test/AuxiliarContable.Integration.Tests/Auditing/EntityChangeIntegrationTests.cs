using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Auditing;
using Shouldly;
using Volo.Abp.Auditing;
using Volo.Abp.AuditLogging;
using Volo.Abp.Guids;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Historial de cambios de entidades contra PostgreSQL real.
///
/// <para>La escritura la hace la auditoría de ABP dentro de una petición HTTP, algo que
/// estos tests no montan. Lo que se prueba aquí es la LECTURA, que es donde está el trabajo
/// propio: cruzar el cambio con su registro de auditoría para saber quién lo hizo, traer las
/// propiedades sin caer en N+1, y que el total siga al filtro.</para>
/// </summary>
public class EntityChangeIntegrationTests : IntegrationTestBase
{
    private readonly IEntityChangeAppService _historial;
    private readonly IAuditingStore _almacen;

    public EntityChangeIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _historial = GetRequiredService<IEntityChangeAppService>();
        _almacen = GetRequiredService<IAuditingStore>();
    }

    /// <summary>
    /// Un cambio va SIEMPRE colgado de un registro de auditoría: ahí es donde vive el
    /// usuario. Por eso se siembran juntos, como los escribe ABP.
    /// </summary>
    private Task SembrarAsync(
        string tipo,
        string entityId,
        EntityChangeType tipoCambio,
        string usuario,
        (string Campo, string? Antes, string? Despues)[] propiedades,
        int haceDias = 0)
    {
        var cuando = DateTime.UtcNow.AddDays(-haceDias);

        // Se escribe por IAuditingStore, que es exactamente por donde pasa ABP en
        // produccion: crea el registro de auditoria, el cambio y sus propiedades de una vez
        // y con las mismas relaciones. Montarlo a mano con los constructores de las
        // entidades daria una fidelidad menor y ataria el test a esas firmas.
        var info = new AuditLogInfo
        {
            ApplicationName = "Test",
            ExecutionTime = cuando,
            UserName = usuario,
            ClientIpAddress = "10.0.0.9",
        };

        info.EntityChanges.Add(new EntityChangeInfo
        {
            ChangeTime = cuando,
            ChangeType = tipoCambio,
            EntityId = entityId,
            EntityTypeFullName = tipo,
            PropertyChanges = propiedades.Select(p => new EntityPropertyChangeInfo
            {
                PropertyName = p.Campo,
                OriginalValue = p.Antes,
                NewValue = p.Despues,
                PropertyTypeFullName = typeof(string).FullName!
            }).ToList()
        });

        return WithUnitOfWorkAsync(() => _almacen.SaveAsync(info));
    }

    [Fact]
    public async Task Un_cambio_trae_QUIEN_lo_hizo_desde_su_registro_de_auditoria()
    {
        // EL TEST QUE IMPORTA. El usuario NO esta en EntityChange: vive en el AuditLog
        // padre. Si el cruce fallara, el historial diria QUE cambio pero no QUIEN — que es
        // justo la mitad por la que existe esta pantalla.
        await SembrarAsync(
            "AuxiliarContable.Organizaciones.Organizacion", "p1", EntityChangeType.Updated, "ana",
            [("Price", "10", "20")]);

        var lista = await _historial.GetListAsync(new GetEntityChangesInput { MaxResultCount = 50 });

        var fila = lista.Items.ShouldHaveSingleItem();
        fila.UserName.ShouldBe("ana");
        fila.ClientIpAddress.ShouldBe("10.0.0.9");
    }

    [Fact]
    public async Task Trae_los_campos_con_su_valor_anterior_y_el_nuevo()
    {
        await SembrarAsync(
            "AuxiliarContable.Organizaciones.Organizacion", "p1", EntityChangeType.Updated, "ana",
            [("Price", "10", "20"), ("Name", "Viejo", "Nuevo")]);

        var fila = (await _historial.GetListAsync(
            new GetEntityChangesInput { MaxResultCount = 50 })).Items.Single();

        fila.Properties.Count.ShouldBe(2);
        var precio = fila.Properties.Single(p => p.PropertyName == "Price");
        precio.OriginalValue.ShouldBe("10");
        precio.NewValue.ShouldBe("20");
    }

    [Fact]
    public async Task El_nombre_que_se_enseña_es_corto_pero_el_completo_sigue_ahi()
    {
        // El nombre completo hace falta para filtrar; en pantalla solo estorba.
        await SembrarAsync(
            "AuxiliarContable.Organizaciones.Organizacion", "p1", EntityChangeType.Created, "ana",
            [("Name", null, "Teclado")]);

        var fila = (await _historial.GetListAsync(
            new GetEntityChangesInput { MaxResultCount = 50 })).Items.Single();

        fila.EntityName.ShouldBe("Organizacion");
        fila.EntityTypeFullName.ShouldBe("AuxiliarContable.Organizaciones.Organizacion");
    }

    [Fact]
    public async Task El_filtro_por_tipo_acota_las_filas_Y_EL_TOTAL()
    {
        await SembrarAsync("AuxiliarContable.Organizaciones.Organizacion", "p1", EntityChangeType.Created, "ana", []);
        await SembrarAsync("AuxiliarContable.Organizaciones.Organizacion", "p2", EntityChangeType.Created, "ana", []);
        await SembrarAsync("AuxiliarContable.Security.BlockedIp", "b1", EntityChangeType.Created, "ana", []);

        var soloIps = await _historial.GetListAsync(new GetEntityChangesInput
        {
            EntityTypeFullName = "AuxiliarContable.Security.BlockedIp",
            MaxResultCount = 50
        });

        soloIps.Items.Count.ShouldBe(1);
        // Si el total no siguiera al filtro, la paginacion mentiria sin dar ningun error.
        soloIps.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task El_filtro_por_id_encuentra_la_historia_de_UNA_fila()
    {
        // Es como se llega aqui: pegando el id de un registro concreto para ver que le ha
        // pasado a lo largo del tiempo.
        await SembrarAsync("AuxiliarContable.Organizaciones.Organizacion", "p1", EntityChangeType.Created, "ana", []);
        await SembrarAsync("AuxiliarContable.Organizaciones.Organizacion", "p1", EntityChangeType.Updated, "beto", []);
        await SembrarAsync("AuxiliarContable.Organizaciones.Organizacion", "p2", EntityChangeType.Created, "ana", []);

        var historia = await _historial.GetListAsync(
            new GetEntityChangesInput { EntityId = "p1", MaxResultCount = 50 });

        historia.Items.Count.ShouldBe(2);
        historia.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task El_filtro_por_usuario_es_parcial_y_no_distingue_mayusculas()
    {
        // Aqui se busca a una persona, no una clave: escribir "ANA" tiene que encontrar a ana.
        await SembrarAsync("AuxiliarContable.Organizaciones.Organizacion", "p1", EntityChangeType.Created, "ana", []);
        await SembrarAsync("AuxiliarContable.Organizaciones.Organizacion", "p2", EntityChangeType.Created, "beto", []);

        var deAna = await _historial.GetListAsync(
            new GetEntityChangesInput { UserName = "ANA", MaxResultCount = 50 });

        deAna.Items.ShouldHaveSingleItem().UserName.ShouldBe("ana");
        deAna.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Lo_anterior_a_la_ventana_queda_fuera()
    {
        await SembrarAsync("AuxiliarContable.Organizaciones.Organizacion", "viejo", EntityChangeType.Created,
            "ana", [], haceDias: 200);
        await SembrarAsync("AuxiliarContable.Organizaciones.Organizacion", "nuevo", EntityChangeType.Created,
            "ana", []);

        var mes = await _historial.GetListAsync(
            new GetEntityChangesInput { Days = 30, MaxResultCount = 50 });

        mes.Items.ShouldHaveSingleItem().EntityId.ShouldBe("nuevo");
    }

    [Fact]
    public async Task Los_tipos_del_desplegable_salen_de_los_datos_sin_repetirse()
    {
        await SembrarAsync("AuxiliarContable.Organizaciones.Organizacion", "p1", EntityChangeType.Created, "ana", []);
        await SembrarAsync("AuxiliarContable.Organizaciones.Organizacion", "p2", EntityChangeType.Created, "ana", []);
        await SembrarAsync("AuxiliarContable.Security.BlockedIp", "b1", EntityChangeType.Created, "ana", []);

        var filtros = await _historial.GetFiltersAsync();

        filtros.EntityTypes.ShouldBe(
            ["AuxiliarContable.Organizaciones.Organizacion", "AuxiliarContable.Security.BlockedIp"]);
    }
}
