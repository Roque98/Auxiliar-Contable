using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Boveda;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Claude interpreta cada operación con todas sus fuentes (se pide desde la mesa y lo hace el trabajo en segundo plano);
/// lo que sigue al día llega a las pólizas y no se le vuelve a preguntar; si cambian las fuentes, queda pendiente.
/// </summary>
public class InterpretacionesIntegrationTests : ConJerarquiaTestBase
{
    private const string Rfc = "GODE561231GR8";
    private const string Uuid = "6F9619FF-8B86-D011-B42D-00C04FC964B1";

    private readonly IInterpretacionesAppService _interpretaciones;
    private readonly InterpretadorDeOperaciones _interpretador;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;

    private static DateTime Dia => new(DateTime.Now.Year, DateTime.Now.Month, 1);

    public InterpretacionesIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _interpretaciones = GetRequiredService<IInterpretacionesAppService>();
        _interpretador = GetRequiredService<InterpretadorDeOperaciones>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _motor.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Taller Lupita");
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
        await ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = Rfc }));
    }

    private static string Xml(string uuid, decimal total) =>
        $"""<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" Fecha="{Dia:yyyy-MM-dd}T10:00:00" Total="{total:0.00}" TipoDeComprobante="I"><cfdi:Emisor Rfc="AAA010101AAA" Nombre="MAQUINARIA DEL BAJIO"/><cfdi:Receptor Rfc="{Rfc}" Nombre="TALLER"/><cfdi:Conceptos><cfdi:Concepto Descripcion="Compresor de aire"/></cfdi:Conceptos><cfdi:Complemento><tfd:TimbreFiscalDigital UUID="{uuid}"/></cfdi:Complemento></cfdi:Comprobante>""";

    private Task SubirAsync(string nombre, string contenido) =>
        ComoAsync(Usuario, () => GetRequiredService<IBovedaAppService>().SubirAsync(_cliente, TipoDeDocumento.Cfdi,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(contenido)), nombre, "application/xml"),
            Dia.Year, Dia.Month));

    private Task<InterpretacionesDelMesDto> VerAsync() =>
        ComoAsync(Administrador, () => _interpretaciones.GetDelMesAsync(_cliente, Dia.Year, Dia.Month));

    [Fact]
    public async Task Se_pide_se_interpreta_en_segundo_plano_y_llega_a_las_polizas()
    {
        await SubirAsync("compresor.xml", Xml(Uuid, 34800m));
        // El cliente no la ve ni la pide.
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _interpretaciones.GetDelMesAsync(_cliente, Dia.Year, Dia.Month)));

        var pedida = await ComoAsync(Administrador, () => _interpretaciones.PedirAsync(_cliente, Dia.Year, Dia.Month, null));
        pedida.EnLaFila.ShouldBe(1);
        pedida.Pendientes.ShouldBe(0);
        _motor.Interpretaciones.ShouldBe(0); // la hace el trabajo en segundo plano, no la petición

        (await _interpretador.ProcesarAsync()).ShouldBe(1);
        var lista = await VerAsync();
        var hecha = lista.Operaciones.ShouldHaveSingleItem();
        (hecha.AlDia, hecha.EnLaFila, hecha.Error).ShouldBe((true, false, (string?)null));
        hecha.Interpretacion!.Objeto.ShouldBe("Compresor de aire");
        _motor.InterpretacionRecibida!.Value.ConFactura.ShouldBeTrue();

        // Pedirla otra vez no le vuelve a preguntar a Claude: ya está al día.
        (await ComoAsync(Administrador, () => _interpretaciones.PedirAsync(_cliente, Dia.Year, Dia.Month, null))).EnLaFila.ShouldBe(0);
        (await _interpretador.ProcesarAsync()).ShouldBe(0);
        _motor.Interpretaciones.ShouldBe(1);

        // Las pólizas reciben lo interpretado (y no llaman a Claude).
        _motor.Contabilidad = (_, ops, _) => new ResultadoContable();
        await ComoAsync(Administrador, () => GetRequiredService<IMesaDeTrabajoAppService>().GenerarPolizasAsync(_cliente, Dia.Year, Dia.Month, new DatosDeCierre()));
        _motor.Contabilizadas.ShouldHaveSingleItem().Interpretacion!.Categoria.ShouldBe("inversion");
        _motor.Interpretaciones.ShouldBe(1);
    }

    [Fact]
    public async Task Si_claude_no_responde_se_queda_en_la_fila_con_el_error()
    {
        await SubirAsync("compresor.xml", Xml(Uuid, 34800m));
        await ComoAsync(Administrador, () => _interpretaciones.PedirAsync(_cliente, Dia.Year, Dia.Month, null));
        _motor.Caido = true;
        (await _interpretador.ProcesarAsync()).ShouldBe(0);
        var x = (await VerAsync()).Operaciones.ShouldHaveSingleItem();
        x.EnLaFila.ShouldBeTrue();
        x.Error.ShouldNotBeNullOrWhiteSpace();
        // No se reintenta en cada vuelta: espera antes de volver a pedir.
        _motor.Caido = false;
        (await _interpretador.ProcesarAsync()).ShouldBe(0);
    }
}
