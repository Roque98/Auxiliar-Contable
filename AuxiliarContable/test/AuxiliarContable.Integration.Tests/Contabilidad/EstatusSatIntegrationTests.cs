using System;
using System.IO;
using System.Linq;
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
using Volo.Abp.Content;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Estatus automático: el servicio público de verificación del SAT dice si cada factura sigue vigente, se canceló o tiene
/// una cancelación en proceso. Lo que cambia se guarda y el impuesto de su mes queda por actualizar; lo cancelado no se
/// vuelve a preguntar. Si el SAT no responde, se deja para después sin tocar nada.
/// </summary>
public class EstatusSatIntegrationTests : ConJerarquiaTestBase
{
    private const string Rfc = "GODE561231GR8";
    private const string Venta = "6F9619FF-8B86-D011-B42D-00C04FC96501";
    private const string Compra = "6F9619FF-8B86-D011-B42D-00C04FC96502";
    private const string Otra = "6F9619FF-8B86-D011-B42D-00C04FC96503";

    private readonly IDescargaDelSatAppService _descarga;
    private readonly IImpuestosAlDiaAppService _alDia;
    private readonly RevisorDeEstatusSat _revisor;
    private readonly VerificadorDeCfdiDePrueba _sat;
    private Guid _cliente;

    private static DateTime Dia => new(DateTime.Now.Year, DateTime.Now.Month, 1);

