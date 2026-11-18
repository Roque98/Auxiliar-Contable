using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Boveda;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Contabilidad al día: lo que entra o cambia en un mes lo deja «por actualizar» y, después de un momento sin cambios,
/// se concilia el banco (si sus archivos cambiaron), se amarra lo seguro sin Claude y se rehacen las pólizas con lo
/// capturado para el cierre. Lo que cambia la propia actualización no la vuelve a pedir.
/// </summary>
public class ContabilidadAlDiaIntegrationTests : ConJerarquiaTestBase
{
    private const string Rfc = "GODE561231GR8";
    private const string Uuid = "6F9619FF-8B86-D011-B42D-00C04FC964A1";

    private readonly IMesaDeTrabajoAppService _mesa;
    private readonly ContabilidadAlDia _alDia;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;

    private static DateTime Dia => new(DateTime.Now.Year, DateTime.Now.Month, 1);

    public ContabilidadAlDiaIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _mesa = GetRequiredService<IMesaDeTrabajoAppService>();
        _alDia = GetRequiredService<ContabilidadAlDia>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _motor.Reiniciar();
        _cliente = await ClienteAsync("Constructora Lupita", Rfc);
    }

    private async Task<Guid> ClienteAsync(string nombre, string? rfc)
    {
        var cliente = await CrearOrganizacionAsync(nombre);
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(cliente, Usuario.Id, RolEnOrganizacion.Miembro));
        if (rfc != null)
        {
            await ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(cliente,
                new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = rfc }));
        }

        return cliente;
    }

    private static string Xml(string uuid, decimal total) =>
        $"""<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" Fecha="{Dia:yyyy-MM-dd}T10:00:00" Total="{total:0.00}" TipoDeComprobante="I"><cfdi:Emisor Rfc="AAA010101AAA" Nombre="MATERIALES DEL BAJIO"/><cfdi:Receptor Rfc="{Rfc}" Nombre="CONSTRUCTORA"/><cfdi:Conceptos><cfdi:Concepto Descripcion="Mortero 50 kg"/></cfdi:Conceptos><cfdi:Complemento><tfd:TimbreFiscalDigital UUID="{uuid}"/></cfdi:Complemento></cfdi:Comprobante>""";

    private Task SubirAsync(string nombre, TipoDeDocumento tipo, string contenido, Guid? cliente = null) =>
        ComoAsync(Usuario, () => GetRequiredService<IBovedaAppService>().SubirAsync(cliente ?? _cliente, tipo,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(contenido)), nombre, "application/octet-stream"),
            Dia.Year, Dia.Month));

    private Task<ContabilidadAlDiaDto> VerAsync(Guid? cliente = null) =>
        ComoAsync(Administrador, () => _mesa.GetContabilidadAlDiaAsync(cliente ?? _cliente, Dia.Year, Dia.Month));

    private Task<int> ActualizarAsync() => _alDia.ActualizarAsync(maximo: 500, espera: TimeSpan.Zero);

    [Fact]
    public async Task Lo_que_entra_concilia_amarra_y_rehace_las_polizas_solo()
    {
        // El cliente no ve la contabilidad al día.
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _mesa.GetContabilidadAlDiaAsync(_cliente, Dia.Year, Dia.Month)));

        _motor.Conciliacion = new ResultadoDeConciliacion
        {
            Cuentas = [new CuentaLeida { Banco = "BANORTE", Cuenta = "0123", Cuadra = true }],
            Movimientos =
            [
                new MovimientoLeido { Fecha = Dia.AddDays(4).ToString("dd/MM/yyyy"), Concepto = "SPEI MATERIALES", Naturaleza = "cargo", Importe = 1160m,
                    Cuenta = "0123", Conciliacion = new ConciliacionDeMovimiento { Estado = "conciliado", Facturas = [Uuid] } },
            ],
        };
        _motor.Contabilidad = (_, ops, _) => new ResultadoContable
        {
            Polizas = [.. ops.Select((o, i) => new PolizaContable { Operacion = o.Id, Tipo = "Egresos", Numero = i + 1 })],
        };

        await SubirAsync("mortero.xml", TipoDeDocumento.Cfdi, Xml(Uuid, 1160m));
        await SubirAsync("banorte.pdf", TipoDeDocumento.EstadoDeCuenta, "%PDF-1.4 estado");
        var antes = await VerAsync();
        antes.Actualizando.ShouldBeTrue();
        (antes.SeActualizaUtc!.Value - DateTime.UtcNow).ShouldBeGreaterThan(TimeSpan.FromMinutes(8));
        (await _alDia.ActualizarAsync(maximo: 500)).ShouldBe(0); // con la espera normal todavía no

        (await ActualizarAsync()).ShouldBe(1);
        var hecho = await VerAsync();
        hecho.Actualizando.ShouldBeFalse();
        hecho.Error.ShouldBeNull();
        hecho.Resumen.ShouldBe("Se concilió el banco · 1 movimiento del banco amarrado · 1 póliza.");
        _motor.EstadosRecibidos.ShouldBe(["banorte.pdf"]);
        _motor.PeticionesDeAmarre.ShouldBe(0); // sin Claude

        // La factura y su pago del banco quedaron en una sola operación, y esa se contabilizó.
        var mandada = _motor.Contabilizadas.ShouldHaveSingleItem();
        mandada.Factura.Uuid.ShouldBe(Uuid);
        mandada.Transaccion.Importe.ShouldBe(1160m);
        (await ComoAsync(Administrador, () => _mesa.GetPolizasAsync(_cliente, Dia.Year, Dia.Month)))!.Resultado.Polizas.Count.ShouldBe(1);

        // Lo que cambió la actualización (la conciliación, la operación del banco) no la vuelve a pedir.
        (await VerAsync()).Actualizando.ShouldBeFalse();
        (await ActualizarAsync()).ShouldBe(0);

        // Llega otra factura: se vuelve a conciliar (cambiaron los archivos del mes) y a contabilizar.
        await SubirAsync("arena.xml", TipoDeDocumento.Cfdi, Xml("6F9619FF-8B86-D011-B42D-00C04FC964A2", 500m));
        (await VerAsync()).Actualizando.ShouldBeTrue();
        (await ActualizarAsync()).ShouldBe(1);
        _motor.EstadosRecibidos.Count.ShouldBe(2);
        _motor.Contabilizadas.Count.ShouldBe(2);
        (await VerAsync()).Resumen.ShouldBe("Se concilió el banco · 2 pólizas.");
    }

    [Fact]
    public async Task Conserva_lo_capturado_para_el_cierre_y_actualizar_ahora_no_espera()
    {
        await SubirAsync("mortero.xml", TipoDeDocumento.Cfdi, Xml(Uuid, 1160m));
        await ComoAsync(Administrador, () => _mesa.GenerarPolizasAsync(_cliente, Dia.Year, Dia.Month,
            new DatosDeCierre { InventarioInicial = 5000m, InventarioFinal = 4500m }));
        _motor.Reiniciar();

        // Sin estados de cuenta no se concilia nada; las pólizas se rehacen con el inventario que se capturó.
        await ComoAsync(Administrador, () => _mesa.ActualizarContabilidadAsync(_cliente, Dia.Year, Dia.Month));
        (await VerAsync()).Actualizando.ShouldBeTrue();
        (await _alDia.ActualizarAsync(maximo: 500)).ShouldBe(1);
        _motor.CierreRecibido.ShouldNotBeNull().InventarioFinal.ShouldBe(4500m);
        _motor.EstadosRecibidos.ShouldBeEmpty();
        var visto = await VerAsync();
        visto.Actualizando.ShouldBeFalse();
        visto.Resumen.ShouldBe("0 pólizas.");
        (await ComoAsync(Administrador, () => _mesa.GetPolizasAsync(_cliente, Dia.Year, Dia.Month)))!.Cierre!.InventarioFinal.ShouldBe(4500m);

        // El cliente no la pide.
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _mesa.ActualizarContabilidadAsync(_cliente, Dia.Year, Dia.Month)));
    }

    [Fact]
    public async Task Sin_rfc_se_detiene_y_si_el_motor_falla_se_reintenta()
    {
        var sinRfc = await ClienteAsync("Sin RFC", null);
        await SubirAsync("mortero.xml", TipoDeDocumento.Cfdi, Xml(Uuid, 1160m), sinRfc);
        (await ActualizarAsync()).ShouldBe(0);
        var detenida = await VerAsync(sinRfc);
        detenida.Actualizando.ShouldBeFalse(); // no se reintenta sola hasta que cambie algo
        detenida.Error.ShouldNotBeNull().ShouldContain("RFC");

        await SubirAsync("mortero.xml", TipoDeDocumento.Cfdi, Xml("6F9619FF-8B86-D011-B42D-00C04FC964A3", 1160m));
        _motor.Caido = true;
        (await ActualizarAsync()).ShouldBe(0);
        var caida = await VerAsync();
        caida.Actualizando.ShouldBeTrue();
        caida.Error.ShouldNotBeNullOrWhiteSpace();

        _motor.Caido = false;
        (await ActualizarAsync()).ShouldBe(1);
        var bien = await VerAsync();
        bien.Actualizando.ShouldBeFalse();
        bien.Error.ShouldBeNull();
    }
}
