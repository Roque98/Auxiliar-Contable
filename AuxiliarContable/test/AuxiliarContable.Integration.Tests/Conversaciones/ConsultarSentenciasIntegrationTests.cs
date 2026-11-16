using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Conversaciones;
using AuxiliarContable.IA;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.Sentencias;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// La librería de sentencias: el chat recibe la conclusión y la clave de cada tesis (no el texto completo si ya está
/// estudiada), vale en las dos clases de conversación, la pantalla busca sin IA y solo el equipo la actualiza.
/// </summary>
public class ConsultarSentenciasIntegrationTests : ConJerarquiaTestBase
{
    private readonly IAsistenteAppService _asistente;
    private readonly ISentenciasAppService _sentencias;
    private readonly ModeloConversacionalDePrueba _modelo;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;

    public ConsultarSentenciasIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _asistente = GetRequiredService<IAsistenteAppService>();
        _sentencias = GetRequiredService<ISentenciasAppService>();
        _modelo = (ModeloConversacionalDePrueba)GetRequiredService<IModeloConversacional>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _modelo.Reiniciar();
        _motor.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Panadería Lupita");
        await WithUnitOfWorkAsync(() =>
            GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
    }

    private static SentenciaDelMotor Tesis(bool estudiada = true) => new()
    {
        Id = "tfja:48314",
        Fuente = "TFJA",
        Clave = "IX-J-2aS-115",
        Tipo = "jurisprudencia",
        Instancia = "Segunda Sección",
        Rubro = "MATERIALIDAD DE LAS OPERACIONES. ES INSUFICIENTE LA PERICIAL CONTABLE QUE SOLO DEMUESTRA EL REGISTRO",
        Fecha = "2026-10-26",
        Texto = "TEXTO_COMPLETO " + new string('x', 5000),
        Estudio = estudiada
            ? new EstudioDeSentencia
            {
                Conclusion = "Registrar la operación no prueba que existió.",
                AFavor = "autoridad",
                ReglaPractica = "Junta contratos, entregables y pagos de cada proveedor.",
                AplicaCuando = "El SAT cuestiona servicios de un proveedor.",
                QueDocumentar = ["contrato", "entregables"],
                Fundamentos = ["CFF art. 28 fr. I"],
                Temas = ["materialidad"],
                Vigencia = "Sin señales de cambio en el texto",
                Relevancia = "alta",
            }
            : null,
    };

    private BloqueDeResultadoDeHerramienta ResultadoQueRecibioElModelo() =>
        _modelo.Solicitudes[1].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>();

    [Fact]
    public async Task El_modelo_recibe_la_conclusion_y_la_clave_sin_el_texto_completo()
    {
        _motor.Sentencias.Add(Tesis());
        _modelo.UsarHerramienta(HerramientasDelAsistente.ConsultarSentencias, new { situacion = "facturas de un proveedor sin evidencia", fuente = "TFJA" });
        _modelo.Contestar("Guarda contratos y entregables (TFJA IX-J-2aS-115).");

        var conversacion = await ComoAsync(Usuario, () =>
            _asistente.EnviarAsync(_cliente, new EnviarMensajeDto { Texto = "Le compré servicios a un proveedor, ¿qué guardo?" }));

        _motor.ConsultasDeSentencias.Single().ShouldBe(("facturas de un proveedor sin evidencia", "TFJA"));
        var resultado = ResultadoQueRecibioElModelo();
        resultado.EsError.ShouldBeFalse();
        resultado.Contenido.ShouldContain("TFJA IX-J-2aS-115");
        resultado.Contenido.ShouldContain("\"aFavor\":\"autoridad\"");
        resultado.Contenido.ShouldContain("CFF art. 28 fr. I");
        resultado.Contenido.ShouldNotContain("TEXTO_COMPLETO");
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Consultó sentencias"]);
    }

