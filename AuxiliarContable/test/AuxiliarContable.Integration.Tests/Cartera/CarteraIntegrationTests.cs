using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Boveda;
using AuxiliarContable.Cartera;
using AuxiliarContable.Conversaciones;
using AuxiliarContable.Documentos;
using AuxiliarContable.IA;
using AuxiliarContable.Identity;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// La cartera del despacho y el chat de toda la cartera: quién la ve, el semáforo, las métricas
/// solo para el Owner, y el candado entre la conversación de la cartera y la de un cliente.
/// </summary>
public class CarteraIntegrationTests : ConJerarquiaTestBase
{
    private readonly ICarteraAppService _cartera;
    private readonly IAsistenteAppService _asistente;
    private readonly ModeloConversacionalDePrueba _modelo;
    private Guid _alDia;
    private Guid _sinNada;

    public CarteraIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _cartera = GetRequiredService<ICarteraAppService>();
        _asistente = GetRequiredService<IAsistenteAppService>();
        _modelo = (ModeloConversacionalDePrueba)GetRequiredService<IModeloConversacional>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _modelo.Reiniciar();
        _alDia = await CrearOrganizacionAsync("Panadería Lupita");
        _sinNada = await CrearOrganizacionAsync("Taller Pepe");
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_alDia, Usuario.Id, RolEnOrganizacion.Miembro));
        await ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_alDia,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = "GODE561231GR8" }));
        await ComoAsync(Administrador, () => GetRequiredService<IBovedaAppService>().SubirAsync(_alDia, TipoDeDocumento.Otro,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes("""<?xml version="1.0"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4"/>""")), "f.xml")));
    }

    [Fact]
    public async Task El_equipo_ve_toda_la_cartera_con_lo_urgente_primero()
    {
        // Sin leer el XML no hay fecha de la factura: cuenta en el mes en que se subió.
        var hoy = DateTime.Now;
        var clientes = await ComoAsync(Administrador, () => _cartera.GetClientesAsync(hoy.Year, hoy.Month));

        clientes.Select(c => c.Id).ShouldContain(_alDia);
        var pepe = clientes.Single(c => c.Id == _sinNada);
        pepe.Semaforo.ShouldBe(SemaforoDeCliente.Urgente);
        pepe.Pendientes.ShouldContain(p => p.Contains("Sin RFC"));
        var lupita = clientes.Single(c => c.Id == _alDia);
        lupita.Rfc.ShouldBe("GODE561231GR8");
        lupita.FacturasDelMes.ShouldBe(1);
        lupita.Pendientes.ShouldContain(p => p.Contains("estado de cuenta"));
        clientes.First().Semaforo.ShouldBe(SemaforoDeCliente.Urgente);
    }

    [Fact]
    public async Task Un_cliente_no_ve_la_cartera_y_las_metricas_son_del_Owner()
    {
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _cartera.GetClientesAsync(2026, 9)));
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Administrador, () => _cartera.GetMetricasAsync(2026, 9)));

        var owner = await CrearUsuarioAsync("duena", AuxiliarRoles.Owner);
        var motor = (MotorFiscalDePrueba)GetRequiredService<AuxiliarContable.MotorFiscal.IMotorFiscal>();
        motor.Reiniciar();
        motor.Gasto =
        [
            new AuxiliarContable.MotorFiscal.GastoDeClaude { Cliente = _alDia.ToString(), Para = "documento", Modelo = "claude-haiku-5-5", Llamadas = 10, Usd = 0.02m },
            new AuxiliarContable.MotorFiscal.GastoDeClaude { Cliente = _alDia.ToString(), Para = "interpretacion", Modelo = "claude-opus-5-5", Llamadas = 2, Usd = 0.10m },
            new AuxiliarContable.MotorFiscal.GastoDeClaude { Cliente = null, Para = "documento", Modelo = "claude-haiku-5-5", Llamadas = 1, Usd = 0.001m },
        ];
        var metricas = await ComoAsync(owner, () => _cartera.GetMetricasAsync(DateTime.Now.Year, DateTime.Now.Month));
        metricas.Clientes.ShouldBeGreaterThanOrEqualTo(2);
        metricas.FacturasDelMes.ShouldBeGreaterThanOrEqualTo(1);
        var lupita = metricas.GastoDeClaude.ShouldNotBeNull().First();
        (lupita.Nombre, lupita.Llamadas, lupita.Usd, lupita.LecturaUsd).ShouldBe(("Panadería Lupita", 12, 0.12m, 0.02m));
        metricas.GastoDeClaude.ShouldContain(g => g.OrganizacionId == null && g.Nombre == "Sin cliente");
        metricas.GastoDeClaudeTotalUsd.ShouldBe(0.121m);
        motor.Reiniciar();
    }

    [Fact]
    public async Task El_chat_de_la_cartera_solo_ve_el_resumen()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.ResumenDeCartera, new { anio = 2026, mes = 9 });
        _modelo.UsarHerramienta(HerramientasDelAsistente.ConsultarPerfil, new { });
        _modelo.Contestar("Taller Pepe está urgente: no tiene RFC.");

        var conversacion = await ComoAsync(Administrador, () => _asistente.EnviarAsync(Guid.Empty, new EnviarMensajeDto { Texto = "¿Quién está urgente?" }));

        conversacion.NombreDelCliente.ShouldBe(AsistenteAppService.NombreDeLaCartera);
        conversacion.PuedeEditar.ShouldBeFalse();
        _modelo.Solicitudes[0].Herramientas.Select(h => h.Nombre)
            .ShouldBe([HerramientasDelAsistente.ResumenDeCartera, HerramientasDelAsistente.CrearCliente,
                    HerramientasDelAsistente.SolicitudesPendientes, HerramientasDelAsistente.ConsultarLeyes,
                    HerramientasDelAsistente.ConsultarSentencias, HerramientasDelAsistente.TarifaIsr, HerramientasDelAsistente.LeerLey,
                    HerramientasDelAsistente.BuscarClaveSat, HerramientasDelAsistente.RevisarClaves],
                "el resumen, el alta, la bandeja de solicitudes, la ley, las sentencias, la tarifa de ISR y las claves SAT (que no son de ningún cliente)");
        var resumen = _modelo.Solicitudes[1].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>();
        resumen.Contenido.ShouldContain("Taller Pepe");
        var perfil = _modelo.Solicitudes[2].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>();
        perfil.EsError.ShouldBeTrue("en la cartera no se consulta el perfil de nadie");
    }

    [Fact]
    public async Task En_la_cartera_busca_la_clave_sin_la_constancia_de_nadie()
    {
        var motor = (MotorFiscalDePrueba)GetRequiredService<AuxiliarContable.MotorFiscal.IMotorFiscal>();
        motor.Reiniciar();
        _modelo.UsarHerramienta(HerramientasDelAsistente.BuscarClaveSat, new { descripcion = "guantes para motociclista" });
        _modelo.UsarHerramienta(HerramientasDelAsistente.RevisarClaves, new { uuid = "6F2A1B3C-4D5E-4F60-8A7B-9C0D1E2F3A4B" });
        _modelo.Contestar("Usa la clave de guantes.");

        await ComoAsync(Administrador, () => _asistente.EnviarAsync(Guid.Empty, new EnviarMensajeDto { Texto = "¿Qué clave uso para guantes de moto?" }));

        var (descripcion, actividades) = motor.ClavesBuscadas.ShouldHaveSingleItem();
        descripcion.ShouldBe("guantes para motociclista");
        actividades.ShouldBeEmpty();
        _modelo.Solicitudes[1].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>().EsError.ShouldBeFalse();
        var porUuid = _modelo.Solicitudes[2].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>();
        porUuid.EsError.ShouldBeTrue("en la cartera no se ven las facturas de ningún cliente");
        motor.Reiniciar();
    }

    [Fact]
    public async Task Un_cliente_no_entra_al_chat_de_la_cartera_ni_pide_el_resumen_desde_el_suyo()
    {
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _asistente.GetAsync(Guid.Empty)));

        _modelo.UsarHerramienta(HerramientasDelAsistente.ResumenDeCartera, new { anio = 2026, mes = 9 });
        _modelo.Contestar("No puedo.");
        await ComoAsync(Usuario, () => _asistente.EnviarAsync(_alDia, new EnviarMensajeDto { Texto = "¿Cómo van los demás clientes?" }));

        _modelo.Solicitudes[0].Herramientas.Select(h => h.Nombre).ShouldNotContain(HerramientasDelAsistente.ResumenDeCartera);
        var resultado = _modelo.Solicitudes[1].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>();
        resultado.EsError.ShouldBeTrue();
        resultado.Contenido.ShouldNotContain("Taller Pepe");
    }

    [Fact]
    public async Task Desde_el_chat_de_la_cartera_se_da_de_alta_un_cliente_y_un_nombre_repetido_no_se_presume()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.CrearCliente, new { nombre = "Ferretería Don Beto" });
        _modelo.UsarHerramienta(HerramientasDelAsistente.CrearCliente, new { nombre = "Ferretería Don Beto" });
        _modelo.Contestar("Listo, di de alta a Ferretería Don Beto.");

        var conversacion = await ComoAsync(Administrador, () =>
            _asistente.EnviarAsync(Guid.Empty, new EnviarMensajeDto { Texto = "Sí, créalo como Ferretería Don Beto" }));

        var nombres = await WithUnitOfWorkAsync(async () =>
            (await GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<Organizacion, Guid>>().GetListAsync()).Select(o => o.Nombre).ToList());
        nombres.Count(n => n == "Ferretería Don Beto").ShouldBe(1);
        _modelo.Solicitudes[2].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>().EsError.ShouldBeTrue();
        // La acción se enseña UNA vez: la del uso que falló (nombre repetido) no aparece.
        conversacion.Mensajes.Last().Acciones.ShouldBe(["Dio de alta al cliente: Ferretería Don Beto"]);
    }

    [Fact]
    public async Task Desde_el_chat_de_un_cliente_no_se_dan_de_alta_clientes()
    {
        _modelo.UsarHerramienta(HerramientasDelAsistente.CrearCliente, new { nombre = "Colado" });
        _modelo.Contestar("No puedo.");

        var conversacion = await ComoAsync(Administrador, () => _asistente.EnviarAsync(_alDia, new EnviarMensajeDto { Texto = "Crea un cliente" }));

        _modelo.Solicitudes[0].Herramientas.Select(h => h.Nombre).ShouldNotContain(HerramientasDelAsistente.CrearCliente);
        _modelo.Solicitudes[1].Turnos.Last().Bloques.Single().ShouldBeOfType<BloqueDeResultadoDeHerramienta>().EsError.ShouldBeTrue();
        conversacion.Mensajes.Last().Acciones.ShouldBeEmpty();
    }
}
