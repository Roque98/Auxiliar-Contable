using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Normativa;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Análisis normativo: el despacho pide, el trabajo de fondo lo hace con el perfil fiscal del cliente (sin RFC) y la
/// conclusión queda guardada; el cliente no lo ve.
/// </summary>
public class NormativaIntegrationTests : ConJerarquiaTestBase
{
    private readonly INormativaAppService _normativa;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;

    public NormativaIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _normativa = GetRequiredService<INormativaAppService>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _motor.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Consultorio de prueba");
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
        await PonerAsync(CamposDelPerfil.Rfc, "GODE561231GR8");
        await PonerAsync(CamposDelPerfil.TipoDePersona, "fisica");
        await PonerAsync(CamposDelPerfil.Regimenes, "[\"612 · Personas Físicas con Actividades Empresariales y Profesionales\"]");
        await PonerAsync(CamposDelPerfil.ActividadesEconomicas, "[\"Consultorios de nutriólogos y dietistas del sector privado (100 %)\"]");
        await PonerAsync(CamposDelPerfil.CodigoPostal, "06000");
    }

    private Task PonerAsync(string campo, string valor) =>
        ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = campo, Valor = valor }));

    [Fact]
    public async Task Pedir_hacer_y_consultar()
    {
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _normativa.GetListaAsync(_cliente)));
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () =>
            _normativa.PedirAsync(_cliente, new PedirAnalisisNormativoDto { Pregunta = "¿IVA?" })));
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () =>
            _normativa.PedirAsync(_cliente, new PedirAnalisisNormativoDto
            {
                Pregunta = "Consulta de nutrición a un paciente del extranjero", ContraparteTipoDePersona = "empresa",
            })));

        var pedido = await ComoAsync(Administrador, () => _normativa.PedirAsync(_cliente, new PedirAnalisisNormativoDto
        {
            Pregunta = "Consulta de nutrición en línea a un paciente que vive en Estados Unidos y paga en dólares",
            FechaDeLaOperacion = new DateTime(2025, 6, 15),
            ContraparteTipoDePersona = "fisica",
            ContraparteResidencia = "us",
            Disparadores = ["extranjero", "no_existe"],
        }));
        pedido.Estado.ShouldBe("Pendiente");
        pedido.Disparadores.ShouldBe(["extranjero"]);
        _normativa.GetDisparadores().ShouldContain(d => d.Clave == "moneda_extranjera");

        (await WithUnitOfWorkAsync(() => GetRequiredService<AnalizadorNormativo>().ProcesarAsync())).ShouldBe(1);
        var (perfil, operacion) = _motor.NormativaRecibida.ShouldHaveSingleItem();
        perfil.TipoPersona.ShouldBe("fisica");
        perfil.Regimenes.ShouldHaveSingleItem().ShouldStartWith("612");
        perfil.Actividades.ShouldHaveSingleItem().ShouldContain("nutriólogos");
        perfil.CodigoPostal.ShouldBe("06000");
        perfil.Ejercicio.ShouldBe(2025);
        System.Text.Json.JsonSerializer.Serialize(perfil).ShouldNotContain("GODE561231GR8");
        operacion.Fecha.ShouldBe("2025-06-15");
        operacion.Contraparte!.Residencia.ShouldBe("US");

        var lista = await ComoAsync(Administrador, () => _normativa.GetListaAsync(_cliente));
        var hecho = lista.ShouldHaveSingleItem();
        hecho.Estado.ShouldBe("Listo");
        hecho.Conclusion.ShouldBe("Tasa 0% si se cumplen los requisitos.");
        hecho.Confianza.ShouldBe("media");
        hecho.Resultado.ShouldBeNull();
        (await ComoAsync(Administrador, () => _normativa.GetAnalisisAsync(_cliente, hecho.Id))).Resultado!.ShouldContain("analisis");

        // Si el motor no responde, se reintenta más tarde (sigue pendiente).
        _motor.Caido = true;
        await ComoAsync(Administrador, () => _normativa.PedirAsync(_cliente, new PedirAnalisisNormativoDto
        {
            Pregunta = "Renta de una casa habitación a una familia, ¿cobra IVA?",
        }));
        (await WithUnitOfWorkAsync(() => GetRequiredService<AnalizadorNormativo>().ProcesarAsync())).ShouldBe(0);
        var otra = (await ComoAsync(Administrador, () => _normativa.GetListaAsync(_cliente))).First();
        otra.Estado.ShouldBe("Pendiente");
        otra.Error!.ShouldContain("no respondieron");
    }
}
