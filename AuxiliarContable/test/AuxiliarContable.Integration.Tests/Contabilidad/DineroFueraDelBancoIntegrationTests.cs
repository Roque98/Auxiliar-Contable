using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Identity;
using AuxiliarContable.Organizaciones;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Efectivo y crédito: el cliente registra lo que no pasa por el banco, lo a crédito pendiente sigue apareciendo
/// hasta pagarse, un mes cerrado no se toca, lo ajeno no existe y el cierre del mes lo resume.
/// </summary>
public class DineroFueraDelBancoIntegrationTests : ConJerarquiaTestBase
{
    private readonly IDineroFueraDelBancoAppService _dinero;
    private Guid _cliente;
    private Guid _otroCliente;
    private IdentityUser _lector = null!;

    public DineroFueraDelBancoIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _dinero = GetRequiredService<IDineroFueraDelBancoAppService>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _cliente = await CrearOrganizacionAsync("Panadería Lupita");
        _otroCliente = await CrearOrganizacionAsync("Otro cliente");
        _lector = await CrearUsuarioAsync("ana", AuxiliarRoles.Usuario);
        await WithUnitOfWorkAsync(async () =>
        {
            var m = GetRequiredService<MembresiasDeOrganizacion>();
            await m.AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro);
            await m.AgregarAsync(_cliente, _lector.Id, RolEnOrganizacion.Lector);
        });
    }

    private static GuardarMovimientoFueraDelBancoDto Nuevo(TipoDeMovimientoFueraDelBanco tipo, DateTime fecha, decimal importe,
        string concepto = "x", DateTime? vence = null) =>
        new() { Tipo = tipo, Fecha = fecha, Importe = importe, Concepto = concepto, Vence = vence };

    private Task CerrarAsync(Guid cliente, DateTime mes) => WithUnitOfWorkAsync(async () =>
    {
        var p = new PeriodoContable(Guid.NewGuid(), cliente, mes.Year, mes.Month);
        p.Cerrar(Administrador.Id, DateTime.UtcNow);
        await GetRequiredService<IRepository<PeriodoContable, Guid>>().InsertAsync(p, autoSave: true);
    });

    [Fact]
    public async Task El_cliente_registra_su_efectivo_y_lo_a_credito_y_ve_los_totales()
    {
        var mes = new DateTime(2026, 10, 1);
        await ComoAsync(Usuario, () => _dinero.CrearAsync(_cliente, Nuevo(TipoDeMovimientoFueraDelBanco.EntradaEnEfectivo, mes.AddDays(4), 3250.5m, "Corte de caja del 5")));
        await ComoAsync(Usuario, () => _dinero.CrearAsync(_cliente, Nuevo(TipoDeMovimientoFueraDelBanco.SalidaEnEfectivo, mes.AddDays(6), 480m, "Gas")));
        await ComoAsync(Usuario, () => _dinero.CrearAsync(_cliente,
            Nuevo(TipoDeMovimientoFueraDelBanco.CompraACredito, mes.AddDays(10), 12000m, "Harina, 40 bultos", vence: mes.AddDays(40))));

        var r = await ComoAsync(Usuario, () => _dinero.GetAsync(_cliente, 2026, 10));

        r.Movimientos.Count.ShouldBe(3);
        r.EntradasEnEfectivo.ShouldBe(3250.50m);
        r.SalidasEnEfectivo.ShouldBe(480m);
        r.PorPagar.ShouldBe(12000m);
        r.PuedeEditar.ShouldBeTrue();
        r.Movimientos.Single(m => m.Tipo == TipoDeMovimientoFueraDelBanco.CompraACredito).Pendiente.ShouldBeTrue();
        r.Movimientos.Single(m => m.Tipo == TipoDeMovimientoFueraDelBanco.EntradaEnEfectivo).Pendiente.ShouldBeFalse();

        // Quien solo ve al cliente lo ve, pero no agrega nada.
        (await ComoAsync(_lector, () => _dinero.GetAsync(_cliente, 2026, 10))).PuedeEditar.ShouldBeFalse();
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(_lector, () => _dinero.CrearAsync(_cliente, Nuevo(TipoDeMovimientoFueraDelBanco.SalidaEnEfectivo, mes, 10m))));
    }

    [Fact]
    public async Task Lo_a_credito_sin_pagar_sigue_apareciendo_hasta_que_se_paga()
    {
        var compra = await ComoAsync(Usuario, () => _dinero.CrearAsync(_cliente,
            Nuevo(TipoDeMovimientoFueraDelBanco.CompraACredito, new DateTime(2026, 9, 20), 5000m, "Refrigerador", vence: new DateTime(2026, 10, 5))));

        var octubre = await ComoAsync(Usuario, () => _dinero.GetAsync(_cliente, 2026, 10));
        var m = octubre.Movimientos.Single();
        m.DeOtroMes.ShouldBeTrue();
        octubre.PorPagar.ShouldBe(5000m);

        await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _dinero.SaldarAsync(_cliente, compra.Id, new DateTime(2026, 9, 1))));
        var pagada = await ComoAsync(Usuario, () => _dinero.SaldarAsync(_cliente, compra.Id, new DateTime(2026, 10, 15)));
        pagada.Pendiente.ShouldBeFalse();

        (await ComoAsync(Usuario, () => _dinero.GetAsync(_cliente, 2026, 11))).Movimientos.ShouldBeEmpty();
        (await ComoAsync(Usuario, () => _dinero.GetAsync(_cliente, 2026, 9))).Movimientos.Single().Saldado.ShouldBe(new DateTime(2026, 10, 15));
    }

    [Fact]
    public async Task Un_mes_cerrado_no_se_toca_pero_su_deuda_se_puede_pagar_en_un_mes_abierto()
    {
        var compra = await ComoAsync(Usuario, () => _dinero.CrearAsync(_cliente,
            Nuevo(TipoDeMovimientoFueraDelBanco.CompraACredito, new DateTime(2026, 8, 10), 900m, "Empaques")));
        await CerrarAsync(_cliente, new DateTime(2026, 8, 1));

        (await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Usuario, () =>
            _dinero.CrearAsync(_cliente, Nuevo(TipoDeMovimientoFueraDelBanco.EntradaEnEfectivo, new DateTime(2026, 8, 30), 100m)))))
            .Message.ShouldContain("ya está cerrado");
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Usuario, () => _dinero.BorrarAsync(_cliente, compra.Id)));
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Usuario, () =>
            _dinero.ActualizarAsync(_cliente, compra.Id, Nuevo(TipoDeMovimientoFueraDelBanco.CompraACredito, new DateTime(2026, 9, 1), 900m))));
        (await ComoAsync(Usuario, () => _dinero.GetAsync(_cliente, 2026, 8))).PuedeEditar.ShouldBeFalse();

        var pagada = await ComoAsync(Usuario, () => _dinero.SaldarAsync(_cliente, compra.Id, new DateTime(2026, 9, 3)));
        pagada.Saldado.ShouldBe(new DateTime(2026, 9, 3));
    }

    [Fact]
    public async Task Lo_ajeno_no_existe_y_no_se_liga_un_documento_de_otro_cliente()
    {
        var ajeno = await ComoAsync(Administrador, () => _dinero.CrearAsync(_otroCliente,
            Nuevo(TipoDeMovimientoFueraDelBanco.SalidaEnEfectivo, new DateTime(2026, 10, 2), 50m)));

        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _dinero.GetAsync(_otroCliente, 2026, 10)));
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _dinero.BorrarAsync(_cliente, ajeno.Id)));

        var conDocumentoAjeno = Nuevo(TipoDeMovimientoFueraDelBanco.SalidaEnEfectivo, new DateTime(2026, 10, 2), 50m);
        conDocumentoAjeno.DocumentoId = Guid.NewGuid();
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _dinero.CrearAsync(_cliente, conDocumentoAjeno)));
    }

    [Fact]
    public async Task El_cierre_del_mes_lo_resume()
    {
        await ComoAsync(Usuario, () => _dinero.CrearAsync(_cliente,
            Nuevo(TipoDeMovimientoFueraDelBanco.EntradaEnEfectivo, new DateTime(2026, 10, 3), 1500m, "Corte de caja")));

        var cierre = await ComoAsync(Administrador, () => GetRequiredService<ICierreAppService>().GetAsync(_cliente, 2026, 10));

        var renglon = cierre.Revision.Single(r => r.Texto.StartsWith("Efectivo y crédito"));
        renglon.Texto.ShouldContain("1 registro");
        renglon.Bloquea.ShouldBeFalse();
    }
}
