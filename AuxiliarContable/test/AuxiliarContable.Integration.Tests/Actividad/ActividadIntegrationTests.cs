using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Actividad;
using AuxiliarContable.Organizaciones;
using Shouldly;
using Volo.Abp.Auditing;
using Volo.Abp.SecurityLog;
using Xunit;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace AuxiliarContable;

/// <summary>
/// La pantalla de Actividad: lo que ABP ya registra, contado en frases, y sin el Developer.
/// Las llamadas se siembran por IAuditingStore y los inicios de sesión por ISecurityLogStore,
/// que es por donde pasan en producción.
/// </summary>
public class ActividadIntegrationTests : ConJerarquiaTestBase
{
    private readonly IActividadAppService _actividad;

    public ActividadIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _actividad = GetRequiredService<IActividadAppService>();
    }

    private static readonly Type Organizaciones = typeof(OrganizacionAppService);

    private Task<OrganizacionDetalleDto> CrearAsync(string nombre) =>
        ComoAsync(Owner, () => GetRequiredService<IOrganizacionAppService>().CreateAsync(new GuardarOrganizacionDto { Nombre = nombre }));

    /// <summary>Una llamada correcta a un servicio, tal como la guarda ABP.</summary>
    private Task LlamoAsync(IdentityUser quien, Type servicio, string metodo, string parametros, int haceMinutos = 0)
    {
        var cuando = DateTime.Now.AddMinutes(-haceMinutos);
        var info = new AuditLogInfo
        {
            ApplicationName = "Test",
            ExecutionTime = cuando,
            UserId = quien.Id,
            UserName = quien.UserName,
            ClientIpAddress = "10.0.0.9",
            HttpStatusCode = 200,
        };
        info.Actions.Add(new AuditLogActionInfo
        {
            ServiceName = servicio.FullName!,
            MethodName = metodo,
            Parameters = parametros,
            ExecutionTime = cuando,
        });
        return WithUnitOfWorkAsync(() => GetRequiredService<IAuditingStore>().SaveAsync(info));
    }

    private Task<PaginaDeActividadDto> VerAsync(IdentityUser como, GetActividadInput? input = null) =>
        ComoAsync(como, () => _actividad.GetListAsync(input ?? new GetActividadInput()));

    [Fact]
    public async Task Cuenta_en_frases_lo_que_hizo_cada_quien()
    {
        var norte = await CrearAsync("Norte");
        await LlamoAsync(Administrador, Organizaciones, "AgregarMiembroAsync",
            $"{{\"id\":\"{norte.Id}\",\"input\":{{\"userId\":\"{Usuario.Id}\"}}}}");
        await LlamoAsync(Administrador, typeof(Invitaciones.InvitacionAppService), "CreateAsync",
            "{\"input\":{\"email\":\"olga@auxiliar.test\",\"rol\":\"usuario\",\"organizacionId\":null}}");

        var textos = (await VerAsync(Owner)).Items.Select(i => $"{i.Usuario}: {i.Texto}").ToList();

        textos.ShouldContain("laura: Añadió a carlos a «Norte»");
        textos.ShouldContain("laura: Invitó a olga@auxiliar.test como Usuario");
    }

    [Fact]
    public async Task Lo_del_Developer_no_aparece_para_nadie_mas()
    {
        await LlamoAsync(Developer, Organizaciones, "CreateAsync", "{\"input\":{\"nombre\":\"Secreta\"}}");
        await WithUnitOfWorkAsync(() => GetRequiredService<ISecurityLogStore>().SaveAsync(new SecurityLogInfo
        {
            ApplicationName = "Test", Identity = "Identity", Action = "LoginSucceeded",
            UserId = Developer.Id, UserName = Developer.UserName, CreationTime = DateTime.Now,
        }));

        (await VerAsync(Owner)).Items.ShouldBeEmpty();
        (await VerAsync(Administrador)).Items.ShouldBeEmpty();

        var delDeveloper = (await VerAsync(Developer)).Items.Select(i => i.Texto).ToList();
        delDeveloper.ShouldContain("Entró a la consola");
        delDeveloper.ShouldContain("Creó la organización «Secreta»");
    }

    [Fact]
    public async Task El_Administrador_no_ve_lo_que_hizo_el_Owner()
    {
        await WithUnitOfWorkAsync(() => GetRequiredService<ISecurityLogStore>().SaveAsync(new SecurityLogInfo
        {
            ApplicationName = "Test", Identity = "Identity", Action = "LoginSucceeded",
            UserId = Owner.Id, UserName = Owner.UserName, CreationTime = DateTime.Now,
        }));

        (await VerAsync(Administrador)).Items.ShouldBeEmpty();
        (await VerAsync(Owner)).Items.ShouldHaveSingleItem().Texto.ShouldBe("Entró a la consola");
    }

    [Fact]
    public async Task Lo_mismo_varias_veces_seguidas_cuenta_una_vez()
    {
        var norte = await CrearAsync("Norte");
        foreach (var minutos in new[] { 1, 5, 12 })
        {
            await LlamoAsync(Administrador, Organizaciones, "UpdateAsync",
                $"{{\"id\":\"{norte.Id}\",\"input\":{{\"nombre\":\"Norte\"}}}}", minutos);
        }

        var edito = (await VerAsync(Owner, new GetActividadInput { UsuarioId = Administrador.Id })).Items.ShouldHaveSingleItem();
        edito.Texto.ShouldBe("Editó la organización «Norte»");
        edito.Veces.ShouldBe(3);
    }

    [Fact]
    public async Task Una_organizacion_borrada_se_sigue_nombrando()
    {
        var norte = await CrearAsync("Norte");
        await ComoAsync(Owner, () => GetRequiredService<IOrganizacionAppService>().DeleteAsync(norte.Id));
        await LlamoAsync(Administrador, Organizaciones, "DeleteAsync", $"{{\"id\":\"{norte.Id}\"}}");

        (await VerAsync(Owner)).Items.Select(i => i.Texto).ShouldContain("Borró la organización «Norte»");
    }

    [Fact]
    public async Task Por_organizacion_sale_lo_de_sus_personas_y_lo_que_se_hizo_con_ella()
    {
        var norte = await CrearAsync("Norte");
        var sur = await CrearAsync("Sur");
        await ComoAsync(Owner, () => GetRequiredService<IOrganizacionAppService>()
            .AgregarMiembroAsync(norte.Id, new AgregarMiembroDto { UserId = Usuario.Id }));

        await WithUnitOfWorkAsync(() => GetRequiredService<ISecurityLogStore>().SaveAsync(new SecurityLogInfo
        {
            ApplicationName = "Test", Identity = "Identity", Action = "LoginSucceeded",
            UserId = Usuario.Id, UserName = Usuario.UserName, CreationTime = DateTime.Now,
        }));
        await LlamoAsync(Administrador, Organizaciones, "UpdateAsync",
            $"{{\"id\":\"{norte.Id}\",\"input\":{{\"nombre\":\"Norte\"}}}}");
        await LlamoAsync(Administrador, Organizaciones, "UpdateAsync",
            $"{{\"id\":\"{sur.Id}\",\"input\":{{\"nombre\":\"Sur\"}}}}");

        var pagina = await VerAsync(Owner, new GetActividadInput { OrganizacionId = norte.Id });
        var textos = pagina.Items.Select(i => $"{i.Usuario}: {i.Texto}").ToList();

        textos.ShouldContain("carlos: Entró a la consola");
        textos.ShouldContain("laura: Editó la organización «Norte»");
        textos.ShouldNotContain(t => t.Contains("Sur"));
        pagina.Items.First(i => i.Usuario == "carlos").Organizaciones.ShouldBe(["Norte"]);
    }

    [Fact]
    public async Task Filtrar_por_tipo()
    {
        await CrearAsync("Norte");
        await LlamoAsync(Administrador, Organizaciones, "CreateAsync", "{\"input\":{\"nombre\":\"Sur\"}}");
        await WithUnitOfWorkAsync(() => GetRequiredService<ISecurityLogStore>().SaveAsync(new SecurityLogInfo
        {
            ApplicationName = "Test", Identity = "Identity", Action = "LoginFailed",
            UserId = Usuario.Id, UserName = Usuario.UserName, CreationTime = DateTime.Now,
        }));

        var sesiones = await VerAsync(Owner, new GetActividadInput { Tipo = TipoDeActividad.Sesion });

        sesiones.Items.ShouldHaveSingleItem().Texto.ShouldBe("Intentó entrar con una contraseña equivocada");
    }
}