    [Fact]
    public async Task Una_tesis_sin_estudiar_va_con_su_texto_recortado()
    {
        _motor.Sentencias.Add(Tesis(estudiada: false));
        _modelo.UsarHerramienta(HerramientasDelAsistente.ConsultarSentencias, new { situacion = "materialidad" });
        _modelo.Contestar("Listo.");

        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, new EnviarMensajeDto { Texto = "materialidad" }));

        var contenido = ResultadoQueRecibioElModelo().Contenido;
        contenido.ShouldContain("TEXTO_COMPLETO");
        contenido.ShouldContain("[…]");
        contenido.Length.ShouldBeLessThan(4500);
    }

    [Fact]
    public async Task Con_el_motor_caido_el_modelo_recibe_un_error_propio_de_sentencias()
    {
        _motor.Caido = true;
        _modelo.UsarHerramienta(HerramientasDelAsistente.ConsultarSentencias, new { situacion = "multa por no declarar" });
        _modelo.Contestar("Ahorita no pude revisar las sentencias.");

        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, new EnviarMensajeDto { Texto = "Me multaron" }));

        var resultado = ResultadoQueRecibioElModelo();
        resultado.EsError.ShouldBeTrue();
        resultado.Contenido.ShouldContain("librería de sentencias no está disponible");
    }

    [Fact]
    public async Task Tambien_se_consulta_desde_la_conversacion_de_toda_la_cartera()
    {
        _motor.Sentencias.Add(Tesis());
        _modelo.UsarHerramienta(HerramientasDelAsistente.ConsultarSentencias, new { situacion = "materialidad de proveedores" });
        _modelo.Contestar("Hay jurisprudencia del TFJA.");

        await ComoAsync(Administrador, () =>
            _asistente.EnviarAsync(Guid.Empty, new EnviarMensajeDto { Texto = "¿Qué dicen los tribunales de materialidad?" }));

        _modelo.Solicitudes[0].Herramientas.Select(h => h.Nombre).ShouldContain(HerramientasDelAsistente.ConsultarSentencias);
        ResultadoQueRecibioElModelo().EsError.ShouldBeFalse();
    }

    [Fact]
    public async Task La_pantalla_busca_sin_IA_y_valida_la_fuente()
    {
        _motor.Sentencias.Add(Tesis());

        var r = await ComoAsync(Usuario, () => _sentencias.BuscarAsync(new BuscarSentenciasDto { Consulta = "materialidad", Fuente = "tfja" }));

        r.Resultados.Single().Clave.ShouldBe("IX-J-2aS-115");
        r.Resultados.Single().Estudio!.AFavor.ShouldBe("autoridad");
        r.HayMas.ShouldBeFalse();
        _motor.ConsultasDeSentencias.Single().ShouldBe(("materialidad", "TFJA"));
        _modelo.Solicitudes.ShouldBeEmpty("la pantalla no usa el chat");

        await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _sentencias.BuscarAsync(new BuscarSentenciasDto { Consulta = "materialidad", Fuente = "DOF" })));
    }

    [Fact]
    public async Task La_pantalla_pagina_de_diez_en_diez_y_dice_si_hay_mas()
    {
        for (var i = 1; i <= 12; i++)
        {
            _motor.Sentencias.Add(new SentenciaDelMotor { Id = $"tfja:{i}", Fuente = "TFJA", Clave = $"T-{i}", Rubro = "RUBRO", Texto = "texto" });
        }

        var primera = await ComoAsync(Usuario, () => _sentencias.BuscarAsync(new BuscarSentenciasDto { Consulta = "RESICO" }));
        var segunda = await ComoAsync(Usuario, () => _sentencias.BuscarAsync(new BuscarSentenciasDto { Consulta = "RESICO", Pagina = 2 }));

        primera.Resultados.Count.ShouldBe(10);
        primera.HayMas.ShouldBeTrue();
        segunda.Resultados.Select(t => t.Clave).ShouldBe(["T-11", "T-12"]);
        segunda.HayMas.ShouldBeFalse();
    }

    [Fact]
    public async Task Solo_el_equipo_actualiza_y_no_se_encima_una_actualizacion_con_otra()
    {
        var cliente = await ComoAsync(Usuario, () => _sentencias.GetEstadoAsync());
        cliente.PuedeActualizar.ShouldBeFalse();
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _sentencias.ActualizarAsync()));
        _motor.Actualizaciones.ShouldBe(0);

        var equipo = await ComoAsync(Administrador, () => _sentencias.GetEstadoAsync());
        equipo.PuedeActualizar.ShouldBeTrue();
        var r = await ComoAsync(Administrador, () => _sentencias.ActualizarAsync());
        r.Estado.Trabajo.Corriendo.ShouldBeTrue();

        await ComoAsync(Administrador, () => _sentencias.ActualizarAsync());
        _motor.Actualizaciones.ShouldBe(1);
    }

    [Fact]
    public async Task Con_el_motor_caido_la_pantalla_lo_dice()
    {
        _motor.Caido = true;

        var error = await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () => _sentencias.GetEstadoAsync()));
        error.Message.ShouldContain("no está disponible");
    }
}
