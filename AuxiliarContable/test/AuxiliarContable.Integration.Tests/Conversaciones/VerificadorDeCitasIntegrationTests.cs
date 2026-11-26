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
/// Quien pregunta no puede saber si una cita está mal. Lo que el asistente cite tiene que venir en lo que consultó; si
/// no, se le devuelve una vez para verificarlo y la persona solo ve la respuesta corregida.
/// </summary>
public class VerificadorDeCitasIntegrationTests : ConJerarquiaTestBase
{
    private readonly IAsistenteAppService _asistente;
    private readonly ModeloConversacionalDePrueba _modelo;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;

    public VerificadorDeCitasIntegrationTests(PostgresFixture fixture) : base(fixture)
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

    [Fact]
    public void Detecta_lo_citado_que_no_vino_en_lo_consultado()
    {
        var consultado = new[] { "[{\"ley\":\"LISR\",\"articulo\":\"27\",\"texto\":\"… fracción III …\"}]", "regla 3.13.4. del RESICO" };

        VerificadorDeCitas.SinRespaldo("Fuente: art. 27 fr. III LISR y regla 3.13.4.", consultado).ShouldBeEmpty();
        VerificadorDeCitas.SinRespaldo("Lo dice el artículo 76 de la LISR y el art. 82 CFF.", consultado)
            .ShouldBe(["art. 76 LISR", "art. 82 CFF"]);
        VerificadorDeCitas.SinRespaldo("Revisa la regla 2.7.1.35.", consultado).ShouldBe(["regla 2.7.1.35."]);
        VerificadorDeCitas.SinRespaldo("Sin citas, solo orientación.", consultado).ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_cita_de_memoria_se_devuelve_y_la_persona_solo_ve_la_respuesta_verificada()
    {
        _modelo.Contestar("La anual de morales vence el 31 de marzo. Fuente: art. 76 LISR.");
        _modelo.UsarHerramienta(HerramientasDelAsistente.ConsultarLeyes, new { pregunta = "artículo 76 LISR declaración anual" });
        _modelo.Contestar("Vence a más tardar el 31 de marzo. Fuente: art. 76 fr. V LISR.");
        _motor.Resultados.Add(new ResultadoDeLey("LISR", "Ley del Impuesto sobre la Renta", "76", null, "2026-01-01", null, null,
            "Las personas morales … V. Presentar declaración … a más tardar el 31 de marzo …"));

        var conversacion = await ComoAsync(Usuario, () =>
            _asistente.EnviarAsync(_cliente, new EnviarMensajeDto { Texto = "¿Cuándo vence la anual de una moral?" }));

        _modelo.Solicitudes.Count.ShouldBe(3);
        var aviso = _modelo.Solicitudes[1].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeTexto>();
        aviso.Texto.ShouldContain("VERIFICACIÓN AUTOMÁTICA");
        aviso.Texto.ShouldContain("art. 76 LISR");
        conversacion.Mensajes.Select(m => m.Rol).ShouldBe([RolDelMensaje.Usuario, RolDelMensaje.Asistente],
            "el borrador sin respaldo no se le enseña a la persona");
        conversacion.Mensajes.Last().Texto.ShouldContain("fr. V");
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Consultó la ley"]);
    }

    [Fact]
    public async Task Lo_citado_que_si_se_consulto_pasa_sin_otra_vuelta()
    {
        _motor.Resultados.Add(new ResultadoDeLey("LISR", "Ley del Impuesto sobre la Renta", "27", null, "2026-01-01", null, null, "Requisitos …"));
        _modelo.UsarHerramienta(HerramientasDelAsistente.ConsultarLeyes, new { pregunta = "requisitos deducciones" });
        _modelo.Contestar("Tienen que ser indispensables. Fuente: art. 27 LISR.");

        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, new EnviarMensajeDto { Texto = "¿Qué necesito para deducir?" }));

        _modelo.Solicitudes.Count.ShouldBe(2);
    }
}
