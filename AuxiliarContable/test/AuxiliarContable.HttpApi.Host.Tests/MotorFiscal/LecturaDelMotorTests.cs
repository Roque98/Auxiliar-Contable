using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.MotorFiscal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// El motor (Python) contesta en snake_case. Si un nombre no casa con la propiedad de .NET, el dato se pierde sin error;
/// aquí se lee una respuesta con la forma real de cada servicio para que eso no pase («valor_16» no lee «Valor16»).
/// </summary>
public class LecturaDelMotorTests
{
    private static MotorFiscalHttp Motor(string json) => new(
        new Fabrica(new Respuesta(json)),
        Options.Create(new MotorFiscalOptions { Url = "http://motor.prueba" }),
        NullLogger<MotorFiscalHttp>.Instance);

    [Fact]
    public async Task La_diot_se_lee_completa()
    {
        const string json = """
            {"periodo": "2026-03", "renglones": [{"valor16": 10000, "devol16": 0, "acr_excl16": 1600, "acr_prop16": 0,
              "no_prop16": 0, "no_req16": 480, "valor_fn": 800, "acr_excl_fn": 64, "acr_prop_fn": 0, "valor_fs": 0,
              "acr_excl_fs": 0, "acr_prop_fs": 0, "tasa0": 400, "exento": 200, "no_objeto": 0, "ret_iva": 0,
              "tipo_tercero": "04", "tipo_operacion": "85", "tipo_operacion_texto": "Otros", "rfc": "PRO030303CCC",
              "nombre": "EMISOR", "cfdi": 1, "iva_acreditable": 1664, "notas": [],
              "detalle": [{"uuid": "U1", "factura": "U1", "fecha": "2026-03-05", "como": "factura PUE", "base": 10000.0,
                           "iva": 1600.0, "no_cumple": "pagado en efectivo"}]}],
             "totales": {"valor16": 10000, "iva_acreditable": 1664, "no_req16": 480},
             "excluidos": [{"uuid": "U2", "rfc": "PRO030303CCC", "nombre": "EMISOR", "fecha": "2026-03-08", "tipo": "Ingreso",
                            "total": 5800.0, "motivo": "PPD sin complemento"}],
             "avisos": [], "proporcion": null, "vencimiento": "2026-04-30",
             "archivo": {"nombre": "DIOT.txt", "contenido_b64": "MDQ="}, "errores": []}
            """;
        var d = await Motor(json).DiotAsync("AAA010101AAA", "2026-03", [], [], []);
        var r = d.Renglones.ShouldHaveSingleItem();
        (r.Valor16, r.AcrExcl16, r.NoReq16, r.ValorFn, r.AcrExclFn, r.Tasa0, r.Exento).ShouldBe((10000m, 1600m, 480m, 800m, 64m, 400m, 200m));
        (r.TipoOperacion, r.TipoOperacionTexto, r.IvaAcreditable).ShouldBe(("85", "Otros", 1664m));
        r.Detalle.ShouldHaveSingleItem().NoCumple.ShouldBe("pagado en efectivo");
        (d.Totales.Valor16, d.Totales.NoReq16).ShouldBe((10000m, 480m));
        d.Excluidos.ShouldHaveSingleItem().Motivo.ShouldBe("PPD sin complemento");
        d.Archivo!.ContenidoB64.ShouldBe("MDQ=");
        d.Vencimiento.ShouldBe("2026-04-30");
    }

    [Fact]
    public async Task Los_estados_y_los_movimientos_de_la_balanza_se_leen()
    {
        const string json = """
            {"balanza": [{"cuenta": "102.01", "nombre": "Bancos", "nivel": 2, "naturaleza": "D", "saldo_inicial": 1,
                          "debe": 2, "haber": 0, "saldo_final": 3}],
             "archivos": [], "vencimiento": "2026-12-03", "avisos": [],
             "estados": {"desde": "2026-01", "hasta": "2026-10", "cuadre": 0.0, "preliminar": true, "advertencias": ["x"],
                         "resultado": [{"concepto": "= Utilidad (pérdida) neta", "importe": 5000.0, "fuente": "B-3 52.1.1 i)"}],
                         "situacion": [{"concepto": "ACTIVO", "importe": null, "fuente": "", "tipo": "titulo"}],
                         "razones": [{"clave": "razon_circulante", "nombre": "Razón circulante", "valor": 69.75,
                                      "unidad": "veces", "lectura": "..."}]},
             "movimientos": [{"cuenta": "102.01", "fecha": "2026-10-05", "poliza": "I-2026-10-0001", "tipo": "Ingresos",
                              "concepto": "Venta", "texto": "", "debe": 5800.0, "haber": 0.0, "uuid": null, "rfc": "CLI010101AA1"}]}
            """;
        var e = await Motor(json).ContabilidadElectronicaAsync("AAA010101AAA", 2026, 10, null, []);
        e.Balanza.ShouldHaveSingleItem().SaldoFinal.ShouldBe(3);
        var ef = e.Estados.ShouldNotBeNull();
        (ef.Desde, ef.Preliminar, ef.Cuadre).ShouldBe(("2026-01", true, 0m));
        ef.Resultado.ShouldHaveSingleItem().Importe.ShouldBe(5000);
        ef.Situacion.ShouldHaveSingleItem().Tipo.ShouldBe("titulo");
        ef.Razones.ShouldHaveSingleItem().Valor.ShouldBe(69.75m);
        var m = e.Movimientos.ShouldHaveSingleItem();
        (m.Poliza, m.Debe, m.Rfc).ShouldBe(("I-2026-10-0001", 5800m, "CLI010101AA1"));
    }

