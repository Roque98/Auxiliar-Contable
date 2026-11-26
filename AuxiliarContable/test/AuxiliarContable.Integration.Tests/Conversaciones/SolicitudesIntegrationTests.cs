using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Conversaciones;
using AuxiliarContable.IA;
using AuxiliarContable.Organizaciones;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Fase 4b: el cliente no cambia su contabilidad; lo que quiere cambiar se lo pide al despacho, que lo atiende o lo
/// rechaza. El despacho le pide documentos al cliente. Desde el chat y desde el servicio, con los mismos permisos.
/// </summary>
public class SolicitudesIntegrationTests : ConJerarquiaTestBase
{
    private readonly ISolicitudesAppService _solicitudes;
    private readonly IAsistenteAppService _asistente;
    private readonly ModeloConversacionalDePrueba _modelo;
    private Guid _cliente;
    private Guid _otroCliente;

    public SolicitudesIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _solicitudes = GetRequiredService<ISolicitudesAppService>();
        _asistente = GetRequiredService<IAsistenteAppService>();
        _modelo = (ModeloConversacionalDePrueba)GetRequiredService<IModeloConversacional>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _modelo.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Panadería Lupita");
        _otroCliente = await CrearOrganizacionAsync("Otro cliente");
        await WithUnitOfWorkAsync(() =>
            GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
    }

    private static CrearSolicitudDto Pedir(string texto, TipoDeSolicitud tipo = TipoDeSolicitud.Cambio) => new() { Tipo = tipo, Texto = texto };

    [Fact]
    public async Task El_cliente_pide_un_cambio_desde_el_chat_y_va_al_despacho()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.Solicitar, new
        {
            tipo = "cambio",
            texto = "La factura de Telmex de octubre se pagó con transferencia, no en efectivo: corregir la forma de pago.",
        });
        _modelo.Contestar("Listo, le dejé tu solicitud al despacho.");

        var conversacion = await ComoAsync(Usuario, () =>
            _asistente.EnviarAsync(_cliente, new EnviarMensajeDto { Texto = "Sí, mándale eso al despacho" }));

        conversacion.Mensajes.Last().Acciones.ShouldBe(["Dejó una solicitud"]);
        var guardada = (await ComoAsync(Usuario, () => _solicitudes.GetListAsync(_cliente))).ShouldHaveSingleItem();
        guardada.Sentido.ShouldBe(SentidoDeSolicitud.AlDespacho);
        guardada.Tipo.ShouldBe(TipoDeSolicitud.Cambio);
        guardada.Estado.ShouldBe(EstadoDeSolicitud.Pendiente);
        guardada.PuedeResolver.ShouldBeFalse("lo que pide el cliente lo resuelve el despacho");
    }

    [Fact]
    public async Task El_cliente_no_resuelve_lo_que_le_pidio_al_despacho_y_el_despacho_si()
    {
        var s = await ComoAsync(Usuario, () => _solicitudes.CrearAsync(_cliente, Pedir("Corregir la forma de pago de Telmex")));

        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () =>
            _solicitudes.ResolverAsync(_cliente, s.Id, new ResolverSolicitudDto { Estado = EstadoDeSolicitud.Atendida, Respuesta = "ya" })));

        var bandeja = await ComoAsync(Administrador, () => _solicitudes.GetPendientesDelDespachoAsync());
        bandeja.ShouldHaveSingleItem().NombreDelCliente.ShouldBe("Panadería Lupita");

        await Should.ThrowAsync<BusinessException>(() => ComoAsync(Administrador, () =>
            _solicitudes.ResolverAsync(_cliente, s.Id, new ResolverSolicitudDto { Estado = EstadoDeSolicitud.Rechazada })));

        var atendida = await ComoAsync(Administrador, () =>
            _solicitudes.ResolverAsync(_cliente, s.Id, new ResolverSolicitudDto { Estado = EstadoDeSolicitud.Atendida, Respuesta = "Corregido a transferencia." }));
        atendida.Estado.ShouldBe(EstadoDeSolicitud.Atendida);
        atendida.Respuesta.ShouldBe("Corregido a transferencia.");
        (await ComoAsync(Administrador, () => _solicitudes.GetPendientesDelDespachoAsync())).ShouldBeEmpty();

        await Should.ThrowAsync<BusinessException>(() => ComoAsync(Administrador, () =>
            _solicitudes.ResolverAsync(_cliente, s.Id, new ResolverSolicitudDto { Estado = EstadoDeSolicitud.Rechazada, Respuesta = "no" })),
            customMessage: "ya resuelta, nadie la vuelve a resolver");
    }

    [Fact]
    public async Task El_despacho_le_pide_un_documento_al_cliente_y_el_cliente_lo_marca_desde_el_chat()
    {
        var s = await ComoAsync(Administrador, () =>
            _solicitudes.CrearAsync(_cliente, Pedir("Sube tu estado de cuenta de septiembre de BBVA", TipoDeSolicitud.Documento)));
        s.Sentido.ShouldBe(SentidoDeSolicitud.AlCliente);

        _modelo.UsarHerramienta(HerramientasDelAsistente.ResolverSolicitud, new { solicitudId = s.Id, estado = "atendida", respuesta = "Ya lo subí" });
        _modelo.Contestar("¡Gracias! Le aviso al despacho.");

        var conversacion = await ComoAsync(Usuario, () =>
            _asistente.EnviarAsync(_cliente, new EnviarMensajeDto { Texto = "Ya subí el estado de cuenta" }));

        conversacion.Mensajes.Last().Acciones.ShouldBe(["Marcó una solicitud como atendida"]);
        var lista = await ComoAsync(Administrador, () => _solicitudes.GetListAsync(_cliente));
        lista.ShouldHaveSingleItem().Estado.ShouldBe(EstadoDeSolicitud.Atendida);
        lista.Single().Respuesta.ShouldBe("Ya lo subí");
    }

    [Fact]
    public async Task Lo_de_otro_cliente_no_existe_y_la_bandeja_es_del_despacho()
    {
        var ajena = await ComoAsync(Administrador, () => _solicitudes.CrearAsync(_otroCliente, Pedir("Documento del otro cliente")));

        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _solicitudes.GetListAsync(_otroCliente)));
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () =>
            _solicitudes.ResolverAsync(_cliente, ajena.Id, new ResolverSolicitudDto { Estado = EstadoDeSolicitud.Atendida })));
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _solicitudes.GetPendientesDelDespachoAsync()));
    }

    [Fact]
    public async Task La_bandeja_se_ve_desde_la_conversacion_de_toda_la_cartera()
    {
        await ComoAsync(Usuario, () => _solicitudes.CrearAsync(_cliente, Pedir("¿Por qué no se dedujo la gasolina de octubre?", TipoDeSolicitud.Aclaracion)));
        _modelo.UsarHerramienta(HerramientasDelAsistente.SolicitudesPendientes, new { });
        _modelo.Contestar("Panadería Lupita pregunta por la gasolina.");

        await ComoAsync(Administrador, () => _asistente.EnviarAsync(Guid.Empty, new EnviarMensajeDto { Texto = "¿Qué nos han pedido?" }));

        var r = _modelo.Solicitudes[1].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>();
        r.EsError.ShouldBeFalse();
        r.Contenido.ShouldContain("Panadería Lupita");
        r.Contenido.ShouldContain("gasolina");
    }
}
