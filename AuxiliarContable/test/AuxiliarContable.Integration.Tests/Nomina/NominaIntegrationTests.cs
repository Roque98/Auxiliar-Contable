using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Nomina;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Nómina del despacho: trabajadores, un periodo con sus incidencias y su cálculo con el motor; lo que falta del patrón
/// se avisa; el cliente no la ve.
/// </summary>
public class NominaIntegrationTests : ConJerarquiaTestBase
{
    private const string Rfc = "GODE561231GR8";

    private readonly INominaAppService _nomina;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;

    public NominaIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _nomina = GetRequiredService<INominaAppService>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _motor.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Panadería Lupita");
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
        await PonerAsync(CamposDelPerfil.Rfc, Rfc);
    }

    private Task PonerAsync(string campo, string valor) =>
        ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = campo, Valor = valor }));

    [Fact]
    public async Task Trabajadores_periodo_incidencias_y_calculo()
    {
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _nomina.GetDelClienteAsync(_cliente, 2026)));

        var vacio = await ComoAsync(Administrador, () => _nomina.GetDelClienteAsync(_cliente, 2026));
        vacio.FaltaEnElPerfil.ShouldContain("registro patronal");
        vacio.FaltaEnElPerfil.ShouldContain("prima de riesgo de trabajo");
        await PonerAsync(CamposDelPerfil.RegistroPatronal, "a12-34567-10-1");
        await PonerAsync(CamposDelPerfil.PrimaDeRiesgo, "0,54355");
        await Should.ThrowAsync<BusinessException>(() => PonerAsync(CamposDelPerfil.PrimaDeRiesgo, "20"));

        var lupe = await ComoAsync(Administrador, () => _nomina.GuardarTrabajadorAsync(_cliente, new GuardarTrabajadorDto
        {
            Nombre = "Lupita Ejemplo", FechaIngreso = new DateTime(2025, 6, 1), SalarioDiario = 500, Fonacot = 300,
            CreditoInfonavitTipo = "porcentaje", CreditoInfonavitValor = 20,
        }));
        var sefue = await ComoAsync(Administrador, () => _nomina.GuardarTrabajadorAsync(_cliente, new GuardarTrabajadorDto
        {
            Nombre = "Ya se fue", FechaIngreso = new DateTime(2024, 1, 1), SalarioDiario = 400,
        }));
        await ComoAsync(Administrador, () => _nomina.DarDeBajaAsync(_cliente, sefue.Id!.Value, new DateTime(2026, 1, 31), "2"));
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () =>
            _nomina.DarDeBajaAsync(_cliente, sefue.Id!.Value, new DateTime(2026, 1, 31), "Z")));
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () =>
            _nomina.GuardarTrabajadorAsync(_cliente, new GuardarTrabajadorDto { Nombre = "Sin sueldo", FechaIngreso = DateTime.Today })));

        var p = await ComoAsync(Administrador, () => _nomina.CrearPeriodoAsync(_cliente,
            new CrearPeriodoDto { Inicio = new DateTime(2026, 3, 1), Fin = new DateTime(2026, 3, 15), Periodicidad = "quincenal" }));
        p.Dias.ShouldBe(15);
        p.Incidencias.ShouldHaveSingleItem().Nombre.ShouldBe("Lupita Ejemplo"); // la que ya se fue no está activa
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () => _nomina.CrearPeriodoAsync(_cliente,
            new CrearPeriodoDto { Inicio = new DateTime(2026, 3, 1), Fin = new DateTime(2026, 3, 15), Periodicidad = "quincenal" })));

        var inc = p.Incidencias;
        inc[0].Incidencias.HorasExtraDobles = 4;
        inc[0].Incidencias.Faltas = 1;
        await ComoAsync(Administrador, () => _nomina.GuardarIncidenciasAsync(_cliente, p.Id, inc));
        inc[0].Incidencias.Faltas = 40;
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () => _nomina.GuardarIncidenciasAsync(_cliente, p.Id, inc)));

        var calculado = await ComoAsync(Administrador, () => _nomina.CalcularAsync(_cliente, p.Id));
        calculado.Resultado.ShouldNotBeNull();
        calculado.CalculadoUtc.ShouldNotBeNull();
        var enviado = _motor.NominaRecibida.ShouldNotBeNull();
        enviado.Patron.Rfc.ShouldBe(Rfc);
        enviado.Patron.PrimaRiesgo.ShouldBe(0.54355m);
        enviado.Periodo.Dias.ShouldBe(15);
        var t = enviado.Trabajadores.ShouldHaveSingleItem();
        (t.Incidencias.HorasExtraDobles, t.Incidencias.Faltas, t.Fonacot, t.CreditoInfonavit!.Valor).ShouldBe((4m, 1, (decimal?)300m, 20m));

        // Cambiar incidencias deja el cálculo viejo fuera.
        inc[0].Incidencias.Faltas = 0;
        (await ComoAsync(Administrador, () => _nomina.GuardarIncidenciasAsync(_cliente, p.Id, inc))).Resultado.ShouldBeNull();
        var lista = await ComoAsync(Administrador, () => _nomina.GetDelClienteAsync(_cliente, 2026));
        lista.Trabajadores.Count.ShouldBe(2);
        lista.Periodos.ShouldHaveSingleItem();
        lista.FaltaEnElPerfil.ShouldNotContain("registro patronal");

        // Avisos al IMSS: las dos altas y la baja quedaron pendientes; cambiar el salario agrega una modificación.
        lista.Movimientos.Select(m => m.Tipo).OrderBy(x => x).ShouldBe(["alta", "alta", "baja"]);
        var conCambio = lupe;
        conCambio.SalarioDiario = 550;
        conCambio.PrimerApellido = "Herrera";
        await ComoAsync(Administrador, () => _nomina.GuardarTrabajadorAsync(_cliente, conCambio));
        var movs = (await ComoAsync(Administrador, () => _nomina.GetDelClienteAsync(_cliente, 2026))).Movimientos;
        movs.Count(m => m.Tipo == "modificacion" && m.SalarioDiario == 550).ShouldBe(1);
        var archivos = await ComoAsync(Administrador, () => _nomina.ArchivosIdseAsync(_cliente, []));
        archivos.Archivos.Sum(a => a.Renglones).ShouldBe(4);
        _motor.IdseRecibido!.Single(m => m.Tipo == "modificacion").PrimerApellido.ShouldBe("HERRERA");
        await ComoAsync(Administrador, () => _nomina.MarcarPresentadosAsync(_cliente, [.. movs.Select(m => m.Id)]));
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () => _nomina.ArchivosIdseAsync(_cliente, [])));

        // Recibos (sin CURP lo dice el motor) y cuotas del mes con el periodo calculado.
        await ComoAsync(Administrador, () => _nomina.CalcularAsync(_cliente, p.Id));
        var recibos = await ComoAsync(Administrador, () => _nomina.RecibosAsync(_cliente, p.Id));
        recibos.Recibos.ShouldHaveSingleItem().Faltan.ShouldContain("CURP");
        var cuotas = await ComoAsync(Administrador, () => _nomina.CuotasDelMesAsync(_cliente, 2026, 3));
        (cuotas.Periodos, cuotas.SinCalcular, cuotas.Cuotas.TotalMensual).ShouldBe((1, 0, 100m));
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _nomina.RecibosAsync(_cliente, p.Id)));
    }
}
