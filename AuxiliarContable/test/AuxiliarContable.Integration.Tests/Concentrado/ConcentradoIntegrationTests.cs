using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Boveda;
using AuxiliarContable.Concentrado;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Hoja concentrado, pestaña «Deudas fiscales»: lo que le toca pagar al cliente de cada obligación que le aplica, con lo
/// atrasado (y su actualización y recargos) dentro de la obligación; de la cifra se baja a las facturas y de la factura a
/// su porqué y su origen. La ven el cliente y el despacho; los pagos los registra el despacho. Datos inventados.
/// </summary>
public class ConcentradoIntegrationTests : ConJerarquiaTestBase
{
    private const string Rfc = "GODE561231GR8";
    private const string Ingreso = "7A9619FF-8B86-D011-B42D-00C04FC96401";
    private const string Gasto = "7A9619FF-8B86-D011-B42D-00C04FC96402";
    private const string Efectivo = "7A9619FF-8B86-D011-B42D-00C04FC96403";

    private readonly IConcentradoAppService _concentrado;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;

    private static DateTime Dia => new(DateTime.Now.Year, DateTime.Now.Month, 1);
    private static string Periodo => $"{Dia:yyyy-MM}";

    public ConcentradoIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _concentrado = GetRequiredService<IConcentradoAppService>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _motor.Reiniciar();
        _motor.TodosLosMeses = true;
        _motor.DetalleDeProvisionales = new DetalleDelCalculo
        {
            Clase = "flujo",
            Ingresos =
            [
                new FacturaDelCalculo
                {
                    Uuid = Ingreso, Fecha = $"{Periodo}-01", Rfc = "XAXX010101000", Nombre = "PUBLICO", Tipo = "Ingreso", Metodo = "PUE", Total = 1160,
                    Base = 1000, Iva = 160, Acumulable = 1000, IvaCobrado = 160, Estado = "cobrada", Meses = [Periodo],
                    PorMes = new() { [Periodo] = new() { ["base"] = 1000, ["iva"] = 160, ["ret_isr"] = 0, ["ret_iva"] = 0 } },
                },
            ],
            Deducciones =
            [
                new FacturaDelCalculo
                {
                    Uuid = Gasto, Fecha = $"{Periodo}-02", Rfc = "AAA010101AAA", Nombre = "DESPACHO DE PRUEBA", Tipo = "Ingreso", Metodo = "PUE", Total = 1160,
                    Base = 1000, Iva = 160, Deducible = 1000, IvaAcreditable = 160, RetIsr = 100, Estado = "considerada", Meses = [Periodo],
                    PorMes = new() { [Periodo] = new() { ["deducible"] = 1000, ["iva_acreditable"] = 160, ["ret_isr"] = 100, ["ret_iva"] = 0 } },
                },
                new FacturaDelCalculo
                {
                    Uuid = Efectivo, Fecha = $"{Periodo}-03", Rfc = "BBB010101BBB", Nombre = "FERRETERIA DE PRUEBA", Tipo = "Ingreso", Metodo = "PUE",
                    Forma = "01", Total = 3480, Base = 3000, Iva = 480, Estado = "no_considerada",
                    Motivos =
                    [
                        new MotivoDelCalculo
                        {
                            Regla = "efectivo", Clase = "requisito", Hipotesis = "pagada en efectivo y pasa de $2,000", Articulo = "art. 27-III LISR",
                            Resultado = "NO DEDUCIBLE", Solucion = "Si fue con transferencia, pide que corrijan la forma de pago.",
                        },
                    ],
                },
            ],
        };
        _cliente = await CrearOrganizacionAsync("Papelería Concentrado");
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
        await ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = Rfc }));
        await ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Regimenes, Valor = JsonSerializer.Serialize(new[] { "612" }) }));
    }

    private Task SubirAsync(string uuid) =>
        ComoAsync(Usuario, () => GetRequiredService<IBovedaAppService>().SubirAsync(_cliente, TipoDeDocumento.Cfdi,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(
                $"""<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" Fecha="{Dia:yyyy-MM-dd}T10:00:00" Total="1160.00" TipoDeComprobante="I"><cfdi:Emisor Rfc="{Rfc}" Nombre="PAPELERIA"/><cfdi:Receptor Rfc="XAXX010101000" Nombre="PUBLICO"/><cfdi:Conceptos><cfdi:Concepto Descripcion="Cuadernos"/></cfdi:Conceptos><cfdi:Complemento><tfd:TimbreFiscalDigital UUID="{uuid}"/></cfdi:Complemento></cfdi:Comprobante>""")),
                uuid + ".xml", "application/xml"),
            Dia.Year, Dia.Month));

    [Fact]
    public async Task Deudas_fiscales_del_cliente_con_atrasos_detalle_y_porque_de_cada_factura()
    {
        await SubirAsync(Ingreso);
        var antes = await ComoAsync(Usuario, () => _concentrado.GetDeudasFiscalesAsync(_cliente, Dia.Year, Dia.Month));
        (antes.Calculado, antes.Actualizando, antes.PuedeRegistrarPagos).ShouldBe((false, true, false));

        await GetRequiredService<ActualizadorDeImpuestos>().ActualizarAsync(espera: TimeSpan.Zero);
        var d = await ComoAsync(Usuario, () => _concentrado.GetDeudasFiscalesAsync(_cliente, Dia.Year, Dia.Month));
        d.Calculado.ShouldBeTrue();
        d.ClaseDelCalculo.ShouldBe("flujo");
        d.Obligaciones.Select(o => o.Clave).ShouldBe(["isr", "iva", "ret_isr"]);
        d.NoAplican.ShouldContain(n => n.StartsWith("Nómina"));
        d.NoAplican.ShouldContain(n => n.StartsWith("IEPS"));
        var ret = d.Obligaciones.Single(o => o.Clave == "ret_isr");
        ret.Impuesto.ShouldBe(100);
        ret.Renglones.ShouldHaveSingleItem().Detalle.ShouldBe("retenciones_a_terceros_isr");

        // Los meses anteriores sin pagar y ya vencidos van dentro del ISR, con su actualización y recargos.
        var vencidos = Enumerable.Range(1, Dia.Month - 1).Count(k => ConcentradoAppService.Limite(Dia.Year, k) < DateTime.Now.Date);
        var isr = d.Obligaciones.Single(o => o.Clave == "isr");
        isr.Impuesto.ShouldBe(19.2m);
        isr.Adeudos.Count.ShouldBe(Dia.Month - 1);
        isr.Adeudos.Count(a => a.Recargos > 0).ShouldBe(vencidos);
        isr.Total.ShouldBe(Math.Round(19.2m + isr.Adeudos.Sum(a => a.Total), 2));
        if (vencidos > 0)
        {
            isr.Estado.ShouldBe("vencido");
            isr.Renglones.ShouldContain(r => r.Fundamento == "art. 21 CFF");
            isr.Recargos.ShouldBeGreaterThan(0);
        }

        d.TotalAPagar.ShouldBe(d.Obligaciones.Sum(o => o.Total));

        // El cliente no registra pagos; el despacho sí, y lo pagado deja de deberse.
        var pago = new RegistrarPagoDto { Anio = Dia.Year, Mes = Dia.Month, Clave = "isr", Importe = 19.2m, FechaDePago = DateTime.Now.Date };
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _concentrado.RegistrarPagoAsync(_cliente, pago)));
        var registrado = await ComoAsync(Administrador, () => _concentrado.RegistrarPagoAsync(_cliente, pago));
        var despues = await ComoAsync(Administrador, () => _concentrado.GetDeudasFiscalesAsync(_cliente, Dia.Year, Dia.Month));
        despues.PuedeRegistrarPagos.ShouldBeTrue();
        var isr2 = despues.Obligaciones.Single(o => o.Clave == "isr");
        isr2.Pagado.ShouldBe(19.2m);
        isr2.Total.ShouldBe(Math.Round(isr2.Adeudos.Sum(a => a.Total), 2));
        await ComoAsync(Administrador, () => _concentrado.BorrarPagoAsync(_cliente, registrado.Id));

        // De la cifra a las facturas: consideradas, no consideradas y pendientes.
        var ded = await ComoAsync(Usuario, () => _concentrado.GetDetalleAsync(_cliente, Dia.Year, Dia.Month, "deducciones", false));
        ded.Grupos.Single(g => g.Clave == "consideradas").Facturas.ShouldHaveSingleItem().Uuid.ShouldBe(Gasto);
        var no = ded.Grupos.Single(g => g.Clave == "no_consideradas");
        no.Facturas.ShouldHaveSingleItem().Uuid.ShouldBe(Efectivo);
        no.Importe.ShouldBe(3000);
        (await ComoAsync(Usuario, () => _concentrado.GetDetalleAsync(_cliente, Dia.Year, Dia.Month, "ingresos", true)))
            .Grupos.Single(g => g.Clave == "consideradas").Importe.ShouldBe(1000);

        // De la factura a su porqué: requisito con solución; y de dónde se tomó la que subió el cliente.
        var e = await ComoAsync(Usuario, () => _concentrado.GetFacturaAsync(_cliente, Dia.Year, Dia.Month, Efectivo.ToLowerInvariant()));
        e.Lado.ShouldBe("deduccion");
        e.Resumen.ShouldContain("No se consideró");
        e.Factura.Motivos.ShouldHaveSingleItem().Solucion.ShouldNotBeNullOrWhiteSpace();
        e.Origen.ShouldBeNull();
        var i = await ComoAsync(Usuario, () => _concentrado.GetFacturaAsync(_cliente, Dia.Year, Dia.Month, Ingreso));
        i.Lado.ShouldBe("ingreso");
        i.Origen.ShouldNotBeNull().Quien.ShouldBe("la subiste tú");
        i.Origen.Archivo.ShouldBe(Ingreso + ".xml");
    }

    [Fact]
    public async Task Quien_no_es_del_cliente_no_ve_sus_deudas()
    {
        var otro = await CrearOrganizacionAsync("Otra empresa");
        // Ni siquiera sabe que existe: el Autorizador contesta «no encontrado».
        await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() =>
            ComoAsync(Usuario, () => _concentrado.GetDeudasFiscalesAsync(otro, Dia.Year, Dia.Month)));
    }

    [Fact]
    public async Task Pestanas_contables_de_la_apertura_y_las_polizas_hasta_la_cuenta()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var guids = GetRequiredService<IGuidGenerator>();
            var apertura = new SaldosDeApertura(guids.Create(), _cliente);
            apertura.Poner(Dia.Year, 1, JsonSerializer.Serialize(new List<SaldoDeApertura>
            {
                new() { Cuenta = "102.01", Deudor = 10000 }, new() { Cuenta = "301.01", Acreedor = 10000 },
            }));
            await GetRequiredService<IRepository<SaldosDeApertura, Guid>>().InsertAsync(apertura);
            var mes = new PolizasDelMes(guids.Create(), _cliente, Dia.Year, Dia.Month);
            mes.Guardar(JsonSerializer.Serialize(new ResultadoContable
            {
                Polizas =
                [
                    new PolizaContable
                    {
                        Tipo = "Diario", Numero = 1, Fecha = $"{Periodo}-03", Concepto = "Ferretería de prueba · tornillos", Rfc = "AAA010101AAA", Uuid = Gasto,
                        Movimientos =
                        [
                            new MovimientoContable { Cuenta = "601.84", Debe = 1000 }, new MovimientoContable { Cuenta = "118.01", Debe = 160 },
                            new MovimientoContable { Cuenta = "201.01", Haber = 1160 },
                        ],
                    },
                    new PolizaContable
                    {
                        Tipo = "Ingresos", Numero = 2, Fecha = $"{Periodo}-04", Concepto = "Cliente de prueba · venta", Rfc = "BBB010101BBB",
                        Movimientos =
                        [
                            new MovimientoContable { Cuenta = "105.01", Debe = 2320 }, new MovimientoContable { Cuenta = "401.01", Haber = 2000 },
                            new MovimientoContable { Cuenta = "209.01", Haber = 320 },
                        ],
                    },
                ],
            }), DateTime.UtcNow, null);
            await GetRequiredService<IRepository<PolizasDelMes, Guid>>().InsertAsync(mes);
        });

        // Deudas contables: proveedores sí; el IVA trasladado no (es de «Deudas fiscales»).
        var deudas = await ComoAsync(Usuario, () => _concentrado.GetRubrosAsync(_cliente, Dia.Year, Dia.Month, "deudas_contables"));
        deudas.HayContabilidad.ShouldBeTrue();
        var proveedores = deudas.Rubros.ShouldHaveSingleItem();
        (proveedores.Codigo, proveedores.Saldo, proveedores.Nif).ShouldBe(("201", 1160m, "C-19"));
        deudas.Total.ShouldBe(1160);

        var activos = await ComoAsync(Usuario, () => _concentrado.GetRubrosAsync(_cliente, Dia.Year, Dia.Month, "activos"));
        activos.Rubros.Select(r => (r.Codigo, r.Saldo)).ShouldBe([("102", 10000m), ("105", 2320m), ("118", 160m)]);

        var capital = await ComoAsync(Usuario, () => _concentrado.GetRubrosAsync(_cliente, Dia.Year, Dia.Month, "capital"));
        capital.Rubros.Single(r => r.Codigo == "301").Saldo.ShouldBe(10000);
        capital.Rubros.Single(r => r.Codigo == "resultado_del_ejercicio").Saldo.ShouldBe(1000);

        var ingresos = await ComoAsync(Usuario, () => _concentrado.GetRubrosAsync(_cliente, Dia.Year, Dia.Month, "ingresos_contables"));
        ingresos.Rubros.ShouldHaveSingleItem().Saldo.ShouldBe(2000);

        // De proveedores a quién se le debe y la póliza.
        var cuenta = await ComoAsync(Usuario, () => _concentrado.GetCuentaAsync(_cliente, Dia.Year, Dia.Month, "201"));
        (cuenta.SaldoInicial, cuenta.Abonos, cuenta.SaldoFinal).ShouldBe((0m, 1160m, 1160m));
        var tercero = cuenta.Terceros.ShouldHaveSingleItem();
        (tercero.Rfc, tercero.Nombre, tercero.Saldo).ShouldBe(("AAA010101AAA", "Ferretería de prueba", 1160m));
        cuenta.Movimientos.ShouldHaveSingleItem().Uuid.ShouldBe(Gasto);
    }
}
