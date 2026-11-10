using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Boveda;
using AuxiliarContable.Documentos;
using AuxiliarContable.Identity;
using AuxiliarContable.Organizaciones;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// La bóveda contra PostgreSQL real: guardar, duplicados por archivo, navegación y búsqueda,
/// descarga descifrada y el aislamiento entre clientes. Leer el contenido es del motor fiscal.
/// </summary>
public class BovedaIntegrationTests : ConJerarquiaTestBase
{
    private const string Xml = """<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0"/>""";

    private readonly IBovedaAppService _boveda;
    private Guid _cliente;
    private Guid _otroCliente;
    private Volo.Abp.Identity.IdentityUser _lector = null!;

    public BovedaIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _boveda = GetRequiredService<IBovedaAppService>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _cliente = await CrearOrganizacionAsync("Panadería Lupita");
        _otroCliente = await CrearOrganizacionAsync("Otro cliente");
        _lector = await CrearUsuarioAsync("lector", AuxiliarRoles.Usuario);
        await WithUnitOfWorkAsync(async () =>
        {
            var membresias = GetRequiredService<MembresiasDeOrganizacion>();
            await membresias.AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro);
            await membresias.AgregarAsync(_cliente, _lector.Id, RolEnOrganizacion.Lector);
        });
    }

    private static IRemoteStreamContent Archivo(string contenido, string nombre) => Archivo(Encoding.UTF8.GetBytes(contenido), nombre);

    private static IRemoteStreamContent Archivo(byte[] bytes, string nombre) =>
        new RemoteStreamContent(new MemoryStream(bytes), nombre, "application/octet-stream");

    private Task<ResultadoDeSubidaDto> SubirAsync(string contenido, string nombre, TipoDeDocumento tipo = TipoDeDocumento.Otro) =>
        ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, tipo, Archivo(contenido, nombre)));

    [Fact]
    public async Task Un_XML_se_guarda_como_factura_sin_leerlo()
    {
        var subido = await SubirAsync(Xml, "factura.xml");

        subido.EsDuplicado.ShouldBeFalse();
        subido.Documento.Tipo.ShouldBe(TipoDeDocumento.Cfdi);
        subido.Documento.Nombre.ShouldBe("factura.xml");

        var resumen = await ComoAsync(Usuario, () => _boveda.GetResumenAsync(_cliente));
        resumen.ShouldHaveSingleItem().Cantidad.ShouldBe(1);

        var porTexto = await ComoAsync(Usuario, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente, Texto = "FACTURA" }));
        porTexto.Items.ShouldHaveSingleItem().Nombre.ShouldBe("factura.xml");

        var deTickets = await ComoAsync(Usuario, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente, Tipo = TipoDeDocumento.Ticket }));
        deTickets.TotalCount.ShouldBe(0);
    }

    private static string XmlConFecha(string fecha) =>
        $"""<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0" Fecha="{fecha}"/>""";

    private async Task<ResumenDelMesDto> UnicoMesAsync() =>
        (await ComoAsync(Usuario, () => _boveda.GetResumenAsync(_cliente))).ShouldHaveSingleItem();

    [Fact]
    public async Task Una_factura_va_al_mes_de_su_XML_aunque_se_suba_despues()
    {
        // Se elige noviembre, pero la factura dice octubre: manda el XML.
        var subido = await ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.Cfdi,
            Archivo(XmlConFecha("2025-10-31T18:30:00"), "factura.xml"), 2025, 11));

        subido.Aviso.ShouldBeNull();
        subido.Documento.Fecha.ShouldBe(new DateTime(2025, 10, 31, 12, 0, 0, DateTimeKind.Utc));
        var mes = await UnicoMesAsync();
        (mes.Anio, mes.Mes).ShouldBe((2025, 10));
        (await ComoAsync(Usuario, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente, Anio = 2025, Mes = 10 })))
            .TotalCount.ShouldBe(1);

        await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _boveda.CambiarMesAsync(_cliente, subido.Documento.Id, 2025, 9)));
    }

    [Fact]
    public async Task Un_estado_de_cuenta_va_al_mes_que_se_elige_y_se_puede_mover()
    {
        var subido = await ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.EstadoDeCuenta,
            Archivo("%PDF-1.7 estado de octubre", "banorte-oct.pdf"), 2025, 10));

        var mes = await UnicoMesAsync();
        (mes.Anio, mes.Mes, mes.Tipo).ShouldBe((2025, 10, TipoDeDocumento.EstadoDeCuenta));

        var movido = await ComoAsync(Usuario, () => _boveda.CambiarMesAsync(_cliente, subido.Documento.Id, 2025, 9));

        movido.Fecha.Month.ShouldBe(9);
        (await UnicoMesAsync()).Mes.ShouldBe(9);
        movido.SubidoUtc.ShouldBe(subido.Documento.SubidoUtc, TimeSpan.FromMilliseconds(1), "Postgres guarda microsegundos");

        // Un lector no mueve, y no se acepta un mes inventado ni medio mes.
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(_lector, () => _boveda.CambiarMesAsync(_cliente, subido.Documento.Id, 2025, 8)));
        await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _boveda.CambiarMesAsync(_cliente, subido.Documento.Id, 2025, 13)));
        await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.Ticket, Archivo("%PDF-1.7 t", "t.pdf"), 2025, null)));
    }

    [Fact]
    public async Task Una_factura_sin_fecha_avisa_y_queda_en_el_mes_elegido()
    {
        var subido = await ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.Cfdi, Archivo(Xml, "rara.xml"), 2025, 10));

        subido.Aviso.ShouldNotBeNull();
        (await UnicoMesAsync()).Mes.ShouldBe(10);
    }

    [Fact]
    public async Task Las_facturas_que_se_subieron_sin_fecha_se_fechan_con_su_XML()
    {
        // Como quedaron antes de este cambio: sin fecha, en el mes de subida.
        var otro = await ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.Otro, Archivo("%PDF-1.7 x", "x.pdf")));
        await WithUnitOfWorkAsync(async () =>
        {
            var (vieja, _) = await GetRequiredService<GestorDeDocumentos>().RecibirAsync(_cliente, TipoDeDocumento.Cfdi, "vieja.xml",
                Encoding.UTF8.GetBytes(XmlConFecha("2025-03-15T09:00:00")));
            vieja.FechaDelDocumento.ShouldBeNull();
        });

        var fechadas = await WithUnitOfWorkAsync(() => GetRequiredService<GestorDeDocumentos>().FecharFacturasSinFechaAsync());

        fechadas.ShouldBe(1, "solo las facturas; el PDF sin mes se queda donde está");
        var lista = await ComoAsync(Usuario, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente, Anio = 2025, Mes = 3 }));
        lista.Items.ShouldHaveSingleItem().Nombre.ShouldBe("vieja.xml");
        otro.Documento.Fecha.ShouldBe(otro.Documento.SubidoUtc);
    }

    [Fact]
    public async Task El_mismo_archivo_dos_veces_no_se_duplica()
    {
        await SubirAsync(Xml, "factura.xml");
        var igual = await SubirAsync(Xml, "copia.xml");

        igual.EsDuplicado.ShouldBeTrue();
        (await ComoAsync(Usuario, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente }))).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Lo_que_no_es_un_formato_admitido_se_rechaza()
    {
        await Should.ThrowAsync<BusinessException>(() =>
            ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.Otro, Archivo("MZ ejecutable", "virus.exe"))));
        (await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.Cfdi, Archivo("%PDF-1.7 factura", "factura.pdf")))))
            .Message.ShouldContain("XML");
        (await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.ConstanciaDeSituacionFiscal, Archivo("%PDF-1.7 csf", "csf.pdf")))))
            .Message.ShouldContain("Perfil fiscal");
    }

    [Fact]
    public async Task Se_descarga_descifrado_y_se_borra()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7 estado de cuenta de prueba");
        var subido = await ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.EstadoDeCuenta, Archivo(pdf, "bbva-oct.pdf")));
        subido.Documento.Tipo.ShouldBe(TipoDeDocumento.EstadoDeCuenta);

        var descarga = await ComoAsync(_lector, () => _boveda.GetArchivoAsync(_cliente, subido.Documento.Id));
        using var copia = new MemoryStream();
        await descarga.GetStream().CopyToAsync(copia);
        copia.ToArray().ShouldBe(pdf);
        descarga.ContentType.ShouldBe("application/pdf");

        await ComoAsync(Usuario, async () => { await _boveda.EliminarAsync(_cliente, subido.Documento.Id); return true; });
        (await ComoAsync(Usuario, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente }))).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Cada_quien_solo_ve_y_toca_lo_de_sus_clientes()
    {
        var subido = await SubirAsync(Xml, "factura.xml");

        // Un lector ve, pero no sube ni borra.
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(_lector, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.Otro, Archivo(Xml, "f.xml"))));
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(_lector, async () => { await _boveda.EliminarAsync(_cliente, subido.Documento.Id); return true; }));

        // Nadie llega a un documento pidiéndolo por otro cliente, ni a la bóveda de otro.
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _boveda.GetResumenAsync(_otroCliente)));
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Administrador, () => _boveda.GetArchivoAsync(_otroCliente, subido.Documento.Id)));

        // El despacho ve la de cualquiera.
        (await ComoAsync(Administrador, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente }))).TotalCount.ShouldBe(1);
    }
}
