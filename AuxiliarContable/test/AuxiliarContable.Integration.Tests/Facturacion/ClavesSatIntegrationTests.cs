using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Boveda;
using AuxiliarContable.Documentos;
using AuxiliarContable.Facturacion;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using Shouldly;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// La clave SAT: buscarla por cómo se llama el producto (Facturar y el asistente) y revisar si la de una factura, emitida
/// o recibida, corresponde a su descripción y a su unidad. Al motor van los conceptos, nunca el RFC ni el nombre.
/// </summary>
public class ClavesSatIntegrationTests : ConJerarquiaTestBase
{
    private const string Uuid = "6F2A1B3C-4D5E-4F60-8A7B-9C0D1E2F3A4B";

    private static readonly string Factura = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital"
            Version="4.0" Fecha="2026-11-10T10:00:00" Total="1160.00" TipoDeComprobante="I" MetodoPago="PUE" FormaPago="03">
          <cfdi:Emisor Rfc="PRO010101AAA" Nombre="Ferretería del Centro" RegimenFiscal="601"/>
          <cfdi:Receptor Rfc="CLI020202BB1" Nombre="Cliente" UsoCFDI="G03"/>
          <cfdi:Conceptos>
            <cfdi:Concepto ClaveProdServ="39111611" ClaveUnidad="E48" Descripcion="Reflector LED 50W" Cantidad="1" ValorUnitario="1000" Importe="1000"/>
            <cfdi:Concepto ClaveProdServ="72151500" ClaveUnidad="E48" Descripcion="Instalación eléctrica" Cantidad="1" ValorUnitario="0" Importe="0"/>
          </cfdi:Conceptos>
          <cfdi:Complemento><tfd:TimbreFiscalDigital Version="1.1" UUID="{Uuid}" FechaTimbrado="2026-11-10T10:01:00"/></cfdi:Complemento>
        </cfdi:Comprobante>
        """;

    private readonly ISolicitudesDeFacturaAppService _facturas;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;
    private Guid _otroCliente;

    public ClavesSatIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _facturas = GetRequiredService<ISolicitudesDeFacturaAppService>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _motor.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Eléctrica Lupita");
        _otroCliente = await CrearOrganizacionAsync("Otro cliente");
        await WithUnitOfWorkAsync(() =>
            GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
    }

    [Fact]
    public void Lee_los_conceptos_del_comprobante()
    {
        var conceptos = ConceptosDelCfdi.Leer(Encoding.UTF8.GetBytes(Factura.Trim()));
        conceptos.Count.ShouldBe(2);
        conceptos[0].ShouldBe(new ConceptoParaRevisar("39111611", "E48", "Reflector LED 50W"));
        ConceptosDelCfdi.Leer(Encoding.UTF8.GetBytes("<no-es-xml")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Buscar_la_clave_devuelve_clave_unidad_y_el_aviso_del_anexo_6()
    {
        var r = await ComoAsync(Usuario, () => _facturas.BuscarClaveAsync(_cliente, new BuscarClaveDto { Descripcion = "reflector Nova LED" }));

        var c = r.Claves.ShouldHaveSingleItem();
        c.Clave.ShouldBe("39111611");
        c.Unidades.Single().Clave.ShouldBe("H87");
        r.Aviso.ShouldNotBeNull().ShouldContain("546");
        r.Ignoradas.ShouldBe(["nova"]);
        _motor.ClavesBuscadas.ShouldHaveSingleItem().Descripcion.ShouldBe("reflector Nova LED");
    }

    [Fact]
    public async Task Revisar_conceptos_marca_la_alerta_en_su_concepto()
    {
        var alertas = await ComoAsync(Usuario, () => _facturas.RevisarConceptosAsync(_cliente, new RevisarConceptosDto
        {
            Conceptos =
            [
                new ConceptoParaRevisarDto { ClaveProdServ = "90101501", ClaveUnidad = "E48", Descripcion = "Entrega" },
                new ConceptoParaRevisarDto(),
                new ConceptoParaRevisarDto { ClaveProdServ = "39111611", ClaveUnidad = "E48", Descripcion = "Reflector" },
            ],
        }));

        var a = alertas.ShouldHaveSingleItem();
        a.Concepto.ShouldBe(2);
        a.Nivel.ShouldBe("alto");
        _motor.ClavesRevisadas.ShouldHaveSingleItem().Conceptos.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Lo_ajeno_no_existe()
    {
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () =>
            _facturas.BuscarClaveAsync(_otroCliente, new BuscarClaveDto { Descripcion = "reflector" })));
        _motor.ClavesBuscadas.ShouldBeEmpty();
    }

    [Fact]
    public async Task Revisa_una_factura_recibida_de_su_boveda_por_su_uuid()
    {
        await ComoAsync(Administrador, () => GetRequiredService<IBovedaAppService>().SubirAsync(_cliente, TipoDeDocumento.Cfdi,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(Factura.Trim())), "factura.xml", "text/xml")));

        var json = await ComoAsync(Usuario, () => GetRequiredService<RevisorDeClaves>().RevisarFacturaAsync(_cliente, Uuid.ToLowerInvariant()));

        json.ShouldContain("Ferretería del Centro");
        json.ShouldContain("es de un PRODUCTO");
        var (conceptos, _) = _motor.ClavesRevisadas.ShouldHaveSingleItem();
        conceptos.Select(c => c.Clave).ShouldBe(["39111611", "72151500"]);

        // Desde otro cliente, ese UUID no existe.
        await Should.ThrowAsync<Exception>(() => ComoAsync(Usuario, () =>
            GetRequiredService<RevisorDeClaves>().RevisarFacturaAsync(_otroCliente, Uuid)));
    }
}
