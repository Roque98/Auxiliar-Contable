using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Bancos;
using AuxiliarContable.Boveda;
using AuxiliarContable.Conversaciones;
using AuxiliarContable.Documentos;
using AuxiliarContable.IA;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Estados de cuenta y conciliación: la consola toma de la bóveda los archivos correctos (de ese
/// cliente, de ese mes, del tipo correcto), se los manda al motor fiscal con el RFC del perfil y
/// devuelve lo que el motor dice. El motor es falso: aquí no se prueba su lógica (eso vive en
/// motores-fiscales), sino lo que hace la consola alrededor.
/// </summary>
public class BancosIntegrationTests : ConJerarquiaTestBase
{
    private const string Xml = """<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0"/>""";

    private readonly IBancosAppService _bancos;
    private readonly IBovedaAppService _boveda;
    private readonly IPerfilFiscalAppService _perfil;
    private readonly MotorFiscalDePrueba _motor;
    private readonly ModeloConversacionalDePrueba _modelo;
    private Guid _cliente;
    private Guid _otroCliente;

    public BancosIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _bancos = GetRequiredService<IBancosAppService>();
        _boveda = GetRequiredService<IBovedaAppService>();
        _perfil = GetRequiredService<IPerfilFiscalAppService>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
        _modelo = (ModeloConversacionalDePrueba)GetRequiredService<IModeloConversacional>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _motor.Reiniciar();
        _modelo.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Panadería Lupita");
        _otroCliente = await CrearOrganizacionAsync("Otro cliente");
        await WithUnitOfWorkAsync(() =>
            GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
    }

    private static int Anio => DateTime.Now.Year;

    private static int Mes => DateTime.Now.Month;

    private Task<ResultadoDeSubidaDto> SubirAsync(Guid cliente, string nombre, TipoDeDocumento tipo, string contenido) =>
        ComoAsync(Administrador, () => _boveda.SubirAsync(cliente, tipo,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(contenido)), nombre, "application/octet-stream")));

    private Task PonerRfcAsync(Guid cliente, string rfc) =>
        ComoAsync(Administrador, () => _perfil.ResponderAsync(cliente, new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = rfc }));

    private static ResultadoDeConciliacion UnResultado() => new()
    {
        Cuentas = [new CuentaLeida { Banco = "BANORTE", Cuenta = "0123456789", Cuadra = true }],
        Movimientos =
        [
            new MovimientoLeido { Fecha = "2026-11-02", Concepto = "SPEI RECIBIDO ACME", Naturaleza = "abono", Importe = 11600m,
                Conciliacion = new ConciliacionDeMovimiento { Estado = "conciliado", Facturas = ["U1"], Probabilidad = 0.99m } },
            new MovimientoLeido { Fecha = "2026-11-20", Concepto = "DEPOSITO EN EFECTIVO", Naturaleza = "abono", Importe = 3480m,
                Conciliacion = new ConciliacionDeMovimiento { Estado = "sin_factura", Motivo = "sin factura identificada" } },
        ],
        Facturas = [new FacturaDelMotor { Uuid = "U1", Tercero = "ACME", Importe = 11600m, Naturaleza = "Emitidos" }],
        FacturasSinMovimiento = [new FacturaDelMotor { Uuid = "U2", Tercero = "PROVEEDORA", Importe = 999m, Motivo = "sin pago en el periodo" }],
    };

    [Fact]
    public async Task Concilia_con_los_archivos_del_mes_de_ese_cliente_y_el_RFC_de_su_perfil()
    {
        await PonerRfcAsync(_cliente, "GODE561231GR8");
        await SubirAsync(_cliente, "banorte.pdf", TipoDeDocumento.EstadoDeCuenta, "%PDF-1.4 estado");
        await SubirAsync(_cliente, "factura.xml", TipoDeDocumento.Cfdi, Xml);
        await SubirAsync(_cliente, "ticket.pdf", TipoDeDocumento.Ticket, "%PDF-1.4 ticket");
        await SubirAsync(_otroCliente, "ajeno.pdf", TipoDeDocumento.EstadoDeCuenta, "%PDF-1.4 ajeno");
        _motor.Conciliacion = UnResultado();

        var r = await ComoAsync(Administrador, () => _bancos.ConciliarAsync(_cliente, Anio, Mes));

        _motor.RfcRecibido.ShouldBe("GODE561231GR8");
        _motor.EstadosRecibidos.ShouldBe(["banorte.pdf"], "ni el ticket ni el estado de cuenta de otro cliente");
        _motor.FacturasRecibidas.ShouldBe(["factura.xml"]);
        r.EstadosDeCuenta.ShouldBe(1);
        r.Facturas.ShouldBe(1);
        r.Resultado.Movimientos.Count.ShouldBe(2);
        r.Resultado.FacturasSinMovimiento.Single().Uuid.ShouldBe("U2");
    }

    [Fact]
    public async Task Otro_mes_no_entra()
    {
        await PonerRfcAsync(_cliente, "GODE561231GR8");
        await SubirAsync(_cliente, "banorte.pdf", TipoDeDocumento.EstadoDeCuenta, "%PDF-1.4 estado");
        var antes = DateTime.Now.AddMonths(-2);

        var error = await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Administrador, () => _bancos.ConciliarAsync(_cliente, antes.Year, antes.Month)));

        error.Message.ShouldContain("No hay estados de cuenta");
        _motor.EstadosRecibidos.ShouldBeEmpty();
    }

    [Fact]
    public async Task Lo_que_llega_al_mes_siguiente_se_concilia_en_su_mes()
    {
        // El estado de cuenta del mes pasado y sus facturas se suben hoy.
        await PonerRfcAsync(_cliente, "GODE561231GR8");
        var anterior = DateTime.Now.AddMonths(-1);
        await ComoAsync(Administrador, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.EstadoDeCuenta,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4 estado")), "banorte.pdf", "application/pdf"),
            anterior.Year, anterior.Month));
        await SubirAsync(_cliente, "factura.xml", TipoDeDocumento.Cfdi,
            $"""<cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0" Fecha="{anterior:yyyy-MM}-28T10:00:00"/>""");
        await SubirAsync(_cliente, "de-hoy.xml", TipoDeDocumento.Cfdi,
            $"""<cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0" Fecha="{DateTime.Now:yyyy-MM}-01T10:00:00"/>""");
        _motor.Conciliacion = UnResultado();

        await ComoAsync(Administrador, () => _bancos.ConciliarAsync(_cliente, anterior.Year, anterior.Month));

        _motor.EstadosRecibidos.ShouldBe(["banorte.pdf"]);
        _motor.FacturasRecibidas.ShouldBe(["factura.xml"], "la de este mes no entra");
    }

    [Fact]
    public async Task Sin_RFC_en_el_perfil_no_se_concilia()
    {
        await SubirAsync(_cliente, "banorte.pdf", TipoDeDocumento.EstadoDeCuenta, "%PDF-1.4 estado");

        var error = await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Administrador, () => _bancos.ConciliarAsync(_cliente, Anio, Mes)));

        error.Message.ShouldContain("RFC");
        _motor.EstadosRecibidos.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_cliente_no_concilia_ni_lee_lo_de_otro()
    {
        var ajeno = await SubirAsync(_otroCliente, "ajeno.pdf", TipoDeDocumento.EstadoDeCuenta, "%PDF-1.4 ajeno");

        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _bancos.ConciliarAsync(_otroCliente, Anio, Mes)));
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(Usuario, () => _bancos.LeerEstadoDeCuentaAsync(_otroCliente, ajeno.Documento.Id)));
        // Ni con su propio cliente y el id de un documento ajeno.
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(Usuario, () => _bancos.LeerEstadoDeCuentaAsync(_cliente, ajeno.Documento.Id)));
        _motor.EstadosRecibidos.ShouldBeEmpty();
    }

    [Fact]
    public async Task Lee_un_estado_de_cuenta_y_rechaza_lo_que_no_lo_es()
    {
        var estado = await SubirAsync(_cliente, "banorte.pdf", TipoDeDocumento.EstadoDeCuenta, "%PDF-1.4 estado");
        var factura = await SubirAsync(_cliente, "factura.xml", TipoDeDocumento.Cfdi, Xml);
        _motor.Lectura = new LecturaDeEstados { Cuentas = [new CuentaLeida { Banco = "BANORTE", Cuadra = true }] };

        var lectura = await ComoAsync(Usuario, () => _bancos.LeerEstadoDeCuentaAsync(_cliente, estado.Documento.Id));

        lectura.Cuentas.Single().Cuadra.ShouldBeTrue();
        _motor.EstadosRecibidos.ShouldBe(["banorte.pdf"]);
        await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _bancos.LeerEstadoDeCuentaAsync(_cliente, factura.Documento.Id)));
    }

    [Fact]
    public async Task Con_el_motor_caido_se_dice_sin_detalles_tecnicos()
    {
        await PonerRfcAsync(_cliente, "GODE561231GR8");
        await SubirAsync(_cliente, "banorte.pdf", TipoDeDocumento.EstadoDeCuenta, "%PDF-1.4 estado");
        _motor.Caido = true;

        var error = await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Administrador, () => _bancos.ConciliarAsync(_cliente, Anio, Mes)));

        error.Message.ShouldContain("no está disponible");
    }

    [Fact]
    public async Task El_despacho_concilia_desde_el_chat_y_el_modelo_recibe_un_resumen()
    {
        await PonerRfcAsync(_cliente, "GODE561231GR8");
        await SubirAsync(_cliente, "banorte.pdf", TipoDeDocumento.EstadoDeCuenta, "%PDF-1.4 estado");
        _motor.Conciliacion = UnResultado();
        _modelo.UsarHerramienta(HerramientasDelAsistente.ConciliarMes, new { anio = Anio, mes = Mes });
        _modelo.Contestar("La cuenta de Banorte cuadra; falta la factura de un depósito de $3,480.");

        var conversacion = await ComoAsync(Administrador, () =>
            Asistente().EnviarAsync(_cliente, new EnviarMensajeDto { Texto = "¿Qué falta conciliar este mes?" }));

        var resumen = _modelo.Solicitudes[1].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>();
        resumen.EsError.ShouldBeFalse();
        resumen.Contenido.ShouldContain("\"conFactura\":1");
        resumen.Contenido.ShouldContain("DEPOSITO EN EFECTIVO");
        resumen.Contenido.ShouldContain("PROVEEDORA");
        resumen.Contenido.ShouldContain("…6789", customMessage: "solo la terminación de la cuenta");
        resumen.Contenido.ShouldNotContain("0123456789");
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Concilió el banco"]);
    }

    [Fact]
    public async Task El_cliente_no_tiene_la_herramienta_de_conciliar()
    {
        _modelo.Contestar("Hola.");

        await ComoAsync(Usuario, () => Asistente().EnviarAsync(_cliente, new EnviarMensajeDto { Texto = "hola" }));

        _modelo.Solicitudes.Single().Herramientas.Select(h => h.Nombre).ShouldNotContain(HerramientasDelAsistente.ConciliarMes);
    }

    private Task SubirFacturaAsync(Guid cliente, string nombre, DateTime fecha) =>
        SubirAsync(cliente, nombre, TipoDeDocumento.Cfdi,
            $"""<cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0" Fecha="{fecha:yyyy-MM-dd}T10:00:00"/>""");

    [Fact]
    public async Task El_diagnostico_de_facturas_manda_primero_las_del_mes_y_luego_la_historia()
    {
        await PonerRfcAsync(_cliente, "GODE561231GR8");
        var mes = new DateTime(Anio, Mes, 1).AddMonths(-1);
        await SubirFacturaAsync(_cliente, "del-mes.xml", mes.AddDays(9));
        await SubirFacturaAsync(_cliente, "hace-un-mes.xml", mes.AddMonths(-1).AddDays(3));
        await SubirFacturaAsync(_cliente, "hace-tres.xml", mes.AddMonths(-3).AddDays(3));
        await SubirFacturaAsync(_cliente, "hace-cuatro.xml", mes.AddMonths(-4).AddDays(3));
        await SubirFacturaAsync(_cliente, "del-siguiente.xml", mes.AddMonths(1).AddDays(3));
        _motor.Diagnostico = new DiagnosticoDeFacturas
        {
            Emitidas = new LadoDelDiagnostico
            {
                PorRevisar = [new ApartadoPorRevisar { Avisos = ["El complemento paga la factura PUE; ¿querían relacionar la PPD?"] }],
            },
        };

        // El cliente también lo ve: es quien corrige sus facturas emitidas.
        var r = await ComoAsync(Usuario, () => _bancos.DiagnosticarFacturasAsync(_cliente, mes.Year, mes.Month));

        _motor.FacturasDiagnosticadas.ShouldBe(["del-mes.xml", "hace-un-mes.xml", "hace-tres.xml"],
            "primero las del mes; la de hace cuatro meses y la del mes siguiente no entran");
        _motor.ArchivosDelPeriodo.ShouldBe(1);
        _motor.RfcRecibido.ShouldBe("GODE561231GR8");
        r.Emitidas.PorRevisar.Single().Avisos.Single().ShouldContain("PUE");
    }

    [Fact]
    public async Task Sin_facturas_del_mes_no_hay_diagnostico_y_lo_ajeno_no_existe()
    {
        await PonerRfcAsync(_cliente, "GODE561231GR8");
        var antes = DateTime.Now.AddMonths(-2);

        var error = await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Administrador, () => _bancos.DiagnosticarFacturasAsync(_cliente, antes.Year, antes.Month)));
        error.Message.ShouldContain("No hay facturas");

        await SubirFacturaAsync(_otroCliente, "ajena.xml", DateTime.Now);
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(Usuario, () => _bancos.DiagnosticarFacturasAsync(_otroCliente, Anio, Mes)));
        _motor.FacturasDiagnosticadas.ShouldBeEmpty();
    }

    [Fact]
    public async Task Mis_facturas_se_clasifican_con_el_giro_del_perfil()
    {
        await PonerRfcAsync(_cliente, "GODE561231GR8");
        await ComoAsync(Administrador, () => _perfil.ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Giro, Valor = "Panadería y pastelería" }));
        var mes = new DateTime(Anio, Mes, 1).AddMonths(-1);
        await SubirFacturaAsync(_cliente, "del-mes.xml", mes.AddDays(9));
        await SubirFacturaAsync(_cliente, "anterior.xml", mes.AddMonths(-1).AddDays(3));
        _motor.Clasificadas = new FacturasClasificadas
        {
            Actividad = new ActividadDelGiro { Id = "A0600", Nombre = "Panadería" },
            Facturas = [new FacturaClasificada { Uuid = "U1", Lado = "recibida", Categoria = "insumo_directo" }],
        };

        // El cliente también las ve: son las suyas.
        var r = await ComoAsync(Usuario, () => _bancos.ClasificarFacturasAsync(_cliente, mes.Year, mes.Month));

        _motor.GiroRecibido.ShouldBe("Panadería y pastelería");
        _motor.FacturasDiagnosticadas.ShouldBe(["del-mes.xml", "anterior.xml"]);
        _motor.ArchivosDelPeriodo.ShouldBe(1);
        r.Actividad!.Nombre.ShouldBe("Panadería");
        r.Facturas.Single().Categoria.ShouldBe("insumo_directo");
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(Usuario, () => _bancos.ClasificarFacturasAsync(_otroCliente, mes.Year, mes.Month)));
    }

    private IAsistenteAppService Asistente() => GetRequiredService<IAsistenteAppService>();
}
