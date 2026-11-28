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
using Volo.Abp.SettingManagement;
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

        // Y aunque el modelo la pidiera, el servicio del perfil la rechaza: vuelve al modelo como error
        // (la conversación sigue) y no se guarda nada.
        _modelo.Reiniciar();
        _modelo.UsarHerramienta(HerramientasDelAsistente.GuardarDato, new { campo = CamposDelPerfil.Giro, valor = "Otro" });
        _modelo.Contestar("No pude.");
        var conversacion = await ComoAsync(_lector, () => _asistente.EnviarAsync(_cliente, Mensaje("Cambia mi giro")));

        _modelo.Solicitudes[1].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>().EsError.ShouldBeTrue();
        conversacion.Mensajes.Last().Acciones.ShouldBeEmpty();
        var perfil = await ComoAsync(Usuario, () => _perfil.GetAsync(_cliente));
        perfil.Campos.Single(c => c.Clave == CamposDelPerfil.Giro).Valor.ShouldBeNull();
    }

    [Fact]
    public async Task Con_el_chat_apagado_no_se_llama_al_modelo()
    {
        var ajustes = GetRequiredService<ISettingManager>();
        await WithUnitOfWorkAsync(() => ajustes.SetGlobalAsync(AuxiliarContable.Settings.AuxiliarContableSettings.AsistenteHabilitado, "false"));
        try
        {
            var conversacion = await ComoAsync(Usuario, () => _asistente.GetAsync(_cliente));
            conversacion.Disponible.ShouldBeFalse();
            await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("hola"))));
            _modelo.Solicitudes.ShouldBeEmpty();
        }
        finally
        {
            await WithUnitOfWorkAsync(() => ajustes.SetGlobalAsync(AuxiliarContable.Settings.AuxiliarContableSettings.AsistenteHabilitado, "true"));
        }
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
            new Volo.Abp.Content.RemoteStreamContent(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                """<?xml version="1.0"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4"/>""")), "venta-pan.xml")));

        _modelo.UsarHerramienta(HerramientasDelAsistente.BuscarDocumentos, new { tipo = "cfdi", texto = "pan" });
        _modelo.UsarHerramienta(HerramientasDelAsistente.ResumenDeDocumentos, new { });
        _modelo.Contestar("Tienes una factura de este mes.");

        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("¿Qué facturas tengo?")));

        var busqueda = _modelo.Solicitudes[1].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>();
        busqueda.EsError.ShouldBeFalse();
        busqueda.Contenido.ShouldContain("\"total\":1");
        busqueda.Contenido.ShouldContain("venta-pan.xml");
        var resumen = _modelo.Solicitudes[2].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>();
        resumen.Contenido.ShouldContain("\"cantidad\":1");
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

    [Fact]
    public async Task El_despacho_invita_desde_el_chat_a_alguien_del_cliente_activo()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.InvitarPersona, new { email = "lupita@panaderia.mx", papel = "dueño" });
        _modelo.Contestar("Listo, le llegará un correo a lupita@panaderia.mx.");

        var conversacion = await ComoAsync(Administrador, () => _asistente.EnviarAsync(_cliente, Mensaje("Sí, invítala como dueña")));

        _modelo.Solicitudes[0].Herramientas.Select(h => h.Nombre).ShouldContain(HerramientasDelAsistente.InvitarPersona);
        var invitacion = await WithUnitOfWorkAsync(() =>
            GetRequiredService<IRepository<AuxiliarContable.Invitaciones.Invitacion, Guid>>().SingleAsync(i => i.Email == "lupita@panaderia.mx"));
        invitacion.OrganizacionId.ShouldBe(_cliente);
        invitacion.RolEnOrganizacion.ShouldBe(RolEnOrganizacion.Administrador);
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Invitó a lupita@panaderia.mx"]);
    }

    [Fact]
    public async Task Un_cliente_no_recibe_la_herramienta_de_invitar()
    {
        _modelo.Contestar("Hola.");

        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("hola")));

        _modelo.Solicitudes[0].Herramientas.Select(h => h.Nombre).ShouldNotContain(HerramientasDelAsistente.InvitarPersona);
    }

    [Fact]
    public async Task El_despacho_administra_a_las_personas_del_cliente_platicando()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.PersonasDelCliente, new { });
        _modelo.UsarHerramienta(HerramientasDelAsistente.CambiarPapel, new { userId = _lector.Id, papel = "miembro" });
        _modelo.UsarHerramienta(HerramientasDelAsistente.QuitarPersona, new { userId = Usuario.Id });
        _modelo.Contestar("Listo.");

        var conversacion = await ComoAsync(Administrador, () => _asistente.EnviarAsync(_cliente, Mensaje("Sube al lector a miembro y quita al otro")));

        _modelo.Solicitudes[1].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>().Contenido.ShouldContain("lector");
        var detalle = await ComoAsync(Administrador, () => GetRequiredService<IOrganizacionAppService>().GetAsync(_cliente));
        detalle.ListaDeMiembros.ShouldHaveSingleItem().UserId.ShouldBe(_lector.Id);
        detalle.ListaDeMiembros.Single().Rol.ShouldBe(RolEnOrganizacion.Miembro);
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Cambió el papel de una persona", "Quitó el acceso a una persona"]);
    }

    [Fact]
    public async Task Un_cliente_no_administra_personas_desde_el_chat()
    {
        _modelo.Contestar("Hola.");

        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("hola")));

        var nombres = _modelo.Solicitudes[0].Herramientas.Select(h => h.Nombre).ToList();
        nombres.ShouldNotContain(HerramientasDelAsistente.PersonasDelCliente);
        nombres.ShouldNotContain(HerramientasDelAsistente.QuitarPersona);
    }

    [Fact]
    public async Task Arma_el_archivo_que_le_piden_y_solo_lo_baja_quien_platico()
    {
        static string Tabla(string antes) => string.Join('\n', antes, "", "| Concepto | Importe |", "|---|---|", "| Luz | $1,200.00 |");

        _modelo.UsarHerramienta(HerramientasDelAsistente.GenerarArchivo, new
        {
            formato = "excel",
            nombre = "Gastos de octubre",
            contenido = Tabla("# Gastos"),
        });
        _modelo.Contestar(Tabla("Listo, abajo está el botón."));

        var conversacion = await ComoAsync(Usuario, () => _asistente.EnviarAsync(_cliente, Mensaje("Pásame mis gastos en Excel")));

        var respuesta = conversacion.Mensajes.Last();
        respuesta.TieneTabla.ShouldBeTrue();
        var archivo = respuesta.Archivos.ShouldHaveSingleItem();
        archivo.Nombre.ShouldBe("Gastos de octubre.xlsx");

        var excel = await ComoAsync(Usuario, () => _asistente.GetArchivoAsync(archivo.MensajeId, archivo.Uso, null));
        excel.FileName.ShouldBe("Gastos de octubre.xlsx");
        excel.ContentType.ShouldContain("spreadsheetml");
        var pdf = await ComoAsync(Usuario, () => _asistente.GetArchivoAsync(respuesta.Id, null, "pdf"));
        pdf.ContentType.ShouldBe("application/pdf");

        // De la conversación de otra persona, no existe.
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(_lector, () => _asistente.GetArchivoAsync(respuesta.Id, null, "pdf")));
    }
}
