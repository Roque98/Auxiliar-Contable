using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Conversaciones;
using AuxiliarContable.IA;
using AuxiliarContable.LeyesDelCliente;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Normativa;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Fase 4a: el asistente CONSULTA y EXPLICA lo guardado del cliente. Busca operaciones, explica el porqué de la
/// deducibilidad con el artículo que dejó el motor, y las pólizas e impuestos completos solo los ve el despacho.
/// Lo de otro cliente no existe.
/// </summary>
public class ConsultasDelAsistenteIntegrationTests : ConJerarquiaTestBase
{
    private const string UuidTelmex = "AAAAAAAA-1111-2222-3333-444444444444";

    private readonly IAsistenteAppService _asistente;
    private readonly ModeloConversacionalDePrueba _modelo;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;
    private Guid _otroCliente;
    private Guid _telmex;

    public ConsultasDelAsistenteIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _asistente = GetRequiredService<IAsistenteAppService>();
        _modelo = (ModeloConversacionalDePrueba)GetRequiredService<IModeloConversacional>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _modelo.Reiniciar();
        _motor.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Panadería Lupita");
        _otroCliente = await CrearOrganizacionAsync("Otro cliente");
        await WithUnitOfWorkAsync(() =>
            GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));

        await WithUnitOfWorkAsync(async () =>
        {
            var operaciones = GetRequiredService<IRepository<Operacion, Guid>>();

            var telmex = new Operacion(Guid.NewGuid(), _cliente);
            telmex.Describir(new DateTime(2026, 10, 5), SentidoDeOperacion.Salida, 1_160m, "Servicio de internet octubre", "Teléfonos de México");
            telmex.LigarFactura(UuidTelmex, null, null);
            telmex.LigarTransaccion(FormaDeTransaccion.Efectivo, null, null, null);
            await operaciones.InsertAsync(telmex);
            _telmex = telmex.Id;

            var sinFactura = new Operacion(Guid.NewGuid(), _cliente);
            sinFactura.Describir(new DateTime(2026, 10, 20), SentidoDeOperacion.Salida, 450m, "Compra de harina", "Molinos del Sur");
            await operaciones.InsertAsync(sinFactura);

            var ajena = new Operacion(Guid.NewGuid(), _otroCliente);
            ajena.Describir(new DateTime(2026, 10, 7), SentidoDeOperacion.Salida, 999m, "Servicio de internet", "Teléfonos de México");
            await operaciones.InsertAsync(ajena);

            // El cálculo de impuestos guardado: la factura de Telmex no se dedujo por pagarse en efectivo.
            var calculo = new PagosProvisionales
            {
                Meses = [new MesDeImpuestos { Periodo = "2026-10", Metodo = "art. 14 LISR", IsrAPagar = 1_200m, IvaAPagar = 300m }],
                Detalle = new DetalleDelCalculo
                {
                    Clase = "pm",
                    Deducciones =
                    [
                        new FacturaDelCalculo
                        {
                            Uuid = UuidTelmex, Fecha = "2026-10-05", Nombre = "Teléfonos de México", Tipo = "Ingreso", Forma = "01",
                            Total = 1_160m, Base = 1_000m, Iva = 160m, Deducible = 0m, Estado = "no_considerada",
                            Motivos =
                            [
                                new MotivoDelCalculo
                                {
                                    Regla = "efectivo_mayor_2000", Clase = "requisito", Hipotesis = "Pago en efectivo",
                                    Articulo = "art. 27 fr. III LISR", Resultado = "no deducible",
                                    Solucion = "Pagar con transferencia o cheque nominativo.",
                                },
                            ],
                        },
                    ],
                },
            };
            var impuestos = new ImpuestosDelMes(Guid.NewGuid(), _cliente, 2026, 10, DateTime.UtcNow.AddHours(-1));
            impuestos.Guardar(JsonSerializer.Serialize(calculo), 2, 0, 0, 0, "h", DateTime.UtcNow, DateTime.UtcNow);
            await GetRequiredService<IRepository<ImpuestosDelMes, Guid>>().InsertAsync(impuestos);

            var contable = new ResultadoContable
            {
                Polizas =
                [
                    new PolizaContable
                    {
                        Operacion = telmex.Id.ToString(), Tipo = "Egresos", Numero = 7, Fecha = "2026-10-05", Concepto = "Internet Telmex",
                        Uuid = UuidTelmex,
                        Movimientos =
                        [
                            new MovimientoContable { Cuenta = "601.84", Nombre = "Gastos no deducibles", Debe = 1_000m },
                            new MovimientoContable { Cuenta = "101.01", Nombre = "Caja", Haber = 1_160m },
                        ],
                    },
                ],
                Revision = [new RenglonDeRevision { Operacion = telmex.Id.ToString(), Codigo = "EFECTIVO_MAYOR_2000", Nivel = "aviso", Mensaje = "Pagado en efectivo: no deducible (art. 27-III LISR)." }],
            };
            var polizas = new PolizasDelMes(Guid.NewGuid(), _cliente, 2026, 10);
            polizas.Guardar(JsonSerializer.Serialize(contable), DateTime.UtcNow, null);
            await GetRequiredService<IRepository<PolizasDelMes, Guid>>().InsertAsync(polizas);
        });
    }

    private BloqueDeResultadoDeHerramienta Resultado(int llamada = 1) =>
        _modelo.Solicitudes[llamada].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>();

    private static EnviarMensajeDto Mensaje(string texto) => new() { Texto = texto };

    [Fact]
    public async Task El_cliente_busca_sus_operaciones_y_no_ve_las_de_otro_cliente()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.BuscarOperaciones, new { texto = "teléfonos", anio = 2026, mes = 10 });
        _modelo.Contestar("Sí, la de Telmex está pagada en efectivo.");

        var conversacion = await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("¿Ya pagué lo de Telmex?")));

        var r = Resultado();
        r.EsError.ShouldBeFalse();
        r.Contenido.ShouldContain("\"total\":1");
        r.Contenido.ShouldContain(UuidTelmex);
        r.Contenido.ShouldContain("efectivo");
        r.Contenido.ShouldNotContain("999", customMessage: "la operación del otro cliente no aparece");
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Buscó en las operaciones"]);
    }

    [Fact]
    public async Task Busca_lo_que_no_tiene_factura()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.BuscarOperaciones, new { falta = "factura", sentido = "salida" });
        _modelo.Contestar("La harina no tiene factura.");

        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("¿Qué gastos no tienen factura?")));

        var r = Resultado();
        r.Contenido.ShouldContain("Compra de harina");
        r.Contenido.ShouldNotContain("Servicio de internet");
    }

    [Fact]
    public async Task El_cliente_pregunta_por_que_no_es_deducible_y_recibe_el_motivo_con_su_articulo_sin_las_polizas()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.ExplicarOperacion, new { uuid = UuidTelmex.ToLowerInvariant() });
        _modelo.Contestar("No se dedujo porque la pagaste en efectivo (art. 27 fr. III LISR).");

        var conversacion = await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("¿Por qué Telmex salió no deducible?")));

        var r = Resultado();
        r.EsError.ShouldBeFalse();
        r.Contenido.ShouldContain("art. 27 fr. III LISR");
        r.Contenido.ShouldContain("no_considerada");
        r.Contenido.ShouldContain("transferencia o cheque nominativo");
        r.Contenido.ShouldContain("EFECTIVO_MAYOR_2000", customMessage: "la revisión contable también se explica");
        r.Contenido.ShouldNotContain("601.84", customMessage: "las pólizas solo las ve el despacho");
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Revisó el porqué de una operación"]);
    }

    [Fact]
    public async Task El_despacho_ve_ademas_la_poliza_de_la_operacion()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.ExplicarOperacion, new { operacionId = _telmex });
        _modelo.Contestar("Se registró en gastos no deducibles.");

        await ComoAsync(Administrador, () => _asistente.EnviarAsync(_cliente, Mensaje("¿Cómo quedó lo de Telmex?")));

        var r = Resultado();
        r.Contenido.ShouldContain("601.84 Gastos no deducibles");
        r.Contenido.ShouldContain("art. 27 fr. III LISR");
    }

    [Fact]
    public async Task Una_operacion_de_otro_cliente_no_existe()
    {
        Guid ajena = default;
        await WithUnitOfWorkAsync(async () =>
            ajena = (await GetRequiredService<IRepository<Operacion, Guid>>().FirstAsync(o => o.OrganizacionId == _otroCliente)).Id);
        _modelo.UsarHerramienta(HerramientasDelAsistente.ExplicarOperacion, new { operacionId = ajena });
        _modelo.Contestar("No la encuentro.");

        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("Explícame esta")));

        var r = Resultado();
        r.EsError.ShouldBeTrue();
        r.Contenido.ShouldBe("No existe en este cliente.");
    }

    [Fact]
    public async Task Polizas_e_impuestos_solo_los_ve_el_despacho()
    {
        _modelo.Contestar("Hola.");
        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("hola")));
        var delCliente = _modelo.Solicitudes[0].Herramientas.Select(h => h.Nombre).ToList();
        delCliente.ShouldContain(HerramientasDelAsistente.BuscarOperaciones);
        delCliente.ShouldContain(HerramientasDelAsistente.ExplicarOperacion);
        delCliente.ShouldContain(HerramientasDelAsistente.LeyesQueLeAplican);
        delCliente.ShouldNotContain(HerramientasDelAsistente.PolizasDelMes);
        delCliente.ShouldNotContain(HerramientasDelAsistente.ImpuestosDelMes);

        // Aunque el modelo la pida sin que se le ofrezca, el servicio la niega y la plática sigue.
        _modelo.Reiniciar();
        _modelo.UsarHerramienta(HerramientasDelAsistente.ImpuestosDelMes, new { anio = 2026, mes = 10 });
        _modelo.Contestar("Eso lo revisa tu contador.");
        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("¿Cuánto pago de ISR?")));
        var negado = Resultado();
        negado.EsError.ShouldBeTrue();
        negado.Contenido.ShouldContain("despacho");
    }

    [Fact]
    public async Task El_despacho_consulta_el_pago_provisional_y_las_polizas_guardadas()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.ImpuestosDelMes, new { anio = 2026, mes = 10 });
        _modelo.UsarHerramienta(HerramientasDelAsistente.PolizasDelMes, new { anio = 2026, mes = 10, texto = "telmex" });
        _modelo.Contestar("ISR a pagar 1,200 e IVA 300.");

        var conversacion = await ComoAsync(Administrador, () => _asistente.EnviarAsync(_cliente, Mensaje("¿Cómo va octubre?")));

        var impuestos = Resultado(1);
        impuestos.EsError.ShouldBeFalse();
        impuestos.Contenido.ShouldContain("art. 14 LISR");
        impuestos.Contenido.ShouldContain("1200");
        impuestos.Contenido.ShouldContain("\"noConsideradas\":1");
        var polizas = Resultado(2);
        polizas.Contenido.ShouldContain("\"coinciden\":1");
        polizas.Contenido.ShouldContain("Caja");
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Consultó los impuestos", "Consultó las pólizas"]);
    }

    [Fact]
    public async Task Antes_de_operar_el_cliente_pregunta_y_el_asistente_recibe_el_expediente_sin_su_nombre()
    {
        foreach (var (campo, valor) in new[]
                 {
                     (CamposDelPerfil.TipoDePersona, "moral"),
                     (CamposDelPerfil.Nombre, "PANADERIA LUPITA S.A. DE C.V."),
                 })
        {
            await ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
                new ResponderDatoDelPerfilDto { Campo = campo, Valor = valor }));
        }

        _modelo.UsarHerramienta(HerramientasDelAsistente.AnalizarOperacion, new
        {
            descripcion = "Voy a vender pan a un cliente de Estados Unidos y me pagaría por transferencia",
            contraparte_tipo = "moral",
            contraparte_residencia = "usa",
            disparadores = new[] { "extranjero", "inventado" },
        });
        _modelo.Contestar("Puede ser exportación (art. 29 LIVA): ¿el pan sale del país?");

        var conversacion = await ComoAsync(Usuario, () =>
            _asistente.EnviarAsync(_cliente, Mensaje("Me salió un cliente en Estados Unidos, ¿qué tengo que cuidar?")));

        var r = Resultado();
        r.EsError.ShouldBeFalse();
        r.Contenido.ShouldContain("¿Califica como exportación?");
        var operacion = _motor.ExpedientesPedidos.ShouldHaveSingleItem();
        operacion.Descripcion.ShouldContain("Estados Unidos");
        operacion.Contraparte!.Residencia.ShouldBe("USA");
        operacion.Disparadores.ShouldBe(["extranjero"], "lo que el motor no conoce no viaja");
        var perfil = _motor.PerfilesDeExpediente.ShouldHaveSingleItem();
        perfil.TipoPersona.ShouldBe("moral");
        perfil.Nombre.ShouldBe("S.A.", "al motor solo va la forma jurídica, nunca el nombre del cliente");
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Analizó la operación con la ley"]);
    }

    [Fact]
    public async Task Encuadra_la_pregunta_con_su_perfil_sin_rfc_ni_nombre_ni_domicilio()
    {
        foreach (var (campo, valor) in new[]
                 {
                     (CamposDelPerfil.TipoDePersona, "fisica"),
                     (CamposDelPerfil.Nombre, "LUPITA PEREZ"),
                     (CamposDelPerfil.Giro, "panadería"),
                     (CamposDelPerfil.DomicilioFiscal, "Calle Uno 5, Centro, Cuernavaca, Morelos"),
                 })
        {
            await ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
                new ResponderDatoDelPerfilDto { Campo = campo, Valor = valor }));
        }

        _modelo.UsarHerramienta(HerramientasDelAsistente.EncuadrarPregunta, new { pregunta = "¿Cuánto gasté de harina este año?" });
        _modelo.Contestar("Para tu panadería la harina es materia prima.");

        var conversacion = await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("¿Cuánto gasté de harina este año?")));

        Resultado().Contenido.ShouldContain("INSUMO");
        var (perfil, pregunta) = _motor.EncuadresPedidos.ShouldHaveSingleItem();
        pregunta.ShouldBe("¿Cuánto gasté de harina este año?");
        perfil.TipoPersona.ShouldBe("fisica");
        perfil.Giro.ShouldBe("panadería");
        perfil.Municipio.ShouldBe("conocido", "del domicilio solo viaja que lo tenemos, nunca la dirección");
        JsonSerializer.Serialize(perfil).ShouldNotContain("LUPITA");
        JsonSerializer.Serialize(perfil).ShouldNotContain("Calle");
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Encuadró la pregunta con tu perfil"]);
    }

    [Fact]
    public async Task Suma_lo_que_gasto_con_varios_terminos_y_separa_lo_que_no_tiene_factura()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.BuscarOperaciones,
            new { terminos = new[] { "harina", "internet" }, sentido = "salida", anio = 2026 });
        _modelo.Contestar("Llevas 1,610.");

        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("¿Cuánto llevo de harina e internet?")));

        var r = Resultado();
        r.EsError.ShouldBeFalse();
        r.Contenido.ShouldContain("\"total\":2");
        r.Contenido.ShouldContain("\"importeTotal\":1610");
        r.Contenido.ShouldContain("\"importeSinFactura\":450", Case.Sensitive, "la harina no tiene factura");
        r.Contenido.ShouldNotContain("999", Case.Sensitive, "lo del otro cliente no se suma");
    }

    [Fact]
    public async Task La_tarifa_semanal_de_isr_sale_del_motor_tambien_en_la_conversacion_de_la_cartera()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.TarifaIsr, new { periodo = "semanal", @base = 2500 });
        _modelo.Contestar("Con 2,500 gravados a la semana se retienen 66.02.");

        var conversacion = await ComoAsync(Administrador, () =>
            _asistente.EnviarAsync(Guid.Empty, Mensaje("¿Cuánto ISR retengo a un sueldo semanal de 2,500 en 2026?")));

        var r = Resultado();
        r.EsError.ShouldBeFalse();
        r.Contenido.ShouldContain("Anexo 8");
        r.Contenido.ShouldContain("isr_a_retener");
        _motor.TarifasPedidas.ShouldHaveSingleItem().ShouldBe((7, 2500m, (int?)null));
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Consultó la tarifa de ISR"]);
    }

    [Fact]
    public async Task Lee_un_anexo_seguido_cuando_la_busqueda_solo_trae_un_trozo()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.LeerLey, new { clave = "RMF-A8", desde = 3 });
        _modelo.Contestar("La tarifa diaria empieza en 0.01 a 27.78 con 1.92 %.");

        var conversacion = await ComoAsync(Administrador, () => _asistente.EnviarAsync(Guid.Empty, Mensaje("Dame la tabla del Anexo 8")));

        Resultado().Contenido.ShouldContain("calculada en días");
        _motor.LeyesLeidasSeguidas.ShouldHaveSingleItem().ShouldBe(("RMF-A8", 3));
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Leyó la ley"]);
    }

    [Fact]
    public async Task Sin_domicilio_no_adivina_el_municipio_y_con_el_que_le_dicen_busca_lo_de_ese_lugar()
    {
        // Sin domicilio fiscal: la herramienta le dice al modelo que pregunte municipio y estado.
        _modelo.UsarHerramienta(HerramientasDelAsistente.LeyesQueLeAplican, new { tema = "anuncios" });
        _modelo.Contestar("Para contestarte necesito saber en qué municipio y estado está tu negocio.");
        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("¿Necesito permiso para poner un anuncio?")));
        var sin = Resultado();
        sin.EsError.ShouldBeFalse();
        sin.Contenido.ShouldContain(ConsultasDelAsistente.FaltaMunicipio);
        _motor.CatalogosPedidos.ShouldBeEmpty();

        // Le dice dónde está: al motor van ese estado y ese municipio, nada de su nombre.
        _modelo.Reiniciar();
        _modelo.UsarHerramienta(HerramientasDelAsistente.LeyesQueLeAplican, new { tema = "protección civil", estado = "Jalisco", municipio = "  Guadalajara " });
        _modelo.Contestar("En Guadalajara te piden el visto bueno de protección civil.");
        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("Guadalajara, Jalisco")));
        var con = Resultado();
        con.EsError.ShouldBeFalse();
        con.Contenido.ShouldContain("Reglamento de Protección Civil de Guadalajara");
        con.Contenido.ShouldNotContain("faltaMunicipio");
        var (perfil, estado) = _motor.CatalogosPedidos.ShouldHaveSingleItem();
        estado.ShouldBe("JAL");
        perfil["municipio"].ShouldBe("Guadalajara");
        perfil.Values.OfType<string>().ShouldNotContain(v => v.Contains("Lupita"));

        // Un estado que no existe no se manda al motor.
        _modelo.Reiniciar();
        _modelo.UsarHerramienta(HerramientasDelAsistente.LeyesQueLeAplican, new { estado = "Narnia", municipio = "Centro" });
        _modelo.Contestar("¿De qué estado?");
        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("Narnia")));
        var mal = Resultado();
        mal.EsError.ShouldBeTrue();
        mal.Contenido.ShouldContain("No reconozco el estado");
    }

    [Fact]
    public async Task En_la_cartera_sin_cliente_busca_las_leyes_del_lugar_que_le_dicen()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.LeyesQueLeAplican, new { estado = "Jalisco", municipio = "Guadalajara" });
        _modelo.Contestar("En Guadalajara lo rige el Reglamento de Protección Civil.");

        var conversacion = await ComoAsync(Administrador, () =>
            _asistente.EnviarAsync(Guid.Empty, Mensaje("¿Qué pide protección civil en Guadalajara, Jalisco?")));

        var r = Resultado();
        r.EsError.ShouldBeFalse();
        r.Contenido.ShouldContain("Reglamento de Protección Civil de Guadalajara");
        var (perfil, estado) = _motor.CatalogosPedidos.ShouldHaveSingleItem();
        estado.ShouldBe("JAL");
        perfil.Keys.OrderBy(k => k).ShouldBe(["estado", "municipio"]);
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Revisó las leyes que te aplican"]);
    }

    [Fact]
    public async Task El_reglamento_que_mandan_por_el_chat_lo_pasa_a_mis_leyes()
    {
        var boveda = GetRequiredService<AuxiliarContable.Boveda.IBovedaAppService>();
        static Volo.Abp.Content.RemoteStreamContent Pdf(string nombre, string texto) =>
            new(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 " + texto)), nombre, "application/pdf");
        await ComoAsync(Usuario, () => boveda.SubirAsync(_cliente, AuxiliarContable.Documentos.TipoDeDocumento.LeyLocal,
            Pdf("Reglamento de Anuncios Guadalajara.pdf", "anuncios")));
        await ComoAsync(Administrador, () => boveda.SubirAsync(_otroCliente, AuxiliarContable.Documentos.TipoDeDocumento.LeyLocal,
            Pdf("Reglamento ajeno.pdf", "ajeno")));

        _modelo.UsarHerramienta(HerramientasDelAsistente.PasarAMisLeyes, new
        {
            archivo = "Reglamento de Anuncios Guadalajara.pdf", ambito = "municipal", estado = "Jalisco", municipio = "Guadalajara", materia = "anuncios",
        });
        _modelo.Contestar("Listo, lo pasé a «Mis leyes»: se está leyendo.");
        var conversacion = await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("Te acabo de mandar el reglamento")));

        var r = Resultado();
        r.EsError.ShouldBeFalse();
        r.Contenido.ShouldContain("Mis leyes");
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Pasó la ley a «Mis leyes»"]);
        var ley = (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<LeyDelCliente, Guid>>().GetListAsync(x => x.OrganizacionId == _cliente)))
            .ShouldHaveSingleItem();
        ley.Ambito.ShouldBe(AmbitoDeLey.Municipal);
        ley.Estado.ShouldBe("JAL");
        ley.Municipio.ShouldBe("Guadalajara");
        ley.DocumentoId.ShouldNotBe(Guid.Empty);

        // Lo de otro cliente no se encuentra, y sin municipio un reglamento municipal no se pasa.
        _modelo.Reiniciar();
        _modelo.UsarHerramienta(HerramientasDelAsistente.PasarAMisLeyes, new { archivo = "Reglamento ajeno.pdf", ambito = "municipal", estado = "Jalisco", municipio = "Zapopan" });
        _modelo.UsarHerramienta(HerramientasDelAsistente.PasarAMisLeyes, new { archivo = "Reglamento de Anuncios Guadalajara.pdf", ambito = "municipal", estado = "Jalisco" });
        _modelo.Contestar("¿De qué municipio es?");
        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("Pásalo")));
        Resultado(1).Contenido.ShouldContain("No encuentro");
        Resultado(2).Contenido.ShouldContain("Falta el municipio");
    }

    [Fact]
    public async Task Lee_el_reglamento_que_subio_el_cliente_y_no_el_de_otro()
    {
        var sha = new string('a', 64);
        await WithUnitOfWorkAsync(async () =>
        {
            var leyes = GetRequiredService<IRepository<LeyDelCliente, Guid>>();
            var suya = new LeyDelCliente(Guid.NewGuid(), _cliente, Guid.NewGuid(), sha, "anuncios.pdf", AmbitoDeLey.Municipal, "JAL",
                "Guadalajara", "anuncios", DateTime.UtcNow);
            suya.Leida(10, 0, 40, "Reglamento de Anuncios de Prueba", DateTime.UtcNow);
            await leyes.InsertAsync(suya);
            var ajena = new LeyDelCliente(Guid.NewGuid(), _otroCliente, Guid.NewGuid(), new string('b', 64), "otra.pdf", AmbitoDeLey.Municipal,
                "JAL", "Zapopan", "anuncios", DateTime.UtcNow);
            ajena.Leida(5, 0, 20, "Reglamento ajeno", DateTime.UtcNow);
            await leyes.InsertAsync(ajena);
        });

        _modelo.UsarHerramienta(HerramientasDelAsistente.BuscarEnMisLeyes, new { texto = "licencia de anuncios adosados" });
        _modelo.Contestar("Sí: el artículo 12 pide licencia anual.");
        var conversacion = await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("Ya lo subí, ¿necesito licencia?")));

        var r = Resultado();
        r.EsError.ShouldBeFalse();
        r.Contenido.ShouldContain("licencia anual");
        r.Contenido.ShouldContain("Guadalajara");
        r.Contenido.ShouldNotContain("Reglamento ajeno");
        var (documentos, texto) = _motor.BusquedasEnLeyes.ShouldHaveSingleItem();
        documentos.ShouldBe([sha]);
        texto.ShouldBe("licencia de anuncios adosados");
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Revisó las leyes que subiste"]);
    }

    [Theory]
    [InlineData("PANADERIA LUPITA S.A. DE C.V.", "S.A.")]
    [InlineData("CONSTRUCTORA DEL SUR S. DE R.L. DE C.V.", "S. DE R.L.")]
    [InlineData("DESARROLLOS NORTE SAPI DE CV", "S.A.P.I.")]
    [InlineData("FUNDACION AYUDA A.C.", "A.C.")]
    [InlineData("JUAN PEREZ LOPEZ", null)]
    [InlineData("", null)]
    public void Del_nombre_solo_sale_la_forma_juridica(string nombre, string? forma) =>
        AnalizadorNormativo.FormaJuridica(nombre).ShouldBe(forma);
}
