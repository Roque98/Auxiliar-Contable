using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Conversaciones;
using AuxiliarContable.IA;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using Shouldly;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// La herramienta que consulta las leyes en el motor fiscal: el modelo recibe el artículo con su
/// cita, un motor caído no rompe la conversación y vale en el chat de un cliente y en el de la cartera.
/// </summary>
public class ConsultarLeyesIntegrationTests : ConJerarquiaTestBase
{
    private readonly IAsistenteAppService _asistente;
    private readonly ModeloConversacionalDePrueba _modelo;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;

    public ConsultarLeyesIntegrationTests(PostgresFixture fixture) : base(fixture)
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
        await WithUnitOfWorkAsync(() =>
            GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
    }

    private static ResultadoDeLey Articulo(string texto) => new(
        "LISR", "Ley del Impuesto sobre la Renta", "27", "Título II, Capítulo II", "2026-01-01", null,
        "https://www.diputados.gob.mx/LeyesBiblio/pdf/LISR.pdf", texto);

    private BloqueDeResultadoDeHerramienta ResultadoQueRecibioElModelo() =>
        _modelo.Solicitudes[1].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>();

    [Fact]
    public async Task El_modelo_recibe_el_articulo_con_su_cita_y_la_accion_se_ve_en_la_conversacion()
    {
        _motor.Resultados.Add(Articulo("Las deducciones autorizadas en este Título deberán reunir los siguientes requisitos: …"));
        _modelo.UsarHerramienta(HerramientasDelAsistente.ConsultarLeyes, new { pregunta = "requisitos de las deducciones", ley = "lisr" });
        _modelo.Contestar("Deben ser estrictamente indispensables. Fuente: art. 27 LISR.");

        var conversacion = await ComoAsync(Usuario, () =>
            _asistente.EnviarAsync(_cliente, new EnviarMensajeDto { Texto = "¿Qué requisitos tiene un gasto para deducirlo?" }));

        _motor.Consultas.Single().ShouldBe(("requisitos de las deducciones", "lisr"));
        var resultado = ResultadoQueRecibioElModelo();
        resultado.EsError.ShouldBeFalse();
        resultado.Contenido.ShouldContain("\"ley\":\"LISR\"");
        resultado.Contenido.ShouldContain("\"articulo\":\"27\"");
        resultado.Contenido.ShouldContain("deducciones autorizadas");
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Consultó la ley"]);
    }

    [Fact]
    public async Task Un_articulo_largo_se_recorta_antes_de_mandarlo_al_modelo()
    {
        _motor.Resultados.Add(Articulo(new string('a', 10_000)));
        _modelo.UsarHerramienta(HerramientasDelAsistente.ConsultarLeyes, new { pregunta = "artículo 27 LISR" });
        _modelo.Contestar("Listo.");

        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, new EnviarMensajeDto { Texto = "artículo 27" }));

        var contenido = ResultadoQueRecibioElModelo().Contenido;
        contenido.Length.ShouldBeLessThan(4000);
        contenido.ShouldContain("[…]");
    }

    [Fact]
    public async Task Si_el_motor_esta_caido_el_modelo_recibe_un_error_y_la_conversacion_sigue()
    {
        _motor.Caido = true;
        _modelo.UsarHerramienta(HerramientasDelAsistente.ConsultarLeyes, new { pregunta = "deducción de automóviles" });
        _modelo.Contestar("Ahorita no pude revisar la ley; pregúntale a tu contador.");

        var conversacion = await ComoAsync(Usuario, () =>
            _asistente.EnviarAsync(_cliente, new EnviarMensajeDto { Texto = "¿Puedo deducir mi coche?" }));

        var resultado = ResultadoQueRecibioElModelo();
        resultado.EsError.ShouldBeTrue();
        resultado.Contenido.ShouldContain("no está disponible");
        conversacion.Mensajes.Last().Texto.ShouldContain("no pude revisar");
    }

    [Fact]
    public async Task La_pantalla_de_leyes_busca_sin_IA_y_un_motor_caido_se_dice()
    {
        var leyes = GetRequiredService<AuxiliarContable.Leyes.ILeyesAppService>();
        _motor.Resultados.Add(Articulo("Las deducciones autorizadas …"));

        var r = await ComoAsync(Usuario, () => leyes.BuscarAsync(new AuxiliarContable.Leyes.BuscarEnLeyesDto { Consulta = "deducciones", Ley = "LISR" }));

        r.Resultados.Single().Ley.ShouldBe("LISR");
        r.Resultados.Single().Articulo.ShouldBe("27");
        r.HayMas.ShouldBeFalse();
        _motor.Consultas.Single().ShouldBe(("deducciones", "LISR"));
        _modelo.Solicitudes.ShouldBeEmpty("la pantalla no usa el chat");

        _motor.Caido = true;
        var error = await Should.ThrowAsync<Volo.Abp.UserFriendlyException>(() =>
            ComoAsync(Usuario, () => leyes.BuscarAsync(new AuxiliarContable.Leyes.BuscarEnLeyesDto { Consulta = "deducciones" })));
        error.Message.ShouldContain("no está disponible");
    }

    [Fact]
    public async Task La_pantalla_pagina_de_diez_en_diez_y_dice_si_hay_mas()
    {
        var leyes = GetRequiredService<AuxiliarContable.Leyes.ILeyesAppService>();
        for (var i = 1; i <= 23; i++)
        {
            _motor.Resultados.Add(Articulo($"Artículo número {i}"));
        }

        var primera = await ComoAsync(Usuario, () => leyes.BuscarAsync(new AuxiliarContable.Leyes.BuscarEnLeyesDto { Consulta = "RESICO" }));
        var tercera = await ComoAsync(Usuario, () => leyes.BuscarAsync(new AuxiliarContable.Leyes.BuscarEnLeyesDto { Consulta = "RESICO", Pagina = 3 }));

        primera.Resultados.Count.ShouldBe(10);
        primera.HayMas.ShouldBeTrue();
        primera.Resultados[0].Texto.ShouldBe("Artículo número 1");
        tercera.Resultados.Select(a => a.Texto).ShouldBe(["Artículo número 21", "Artículo número 22", "Artículo número 23"]);
        tercera.HayMas.ShouldBeFalse();
    }

    [Fact]
    public async Task Tambien_se_consulta_desde_la_conversacion_de_toda_la_cartera()
    {
        _motor.Resultados.Add(Articulo("Las personas morales deberán presentar declaración anual …"));
        _modelo.UsarHerramienta(HerramientasDelAsistente.ConsultarLeyes, new { pregunta = "plazo declaración anual personas morales" });
        _modelo.Contestar("A más tardar el 31 de marzo. Fuente: art. 76 fr. V LISR.");

        await ComoAsync(Administrador, () =>
            _asistente.EnviarAsync(Guid.Empty, new EnviarMensajeDto { Texto = "¿Cuándo vence la anual de morales?" }));

        _modelo.Solicitudes[0].Herramientas.Select(h => h.Nombre).ShouldContain(HerramientasDelAsistente.ConsultarLeyes);
        ResultadoQueRecibioElModelo().EsError.ShouldBeFalse();
        _motor.Consultas.Count.ShouldBe(1);
    }
}
