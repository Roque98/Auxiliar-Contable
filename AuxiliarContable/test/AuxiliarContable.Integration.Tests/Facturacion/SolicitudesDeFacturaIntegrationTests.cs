using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Boveda;
using AuxiliarContable.Documentos;
using AuxiliarContable.Facturacion;
using AuxiliarContable.Identity;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Identity;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Pedir factura: el cliente pide (los totales los calcula el servidor), el despacho ve la cola de todos los clientes
/// y la atiende con el XML timbrado de la bóveda de ESE cliente o la rechaza; el cliente cancela mientras está
/// pendiente; la constancia del receptor llena sus datos.
/// </summary>
public class SolicitudesDeFacturaIntegrationTests : ConJerarquiaTestBase
{
    private const string Xml = """<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0"/>""";

    private readonly ISolicitudesDeFacturaAppService _facturas;
    private readonly MotorFiscalDePrueba _motor;
    private Guid _cliente;
    private Guid _otroCliente;
    private IdentityUser _lector = null!;

    public SolicitudesDeFacturaIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _facturas = GetRequiredService<ISolicitudesDeFacturaAppService>();
        _motor = (MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _motor.Reiniciar();
        _cliente = await CrearOrganizacionAsync("Panadería Lupita");
        _otroCliente = await CrearOrganizacionAsync("Otro cliente");
        _lector = await CrearUsuarioAsync("ana", AuxiliarRoles.Usuario);
        await WithUnitOfWorkAsync(async () =>
        {
            var m = GetRequiredService<MembresiasDeOrganizacion>();
            await m.AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro);
            await m.AgregarAsync(_cliente, _lector.Id, RolEnOrganizacion.Lector);
        });
    }

    private static CrearSolicitudDeFacturaDto Pedido(params ConceptoDto[] conceptos) => new()
    {
        Receptor = new ReceptorDto { Rfc = "cli020202bb1", Nombre = "Cafetería La Esquina", CodigoPostal = "06600", Regimen = "626" },
        UsoCfdi = "G03",
        FormaDePago = "03",
        MetodoDePago = "PUE",
        Conceptos = conceptos.Length > 0 ? [.. conceptos]
            :
            [
                new ConceptoDto { ClaveProdServ = "50181900", Descripcion = "Pan dulce, caja de 50", Cantidad = 2, ClaveUnidad = "h87", PrecioUnitario = 450m, TasaIva = 0m },
                new ConceptoDto { ClaveProdServ = "90101501", Descripcion = "Servicio de entrega", Cantidad = 1, ClaveUnidad = "E48", PrecioUnitario = 100m, TasaIva = 0.16m },
            ],
        Nota = "Que diga el folio del pedido 123",
    };

    private Task<ResultadoDeSubidaDto> SubirAsync(Guid cliente, string nombre, TipoDeDocumento tipo, string contenido) =>
        ComoAsync(Administrador, () => GetRequiredService<IBovedaAppService>().SubirAsync(cliente, tipo,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(contenido)), nombre, "application/octet-stream")));

    [Fact]
    public async Task El_cliente_pide_y_el_servidor_calcula_los_totales()
    {
        var s = await ComoAsync(Usuario, () => _facturas.CrearAsync(_cliente, Pedido()));

        s.Estado.ShouldBe(EstadoDeSolicitudDeFactura.Pendiente);
        s.Receptor.Rfc.ShouldBe("CLI020202BB1");
        s.Receptor.Nombre.ShouldBe("CAFETERÍA LA ESQUINA");
        s.Subtotal.ShouldBe(1000m);
        s.Iva.ShouldBe(16m);
        s.Total.ShouldBe(1016m);
        s.Conceptos[0].ClaveUnidad.ShouldBe("H87");

        var lista = await ComoAsync(Usuario, () => _facturas.GetListAsync(_cliente));
        lista.Solicitudes.Single().Id.ShouldBe(s.Id);
        lista.Receptores.Single().Rfc.ShouldBe("CLI020202BB1");
        lista.PuedePedir.ShouldBeTrue();
        lista.EsEquipo.ShouldBeFalse();
    }

    [Fact]
    public async Task Un_concepto_mal_capturado_o_sin_conceptos_no_se_acepta()
    {
        await Should.ThrowAsync<BusinessException>(() => ComoAsync(Usuario, () => _facturas.CrearAsync(_cliente,
            Pedido(new ConceptoDto { ClaveProdServ = "50181900", Descripcion = "x", Cantidad = 1, ClaveUnidad = "H87", PrecioUnitario = 10m, TasaIva = 0.10m }))));
        var sinConceptos = Pedido();
        sinConceptos.Conceptos = [];
        await Should.ThrowAsync<Exception>(() => ComoAsync(Usuario, () => _facturas.CrearAsync(_cliente, sinConceptos)));
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(_lector, () => _facturas.CrearAsync(_cliente, Pedido())));
    }

    [Fact]
    public async Task El_despacho_ve_la_cola_y_la_atiende_con_el_XML_de_ese_cliente()
    {
        var s = await ComoAsync(Usuario, () => _facturas.CrearAsync(_cliente, Pedido()));

        var cola = await ComoAsync(Administrador, () => _facturas.GetPendientesAsync());
        cola.Single().Cliente.ShouldBe("Panadería Lupita");
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _facturas.GetPendientesAsync()));

        var ajeno = await SubirAsync(_otroCliente, "ajena.xml", TipoDeDocumento.Cfdi, Xml);
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Administrador, () =>
            _facturas.AtenderAsync(_cliente, s.Id, new AtenderSolicitudDto { XmlDocumentoId = ajeno.Documento.Id })));
        var pdf = await SubirAsync(_cliente, "factura.pdf", TipoDeDocumento.Otro, "%PDF-1.4 factura");
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () =>
            _facturas.AtenderAsync(_cliente, s.Id, new AtenderSolicitudDto { XmlDocumentoId = pdf.Documento.Id })));
        var xml = await SubirAsync(_cliente, "A-15.xml", TipoDeDocumento.Cfdi, Xml);
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () =>
            _facturas.AtenderAsync(_cliente, s.Id, new AtenderSolicitudDto { XmlDocumentoId = xml.Documento.Id })));

        var emitida = await ComoAsync(Administrador, () => _facturas.AtenderAsync(_cliente, s.Id,
            new AtenderSolicitudDto { XmlDocumentoId = xml.Documento.Id, PdfDocumentoId = pdf.Documento.Id, Respuesta = "Folio A-15" }));

        emitida.Estado.ShouldBe(EstadoDeSolicitudDeFactura.Emitida);
        emitida.NombreDelXml.ShouldBe("A-15.xml");
        emitida.NombreDelPdf.ShouldBe("factura.pdf");
        (await ComoAsync(Administrador, () => _facturas.GetPendientesAsync())).ShouldBeEmpty();
        await Should.ThrowAsync<BusinessException>(() => ComoAsync(Administrador, () =>
            _facturas.RechazarAsync(_cliente, s.Id, new RechazarSolicitudDto { Respuesta = "tarde" })));
    }

    [Fact]
    public async Task Se_rechaza_con_motivo_y_el_cliente_cancela_solo_lo_pendiente()
    {
        var a = await ComoAsync(Usuario, () => _facturas.CrearAsync(_cliente, Pedido()));
        var b = await ComoAsync(Usuario, () => _facturas.CrearAsync(_cliente, Pedido()));

        var rechazada = await ComoAsync(Administrador, () => _facturas.RechazarAsync(_cliente, a.Id,
            new RechazarSolicitudDto { Respuesta = "El nombre del receptor no coincide con su constancia" }));
        rechazada.Estado.ShouldBe(EstadoDeSolicitudDeFactura.Rechazada);
        rechazada.Respuesta!.ShouldContain("no coincide");

        (await ComoAsync(Usuario, () => _facturas.CancelarAsync(_cliente, b.Id))).Estado.ShouldBe(EstadoDeSolicitudDeFactura.Cancelada);
        await Should.ThrowAsync<BusinessException>(() => ComoAsync(Usuario, () => _facturas.CancelarAsync(_cliente, a.Id)));
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _facturas.CancelarAsync(_otroCliente, b.Id)));
    }

    [Fact]
    public async Task La_constancia_del_receptor_llena_sus_datos()
    {
        _motor.Constancia = new ConstanciaLeida
        {
            EsConstancia = true, Rfc = "CLI020202BB1", Nombre = "CAFETERIA LA ESQUINA", CodigoPostal = "06600",
            Regimenes = ["626 - Régimen Simplificado de Confianza"],
        };

        var r = await ComoAsync(Usuario, () => _facturas.LeerConstanciaDelReceptorAsync(_cliente,
            new LeerConstanciaDelReceptorDto { PdfBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("%PDF-1.4 csf")) }));

        r.Rfc.ShouldBe("CLI020202BB1");
        r.CodigoPostal.ShouldBe("06600");
        r.Regimen.ShouldBe("626");

        _motor.Constancia = new ConstanciaLeida { EsConstancia = false };
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Usuario, () => _facturas.LeerConstanciaDelReceptorAsync(_cliente,
            new LeerConstanciaDelReceptorDto { PdfBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("%PDF-1.4 otra")) })));
    }
}
