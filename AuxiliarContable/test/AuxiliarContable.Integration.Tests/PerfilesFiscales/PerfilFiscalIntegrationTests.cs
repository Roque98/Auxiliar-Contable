using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Documentos;
using AuxiliarContable.Identity;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// El perfil fiscal contra PostgreSQL real: subir la CSF (cifrada), quién ve y quién cambia,
/// y la validación de lo que escribe una persona. Leer la CSF es del motor fiscal.
/// </summary>
public class PerfilFiscalIntegrationTests : ConJerarquiaTestBase
{
    private readonly IPerfilFiscalAppService _perfil;
    private Guid _cliente;
    private Guid _otroCliente;
    private Volo.Abp.Identity.IdentityUser _lector = null!;

    public PerfilFiscalIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _perfil = GetRequiredService<IPerfilFiscalAppService>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _cliente = await CrearOrganizacionAsync("Eduardo Gómez");
        _otroCliente = await CrearOrganizacionAsync("Otro cliente");
        _lector = await CrearUsuarioAsync("lupita", AuxiliarRoles.Usuario);
        await WithUnitOfWorkAsync(async () =>
        {
            var membresias = GetRequiredService<MembresiasDeOrganizacion>();
            await membresias.AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro);
            await membresias.AgregarAsync(_cliente, _lector.Id, RolEnOrganizacion.Lector);
        });
    }

    /// <summary>Un PDF de verdad con capa de texto, como los que descarga el SAT.</summary>
    private static byte[] PdfConTexto(params string[] lineas)
    {
        var constructor = new PdfDocumentBuilder();
        var pagina = constructor.AddPage(PageSize.Letter);
        var fuente = constructor.AddStandard14Font(Standard14Font.Helvetica);
        var y = 750d;
        foreach (var linea in lineas)
        {
            pagina.AddText(linea, 9, new PdfPoint(30, y), fuente);
            y -= 14;
        }

        return constructor.Build();
    }

    private static readonly string[] Csf =
    [
        "CEDULA DE IDENTIFICACION FISCAL - CONSTANCIA DE SITUACION FISCAL",
        "RFC: GODE561231GR8   idCIF: 12345678901",
        "Lugar y Fecha de Emision CUAUHTEMOC, CIUDAD DE MEXICO A 20 DE OCTUBRE DE 2026",
        "Datos de Identificacion del Contribuyente: CURP: GODE561231HDFRRN09",
        "Nombre (s): EDUARDO   Primer Apellido: GOMEZ   Segundo Apellido: DIAZ",
        "Fecha inicio de operaciones: 01 DE ENERO DE 2015   Estatus en el padron: ACTIVO",
        "Datos del domicilio registrado  Codigo Postal:06600  Tipo de Vialidad: CALLE",
        "Nombre de Vialidad: LIVERPOOL  Numero Exterior: 10  Colonia: JUAREZ",
        "Actividades Economicas: 1 Otros servicios profesionales 100 01/01/2015",
        "Regimenes: Regimen Simplificado de Confianza 01/01/2022",
        "Obligaciones: Pago definitivo mensual de IVA  01/01/2022",
    ];

    private static IRemoteStreamContent Archivo(byte[] bytes, string nombre = "constancia.pdf") =>
        new RemoteStreamContent(new MemoryStream(bytes), nombre, "application/pdf");

    [Fact]
    public async Task El_cliente_sube_su_CSF_y_queda_cifrada_en_su_expediente()
    {
        var subido = await ComoAsync(Usuario, () => _perfil.SubirCsfAsync(_cliente, Archivo(PdfConTexto(Csf))));

        subido.Csf.ShouldNotBeNull();
        subido.Csf!.Estado.ShouldBe(EstadoDeDocumento.Recibido);
        subido.PuedeEditar.ShouldBeTrue();

        // En el almacén NO está en claro.
        var crudo = await WithUnitOfWorkAsync(() => GetRequiredService<IBlobContainer<DocumentosContainer>>()
            .GetAllBytesAsync($"{_cliente:N}/{subido.Csf.DocumentoId:N}"));
        Encoding.Latin1.GetString(crudo).ShouldNotContain("GODE561231GR8");
        Encoding.Latin1.GetString(crudo).ShouldNotContain("%PDF");

        // Se guarda en el expediente y no se lee: leerla y proponer sus datos es del motor fiscal.
        var perfil = await ComoAsync(Usuario, () => _perfil.GetAsync(_cliente));
        perfil.Csf!.Estado.ShouldBe(EstadoDeDocumento.Recibido);
        perfil.Campos.ShouldAllBe(c => c.Valor == null);
    }

    [Fact]
    public async Task Cada_dato_deja_constancia_de_quien_lo_dijo()
    {
        var delCliente = await ComoAsync(Usuario, () => _perfil.ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.TieneTrabajadores, Valor = "Sí" }));
        var trabajadores = delCliente.Campos.Single(c => c.Clave == CamposDelPerfil.TieneTrabajadores);
        trabajadores.Valor.ShouldBe("si");
        trabajadores.Origen.ShouldBe(OrigenDelDato.RespondidoPorElCliente);

        var delDespacho = await ComoAsync(Administrador, () => _perfil.ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Giro, Valor = "Consultoría en sistemas" }));
        delDespacho.Campos.Single(c => c.Clave == CamposDelPerfil.Giro).Origen.ShouldBe(OrigenDelDato.CapturadoPorElDespacho);
    }

    [Theory]
    [InlineData(CamposDelPerfil.Rfc, "NO-ES-RFC")]
    [InlineData(CamposDelPerfil.CodigoPostal, "6600")]
    [InlineData(CamposDelPerfil.FechaInicioDeOperaciones, "01/01/2015")]
    [InlineData(CamposDelPerfil.EmiteFacturas, "a veces")]
    [InlineData(CamposDelPerfil.Bancos, "BBVA")]
    public async Task Lo_que_escribe_una_persona_se_valida(string campo, string valor)
    {
        var error = await Should.ThrowAsync<BusinessException>(() => ComoAsync(Usuario,
            () => _perfil.ResponderAsync(_cliente, new ResponderDatoDelPerfilDto { Campo = campo, Valor = valor })));

        error.Code.ShouldBe("AuxiliarContable:DatoDelPerfilInvalido");
    }

    [Fact]
    public async Task Un_cliente_no_ve_ni_toca_el_perfil_de_otro()
    {
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _perfil.GetAsync(_otroCliente)));
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            ComoAsync(Usuario, () => _perfil.SubirCsfAsync(_otroCliente, Archivo(PdfConTexto(Csf)))));
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _perfil.ResponderAsync(_otroCliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Giro, Valor = "x" })));

        (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<Documento, Guid>>().CountAsync(d => d.OrganizacionId == _otroCliente)))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Cada_quien_ve_en_la_lista_solo_los_clientes_que_le_tocan()
    {
        var delCliente = await ComoAsync(Usuario, () => _perfil.GetClientesAsync());
        delCliente.ShouldHaveSingleItem().Id.ShouldBe(_cliente);
        delCliente[0].PuedeEditar.ShouldBeTrue();

        (await ComoAsync(_lector, () => _perfil.GetClientesAsync())).ShouldHaveSingleItem().PuedeEditar.ShouldBeFalse();

        var delDespacho = await ComoAsync(Administrador, () => _perfil.GetClientesAsync());
        delDespacho.Select(c => c.Id).ShouldBe([_cliente, _otroCliente], ignoreOrder: true);
        delDespacho.ShouldAllBe(c => c.PuedeEditar);
    }

    [Fact]
    public async Task Quien_solo_ve_lee_el_perfil_pero_no_lo_cambia()
    {
        var perfil = await ComoAsync(_lector, () => _perfil.GetAsync(_cliente));
        perfil.PuedeEditar.ShouldBeFalse();

        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(_lector, () => _perfil.ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Giro, Valor = "x" })));
    }

    [Fact]
    public async Task Solo_se_acepta_un_PDF()
    {
        var error = await Should.ThrowAsync<BusinessException>(() => ComoAsync(Usuario,
            () => _perfil.SubirCsfAsync(_cliente, Archivo(Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"), "csf.pdf"))));

        error.Code.ShouldBe("AuxiliarContable:DocumentoTipoNoAdmitido");
    }

    [Fact]
    public async Task Subir_dos_veces_la_misma_constancia_no_duplica_el_documento()
    {
        var pdf = PdfConTexto(Csf);
        var primero = await ComoAsync(Usuario, () => _perfil.SubirCsfAsync(_cliente, Archivo(pdf)));
        var segundo = await ComoAsync(Usuario, () => _perfil.SubirCsfAsync(_cliente, Archivo(pdf)));

        segundo.Csf!.DocumentoId.ShouldBe(primero.Csf!.DocumentoId);
        (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<Documento, Guid>>().CountAsync(d => d.OrganizacionId == _cliente)))
            .ShouldBe(1);
    }
}