    public EstatusSatIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _descarga = GetRequiredService<IDescargaDelSatAppService>();
        _alDia = GetRequiredService<IImpuestosAlDiaAppService>();
        _revisor = GetRequiredService<RevisorDeEstatusSat>();
        _revisor.Pausa = TimeSpan.Zero;
        _sat = (VerificadorDeCfdiDePrueba)GetRequiredService<IVerificadorDeCfdi>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _sat.Reiniciar();
        ((MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>()).Reiniciar();
        _cliente = await CrearOrganizacionAsync("Herrería Lupita");
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
        var perfil = GetRequiredService<IPerfilFiscalAppService>();
        await ComoAsync(Administrador, () => perfil.ResponderAsync(_cliente, new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = Rfc }));
        await ComoAsync(Administrador, () => perfil.ResponderAsync(_cliente, new ResponderDatoDelPerfilDto
        {
            Campo = CamposDelPerfil.Regimenes, Valor = JsonSerializer.Serialize(new[] { "612" }),
        }));
    }

    private Task SubirAsync(string uuid, string emisor, string receptor, string total) =>
        ComoAsync(Usuario, () => GetRequiredService<IBovedaAppService>().SubirAsync(_cliente, TipoDeDocumento.Cfdi,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(
                $"""<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" Fecha="{Dia:yyyy-MM-dd}T10:00:00" Total="{total}" TipoDeComprobante="I"><cfdi:Emisor Rfc="{emisor}" Nombre="E"/><cfdi:Receptor Rfc="{receptor}" Nombre="R"/><cfdi:Conceptos><cfdi:Concepto Descripcion="Reja"/></cfdi:Conceptos><cfdi:Complemento><tfd:TimbreFiscalDigital UUID="{uuid}"/></cfdi:Complemento></cfdi:Comprobante>""")),
                uuid + ".xml", "application/xml"),
            Dia.Year, Dia.Month));

    private Task<FacturasDelSatDelMesDto> MesAsync() =>
        ComoAsync(Administrador, async () => (await _descarga.GetAsync(_cliente, Dia.Year, Dia.Month)).Meses.Last());

    [Fact]
    public async Task El_sat_dice_quien_se_cancelo_y_el_impuesto_se_vuelve_a_calcular()
    {
        await SubirAsync(Venta, Rfc, "XAXX010101000", "5800.00");
        await SubirAsync(Compra, "AAA010101AAA", Rfc, "1160");
        await SubirAsync(Otra, "AAA010101AAA", Rfc, "232.5");
        _sat.Respuestas[Venta] = new EstatusDelSat("Cancelado", "Cancelado sin aceptación", "Cancelable sin aceptación", "S");
        _sat.Respuestas[Compra] = new EstatusDelSat("Vigente", "En proceso", "Cancelable con aceptación", "S");
        _sat.Respuestas[Otra] = new EstatusDelSat("No Encontrado", null, null, "N - 602: Comprobante no encontrado.");

        // El impuesto ya se había calculado: la revisión lo deja por actualizar si algo cambia.
        await GetRequiredService<ActualizadorDeImpuestos>().ActualizarAsync(maximo: 500, espera: TimeSpan.Zero);
        (await ComoAsync(Administrador, () => _alDia.GetAsync(_cliente, Dia.Year, Dia.Month))).Actualizando.ShouldBeFalse();

        var r = await ComoAsync(Administrador, () => _descarga.RevisarEstatusAsync(_cliente, Dia.Year, Dia.Month));
        r.Revisadas.ShouldBe(3);
        r.Cambiaron.ShouldBe(3);
        r.Canceladas.ShouldBe(1);
        r.EnProceso.ShouldBe(1);
        r.NoEncontradas.ShouldBe(1);
        // Al SAT va el total tal cual viene en el XML, con los RFC de la factura.
        _sat.Preguntas.ShouldContain((Rfc, "XAXX010101000", "5800.00", Venta));
        _sat.Preguntas.ShouldContain(("AAA010101AAA", Rfc, "232.5", Otra));

        var mes = await MesAsync();
        mes.Canceladas.ShouldBe(1);
        mes.EnProceso.ShouldBe(1);
        mes.NoEncontradas.ShouldBe(1);
        mes.SinEstatus.ShouldBe(0);
        (await ComoAsync(Administrador, () => _alDia.GetAsync(_cliente, Dia.Year, Dia.Month))).Actualizando.ShouldBeTrue();

        // Otra vuelta: la cancelada ya no se pregunta y, si nada cambió, nada se toca.
        _sat.Preguntas.Clear();
        var otra = await ComoAsync(Administrador, () => _descarga.RevisarEstatusAsync(_cliente, Dia.Year, Dia.Month));
        otra.Revisadas.ShouldBe(2);
        otra.Cambiaron.ShouldBe(0);
        _sat.Preguntas.ShouldNotContain(p => p.Uuid == Venta);
    }

    [Fact]
    public async Task La_vuelta_de_la_noche_revisa_sola_y_si_el_sat_no_responde_no_toca_nada()
    {
        await SubirAsync(Venta, Rfc, "XAXX010101000", "100.00");
        _sat.Caido = true;
        var caido = await _revisor.RevisarLaNocheAsync(DateTime.Now, maximo: 5000);
        caido.SatSinResponder.ShouldBeTrue();
        caido.Cambiaron.ShouldBe(0);
        (await MesAsync()).SinEstatus.ShouldBe(1);

        _sat.Caido = false;
        _sat.Respuestas[Venta] = new EstatusDelSat("Cancelado", null, null, "S");
        var noche = await _revisor.RevisarLaNocheAsync(DateTime.Now, maximo: 5000);
        noche.Cambiaron.ShouldBeGreaterThan(0);
        (await MesAsync()).Canceladas.ShouldBe(1);
    }

    [Theory]
    [InlineData("Vigente", null, "Vigente")]
    [InlineData("Vigente", "En proceso", "En proceso de cancelación")]
    [InlineData("Vigente", "Solicitud rechazada", "Vigente")]
    [InlineData("Cancelado", "Plazo vencido", "Cancelado")]
    [InlineData("No Encontrado", null, "No encontrado en el SAT")]
    public void Lo_que_contesta_el_sat_se_guarda_en_pocas_palabras(string estado, string? proceso, string esperado)
    {
        new EstatusDelSat(estado, proceso, null, null).ParaGuardar.ShouldBe(esperado);
    }
}
