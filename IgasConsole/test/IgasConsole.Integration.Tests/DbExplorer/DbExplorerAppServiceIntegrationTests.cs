using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.DbExplorer;
using IgasConsole.Organizaciones;
using Volo.Abp.Domain.Repositories;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace IgasConsole;

/// <summary>
/// Tests de integración del explorador de BD contra PostgreSQL real (local, sin Docker).
/// Cubren lectura, CRUD en tablas de la app, solo-lectura en tablas de sistema y
/// la protección anti-inyección (identificadores validados contra el esquema).
/// </summary>
public class DbExplorerAppServiceIntegrationTests : IntegrationTestBase
{
    private readonly IDbExplorerAppService _dbExplorer;
    private readonly IRepository<Organizacion, Guid> _organizaciones;

    public DbExplorerAppServiceIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _dbExplorer = GetRequiredService<IDbExplorerAppService>();
        _organizaciones = GetRequiredService<IRepository<Organizacion, Guid>>();
    }

    [Fact]
    public async Task GetTables_marca_AppOrganizaciones_editable_y_AbpUsers_solo_lectura()
    {
        var tables = await _dbExplorer.GetTablesAsync();

        tables.ShouldContain(t => t.Name == "AppOrganizaciones" && t.IsEditable);
        tables.ShouldContain(t => t.Name == "AbpUsers" && !t.IsEditable);
    }

    [Fact]
    public async Task GetRows_devuelve_las_filas_de_AppOrganizaciones()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await _organizaciones.InsertAsync(Nuevo("ventas"), autoSave: true);
            await _organizaciones.InsertAsync(Nuevo("inventario"), autoSave: true);
        });

        var result = await _dbExplorer.GetRowsAsync(new GetRowsInput { Table = "AppOrganizaciones", MaxResultCount = 10 });

        result.TotalCount.ShouldBe(2);
        result.Rows.Count.ShouldBe(2);
        result.Columns.ShouldContain(c => c.Name == "Nombre");
        result.Columns.ShouldContain(c => c.Name == "IsDeleted");
    }

    [Fact]
    public async Task Insert_Update_Delete_en_AppOrganizaciones_funciona()
    {
        var id = Guid.NewGuid();

        // INSERT
        await _dbExplorer.InsertAsync(new InsertRowInput
        {
            Table = "AppOrganizaciones",
            Values = new Dictionary<string, object?>
            {
                ["Id"] = id,
                ["Nombre"] = "Insertada",
                ["Descripcion"] = "desde el explorador",
                ["ExtraProperties"] = "{}",
                ["ConcurrencyStamp"] = Guid.NewGuid().ToString("N"),
                ["CreationTime"] = DateTime.Now,
                ["IsDeleted"] = false
            }
        });

        var afterInsert = await _dbExplorer.GetRowsAsync(new GetRowsInput { Table = "AppOrganizaciones", MaxResultCount = 10 });
        afterInsert.TotalCount.ShouldBe(1);

        // UPDATE
        await _dbExplorer.UpdateAsync(new UpdateRowInput
        {
            Table = "AppOrganizaciones",
            Key = new Dictionary<string, object?> { ["Id"] = id },
            Values = new Dictionary<string, object?> { ["Descripcion"] = "editada" }
        });

        var afterUpdate = await _dbExplorer.GetRowsAsync(new GetRowsInput { Table = "AppOrganizaciones", MaxResultCount = 10 });
        afterUpdate.Rows.Single()["Descripcion"]!.ToString().ShouldBe("editada");

        // DELETE
        await _dbExplorer.DeleteAsync(new DeleteRowInput
        {
            Table = "AppOrganizaciones",
            Key = new Dictionary<string, object?> { ["Id"] = id }
        });

        var afterDelete = await _dbExplorer.GetRowsAsync(new GetRowsInput { Table = "AppOrganizaciones", MaxResultCount = 10 });
        afterDelete.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Editar_una_tabla_de_sistema_lanza_por_ser_solo_lectura()
    {
        await Should.ThrowAsync<UserFriendlyException>(async () =>
            await _dbExplorer.DeleteAsync(new DeleteRowInput
            {
                Table = "AbpUsers",
                Key = new Dictionary<string, object?> { ["Id"] = Guid.NewGuid() }
            }));
    }

    [Fact]
    public async Task Tabla_inexistente_lanza_error_controlado()
    {
        // Nombre malicioso: se pasa como VALOR a INFORMATION_SCHEMA, no se ejecuta.
        await Should.ThrowAsync<UserFriendlyException>(async () =>
            await _dbExplorer.GetSchemaAsync(DbExplorerConsts.DefaultSchema, "NoExiste; DROP TABLE AppOrganizaciones;--"));
    }

    [Fact]
    public async Task Columna_de_orden_invalida_lanza_error_controlado()
    {
        await Should.ThrowAsync<AbpException>(async () =>
            await _dbExplorer.GetRowsAsync(new GetRowsInput
            {
                Table = "AppOrganizaciones",
                SortColumn = "1; DROP TABLE AppOrganizaciones;--"
            }));
    }

    private static Organizacion Nuevo(string nombre)
    {
        return new Organizacion(Guid.NewGuid(), nombre, null);
    }
}
