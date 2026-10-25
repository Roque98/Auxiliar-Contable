using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Identity;
using AuxiliarContable.Invitaciones;
using AuxiliarContable.Privacidad;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// El aviso de privacidad: sin aceptarlo no se crea la cuenta, y lo aceptado queda como
/// constancia (qué versión y cuándo), una fila por persona y versión.
/// </summary>
public class PrivacidadIntegrationTests : ConJerarquiaTestBase
{
    private readonly IRegistroAppService _registro;
    private readonly IPrivacidadAppService _privacidad;
    private readonly IRepository<ConsentimientoDePrivacidad, Guid> _consentimientos;

    public PrivacidadIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _registro = GetRequiredService<IRegistroAppService>();
        _privacidad = GetRequiredService<IPrivacidadAppService>();
        _consentimientos = GetRequiredService<IRepository<ConsentimientoDePrivacidad, Guid>>();
    }

    private async Task<string> InvitarAsync(string email)
    {
        var organizacion = await CrearOrganizacionAsync();
        var invitacion = await ComoAsync(Owner, () => GetRequiredService<IInvitacionAppService>()
            .CreateAsync(new CrearInvitacionDto { Email = email, Rol = AuxiliarRoles.Usuario, OrganizacionId = organizacion }));

        var token = string.Empty;
        await WithUnitOfWorkAsync(async () =>
        {
            var entidad = await GetRequiredService<IRepository<Invitacion, Guid>>().GetAsync(invitacion.Id);
            token = await GetRequiredService<GestorDeInvitaciones>().EmitirTokenAsync(entidad);
        });
        return token;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2020-01-01")]
    public async Task Sin_aceptar_el_aviso_vigente_no_se_crea_la_cuenta(string? version)
    {
        var token = await InvitarAsync("eva@auxiliar.test");

        var error = await Should.ThrowAsync<BusinessException>(() => WithUnitOfWorkAsync(() => _registro.AceptarAsync(
            new AceptarInvitacionDto { Token = token, UserName = "eva", Password = Clave, VersionDelAvisoAceptada = version })));

        error.Code.ShouldBe("AuxiliarContable:AvisoDePrivacidadDesactualizado");
        (await WithUnitOfWorkAsync(() => Usuarios.FindByNameAsync("eva"))).ShouldBeNull();
        // La invitación sigue sirviendo: el rechazo no la gasta.
        (await WithUnitOfWorkAsync(() => _registro.ConsultarAsync(new TokenDeInvitacionDto { Token = token }))).Vigente.ShouldBeTrue();
    }

    [Fact]
    public async Task Al_registrarse_queda_constancia_de_la_version_aceptada()
    {
        var token = await InvitarAsync("fer@auxiliar.test");

        await WithUnitOfWorkAsync(() => _registro.AceptarAsync(new AceptarInvitacionDto
        {
            Token = token,
            UserName = "fer",
            Password = Clave,
            VersionDelAvisoAceptada = AvisoDePrivacidadConsts.VersionVigente,
        }));

        var fer = (await WithUnitOfWorkAsync(() => Usuarios.FindByNameAsync("fer")))!;
        var constancia = (await WithUnitOfWorkAsync(() => _consentimientos.GetListAsync(c => c.UserId == fer.Id))).ShouldHaveSingleItem();
        constancia.Version.ShouldBe(AvisoDePrivacidadConsts.VersionVigente);
        constancia.AceptadoUtc.Kind.ShouldBe(DateTimeKind.Utc);
        (await ComoAsync(fer, () => _privacidad.GetEstadoAsync())).Aceptada.ShouldBeTrue();
    }

    [Fact]
    public async Task Quien_ya_tenia_cuenta_lo_acepta_una_vez_y_solo_para_si()
    {
        var antes = await ComoAsync(Administrador, () => _privacidad.GetEstadoAsync());
        antes.Aceptada.ShouldBeFalse();
        antes.VersionVigente.ShouldBe(AvisoDePrivacidadConsts.VersionVigente);

        var dto = new AceptarAvisoDePrivacidadDto { Version = AvisoDePrivacidadConsts.VersionVigente };
        await ComoAsync(Administrador, () => _privacidad.AceptarAsync(dto));
        await ComoAsync(Administrador, () => _privacidad.AceptarAsync(dto));

        (await ComoAsync(Administrador, () => _privacidad.GetEstadoAsync())).Aceptada.ShouldBeTrue();
        (await WithUnitOfWorkAsync(() => _consentimientos.GetListAsync(c => c.UserId == Administrador.Id))).Count.ShouldBe(1);
        // Lo de uno no cuenta para otro.
        (await ComoAsync(Usuario, () => _privacidad.GetEstadoAsync())).Aceptada.ShouldBeFalse();
    }

    [Fact]
    public async Task No_se_acepta_una_version_que_ya_no_es_la_vigente()
    {
        var error = await Should.ThrowAsync<BusinessException>(() => ComoAsync(Usuario,
            () => _privacidad.AceptarAsync(new AceptarAvisoDePrivacidadDto { Version = "2020-01-01" })));

        error.Code.ShouldBe("AuxiliarContable:AvisoDePrivacidadDesactualizado");
        (await WithUnitOfWorkAsync(() => _consentimientos.GetListAsync(c => c.UserId == Usuario.Id))).ShouldBeEmpty();
    }
}
