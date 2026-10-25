using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Conversaciones;
using AuxiliarContable.Identity;
using AuxiliarContable.IA;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// El chat contra PostgreSQL real con un modelo programado: el bucle de herramientas, que lo
/// que guarda el asistente pasa por las mismas reglas y permisos que la pantalla, y que el
/// cliente activo lo fija el código.
/// </summary>
public class AsistenteIntegrationTests : ConJerarquiaTestBase
{
    private readonly IAsistenteAppService _asistente;
    private readonly IPerfilFiscalAppService _perfil;
    private readonly ModeloConversacionalDePrueba _modelo;
    private Guid _cliente;
    private Guid _otroCliente;
    private Volo.Abp.Identity.IdentityUser _lector = null!;

    public AsistenteIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _asistente = GetRequiredService<IAsistenteAppService>();
        _perfil = GetRequiredService<IPerfilFiscalAppService>();
        _modelo = (ModeloConversacionalDePrueba)GetRequiredService<IModeloConversacional>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _modelo.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Panadería Lupita");
        _otroCliente = await CrearOrganizacionAsync("Otro cliente");
        _lector = await CrearUsuarioAsync("lector", AuxiliarRoles.Usuario);
        await WithUnitOfWorkAsync(async () =>
        {
            var membresias = GetRequiredService<MembresiasDeOrganizacion>();
            await membresias.AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro);
            await membresias.AgregarAsync(_cliente, _lector.Id, RolEnOrganizacion.Lector);
        });
    }

    private static EnviarMensajeDto Mensaje(string texto) => new() { Texto = texto };

    [Fact]
    public async Task El_cliente_platica_y_el_asistente_guarda_lo_que_le_dice_en_el_perfil()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.ConsultarPerfil, new { });
        _modelo.UsarHerramienta(HerramientasDelAsistente.GuardarDato, new { campo = CamposDelPerfil.Giro, valor = "Panadería" });
        _modelo.Contestar("¡Listo! Anoté que tu negocio es una panadería. ¿Emites facturas?");

        var conversacion = await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("Tengo una panadería")));

        conversacion.NombreDelCliente.ShouldBe("Panadería Lupita");
        conversacion.EsDelDespacho.ShouldBeFalse();
        conversacion.Mensajes.Select(m => m.Rol).ShouldBe([RolDelMensaje.Usuario, RolDelMensaje.Asistente]);
        conversacion.Mensajes[1].Texto.ShouldStartWith("¡Listo!");
        conversacion.Mensajes[1].Acciones.ShouldBe(["Guardó: Giro o a qué se dedica"], ignoreOrder: true, customMessage: "la acción se ve en la conversación");

        var perfil = await ComoAsync(Usuario, () => _perfil.GetAsync(_cliente));
        var giro = perfil.Campos.Single(c => c.Clave == CamposDelPerfil.Giro);
        giro.Valor.ShouldBe("Panadería");
        giro.Origen.ShouldBe(OrigenDelDato.RespondidoPorElCliente);
        giro.Confirmado.ShouldBeTrue();

        // Tres llamadas: la última ya recibe los dos resultados de herramientas, en orden.
        _modelo.Solicitudes.Count.ShouldBe(3);
        var ultima = _modelo.Solicitudes[2];
        ultima.Turnos.Count.ShouldBe(5);
        ultima.Turnos[0].Rol.ShouldBe(RolDelTurno.Usuario);
        var resultado = ultima.Turnos[4].Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>();
        resultado.EsError.ShouldBeFalse();
        resultado.Contenido.ShouldContain("Panadería");
        ultima.Instrucciones.ShouldContain("Panadería Lupita");
    }

    [Fact]
    public async Task Un_dato_invalido_vuelve_al_modelo_como_error_y_no_se_guarda()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.GuardarDato, new { campo = CamposDelPerfil.CodigoPostal, valor = "123" });
        _modelo.Contestar("Ese código postal no parece completo, ¿me lo repites?");

        var conversacion = await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("mi CP es 123")));

        conversacion.Mensajes.Last().Texto.ShouldContain("código postal");
        var resultado = _modelo.Solicitudes[1].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>();
        resultado.EsError.ShouldBeTrue();
        resultado.Contenido.ShouldContain("DatoDelPerfilInvalido");

        var perfil = await ComoAsync(Usuario, () => _perfil.GetAsync(_cliente));
        perfil.Campos.Single(c => c.Clave == CamposDelPerfil.CodigoPostal).Valor.ShouldBeNull();
    }

    [Fact]
    public async Task Un_lector_solo_consulta_el_modelo_ni_siquiera_recibe_las_herramientas_de_guardar()
    {
        _modelo.Contestar("Tu perfil está casi completo.");

        await ComoAsync(_lector, () => _asistente.EnviarAsync(_cliente, Mensaje("¿Cómo voy?")));

        var nombres = _modelo.Solicitudes.Single().Herramientas.Select(h => h.Nombre).ToList();
        nombres.ShouldContain(HerramientasDelAsistente.ConsultarPerfil);
        nombres.ShouldNotContain(HerramientasDelAsistente.GuardarDato);
        nombres.ShouldNotContain(HerramientasDelAsistente.ConfirmarDato);

        // Y aunque el modelo la pidiera, el servicio del perfil la rechaza.
        _modelo.Reiniciar();
        _modelo.UsarHerramienta(HerramientasDelAsistente.GuardarDato, new { campo = CamposDelPerfil.Giro, valor = "Otro" });
        _modelo.Contestar("No pude.");
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(_lector, () => _asistente.EnviarAsync(_cliente, Mensaje("Cambia mi giro"))));
    }

    [Fact]
    public async Task Nadie_platica_sobre_un_cliente_que_no_es_suyo()
    {
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _asistente.GetAsync(_otroCliente)));
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(Usuario, () => _asistente.EnviarAsync(_otroCliente, Mensaje("hola"))));
        _modelo.Solicitudes.ShouldBeEmpty();
    }

    [Fact]
    public async Task El_despacho_platica_sobre_cualquier_cliente_y_lo_que_guarda_queda_como_del_despacho()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.GuardarDato, new { campo = CamposDelPerfil.TieneTrabajadores, valor = "si" });
        _modelo.Contestar("Anotado.");

        var conversacion = await ComoAsync(Administrador, () => _asistente.EnviarAsync(_otroCliente, Mensaje("Tiene 3 empleados")));

        conversacion.EsDelDespacho.ShouldBeTrue();
        _modelo.Solicitudes[0].Instrucciones.ShouldContain("EQUIPO del despacho");
        var perfil = await ComoAsync(Administrador, () => _perfil.GetAsync(_otroCliente));
        perfil.Campos.Single(c => c.Clave == CamposDelPerfil.TieneTrabajadores).Origen.ShouldBe(OrigenDelDato.CapturadoPorElDespacho);
    }

    [Fact]
    public async Task La_conversacion_sigue_y_cada_persona_tiene_la_suya()
    {
        _modelo.Contestar("Hola, ¿en qué te ayudo?");
        _modelo.Contestar("Claro.");
        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("Hola")));
        var segunda = await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("¿Me ayudas?")));

        segunda.Mensajes.Count.ShouldBe(4);
        _modelo.Solicitudes[1].Turnos.Count.ShouldBe(3, "el modelo recibe el historial");

        // El despacho, en el mismo cliente, empieza la suya.
        (await ComoAsync(Administrador, () => _asistente.GetAsync(_cliente))).Mensajes.ShouldBeEmpty();

        // Empezar de nuevo deja la conversación vacía, pero no toca el perfil.
        (await ComoAsync(Usuario, () => _asistente.EmpezarDeNuevoAsync(_cliente))).Mensajes.ShouldBeEmpty();
        (await ComoAsync(Usuario, () => _asistente.GetAsync(_cliente))).Mensajes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Si_el_modelo_falla_no_queda_nada_a_medias()
    {
        _modelo.Fallar(MotivoDeErrorDeIA.NoDisponible);

        var error = await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("Hola"))));

        error.Message.ShouldContain("saturado");
        (await ComoAsync(Usuario, () => _asistente.GetAsync(_cliente))).Mensajes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_modelo_que_no_para_de_pedir_herramientas_se_corta()
    {
        for (var i = 0; i < ConversacionConsts.MaxVueltasDeHerramientas; i++)
        {
            _modelo.UsarHerramienta(HerramientasDelAsistente.ConsultarPerfil, new { });
        }

        var conversacion = await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("Hola")));

        _modelo.Solicitudes.Count.ShouldBe(ConversacionConsts.MaxVueltasDeHerramientas);
        conversacion.Mensajes.Last().Texto.ShouldContain("No pude terminar");
    }

    [Fact]
    public async Task El_asistente_busca_en_la_boveda_del_cliente_activo()
    {
        await ComoAsync(Usuario, () => _perfil.ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = "GODE561231GR8" }));
        await ComoAsync(Usuario, () => GetRequiredService<Boveda.IBovedaAppService>().SubirAsync(_cliente, Documentos.TipoDeDocumento.Otro,
            new Volo.Abp.Content.RemoteStreamContent(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(Cfdis.LectorDeCfdiTests.Cfdi40())), "f.xml")));

        _modelo.UsarHerramienta(HerramientasDelAsistente.BuscarDocumentos, new { anio = 2026, mes = 9, direccion = "emitida" });
        _modelo.UsarHerramienta(HerramientasDelAsistente.ResumenDeDocumentos, new { });
        _modelo.Contestar("En septiembre facturaste $1,160.00.");

        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("¿Cuánto facturé en septiembre?")));

        var busqueda = _modelo.Solicitudes[1].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>();
        busqueda.EsError.ShouldBeFalse();
        busqueda.Contenido.ShouldContain("\"total\":1");
        busqueda.Contenido.ShouldContain("Pan dulce");
        var resumen = _modelo.Solicitudes[2].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>();
        resumen.Contenido.ShouldContain("\"emitido\":1160");
    }

    [Fact]
    public async Task Sin_modelo_configurado_lo_dice_sin_guardar_el_mensaje()
    {
        _modelo.EstaConfigurado = false;

        (await ComoAsync(Usuario, () => _asistente.GetAsync(_cliente))).Disponible.ShouldBeFalse();
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("Hola"))));
        var mensajes = await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<MensajeDeConversacion, Guid>>().GetCountAsync());
        mensajes.ShouldBe(0);
    }
}
