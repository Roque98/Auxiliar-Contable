using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.LeyesDelCliente;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// «Mis leyes»: el cliente sube su ley estatal, el trabajador la lee y la analiza contra su perfil (sin RFC), el despacho
/// decide cada hallazgo y lo aceptado reemplaza al valor del sistema solo para ese cliente.
/// </summary>
public class LeyesDelClienteIntegrationTests : ConJerarquiaTestBase
{
    private readonly ILeyesDelClienteAppService _leyes;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;
    private Guid _otro;

    public LeyesDelClienteIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _leyes = GetRequiredService<ILeyesDelClienteAppService>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _motor.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Panadería de prueba");
        _otro = await CrearOrganizacionAsync("Otro cliente");
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
        await PonerAsync(CamposDelPerfil.Rfc, "GODE561231GR8");
        await PonerAsync(CamposDelPerfil.TipoDePersona, "moral");
        await PonerAsync(CamposDelPerfil.ActividadesEconomicas, "[\"Panificación tradicional (100 %)\"]");
        await PonerAsync(CamposDelPerfil.DomicilioFiscal, "Av. Juárez 100, Centro, Guadalajara, Jalisco, 44100");
        await PonerAsync(CamposDelPerfil.TieneTrabajadores, "si");
    }

    private Task PonerAsync(string campo, string valor) =>
        ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = campo, Valor = valor }));

    private static IRemoteStreamContent Pdf(string texto, string nombre = "ley_hacienda.pdf") =>
        new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.7 " + texto)), nombre, "application/pdf");

    private Task<int> ProcesarAsync() => WithUnitOfWorkAsync(() => GetRequiredService<ProcesadorDeLeyesDelCliente>().ProcesarAsync());

    [Fact]
    public async Task El_cliente_sube_el_despacho_decide_y_lo_aceptado_reemplaza_al_sistema()
    {
        // El cliente sube; el estado se propone del domicilio fiscal
        var mias = await ComoAsync(Usuario, () => _leyes.GetMisLeyesAsync(_cliente));
        mias.EsDelDespacho.ShouldBeFalse();
        mias.EstadoDelCliente.ShouldBe("JAL");
        mias.Mensaje.ShouldContain("sube las leyes en este apartado");
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Usuario, () =>
            _leyes.SubirAsync(_cliente, new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes("hola")), "x.txt", "text/plain"), "estatal")));
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Usuario, () => _leyes.SubirAsync(_cliente, Pdf("a"), "municipal", "JAL")));
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Usuario, () => _leyes.SubirAsync(_cliente, Pdf("a"), "estatal", "XX")));
        var ley = await ComoAsync(Usuario, () => _leyes.SubirAsync(_cliente, Pdf("ley de hacienda"), "estatal", "JAL", null, "Ley de hacienda"));
        ley.Lectura.ShouldBe("Pendiente");
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Usuario, () => _leyes.SubirAsync(_cliente, Pdf("ley de hacienda"), "estatal", "JAL")));

        // Lo ajeno no se ve (404)
        await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => ComoAsync(Usuario, () => _leyes.GetMisLeyesAsync(_otro)));

        // Primera vuelta: lee y analiza (perfil sin RFC, con el estado y los valores del sistema)
        (await ProcesarAsync()).ShouldBe(2);
        var (docs, perfil, sistema) = _motor.LeyesAnalizadas.ShouldHaveSingleItem();
        docs.ShouldHaveSingleItem().Length.ShouldBe(64);
        perfil["estado"].ShouldBe("JAL");
        perfil["giro"]!.ToString()!.ShouldContain("Panificación");
        System.Text.Json.JsonSerializer.Serialize(perfil).ShouldNotContain("GODE561231GR8");
        sistema.ShouldContainKey("isn_dia");

        // Nada cambió: no se vuelve a llamar a Claude
        (await ProcesarAsync()).ShouldBe(0);
        _motor.LeyesAnalizadas.Count.ShouldBe(1);

        var d = await ComoAsync(Usuario, () => _leyes.GetMisLeyesAsync(_cliente));
        var l = d.Leyes.ShouldHaveSingleItem();
        l.Lectura.ShouldBe("Lista");
        l.Analisis.ShouldBe("analizada");
        l.Titulo.ShouldBe("LEY DE HACIENDA DEL ESTADO DE PRUEBA");
        d.Hallazgos.Count.ShouldBe(3);
        d.Hallazgos.ShouldAllBe(h => h.Decision == "Propuesto");

        // Solo el despacho decide
        var tasa = d.Hallazgos.Single(h => h.Clave == LeyesDelClienteConsts.IsnTasa);
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () =>
            _leyes.DecidirAsync(_cliente, tasa.Id, new DecidirHallazgoDto { Aceptar = true })));
        (await ComoAsync(Administrador, () => _leyes.DecidirAsync(_cliente, tasa.Id, new DecidirHallazgoDto { Aceptar = true }))).Decision.ShouldBe("Aceptado");
        var dia = d.Hallazgos.Single(h => h.Clave == LeyesDelClienteConsts.IsnDia);
        await ComoAsync(Administrador, () => _leyes.DecidirAsync(_cliente, dia.Id, new DecidirHallazgoDto { Aceptar = true }));
        var pc = d.Hallazgos.Single(h => h.Tema == "proteccion_civil");
        await ComoAsync(Administrador, () => _leyes.DecidirAsync(_cliente, pc.Id, new DecidirHallazgoDto { Aceptar = true }));

        var ajustes = await WithUnitOfWorkAsync(() => GetRequiredService<AjustesDeLeyes>().DeAsync(_cliente));
        ajustes.IsnTasa!.Numero.ShouldBe(0.025m);
        ajustes.IsnTasa.Fundamento.ShouldContain("Artículo 3");
        ajustes.IsnDia!.Numero.ShouldBe(12m);
        ajustes.RecargosTasa.ShouldBeNull();
        ajustes.Obligaciones.ShouldHaveSingleItem().Titulo.ShouldBe("Dictamen de protección civil");
        (await WithUnitOfWorkAsync(() => GetRequiredService<AjustesDeLeyes>().DeAsync(_otro))).Vacio.ShouldBeTrue();

        // Si el perfil cambia, la ley se vuelve a analizar sola; lo ya decidido se queda sin duplicarse
        await PonerAsync(CamposDelPerfil.ActividadesEconomicas, "[\"Panificación tradicional (60 %)\",\"Cafetería (40 %)\"]");
        (await ProcesarAsync()).ShouldBe(1);
        _motor.LeyesAnalizadas.Count.ShouldBe(2);
        var d2 = await ComoAsync(Administrador, () => _leyes.GetMisLeyesAsync(_cliente));
        d2.EsDelDespacho.ShouldBeTrue();
        d2.Hallazgos.Count.ShouldBe(3);
        d2.Hallazgos.Count(h => h.Decision == "Aceptado").ShouldBe(3);

        // Preguntar
        var p = await ComoAsync(Usuario, () => _leyes.PreguntarAsync(_cliente, new PreguntarSobreLeyesDto { Pregunta = "¿Hasta qué día pago el ISN?" }));
        p.Estado.ShouldBe("Pendiente");
        (await ProcesarAsync()).ShouldBe(1);
        var contestada = (await ComoAsync(Usuario, () => _leyes.GetMisLeyesAsync(_cliente))).Preguntas.ShouldHaveSingleItem();
        contestada.Estado.ShouldBe("Lista");
        contestada.Respuesta!.ShouldContain("día 12");

        // Si el motor perdió la lectura, se le vuelve a mandar el PDF
        _motor.LeyesLeidas.Clear();
        await ComoAsync(Administrador, () => _leyes.ReanalizarAsync(_cliente, l.Id));
        (await ProcesarAsync()).ShouldBe(1);          // análisis -> 404 -> vuelve a leer
        (await ProcesarAsync()).ShouldBe(2);          // lee y analiza
        _motor.LeyesAnalizadas.Count.ShouldBe(3);

        // Con valores aceptados, el cliente no la puede quitar; el despacho sí, y deja de contar
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Usuario, () => _leyes.EliminarAsync(_cliente, l.Id)));
        await ComoAsync(Administrador, () => _leyes.EliminarAsync(_cliente, l.Id));
        (await WithUnitOfWorkAsync(() => GetRequiredService<AjustesDeLeyes>().DeAsync(_cliente))).Vacio.ShouldBeTrue();
    }

    [Fact]
    public async Task Un_valor_absurdo_no_se_puede_aceptar_y_el_motor_caido_se_reintenta()
    {
        _motor.AnalisisDeLeyes = _motor.AnalisisDeLeyes.Replace("\"valor_numerico\":0.025", "\"valor_numerico\":2.5");
        await ComoAsync(Usuario, () => _leyes.SubirAsync(_cliente, Pdf("otra ley"), "estatal", "JAL"));
        (await ProcesarAsync()).ShouldBe(2);
        var tasa = (await ComoAsync(Administrador, () => _leyes.GetMisLeyesAsync(_cliente))).Hallazgos.Single(h => h.Clave == LeyesDelClienteConsts.IsnTasa);
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () =>
            _leyes.DecidirAsync(_cliente, tasa.Id, new DecidirHallazgoDto { Aceptar = true })));

        _motor.Caido = true;
        await ComoAsync(Usuario, () => _leyes.SubirAsync(_cliente, Pdf("tercera ley"), "municipal", "JAL", "Zapopan", "Reglamento de comercio"));
        (await ProcesarAsync()).ShouldBe(0);
        var caida = (await ComoAsync(Usuario, () => _leyes.GetMisLeyesAsync(_cliente))).Leyes.First(x => x.Municipio == "Zapopan");
        caida.Lectura.ShouldBe("Pendiente");
        caida.Error!.ShouldContain("no respondió");
    }
}