    [Fact]
    public async Task El_papel_de_impuestos_trae_sus_renglones()
    {
        const string json = """
            {"regimen": {"principal": "612", "claves": ["612"], "flujo": true}, "avisos": [], "supuestos": [], "errores": [],
             "archivo": null,
             "meses": [{"periodo": "2026-03", "isr_a_pagar": 373.48, "renglones_isr": [
                         {"concepto": "(=) ISR a pagar", "importe": 373.48, "fundamento": "", "tipo": "resultado"}],
                        "renglones_iva": [{"concepto": "(×) Tasa", "importe": 8.0, "fundamento": "art. 18-M LIVA", "tipo": "tasa"}]}]}
            """;
        var p = await Motor(json).PagosProvisionalesAsync("AAA010101AAA", [], "2026-03", null, [], [], null, false);
        var mes = p.Meses.ShouldHaveSingleItem();
        mes.RenglonesIsr.ShouldHaveSingleItem().Importe.ShouldBe(373.48m);
        mes.RenglonesIva.Single().Tipo.ShouldBe("tasa");
    }

    [Fact]
    public async Task La_anual_se_lee_completa()
    {
        const string json = """
            {"ejercicio": 2026, "tipo": "Persona moral, régimen general (art. 9 LISR)", "regimen": "601",
             "renglones": [{"concepto": "(=) ISR a cargo", "importe": 36000.0, "fundamento": "", "tipo": "resultado"}],
             "isr_del_ejercicio": 45000.0, "a_cargo": 36000.0, "a_favor": 0.0, "coeficiente_siguiente": 1.0,
             "deducciones_personales": [{"uso": "D01", "concepto": "Honorarios médicos", "rfc": "PRO030303CCC", "nombre": "X",
                                         "fecha": "2026-05-10", "importe": 1160.0, "uuid": "U", "efectivo": false}],
             "avisos": ["a"], "supuestos": [], "vence": "2027-03-31", "captura": {}, "errores": [],
             "retenciones": [{"uuid": "R1", "tipo": "intereses", "descripcion": "Intereses", "rfc_emisor": "BAN010101AAA",
                              "nombre_emisor": "BANCO", "periodo": "2026-12", "monto_operacion": 5000.0, "isr_retenido": 450.0,
                              "iva_retenido": 0.0}]}
            """;
        var a = await Motor(json).AnualAsync("AAA010101AAA", [], 2026, [], [], null, null, null, new Dictionary<string, decimal>());
        (a.IsrDelEjercicio, a.ACargo, a.AFavor, a.CoeficienteSiguiente, a.Vence).ShouldBe((45000m, 36000m, 0m, (decimal?)1.0m, "2027-03-31"));
        a.Renglones.ShouldHaveSingleItem().Tipo.ShouldBe("resultado");
        a.DeduccionesPersonales.ShouldHaveSingleItem().Importe.ShouldBe(1160);
        var r = a.Retenciones.ShouldHaveSingleItem();
        (r.Tipo, r.RfcEmisor, r.Periodo, r.IsrRetenido).ShouldBe(("intereses", "BAN010101AAA", "2026-12", 450m));
    }

    [Fact]
    public async Task La_interpretacion_de_claude_se_lee_completa()
    {
        const string json = """
            {"version": "2026-11-18", "huella": "abc", "fuentes": ["factura", "ticket_o_nota", "movimiento_del_banco"],
             "interpretacion": {"objeto": "Laptop", "coinciden_las_fuentes": "si", "diferencias": ["x"], "categoria": "inversion",
               "juicio_contable": "Activo fijo (NIF C-6)", "tratamiento_fiscal": "Inversión al 30 %", "confianza": "alta",
               "proveedor": {"que_vende": "Equipo de cómputo", "lo_conozco": false},
               "conceptos": [{"indice": 0, "que_es": "Laptop", "clave_coincide": true, "categoria": "inversion", "cuenta": "156.01",
                              "isr": "inversion", "iva": "acreditable", "porcentaje_inversion": 30, "fundamento": "art. 34-VII LISR"}],
               "alertas": [{"tipo": "requisito_fiscal", "mensaje": "m", "fundamento": "art. 27 LISR"}],
               "preguntas_al_cliente": ["¿Quién la usa?"]}}
            """;
        var r = await Motor(json).InterpretarOperacionAsync(new PerfilParaInterpretar(), new OperacionParaInterpretar(), null, null, null);
        r.Fuentes.Count.ShouldBe(3);
        var i = r.Interpretacion;
        (i.Objeto, i.CoincidenLasFuentes, i.JuicioContable, i.Confianza).ShouldBe(("Laptop", "si", "Activo fijo (NIF C-6)", "alta"));
        (i.Proveedor.QueVende, i.Proveedor.LoConozco).ShouldBe(("Equipo de cómputo", false));
        var c = i.Conceptos.ShouldHaveSingleItem();
        (c.ClaveCoincide, c.Cuenta, c.PorcentajeInversion, c.Fundamento).ShouldBe(((bool?)true, "156.01", (decimal?)30m, "art. 34-VII LISR"));
        i.Alertas.ShouldHaveSingleItem().Tipo.ShouldBe("requisito_fiscal");
        i.PreguntasAlCliente.ShouldHaveSingleItem();
    }

    private sealed class Respuesta(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class Fabrica(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
