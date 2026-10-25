using System;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Organizaciones;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace IgasConsole;

/// <summary>
/// Organizaciones (los clientes del despacho) y QUIÉN VE QUÉ, contra PostgreSQL real.
///
/// <para>El Usuario hace de cliente (sin el permiso de Organizaciones, ver
/// <c>AlcanceSegunRol</c>); Owner y Administrador son el equipo del despacho.</para>
/// </summary>
public class OrganizacionIntegrationTests : ConJerarquiaTestBase
{
    private readonly IOrganizacionAppService _organizaciones;
    private readonly IMiOrganizacionAppService _miOrganizacion;

    public OrganizacionIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _organizaciones = GetRequiredService<IOrganizacionAppService>();
        _miOrganizacion = GetRequiredService<IMiOrganizacionAppService>();
    }

    private Task<OrganizacionDetalleDto> CrearAsync(string nombre, string? descripcion = null)
    {
        return ComoAsync(Owner, () => _organizaciones.CreateAsync(new GuardarOrganizacionDto
        {
            Nombre = nombre,
            Descripcion = descripcion,
        }));
    }

    private async Task<string[]> OrganizacionesQueVeAsync(IdentityUser como) =>
        (await ComoAsync(como, () => _miOrganizacion.GetListAsync())).Select(o => o.Nombre).OrderBy(x => x).ToArray();

    [Fact]
    public async Task Un_cliente_ve_solo_las_organizaciones_de_las_que_es_miembro()
    {
        var norte = await CrearAsync("Norte");
        await CrearAsync("Sur");

        (await OrganizacionesQueVeAsync(Usuario)).ShouldBeEmpty();

        await ComoAsync(Owner, () => _organizaciones.AgregarMiembroAsync(norte.Id, new AgregarMiembroDto { UserId = Usuario.Id }));
        (await OrganizacionesQueVeAsync(Usuario)).ShouldBe(["Norte"]);
    }

    [Fact]
    public void Un_cliente_no_gestiona_organizaciones()
    {
        // ⚠️ En los tests la autorización está en «todo permitido» (AddAlwaysAllowAuthorization):
        // llamar al servicio como Usuario NO lanzaría nada. Lo que deja fuera a un cliente es que
        // el servicio exige el permiso y su rol nace sin él; eso es lo que se comprueba.
        typeof(OrganizacionAppService)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Select(a => a.Policy)
            .ShouldContain(Permissions.IgasConsolePermissions.Organizaciones.Default);

        Identity.PermisosInicialesPorRol.Mapa[Identity.IgasRoles.Usuario]
            .ShouldNotContain(Permissions.IgasConsolePermissions.Organizaciones.Default);
    }

    [Fact]
    public async Task El_equipo_ve_y_edita_a_todos_los_clientes()
    {
        var norte = await CrearAsync("Norte");
        await CrearAsync("Sur");

        (await ComoAsync(Administrador, () => _organizaciones.GetListAsync(new GetOrganizacionesInput())))
            .Items.Select(o => o.Nombre).ShouldBe(["Norte", "Sur"]);

        var editada = await ComoAsync(Administrador, () => _organizaciones.UpdateAsync(norte.Id,
            new GuardarOrganizacionDto { Nombre = "Norte SA de CV", Descripcion = "Cliente desde 2020" }));
        editada.Nombre.ShouldBe("Norte SA de CV");
        editada.Descripcion.ShouldBe("Cliente desde 2020");
    }

    [Fact]
    public async Task El_filtro_busca_en_nombre_y_descripcion()
    {
        await CrearAsync("Norte", "Restaurantes");
        await CrearAsync("Sur", "Ferretería");

        (await ComoAsync(Owner, () => _organizaciones.GetListAsync(new GetOrganizacionesInput { Filtro = "ferre" })))
            .Items.ShouldHaveSingleItem().Nombre.ShouldBe("Sur");
        (await ComoAsync(Owner, () => _organizaciones.GetListAsync(new GetOrganizacionesInput { Filtro = "NOR" })))
            .Items.ShouldHaveSingleItem().Nombre.ShouldBe("Norte");
    }

    [Fact]
    public async Task Quitar_a_alguien_o_borrar_la_organizacion_le_quita_el_acceso()
    {
        var grupo = await CrearAsync("Norte");
        await ComoAsync(Owner, () => _organizaciones.AgregarMiembroAsync(grupo.Id, new AgregarMiembroDto { UserId = Usuario.Id }));
        (await OrganizacionesQueVeAsync(Usuario)).ShouldBe(["Norte"]);

        await ComoAsync(Administrador, () => _organizaciones.QuitarMiembroAsync(grupo.Id, Usuario.Id));
        (await OrganizacionesQueVeAsync(Usuario)).ShouldBeEmpty();

        await ComoAsync(Owner, () => _organizaciones.AgregarMiembroAsync(grupo.Id, new AgregarMiembroDto { UserId = Usuario.Id }));
        await ComoAsync(Owner, () => _organizaciones.DeleteAsync(grupo.Id));
        (await OrganizacionesQueVeAsync(Usuario)).ShouldBeEmpty();
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Owner, () => _organizaciones.GetAsync(grupo.Id)));
    }

    [Fact]
    public async Task El_nombre_no_se_repite_entre_las_vivas()
    {
        var norte = await CrearAsync("Grupo Norte");

        await Should.ThrowAsync<UserFriendlyException>(() => CrearAsync("grupo norte"));

        await ComoAsync(Owner, () => _organizaciones.DeleteAsync(norte.Id));
        (await CrearAsync("Grupo Norte")).Nombre.ShouldBe("Grupo Norte");
    }

    [Fact]
    public async Task Solo_se_asigna_a_quien_uno_alcanza()
    {
        var grupo = await CrearAsync("Norte");

        // El Owner no alcanza al Developer; el Administrador no alcanza al Owner.
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(Owner, () => _organizaciones.AgregarMiembroAsync(grupo.Id, new AgregarMiembroDto { UserId = Developer.Id })));
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(Administrador, () => _organizaciones.AgregarMiembroAsync(grupo.Id, new AgregarMiembroDto { UserId = Owner.Id })));

        var candidatos = await ComoAsync(Owner, () => _organizaciones.GetCandidatosAsync(grupo.Id, null));
        candidatos.Select(c => c.UserName).ShouldBe(["carlos", "laura", "tio"], ignoreOrder: true);

        var delAdministrador = await ComoAsync(Administrador, () => _organizaciones.GetCandidatosAsync(grupo.Id, null));
        delAdministrador.Select(c => c.UserName).ShouldBe(["carlos", "laura"], ignoreOrder: true);
    }

    [Fact]
    public async Task Si_el_Developer_es_miembro_el_Owner_no_lo_ve()
    {
        var grupo = await CrearAsync("Norte");
        await ComoAsync(Developer, () => _organizaciones.AgregarMiembroAsync(grupo.Id, new AgregarMiembroDto { UserId = Developer.Id }));
        await ComoAsync(Owner, () => _organizaciones.AgregarMiembroAsync(grupo.Id, new AgregarMiembroDto { UserId = Usuario.Id }));

        var visto = await ComoAsync(Owner, () => _organizaciones.GetAsync(grupo.Id));
        visto.ListaDeMiembros.Select(m => m.UserName).ShouldBe(["carlos"]);
        visto.Miembros.ShouldBe(1);
        (await ComoAsync(Owner, () => _organizaciones.GetListAsync(new GetOrganizacionesInput()))).Items.Single().Miembros.ShouldBe(1);

        (await ComoAsync(Developer, () => _organizaciones.GetAsync(grupo.Id))).Miembros.ShouldBe(2);
    }
}
