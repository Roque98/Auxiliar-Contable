using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// El botón «Conciliar»: se pide, el trabajo de fondo la corre con los permisos de quien la pidió, todos los pasos de
/// cada mes terminan (bien, con aviso, omitidos o con error, nunca a medias) y el cliente no la ve. Y los cobros PPD sin
/// complemento que muestra la contabilidad llegan al cálculo de impuestos.
/// </summary>
public class ConciliacionIntegralIntegrationTests : ConJerarquiaTestBase
{
    private readonly IConciliacionIntegralAppService _conciliar;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;

    public ConciliacionIntegralIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _conciliar = GetRequiredService<IConciliacionIntegralAppService>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _motor.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Cliente que concilia");
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
        await ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = "GODE561231GR8" }));
    }

    [Fact]
    public async Task Pedir_correr_y_ver_cada_paso()
    {
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _conciliar.PedirAsync(_cliente, 2026, 3)));
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () => _conciliar.PedirAsync(_cliente, 2099, 1)));
        (await ComoAsync(Administrador, () => _conciliar.GetUltimaAsync(_cliente))).ShouldBeNull();

        var pedida = await ComoAsync(Administrador, () => _conciliar.PedirAsync(_cliente, 2026, 2));
        pedida.Estado.ShouldBe("Pendiente");
        (await ComoAsync(Administrador, () => _conciliar.PedirAsync(_cliente, 2026, 2))).Id.ShouldBe(pedida.Id); // no se encolan dos

        (await GetRequiredService<ConciliadorIntegral>().ProcesarAsync()).ShouldBeTrue();

        var hecha = (await ComoAsync(Administrador, () => _conciliar.GetUltimaAsync(_cliente)))!;
        hecha.Estado.ShouldBe("Lista", hecha.Error);
        // Descarga del SAT + 10 pasos por cada mes abierto (enero y febrero).
        hecha.Pasos.Count.ShouldBe(1 + 10 * 2);
        hecha.Pasos.ShouldAllBe(p => p.Estado != "pendiente" && p.Estado != "trabajando");
        hecha.Pasos.Where(p => p.Periodo == "2026-02").Select(p => p.Clave).ShouldBe(
            ["facturas", "documentos", "banco", "amarre", "interpretacion", "nomina", "contabilidad", "impuestos", "estados", "normativa"]);
        hecha.Meses.Select(m => m.Mes).ShouldBe([1, 2]);
        (await GetRequiredService<ConciliadorIntegral>().ProcesarAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Los_cobros_ppd_sin_complemento_van_al_calculo()
    {
        var resultado = new ResultadoContable
        {
            Polizas =
            [
                new PolizaContable
                {
                    Operacion = "op-1", Tipo = "Ingresos", Fecha = "2026-03-07", Uuid = "AAAAAAAA-0000-0000-0000-000000000001", Total = 11600,
                    Movimientos = [new() { Cuenta = "102.01", Debe = 11600 }, new() { Cuenta = "105.01", Haber = 11600 }],
                },
                new PolizaContable
                {
                    Operacion = "op-2", Tipo = "Ingresos", Fecha = "2026-03-09", Uuid = "AAAAAAAA-0000-0000-0000-000000000002", Total = 5800,
                    Movimientos = [new() { Cuenta = "102.01", Debe = 5800 }, new() { Cuenta = "105.01", Haber = 5800 }],
                },
            ],
            Revision = [new() { Operacion = "op-1", Codigo = "PPD_SIN_COMPLEMENTO", Nivel = "aviso", Mensaje = "falta" }],
        };
        await WithUnitOfWorkAsync(async () =>
        {
            var fila = new PolizasDelMes(Guid.NewGuid(), _cliente, 2026, 3);
            fila.Guardar(JsonSerializer.Serialize(resultado), DateTime.UtcNow, null);
            await GetRequiredService<IRepository<PolizasDelMes, Guid>>().InsertAsync(fila, autoSave: true);
        });

        var cobros = await WithUnitOfWorkAsync(() => GetRequiredService<CalculadoraDeImpuestos>().CobrosDelBancoAsync(_cliente, 2026, 3));
        var c = cobros.ShouldHaveSingleItem(); // la op-2 tiene su complemento (no está en la revisión)
        c.Uuid.ShouldEndWith("0001");
        c.Fecha.ShouldBe("2026-03-07");
        c.Importe.ShouldBe(11600);
        c.Lado.ShouldBe("emitida");
        (await WithUnitOfWorkAsync(() => GetRequiredService<CalculadoraDeImpuestos>().CobrosDelBancoAsync(_cliente, 2026, 2))).ShouldBeEmpty();
    }
}
