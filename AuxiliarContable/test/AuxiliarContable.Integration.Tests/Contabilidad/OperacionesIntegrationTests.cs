using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Boveda;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Documentos;
using AuxiliarContable.Identity;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Operaciones (factura · transacción · control interno): lo que se sube abre su operación, el mismo UUID o la misma
/// referencia la amarran sola, el efectivo y el crédito quedan también en «Efectivo y crédito», «no hay» pide motivo,
/// las más completas van primero y lo ajeno no existe.
/// </summary>
public class OperacionesIntegrationTests : ConJerarquiaTestBase
{
    private const string Uuid = "6F9619FF-8B86-D011-B42D-00C04FC964FF";

    private readonly IOperacionesAppService _operaciones;
    private Guid _cliente;
    private Guid _otroCliente;

    private static DateTime Dia => new(DateTime.Now.Year, DateTime.Now.Month, 1);

    public OperacionesIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _operaciones = GetRequiredService<IOperacionesAppService>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _cliente = await CrearOrganizacionAsync("Panadería Lupita");
        _otroCliente = await CrearOrganizacionAsync("Otro cliente");
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
        await ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = "GODE561231GR8" }));
    }

    private static string Xml(string uuid, string emisor = "AAA010101AAA", string receptor = "GODE561231GR8") =>
        $"""<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" Fecha="{Dia:yyyy-MM-dd}T10:00:00" Total="1160.00" TipoDeComprobante="I"><cfdi:Emisor Rfc="{emisor}" Nombre="HARINAS DEL NORTE"/><cfdi:Receptor Rfc="{receptor}" Nombre="PANADERIA LUPITA"/><cfdi:Conceptos><cfdi:Concepto Descripcion="Harina de trigo 44 kg"/></cfdi:Conceptos><cfdi:Complemento><tfd:TimbreFiscalDigital UUID="{uuid.ToLowerInvariant()}"/></cfdi:Complemento></cfdi:Comprobante>""";

    private async Task<Guid> SubirAsync(Guid cliente, string nombre, TipoDeDocumento tipo, string contenido)
    {
        var r = await ComoAsync(Usuario, () => GetRequiredService<IBovedaAppService>().SubirAsync(cliente, tipo,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(contenido)), nombre, "application/octet-stream"),
            Dia.Year, Dia.Month));
        return r.Documento.Id;
    }

    private static GuardarOperacionDto Datos(OperacionDelClienteDto o) => new()
    {
        Fecha = o.Fecha, Sentido = o.Sentido, Importe = o.Importe, Descripcion = o.Descripcion, Contraparte = o.Contraparte,
    };

    [Fact]
    public async Task El_xml_abre_su_operacion_y_el_ticket_se_le_amarra_por_uuid()
    {
        var xml = await SubirAsync(_cliente, "harina.xml", TipoDeDocumento.Cfdi, Xml(Uuid));
        var factura = await ComoAsync(Usuario, () => _operaciones.DeDocumentoAsync(_cliente, xml));
        factura.Factura.Estado.ShouldBe(EstadoDePata.Ligada);
        factura.Factura.Referencia.ShouldBe(Uuid);
        factura.Sentido.ShouldBe(SentidoDeOperacion.Salida);
        factura.Importe.ShouldBe(1160m);
        factura.Contraparte.ShouldBe("HARINAS DEL NORTE");
        factura.Descripcion.ShouldBe("Harina de trigo 44 kg");
        // Pedirla otra vez no duplica.
        (await ComoAsync(Usuario, () => _operaciones.DeDocumentoAsync(_cliente, xml))).Id.ShouldBe(factura.Id);

        // El ticket abre la suya; al darle el UUID se junta con la de la factura.
        var ticket = await SubirAsync(_cliente, "nota-128.pdf", TipoDeDocumento.ControlInterno, "%PDF-1.4 nota");
        var control = await ComoAsync(Usuario, () => _operaciones.DeDocumentoAsync(_cliente, ticket));
        control.ControlInterno.Estado.ShouldBe(EstadoDePata.Ligada);
        control.Id.ShouldNotBe(factura.Id);
        var datos = Datos(control);
        datos.Factura = new GuardarPataDto { Estado = EstadoDePata.Ligada, Referencia = Uuid.ToLowerInvariant() };
        datos.Transaccion = new GuardarPataDto { Estado = EstadoDePata.Ligada, Forma = FormaDeTransaccion.Banco, Referencia = "SPEI 4471" };
        var junta = await ComoAsync(Usuario, () => _operaciones.ActualizarAsync(_cliente, control.Id, datos));

        junta.Id.ShouldBe(factura.Id);
        junta.Ligadas.ShouldBe(3);
        junta.ControlInterno.NombreDelDocumento.ShouldBe("nota-128.pdf");
        junta.Factura.NombreDelDocumento.ShouldBe("harina.xml");
        junta.Transaccion.Referencia.ShouldBe("SPEI 4471");
        var mes = await ComoAsync(Usuario, () => _operaciones.GetListAsync(_cliente, Dia.Year, Dia.Month));
        mes.Operaciones.Count.ShouldBe(1);
        mes.PuedeEditar.ShouldBeTrue();

        // Lo ajeno no existe.
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _operaciones.GetListAsync(_otroCliente, Dia.Year, Dia.Month)));
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _operaciones.ActualizarAsync(_otroCliente, junta.Id, datos)));
    }

    [Fact]
    public async Task El_efectivo_y_el_credito_quedan_en_efectivo_y_credito()
    {
        var o = await ComoAsync(Usuario, () => _operaciones.CrearAsync(_cliente, new GuardarOperacionDto
        {
            Fecha = Dia, Sentido = SentidoDeOperacion.Salida, Importe = 480m, Descripcion = "Clavos y pegamento", Contraparte = "Ferretería",
            Transaccion = new GuardarPataDto { Estado = EstadoDePata.Ligada, Forma = FormaDeTransaccion.Credito, Vence = Dia.AddDays(30) },
            Factura = new GuardarPataDto { Estado = EstadoDePata.NoHay, Motivo = "No se facturó" },
        }));

        o.Transaccion.Forma.ShouldBe(FormaDeTransaccion.Credito);
        o.Transaccion.Vence.ShouldBe(Dia.AddDays(30));
        o.Factura.Estado.ShouldBe(EstadoDePata.NoHay);
        o.Respondidas.ShouldBe(2);
        var movimientos = GetRequiredService<IRepository<MovimientoFueraDelBanco, Guid>>();
        var m = await WithUnitOfWorkAsync(() => movimientos.GetAsync(o.Transaccion.MovimientoFueraDelBancoId!.Value));
        m.Tipo.ShouldBe(TipoDeMovimientoFueraDelBanco.CompraACredito);
        m.Importe.ShouldBe(480m);

        // Si al final se pagó por transferencia, el registro de crédito se quita.
        var datos = Datos(o);
        datos.Transaccion = new GuardarPataDto { Estado = EstadoDePata.Ligada, Forma = FormaDeTransaccion.Banco, Referencia = "TRF 9921" };
        var banco = await ComoAsync(Usuario, () => _operaciones.ActualizarAsync(_cliente, o.Id, datos));
        banco.Transaccion.MovimientoFueraDelBancoId.ShouldBeNull();
        (await WithUnitOfWorkAsync(() => movimientos.FindAsync(m.Id))).ShouldBeNull();

        // «No hay» sin motivo no se acepta; efectivo sin importe tampoco.
        datos.ControlInterno = new GuardarPataDto { Estado = EstadoDePata.NoHay };
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Usuario, () => _operaciones.ActualizarAsync(_cliente, o.Id, datos)));
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Usuario, () => _operaciones.CrearAsync(_cliente, new GuardarOperacionDto
        {
            Fecha = Dia, Descripcion = "Sin importe",
            Transaccion = new GuardarPataDto { Estado = EstadoDePata.Ligada, Forma = FormaDeTransaccion.Efectivo },
        })));
    }

    [Fact]
    public async Task Todo_incluye_lo_de_antes_y_pone_primero_lo_mas_completo()
    {
        // Un gasto en efectivo registrado antes de que existieran las operaciones, y una venta facturada.
        await ComoAsync(Usuario, () => GetRequiredService<IDineroFueraDelBancoAppService>().CrearAsync(_cliente, new GuardarMovimientoFueraDelBancoDto
        {
            Tipo = TipoDeMovimientoFueraDelBanco.SalidaEnEfectivo, Fecha = Dia, Importe = 150m, Concepto = "Gas",
        }));
        await SubirAsync(_cliente, "venta.xml", TipoDeDocumento.Cfdi,
            Xml("11111111-2222-3333-4444-555555555555", emisor: "GODE561231GR8", receptor: "XAXX010101000"));

        var mes = await ComoAsync(Usuario, () => _operaciones.GetListAsync(_cliente, Dia.Year, Dia.Month));

        mes.Operaciones.Count.ShouldBe(2);
        mes.Operaciones.ShouldContain(o => o.Descripcion == "Gas" && o.Transaccion.Forma == FormaDeTransaccion.Efectivo);
        var venta = mes.Operaciones.Single(o => o.Factura.Estado == EstadoDePata.Ligada);
        venta.Sentido.ShouldBe(SentidoDeOperacion.Entrada);
        venta.Contraparte.ShouldBe("PANADERIA LUPITA");

        // Al completar la venta, sube al primer lugar.
        var datos = Datos(venta);
        datos.Transaccion = new GuardarPataDto { Estado = EstadoDePata.Ligada, Forma = FormaDeTransaccion.Banco, Referencia = "DEP 0042" };
        datos.ControlInterno = new GuardarPataDto { Estado = EstadoDePata.NoHay, Motivo = "No tengo comprobante interno" };
        await ComoAsync(Usuario, () => _operaciones.ActualizarAsync(_cliente, venta.Id, datos));
        mes = await ComoAsync(Usuario, () => _operaciones.GetListAsync(_cliente, Dia.Year, Dia.Month));
        mes.Operaciones.Count.ShouldBe(2);
        mes.Operaciones[0].Id.ShouldBe(venta.Id);

        // Un estado de cuenta no abre operación: sus movimientos se amarran después.
        var estado = await SubirAsync(_cliente, "banorte.pdf", TipoDeDocumento.EstadoDeCuenta, "%PDF-1.4 estado");
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Usuario, () => _operaciones.DeDocumentoAsync(_cliente, estado)));
    }
    [Fact]
    public async Task La_guia_lee_la_forma_de_pago_y_recuerda_lo_que_se_subira_despues()
    {
        const uuid = "7A9619FF-8B86-D011-B42D-00C04FC964AA";
        var contenido = Xml(uuid)
            .Replace("TipoDeComprobante=\"I\"", "TipoDeComprobante=\"I\" MetodoPago=\"PPD\" FormaPago=\"99\" Moneda=\"MXN\"")
            .Replace("<cfdi:Emisor", "<cfdi:CfdiRelacionados TipoRelacion=\"04\"><cfdi:CfdiRelacionado UUID=\"" + Uuid.ToLowerInvariant() + "\"/></cfdi:CfdiRelacionados><cfdi:Emisor");
        var xml = await SubirAsync(_cliente, "ppd.xml", TipoDeDocumento.Cfdi, contenido);
        var o = await ComoAsync(Usuario, () => _operaciones.DeDocumentoAsync(_cliente, xml));

        var f = (await ComoAsync(Usuario, () => _operaciones.GetFacturaAsync(_cliente, o.Id)))!;
        f.Tipo.ShouldBe("I");
        f.Metodo.ShouldBe("PPD");
        f.Forma.ShouldBe("99");
        f.Moneda.ShouldBe("MXN");
        f.TipoRelacion.ShouldBe("04");
        f.UuidRelacionado.ShouldBe(Uuid);

        // «Lo subiré después»: la pata sigue pendiente, con lo que dijo, y aparece en lo por subir.
        var datos = Datos(o);
        datos.ControlInterno = new GuardarPataDto { Estado = EstadoDePata.Pendiente, Motivo = "Lo subiré después" };
        datos.Transaccion = new GuardarPataDto { Estado = EstadoDePata.Pendiente, Motivo = OperacionesAppService.MotivoDelEstadoDeCuenta + " al conciliar." };
        var r = await ComoAsync(Usuario, () => _operaciones.ActualizarAsync(_cliente, o.Id, datos));
        r.ControlInterno.Estado.ShouldBe(EstadoDePata.Pendiente);
        r.ControlInterno.Motivo.ShouldBe("Lo subiré después");

        var porSubir = await ComoAsync(Usuario, () => _operaciones.GetPorSubirAsync(_cliente));
        var uno = porSubir.ShouldHaveSingleItem(); // lo del estado de cuenta no se le recuerda
        uno.Pata.ShouldBe(PataDeOperacion.ControlInterno);
        uno.OperacionId.ShouldBe(o.Id);

        // Sin XML no hay datos de factura; lo ajeno no existe.
        var sinXml = await ComoAsync(Usuario, () => _operaciones.CrearAsync(_cliente, new GuardarOperacionDto
        {
            Fecha = Dia, Sentido = SentidoDeOperacion.Salida, Importe = 100m, Descripcion = "Café",
        }));
        (await ComoAsync(Usuario, () => _operaciones.GetFacturaAsync(_cliente, sinXml.Id))).ShouldBeNull();
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _operaciones.GetPorSubirAsync(_otroCliente)));
    }
}
