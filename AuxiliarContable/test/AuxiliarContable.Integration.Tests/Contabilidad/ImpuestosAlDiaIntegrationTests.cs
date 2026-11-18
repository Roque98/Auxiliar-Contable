using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Boveda;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Documentos;
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
/// Impuestos al día: cada factura que entra deja el mes «por actualizar» y el actualizador lo recalcula solo, después de
/// un momento sin cambios (un paquete de cien XML se calcula una vez). Cambiar el régimen o el coeficiente también.
/// </summary>
public class ImpuestosAlDiaIntegrationTests : ConJerarquiaTestBase
{
    private const string Rfc = "GODE561231GR8";

    private readonly IImpuestosAlDiaAppService _alDia;
    private readonly ActualizadorDeImpuestos _actualizador;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;

    private static DateTime Dia => new(DateTime.Now.Year, DateTime.Now.Month, 1);

    public ImpuestosAlDiaIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _alDia = GetRequiredService<IImpuestosAlDiaAppService>();
        _actualizador = GetRequiredService<ActualizadorDeImpuestos>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _motor.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Papelería Lupita");
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
        await PonerEnElPerfilAsync(CamposDelPerfil.Rfc, Rfc);
        await PonerEnElPerfilAsync(CamposDelPerfil.Regimenes, JsonSerializer.Serialize(new[] { "612" }));
    }

    private Task PonerEnElPerfilAsync(string campo, string valor) =>
        ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = campo, Valor = valor }));

    private Task SubirFacturaAsync(string uuid) =>
        ComoAsync(Usuario, () => GetRequiredService<IBovedaAppService>().SubirAsync(_cliente, TipoDeDocumento.Cfdi,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(
                $"""<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" Fecha="{Dia:yyyy-MM-dd}T10:00:00" Total="1160.00" TipoDeComprobante="I"><cfdi:Emisor Rfc="{Rfc}" Nombre="PAPELERIA"/><cfdi:Receptor Rfc="XAXX010101000" Nombre="PUBLICO"/><cfdi:Conceptos><cfdi:Concepto Descripcion="Cuadernos"/></cfdi:Conceptos><cfdi:Complemento><tfd:TimbreFiscalDigital UUID="{uuid}"/></cfdi:Complemento></cfdi:Comprobante>""")),
                uuid + ".xml", "application/xml"),
            Dia.Year, Dia.Month));

    private Task<ImpuestosAlDiaDto> VerAsync() => ComoAsync(Administrador, () => _alDia.GetAsync(_cliente, Dia.Year, Dia.Month));

    [Fact]
    public async Task Cada_factura_que_entra_recalcula_el_impuesto_del_mes()
    {
        // El cliente no ve los impuestos al día.
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _alDia.GetAsync(_cliente, Dia.Year, Dia.Month)));

        await SubirFacturaAsync("6F9619FF-8B86-D011-B42D-00C04FC96401");
        var antes = await VerAsync();
        antes.Actualizando.ShouldBeTrue();
        antes.Calculado.ShouldBeFalse();

        (await _actualizador.ActualizarAsync(espera: TimeSpan.Zero)).ShouldBeGreaterThan(0);
        var uno = await VerAsync();
        uno.Actualizando.ShouldBeFalse();
        uno.Calculado.ShouldBeTrue();
        uno.Facturas.ShouldBe(1);
        uno.DelMes.ShouldNotBeNull().IsrAPagar.ShouldBe(19.2m);
        uno.Resultado!.Archivo.ShouldBeNull();

        // Llega otra: el mes vuelve a quedar por actualizar; con la espera normal todavía no se toca (se juntan cambios).
        await SubirFacturaAsync("6F9619FF-8B86-D011-B42D-00C04FC96402");
        (await VerAsync()).Actualizando.ShouldBeTrue();
        var llamadas = _motor.LlamadasDeProvisionales;
        await _actualizador.ActualizarAsync();
        _motor.LlamadasDeProvisionales.ShouldBe(llamadas);

        await _actualizador.ActualizarAsync(espera: TimeSpan.Zero);
        var dos = await VerAsync();
        dos.Actualizando.ShouldBeFalse();
        dos.Facturas.ShouldBe(2);
        dos.DelMes!.IsrAPagar.ShouldBe(38.4m);
        _motor.Provisionales!.Value.Regimenes.ShouldBe(["612"]);
    }

    [Fact]
    public async Task Cambiar_el_coeficiente_en_el_perfil_recalcula_y_si_el_motor_falla_se_reintenta()
    {
        await SubirFacturaAsync("6F9619FF-8B86-D011-B42D-00C04FC96403");
        await _actualizador.ActualizarAsync(espera: TimeSpan.Zero);
        (await VerAsync()).Actualizando.ShouldBeFalse();

        await PonerEnElPerfilAsync(CamposDelPerfil.CoeficienteDeUtilidad, "0,1834");
        (await VerAsync()).Actualizando.ShouldBeTrue();

        _motor.Caido = true;
        await _actualizador.ActualizarAsync(espera: TimeSpan.Zero);
        var caido = await VerAsync();
        caido.Actualizando.ShouldBeTrue();
        caido.Error.ShouldNotBeNullOrWhiteSpace();
        caido.Calculado.ShouldBeTrue(); // se sigue viendo el último cálculo

        _motor.Caido = false;
        await _actualizador.ActualizarAsync(espera: TimeSpan.Zero);
        var bien = await VerAsync();
        bien.Actualizando.ShouldBeFalse();
        bien.Error.ShouldBeNull();
        _motor.CoeficienteRecibido.ShouldBe(0.1834m);
    }

    [Fact]
    public async Task El_coeficiente_de_utilidad_es_un_numero_entre_cero_y_uno()
    {
        await Should.ThrowAsync<BusinessException>(() => PonerEnElPerfilAsync(CamposDelPerfil.CoeficienteDeUtilidad, "1.5"));
        await Should.ThrowAsync<BusinessException>(() => PonerEnElPerfilAsync(CamposDelPerfil.CoeficienteDeUtilidad, "mucho"));
    }
}
