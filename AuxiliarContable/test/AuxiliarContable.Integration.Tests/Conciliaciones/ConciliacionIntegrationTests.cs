using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Boveda;
using AuxiliarContable.Cfdis;
using AuxiliarContable.Conciliaciones;
using AuxiliarContable.Conversaciones;
using AuxiliarContable.Documentos;
using AuxiliarContable.EstadosDeCuenta;
using AuxiliarContable.IA;
using AuxiliarContable.Identity;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// La conciliación de un mes de punta a punta contra PostgreSQL: facturas XML y un estado de
/// cuenta (IA de prueba) → propuestas por reglas → confirmar, rechazar, unir a mano, «no lleva
/// factura» y los faltantes.
/// </summary>
public class ConciliacionIntegrationTests : ConJerarquiaTestBase
{
    private const string Rfc = "GODE561231GR8";
    private readonly IBovedaAppService _boveda;
    private readonly IConciliacionAppService _conciliacion;
    private readonly ModeloDeIADePrueba _ia;
    private Guid _cliente;
    private Guid _otroCliente;

    public ConciliacionIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _boveda = GetRequiredService<IBovedaAppService>();
        _conciliacion = GetRequiredService<IConciliacionAppService>();
        _ia = (ModeloDeIADePrueba)GetRequiredService<IModeloDeIA>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _ia.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Panadería Lupita");
        _otroCliente = await CrearOrganizacionAsync("Otro cliente");
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
        await ComoAsync(Usuario, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = Rfc }));
    }

    private Task<ResultadoDeSubidaDto> FacturaAsync(string emisor, string receptor, string total, string fecha) =>
        ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.Otro, new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(
            LectorDeCfdiTests.Cfdi40(uuid: Guid.NewGuid().ToString(), emisor: emisor, receptor: receptor, total: total, fecha: fecha))), "f.xml")));

    private async Task EstadoDeCuentaAsync(decimal saldoFinal, params object[] movimientos)
    {
        var subido = await ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.EstadoDeCuenta,
            new RemoteStreamContent(new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 " + Guid.NewGuid())), "banco.pdf")));
        _ia.Responder(new
        {
            esEstadoDeCuenta = true, banco = "BBVA", cuenta = "1234", moneda = "MXN",
            periodoInicio = "2026-10-01", periodoFin = "2026-10-31", saldoInicial = 10000m, saldoFinal, movimientos,
        });
        await WithUnitOfWorkAsync(() => GetRequiredService<ProcesadorDeEstadoDeCuenta>().ProcesarAsync(subido.Documento.Id));
    }

    [Fact]
    public async Task Propone_confirma_y_deja_a_la_vista_lo_que_falta()
    {
        var venta = await FacturaAsync(Rfc, "ACM010101ABC", "1160.00", "2026-10-02T10:00:00");
        var gasto = await FacturaAsync("CFE370814QI0", Rfc, "500.00", "2026-10-06T10:00:00");
        await FacturaAsync(Rfc, "XYZ010101AAA", "800.00", "2026-10-20T10:00:00"); // venta sin depósito
        await EstadoDeCuentaAsync(10000m + 1160m - 500m + 300m - 50m,
            new { fecha = "2026-10-03", concepto = "SPEI RECIBIDO ACM010101ABC", cargo = 0m, abono = 1160m },
            new { fecha = "2026-10-07", concepto = "PAGO CFE", cargo = 500m, abono = 0m },
            new { fecha = "2026-10-15", concepto = "DEPOSITO EFECTIVO", cargo = 0m, abono = 300m },
            new { fecha = "2026-10-31", concepto = "COMISION", cargo = 50m, abono = 0m });

        var mes = await ComoAsync(Usuario, () => _conciliacion.ProponerAsync(_cliente, 2026, 10));

        mes.HayEstadosDeCuenta.ShouldBeTrue();
        mes.Propuestas.Count.ShouldBe(2);
        mes.Propuestas.Single(p => p.Factura.EsVenta).Motivo.ShouldContain("RFC");
        mes.DepositosSinFactura.ShouldHaveSingleItem().Concepto.ShouldBe("DEPOSITO EFECTIVO");
        mes.CargosSinFactura.ShouldHaveSingleItem().Concepto.ShouldBe("COMISION");
        mes.VentasSinDeposito.ShouldHaveSingleItem().Total.ShouldBe(800m);
        mes.GastosSinCargo.ShouldBeEmpty();

        // Confirmar una y rechazar la otra: la rechazada vuelve a faltantes y no se repropone.
        var deVenta = mes.Propuestas.Single(p => p.Factura.Id == mes.Propuestas.Single(x => x.Factura.EsVenta).Factura.Id);
        var deGasto = mes.Propuestas.Single(p => !p.Factura.EsVenta);
        await ComoAsync(Usuario, async () => { await _conciliacion.ConfirmarAsync(_cliente, deVenta.Id); return true; });
        await ComoAsync(Usuario, async () => { await _conciliacion.RechazarAsync(_cliente, deGasto.Id); return true; });
        mes = await ComoAsync(Usuario, () => _conciliacion.ProponerAsync(_cliente, 2026, 10));

        mes.Confirmadas.ShouldHaveSingleItem().Movimiento.Abono.ShouldBe(1160m);
        mes.Propuestas.ShouldBeEmpty();
        mes.CargosSinFactura.Count.ShouldBe(2);
        mes.GastosSinCargo.ShouldHaveSingleItem().Total.ShouldBe(500m);

        // A mano: el cargo de CFE con su factura. Y la comisión «no lleva factura».
        var pagoCfe = mes.CargosSinFactura.Single(m => m.Concepto == "PAGO CFE");
        await ComoAsync(Usuario, async () =>
        {
            await _conciliacion.ConciliarAManoAsync(_cliente, new ConciliarAManoDto { MovimientoId = pagoCfe.Id, CfdiId = mes.GastosSinCargo.Single().Id });
            return true;
        });
        var comision = mes.CargosSinFactura.Single(m => m.Concepto == "COMISION");
        await ComoAsync(Usuario, async () =>
        {
            await _conciliacion.MarcarSinFacturaAsync(_cliente, new MarcarSinFacturaDto { MovimientoId = comision.Id, Porque = "Comisión del banco" });
            return true;
        });

        mes = await ComoAsync(Usuario, () => _conciliacion.GetDelMesAsync(_cliente, 2026, 10));
        mes.Confirmadas.Count.ShouldBe(2);
        mes.CargosSinFactura.ShouldBeEmpty();
        mes.SinFacturaJustificados.ShouldHaveSingleItem().SinFacturaPorque.ShouldBe("Comisión del banco");
        mes.DepositosSinFactura.ShouldHaveSingleItem();
        mes.VentasSinDeposito.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Un_estado_de_cuenta_que_no_cuadra_no_entra()
    {
        await FacturaAsync(Rfc, "ACM010101ABC", "1160.00", "2026-10-02T10:00:00");
        await EstadoDeCuentaAsync(99999m, new { fecha = "2026-10-03", concepto = "SPEI", cargo = 0m, abono = 1160m });

        var mes = await ComoAsync(Usuario, () => _conciliacion.ProponerAsync(_cliente, 2026, 10));

        mes.EstadosSinRevisar.ShouldBe(1);
        mes.Propuestas.ShouldBeEmpty();
        mes.DepositosSinFactura.ShouldBeEmpty();
    }

    [Fact]
    public async Task Lo_ajeno_no_se_toca()
    {
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _conciliacion.GetDelMesAsync(_otroCliente, 2026, 10)));
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, async () =>
        {
            await _conciliacion.ConfirmarAsync(_otroCliente, Guid.NewGuid());
            return true;
        }));
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Usuario, () => _conciliacion.GetDelMesAsync(_cliente, 2026, 13)));
    }

    [Fact]
    public async Task Se_concilia_platicando_y_un_id_ajeno_no_tumba_la_conversacion()
    {
        await FacturaAsync(Rfc, "ACM010101ABC", "1160.00", "2026-10-02T10:00:00");
        await FacturaAsync("CFE370814QI0", Rfc, "500.00", "2026-10-06T10:00:00");
        await EstadoDeCuentaAsync(10000m + 1160m - 500m - 50m,
            new { fecha = "2026-10-03", concepto = "SPEI RECIBIDO ACM010101ABC", cargo = 0m, abono = 1160m },
            new { fecha = "2026-10-07", concepto = "PAGO CFE", cargo = 500m, abono = 0m },
            new { fecha = "2026-10-31", concepto = "COMISION", cargo = 50m, abono = 0m });
        var mes = await ComoAsync(Usuario, () => _conciliacion.ProponerAsync(_cliente, 2026, 10));
        var deVenta = mes.Propuestas.Single(p => p.Factura.EsVenta);
        var deGasto = mes.Propuestas.Single(p => !p.Factura.EsVenta);
        var comision = mes.CargosSinFactura.Single(m => m.Concepto == "COMISION");

        var modelo = (ModeloConversacionalDePrueba)GetRequiredService<IModeloConversacional>();
        modelo.Reiniciar();
        modelo.UsarHerramienta(HerramientasDelAsistente.ConfirmarPareja, new { id = deVenta.Id });
        modelo.UsarHerramienta(HerramientasDelAsistente.RechazarPareja, new { id = deGasto.Id });
        modelo.UsarHerramienta(HerramientasDelAsistente.MarcarSinFactura, new { movimientoId = comision.Id, porque = "Comisión del banco" });
        modelo.UsarHerramienta(HerramientasDelAsistente.ConfirmarPareja, new { id = Guid.NewGuid() });
        modelo.Contestar("Listo: confirmé el depósito de $1,160 y marqué la comisión.");

        var conversacion = await ComoAsync(Usuario, () =>
            GetRequiredService<IAsistenteAppService>().EnviarAsync(_cliente, new EnviarMensajeDto { Texto = "Sí a la primera, no a la segunda; la comisión no lleva factura" }));

        mes = await ComoAsync(Usuario, () => _conciliacion.GetDelMesAsync(_cliente, 2026, 10));
        mes.Confirmadas.ShouldHaveSingleItem().Id.ShouldBe(deVenta.Id);
        mes.Propuestas.ShouldBeEmpty();
        mes.SinFacturaJustificados.ShouldHaveSingleItem().SinFacturaPorque.ShouldBe("Comisión del banco");
        modelo.Solicitudes[4].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>().EsError.ShouldBeTrue();
        conversacion.Mensajes.Last().Acciones.ShouldBe([
            "Confirmó una pareja de la conciliación", "Descartó una pareja propuesta", "Marcó un movimiento sin factura"]);
    }

    [Fact]
    public async Task Un_estado_que_no_cuadra_se_corrige_y_confirma_platicando()
    {
        // La IA leyó mal el saldo final: 9,000 en vez de 11,160.
        await EstadoDeCuentaAsync(9000m, new { fecha = "2026-10-03", concepto = "SPEI", cargo = 0m, abono = 1160m });
        var estados = GetRequiredService<IEstadosDeCuentaAppService>();
        var estado = (await ComoAsync(Usuario, () => estados.GetListAsync(_cliente))).Single();
        estado.Cuadra.ShouldBeFalse();

        var modelo = (ModeloConversacionalDePrueba)GetRequiredService<IModeloConversacional>();
        modelo.Reiniciar();
        modelo.UsarHerramienta(HerramientasDelAsistente.EstadosDeCuenta, new { });
        modelo.UsarHerramienta(HerramientasDelAsistente.CorregirSaldos, new { id = estado.Id, saldoFinal = 11160m });
        modelo.UsarHerramienta(HerramientasDelAsistente.ConfirmarEstadoDeCuenta, new { id = estado.Id });
        modelo.Contestar("Ya cuadra y quedó confirmado.");

        var conversacion = await ComoAsync(Usuario, () =>
            GetRequiredService<IAsistenteAppService>().EnviarAsync(_cliente, new EnviarMensajeDto { Texto = "El saldo final dice 11,160. Sí, confírmalo" }));

        modelo.Solicitudes[1].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>().Contenido.ShouldContain("\"cuadra\":false");
        estado = (await ComoAsync(Usuario, () => estados.GetListAsync(_cliente))).Single();
        estado.Cuadra.ShouldBeTrue();
        estado.Revision.ShouldBe(RevisionDelEstadoDeCuenta.Confirmado);
        conversacion.Mensajes.Last().Acciones.ShouldBe([
            "Corrigió los saldos de un estado de cuenta", "Confirmó un estado de cuenta"]);
    }
}
