using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Boveda;
using AuxiliarContable.Cfdis;
using AuxiliarContable.Documentos;
using AuxiliarContable.Identity;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// La bóveda contra PostgreSQL real: CFDI leídos al subir, duplicados por archivo y por UUID,
/// navegación y búsqueda, descarga descifrada y el aislamiento entre clientes.
/// </summary>
public class BovedaIntegrationTests : ConJerarquiaTestBase
{
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
        await ComoAsync(Usuario, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = "GODE561231GR8" }));
    }

    private static IRemoteStreamContent Archivo(string contenido, string nombre) => Archivo(Encoding.UTF8.GetBytes(contenido), nombre);

    private static IRemoteStreamContent Archivo(byte[] bytes, string nombre) =>
        new RemoteStreamContent(new MemoryStream(bytes), nombre, "application/octet-stream");

    private Task<ResultadoDeSubidaDto> SubirAsync(string xml, string nombre = "factura.xml") =>
        ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.Otro, Archivo(xml, nombre)));

    [Fact]
    public async Task Un_XML_se_lee_como_CFDI_al_subirlo_y_sabe_si_es_venta_o_gasto()
    {
        var venta = await SubirAsync(LectorDeCfdiTests.Cfdi40());
        var gasto = await SubirAsync(LectorDeCfdiTests.Cfdi40(uuid: Guid.NewGuid().ToString(), emisor: "CFE370814QI0", receptor: "GODE561231GR8",
            total: "500.00", fecha: "2026-10-02T09:00:00"), "cfe.xml");

        venta.EsDuplicado.ShouldBeFalse();
        venta.Aviso.ShouldBeNull();
        venta.Documento.Tipo.ShouldBe(TipoDeDocumento.Cfdi);
        venta.Documento.Estado.ShouldBe(EstadoDeDocumento.Procesado);
        venta.Documento.Fecha.ShouldBe(new DateTime(2026, 9, 15, 10, 30, 0));
        venta.Documento.Cfdi!.Direccion.ShouldBe(DireccionDelCfdi.Emitido);
        gasto.Documento.Cfdi!.Direccion.ShouldBe(DireccionDelCfdi.Recibido);

        var resumen = await ComoAsync(Usuario, () => _boveda.GetResumenAsync(_cliente));
        var septiembre = resumen.Single(r => r.Anio == 2026 && r.Mes == 9 && r.Tipo == TipoDeDocumento.Cfdi);
        septiembre.Cantidad.ShouldBe(1);
        septiembre.TotalEmitido.ShouldBe(1160m);
        resumen.Single(r => r.Mes == 10 && r.Tipo == TipoDeDocumento.Cfdi).TotalRecibido.ShouldBe(500m);

        var deOctubre = await ComoAsync(Usuario, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente, Anio = 2026, Mes = 10 }));
        deOctubre.Items.ShouldHaveSingleItem().Nombre.ShouldBe("cfe.xml");

        var gastos = await ComoAsync(Usuario, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente, Direccion = DireccionDelCfdi.Recibido }));
        gastos.TotalCount.ShouldBe(1);

        var porTexto = await ComoAsync(Usuario, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente, Texto = "cfe370814" }));
        porTexto.Items.ShouldHaveSingleItem().Nombre.ShouldBe("cfe.xml");
    }

    [Fact]
    public async Task El_mismo_CFDI_dos_veces_no_se_duplica_aunque_cambien_los_bytes()
    {
        await SubirAsync(LectorDeCfdiTests.Cfdi40());
        var igual = await SubirAsync(LectorDeCfdiTests.Cfdi40());
        var otraDescarga = await SubirAsync(LectorDeCfdiTests.Cfdi40() + "\n<!-- descargado otra vez -->", "copia.xml");

        igual.EsDuplicado.ShouldBeTrue();
        otraDescarga.EsDuplicado.ShouldBeTrue();
        (await ComoAsync(Usuario, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente }))).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Un_CFDI_ajeno_al_RFC_del_cliente_se_guarda_con_aviso()
    {
        var ajeno = await SubirAsync(LectorDeCfdiTests.Cfdi40(emisor: "AAA010101AAA", receptor: "BBB010101BBB"));

        ajeno.Aviso.ShouldNotBeNull();
        ajeno.Aviso!.ShouldContain("GODE561231GR8");
        ajeno.Documento.Cfdi!.Direccion.ShouldBe(DireccionDelCfdi.Desconocida);
    }

    [Fact]
    public async Task Lo_que_no_es_CFDI_ni_un_formato_admitido_se_rechaza()
    {
        (await Should.ThrowAsync<UserFriendlyException>(() => SubirAsync("<html><body>hola</body></html>", "pagina.xml")))
            .Message.ShouldContain("no es un CFDI");
        await Should.ThrowAsync<BusinessException>(() =>
            ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.Otro, Archivo("MZ ejecutable", "virus.exe"))));
        (await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.Cfdi, Archivo("%PDF-1.7 factura", "factura.pdf")))))
            .Message.ShouldContain("XML");
    }

    [Fact]
    public async Task Se_descarga_descifrado_y_se_borra()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7 estado de cuenta de prueba");
        var subido = await ComoAsync(Usuario, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.EstadoDeCuenta, Archivo(pdf, "bbva-oct.pdf")));

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
        var subido = await SubirAsync(LectorDeCfdiTests.Cfdi40());

        // Un lector ve, pero no sube ni borra.
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(_lector, () => _boveda.SubirAsync(_cliente, TipoDeDocumento.Otro, Archivo(LectorDeCfdiTests.Cfdi40(), "f.xml"))));
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(_lector, async () => { await _boveda.EliminarAsync(_cliente, subido.Documento.Id); return true; }));

        // Nadie llega a un documento pidiéndolo por otro cliente, ni a la bóveda de otro.
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _boveda.GetResumenAsync(_otroCliente)));
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Administrador, () => _boveda.GetArchivoAsync(_otroCliente, subido.Documento.Id)));

        // El despacho ve la de cualquiera.
        (await ComoAsync(Administrador, () => _boveda.GetListAsync(new BuscarEnBovedaDto { OrganizacionId = _cliente }))).TotalCount.ShouldBe(1);
    }
}
