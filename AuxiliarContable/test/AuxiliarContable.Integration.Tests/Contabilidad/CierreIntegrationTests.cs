using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Bancos;
using AuxiliarContable.Boveda;
using AuxiliarContable.Cartera;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// El cierre mensual: conciliar guarda la conciliación y deja partidas por lo que no tiene factura;
/// el despacho las resuelve (o le pregunta al cliente), cierra el mes y un mes cerrado ya no cambia.
/// </summary>
public class CierreIntegrationTests : ConJerarquiaTestBase
{
    private const string Rfc = "GODE561231GR8";

    private readonly ICierreAppService _cierre;
    private readonly IBancosAppService _bancos;
    private readonly IBovedaAppService _boveda;
    private readonly IPerfilFiscalAppService _perfil;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;
    private Guid _otroCliente;

    public CierreIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _cierre = GetRequiredService<ICierreAppService>();
        _bancos = GetRequiredService<IBancosAppService>();
        _boveda = GetRequiredService<IBovedaAppService>();
        _perfil = GetRequiredService<IPerfilFiscalAppService>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _motor.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Panadería Lupita");
        _otroCliente = await CrearOrganizacionAsync("Otro cliente");
        await WithUnitOfWorkAsync(() =>
            GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
        await ComoAsync(Administrador, () => _perfil.ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = Rfc }));
    }

    private static int Anio => DateTime.UtcNow.Year;

    private static int Mes => DateTime.UtcNow.Month;

    private static RemoteStreamContent Archivo(string nombre, string contenido) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(contenido)), nombre, "application/octet-stream");

    private Task<ResultadoDeSubidaDto> SubirAsync(string nombre, TipoDeDocumento tipo, string contenido, Guid? cliente = null) =>
        ComoAsync(Administrador, () => _boveda.SubirAsync(cliente ?? _cliente, tipo, Archivo(nombre, contenido), Anio, Mes));

    private static string Factura(string uuid) =>
        $"""<cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0" Fecha="{DateTime.UtcNow:yyyy-MM}-02T10:00:00" Folio="{uuid}"/>""";

    private static ResultadoDeConciliacion UnResultado(bool conDeposito = true) => new()
    {
        Cuentas = [new CuentaLeida { Banco = "BANORTE", Cuenta = "0123456789", Cuadra = true }],
        Movimientos =
        [
            new MovimientoLeido { Cuenta = "0123456789", Fecha = "2026-11-02", Concepto = "SPEI RECIBIDO ACME", Naturaleza = "abono", Importe = 11600m,
                Conciliacion = new ConciliacionDeMovimiento { Estado = "conciliado", Facturas = ["U1"] } },
            new MovimientoLeido { Cuenta = "0123456789", Fecha = "2026-11-20", Concepto = "DEPOSITO EN EFECTIVO", Naturaleza = "abono", Importe = 3480m,
                Conciliacion = new ConciliacionDeMovimiento { Estado = conDeposito ? "sin_factura" : "conciliado" } },
            new MovimientoLeido { Cuenta = "0123456789", Fecha = "2026-11-25", Concepto = "TRASPASO A CTA 999", Naturaleza = "cargo", Importe = 5000m,
                Conciliacion = new ConciliacionDeMovimiento { Estado = "pendiente" } },
        ],
    };

    private async Task ConDocumentosYConciliadoAsync(ResultadoDeConciliacion? resultado = null)
    {
        await SubirAsync("banorte.pdf", TipoDeDocumento.EstadoDeCuenta, "%PDF-1.4 estado");
        await SubirAsync("factura.xml", TipoDeDocumento.Cfdi, Factura("U1"));
        _motor.Conciliacion = resultado ?? UnResultado();
        await ComoAsync(Administrador, () => _bancos.ConciliarAsync(_cliente, Anio, Mes));
    }

    [Fact]
    public async Task Sin_estado_de_cuenta_faltan_documentos()
    {
        var cierre = await ComoAsync(Administrador, () => _cierre.GetAsync(_cliente, Anio, Mes));

        cierre.Etapa.ShouldBe(EtapaDelPeriodo.FaltanDocumentos);
        cierre.Conciliacion.ShouldBeNull();
        cierre.Revision.First().Hecho.ShouldBeFalse();
    }

    [Fact]
    public async Task Conciliar_guarda_la_conciliacion_y_deja_una_partida_por_movimiento_sin_factura()
    {
        await ConDocumentosYConciliadoAsync();

        var cierre = await ComoAsync(Usuario, () => _cierre.GetAsync(_cliente, Anio, Mes));

        cierre.Etapa.ShouldBe(EtapaDelPeriodo.ConPendientes);
        cierre.Conciliacion.ShouldNotBeNull();
        cierre.Conciliacion!.Resultado.Movimientos.Count.ShouldBe(3, "se guarda tal cual la devolvió el motor");
        cierre.Partidas.Count.ShouldBe(2);
        cierre.Partidas.ShouldAllBe(p => p.Pendiente && p.Resolucion == ResolucionDePartida.SinResolver);
        cierre.Partidas.ShouldContain(p => p.Descripcion.Contains("DEPOSITO EN EFECTIVO"));
        cierre.PuedeCerrar.ShouldBeFalse("el cliente no cierra");
    }

    [Fact]
    public async Task Lo_resuelto_sobrevive_a_otra_conciliacion_y_lo_que_ya_tiene_factura_se_quita()
    {
        await ConDocumentosYConciliadoAsync();
        var partidas = (await ComoAsync(Administrador, () => _cierre.GetAsync(_cliente, Anio, Mes))).Partidas;
        var traspaso = partidas.Single(p => p.Descripcion.Contains("TRASPASO"));
        await ComoAsync(Administrador, () => _cierre.ResolverAsync(_cliente, traspaso.Id,
            new ResolverPartidaDto { Resolucion = ResolucionDePartida.TraspasoPropio, Nota = "A su cuenta de inversión" }));

        // Llegó la factura del depósito: el motor ya lo concilia.
        _motor.Conciliacion = UnResultado(conDeposito: false);
        await ComoAsync(Administrador, () => _bancos.ConciliarAsync(_cliente, Anio, Mes));
        var cierre = await ComoAsync(Administrador, () => _cierre.GetAsync(_cliente, Anio, Mes));

        var unica = cierre.Partidas.ShouldHaveSingleItem();
        unica.Id.ShouldBe(traspaso.Id);
        unica.Resolucion.ShouldBe(ResolucionDePartida.TraspasoPropio);
        unica.Nota.ShouldBe("A su cuenta de inversión");
        cierre.Etapa.ShouldBe(EtapaDelPeriodo.ListoParaCerrar);
    }

    [Fact]
    public async Task El_despacho_pregunta_y_el_cliente_contesta_desde_su_pantalla()
    {
        await ConDocumentosYConciliadoAsync();
        var deposito = (await ComoAsync(Administrador, () => _cierre.GetAsync(_cliente, Anio, Mes)))
            .Partidas.Single(p => p.Descripcion.Contains("DEPOSITO"));

        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () => _cierre.ResolverAsync(_cliente, deposito.Id,
            new ResolverPartidaDto { Resolucion = ResolucionDePartida.PreguntarAlCliente })));
        await ComoAsync(Administrador, () => _cierre.ResolverAsync(_cliente, deposito.Id,
            new ResolverPartidaDto { Resolucion = ResolucionDePartida.PreguntarAlCliente, Nota = "¿De qué venta es este depósito?" }));

        var preguntas = await ComoAsync(Usuario, () => _cierre.GetPreguntasAsync(_cliente));
        preguntas.ShouldHaveSingleItem().Nota.ShouldBe("¿De qué venta es este depósito?");

        var respondida = await ComoAsync(Usuario, () => _cierre.ResponderAsync(_cliente, deposito.Id,
            new ResponderPartidaDto { Respuesta = "Venta de mostrador, ya pedí la factura global" }));

        respondida.Respuesta.ShouldBe("Venta de mostrador, ya pedí la factura global");
        respondida.Pendiente.ShouldBeTrue("contestar no la resuelve: decide el despacho");
        (await ComoAsync(Usuario, () => _cierre.GetPreguntasAsync(_cliente))).ShouldBeEmpty();
    }

    [Fact]
    public async Task El_cliente_no_resuelve_ni_cierra_y_no_ve_lo_de_otro()
    {
        await ConDocumentosYConciliadoAsync();
        var partida = (await ComoAsync(Usuario, () => _cierre.GetAsync(_cliente, Anio, Mes))).Partidas.First();

        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _cierre.ResolverAsync(_cliente, partida.Id,
            new ResolverPartidaDto { Resolucion = ResolucionDePartida.Revisado })));
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _cierre.CerrarAsync(_cliente, Anio, Mes)));
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _cierre.GetAsync(_otroCliente, Anio, Mes)));
        // Ni con su propio cliente y una partida que no es de él.
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Administrador, () => _cierre.ResolverAsync(_otroCliente, partida.Id,
            new ResolverPartidaDto { Resolucion = ResolucionDePartida.Revisado })));
    }

    [Fact]
    public async Task Si_cambian_los_documentos_hay_que_volver_a_conciliar()
    {
        await ConDocumentosYConciliadoAsync();
        await SubirAsync("otra.xml", TipoDeDocumento.Cfdi, Factura("U9"));

        var cierre = await ComoAsync(Administrador, () => _cierre.GetAsync(_cliente, Anio, Mes));

        cierre.Etapa.ShouldBe(EtapaDelPeriodo.PorConciliar);
        cierre.ConciliacionDesactualizada.ShouldBeTrue();
    }

    [Fact]
    public async Task Solo_se_cierra_lo_que_esta_listo_y_cerrado_ya_no_cambia_hasta_reabrirlo()
    {
        await ConDocumentosYConciliadoAsync();
        var error = await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () => _cierre.CerrarAsync(_cliente, Anio, Mes)));
        error.Message.ShouldContain("sin factura");

        foreach (var p in (await ComoAsync(Administrador, () => _cierre.GetAsync(_cliente, Anio, Mes))).Partidas)
        {
            await ComoAsync(Administrador, () => _cierre.ResolverAsync(_cliente, p.Id, new ResolverPartidaDto { Resolucion = ResolucionDePartida.SinFactura }));
        }

        var cerrado = await ComoAsync(Administrador, () => _cierre.CerrarAsync(_cliente, Anio, Mes));
        cerrado.Etapa.ShouldBe(EtapaDelPeriodo.Cerrado);
        cerrado.CerradoUtc.ShouldNotBeNull();

        var estado = (await ComoAsync(Administrador, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente, MaxResultCount = 10 })))
            .Items.First(d => d.Tipo == TipoDeDocumento.EstadoDeCuenta);
        (await Should.ThrowAsync<UserFriendlyException>(() => SubirAsync("tarde.xml", TipoDeDocumento.Cfdi, Factura("U7"))))
            .Message.ShouldContain("cerrado");
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () => _boveda.EliminarAsync(_cliente, estado.Id)));
        var antes = DateTime.UtcNow.AddMonths(-1);
        await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Administrador, () => _boveda.CambiarMesAsync(_cliente, estado.Id, antes.Year, antes.Month)));
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () => _bancos.ConciliarAsync(_cliente, Anio, Mes)));

        var reabierto = await ComoAsync(Administrador, () => _cierre.ReabrirAsync(_cliente, Anio, Mes));
        reabierto.Etapa.ShouldBe(EtapaDelPeriodo.ListoParaCerrar);
        await SubirAsync("tarde.xml", TipoDeDocumento.Cfdi, Factura("U7"));
    }

    [Fact]
    public async Task La_cartera_dice_en_que_va_el_cierre_de_cada_cliente()
    {
        await ConDocumentosYConciliadoAsync();

        var clientes = await ComoAsync(Administrador, () => GetRequiredService<ICarteraAppService>().GetClientesAsync(Anio, Mes));

        var lupita = clientes.Single(c => c.Id == _cliente);
        lupita.Etapa.ShouldBe(EtapaDelPeriodo.ConPendientes);
        lupita.PartidasPendientes.ShouldBe(2);
        clientes.Single(c => c.Id == _otroCliente).Etapa.ShouldBe(EtapaDelPeriodo.FaltanDocumentos);
    }

    [Fact]
    public async Task El_descargador_sube_facturas_por_el_RFC_del_cliente()
    {
        var r = await ComoAsync(Administrador, () => _boveda.SubirFacturaAsync(" gode561231gr8 ", Archivo("u1.xml", Factura("U1"))));

        r.EsDuplicado.ShouldBeFalse();
        (await ComoAsync(Administrador, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente, MaxResultCount = 10 })))
            .Items.ShouldHaveSingleItem().Tipo.ShouldBe(TipoDeDocumento.Cfdi);
        (await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Administrador, () => _boveda.SubirFacturaAsync("XAXX010101000", Archivo("u2.xml", Factura("U2"))))))
            .Message.ShouldContain("No tienes un cliente");
    }

    [Fact]
    public async Task Con_el_RFC_de_un_cliente_ajeno_no_se_sube_nada()
    {
        await ComoAsync(Administrador, () => _perfil.ResponderAsync(_otroCliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = "AAA010101AAA" }));

        var error = await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _boveda.SubirFacturaAsync("AAA010101AAA", Archivo("u1.xml", Factura("U1")))));

        error.Message.ShouldContain("No tienes un cliente", customMessage: "ajeno e inexistente dicen lo mismo");
    }

    [Fact]
    public async Task Un_estado_de_cuenta_sin_mes_lo_acomoda_el_motor_leyendo_su_periodo()
    {
        var subido = await ComoAsync(Administrador, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.EstadoDeCuenta, Archivo("bbva.pdf", "%PDF-1.4 bbva")));
        subido.Aviso.ShouldNotBeNull();
        _motor.Lectura = new LecturaDeEstados
        {
            Cuentas = [new CuentaLeida { Banco = "BBVA", PeriodoInicial = "2026-09-15", PeriodoFinal = "2026-10-14" }],
        };

        await WithUnitOfWorkAsync(() => GetRequiredService<FechadorDeEstadosDeCuenta>().FecharAsync(subido.Documento.Id));

        var documento = await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<Documento, Guid>>().GetAsync(subido.Documento.Id));
        documento.FechaDelDocumento.ShouldBe(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), "del 15/09 al 14/10: la mitad cae en septiembre");
        documento.Estado.ShouldBe(EstadoDeDocumento.Procesado);
    }

    [Fact]
    public async Task Si_el_motor_no_lee_el_periodo_queda_para_ponerle_el_mes_a_mano()
    {
        var subido = await ComoAsync(Administrador, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.EstadoDeCuenta, Archivo("foto.jpg", "%PDF-1.4 foto")));
        _motor.Lectura = new LecturaDeEstados { Cuentas = [new CuentaLeida { Banco = "BBVA" }] };

        await WithUnitOfWorkAsync(() => GetRequiredService<FechadorDeEstadosDeCuenta>().FecharAsync(subido.Documento.Id));
        var conError = (await ComoAsync(Administrador, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente, MaxResultCount = 10 })))
            .Items.Single();
        conError.Estado.ShouldBe(EstadoDeDocumento.ConError);
        conError.Motivo.ShouldBe(FechadorDeEstadosDeCuenta.SinPeriodo);

        var anterior = DateTime.UtcNow.AddMonths(-1);
        var corregido = await ComoAsync(Administrador, () => _boveda.CambiarMesAsync(_cliente, subido.Documento.Id, anterior.Year, anterior.Month));

        corregido.Estado.ShouldBe(EstadoDeDocumento.Procesado);
        corregido.Motivo.ShouldBeNull();
    }

    [Fact]
    public async Task Lo_que_ya_tiene_mes_no_lo_mueve_el_motor()
    {
        var subido = await SubirAsync("banorte.pdf", TipoDeDocumento.EstadoDeCuenta, "%PDF-1.4 estado");
        _motor.Lectura = new LecturaDeEstados { Cuentas = [new CuentaLeida { PeriodoFinal = "2020-01-31" }] };

        await WithUnitOfWorkAsync(() => GetRequiredService<FechadorDeEstadosDeCuenta>().FecharAsync(subido.Documento.Id));

        _motor.EstadosRecibidos.ShouldBeEmpty("ni siquiera se le pregunta");
    }
}
