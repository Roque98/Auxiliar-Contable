using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Boveda;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Documentos;
using AuxiliarContable.Nomina;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Servicios especializados (REPSE): los contratos del cliente, la revisión del mes con las facturas de la otra parte y
/// las casillas de los documentos, el informe cuatrimestral, el aviso en la guía diaria y el recordatorio diario del
/// despacho. El cliente no la ve. Datos de prueba: RFC genéricos.
/// </summary>
public class RepseIntegrationTests : ConJerarquiaTestBase
{
    private const string Rfc = "GODE561231GR8";
    private const string Contratista = "AAA010101AAA";
    private const string Beneficiario = "BBB010101BBB";

    private readonly IRepseAppService _repse;
    private Guid _cliente;

    private static DateTime Dia => new(DateTime.Now.Year, DateTime.Now.Month, 1);

    public RepseIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _repse = GetRequiredService<IRepseAppService>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _cliente = await CrearOrganizacionAsync("Limpiezas Ejemplo");
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Miembro));
        await ComoAsync(Administrador, () => GetRequiredService<IPerfilFiscalAppService>().ResponderAsync(_cliente,
            new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = Rfc }));
    }

    private static string Xml(string uuid, string emisor, string receptor) =>
        $"""<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" Fecha="{Dia:yyyy-MM-dd}T10:00:00" Total="11600.00" TipoDeComprobante="I" MetodoPago="PUE" FormaPago="03"><cfdi:Emisor Rfc="{emisor}" Nombre="EMISOR DE PRUEBA"/><cfdi:Receptor Rfc="{receptor}" Nombre="RECEPTOR DE PRUEBA"/><cfdi:Conceptos><cfdi:Concepto Descripcion="Servicio de vigilancia"/></cfdi:Conceptos><cfdi:Complemento><tfd:TimbreFiscalDigital UUID="{uuid.ToLowerInvariant()}"/></cfdi:Complemento></cfdi:Comprobante>""";

    private async Task<Guid> SubirAsync(string nombre, string contenido)
    {
        var r = await ComoAsync(Usuario, () => GetRequiredService<IBovedaAppService>().SubirAsync(_cliente, TipoDeDocumento.Cfdi,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(contenido)), nombre, "application/octet-stream"),
            Dia.Year, Dia.Month));
        return r.Documento.Id;
    }

    [Fact]
    public async Task Contratos_revision_del_mes_informe_y_avisos()
    {
        await Should.ThrowAsync<AbpAuthorizationException>(() => ComoAsync(Usuario, () => _repse.GetDelMesAsync(_cliente, Dia.Year, Dia.Month)));
        (await ComoAsync(Administrador, () => _repse.GetDelMesAsync(_cliente, Dia.Year, Dia.Month))).Contratos.ShouldBeEmpty();

        var nomina = GetRequiredService<INominaAppService>();
        var lupe = await ComoAsync(Administrador, () => nomina.GuardarTrabajadorAsync(_cliente, new GuardarTrabajadorDto
        {
            Nombre = "Trabajadora Prueba", FechaIngreso = new DateTime(2025, 1, 1), SalarioDiario = 500, SalarioDiarioIntegrado = 523.97m,
            Curp = "XEXX010101MDFXXX01", Nss = "12345678901",
        }));

        // Recibe vigilancia de un contratista (sin folio REPSE todavía) y presta limpieza a un beneficiario.
        var recibe = await ComoAsync(Administrador, () => _repse.GuardarContratoAsync(_cliente, new GuardarContratoEspecializadoDto
        {
            Rol = "recibe", ContraparteRfc = Contratista.ToLowerInvariant(), ContraparteNombre = "Vigilancia de prueba", Objeto = "Vigilancia",
            Inicio = new DateTime(2026, 1, 1), TrabajadoresAproximados = 2,
        }));
        recibe.ContraparteRfc.ShouldBe(Contratista);
        var presta = await ComoAsync(Administrador, () => _repse.GuardarContratoAsync(_cliente, new GuardarContratoEspecializadoDto
        {
            Rol = "presta", ContraparteRfc = Beneficiario, ContraparteNombre = "Beneficiario de prueba", Objeto = "Limpieza de oficinas",
            Inicio = new DateTime(2026, 1, 1), TrabajadoresAproximados = 1, RegistroRepse = "AR1234/2025",
            RegistroVigenteHasta = new DateTime(2027, 1, 20), Trabajadores = [lupe.Id!.Value, Guid.NewGuid()],
        }));
        presta.Trabajadores.ShouldBe([lupe.Id!.Value]); // el ajeno no se asigna
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () => _repse.GuardarContratoAsync(_cliente,
            new GuardarContratoEspecializadoDto { Rol = "otro", ContraparteRfc = Contratista, ContraparteNombre = "X", Objeto = "X", Inicio = Dia })));

        // Una factura recibida del contratista en el mes: hay que recabar sus documentos a más tardar el último día del siguiente.
        var xml = await SubirAsync("vigilancia.xml", Xml("5B9619FF-8B86-D011-B42D-00C04FC964BB", Contratista, Rfc));
        var mes = await ComoAsync(Administrador, () => _repse.GetDelMesAsync(_cliente, Dia.Year, Dia.Month));
        mes.PuedeEditar.ShouldBeTrue();
        var delMes = mes.DelMes.Single(c => c.ContratoId == recibe.Id);
        (delMes.Facturas, delMes.Importe, delMes.Vencido).ShouldBe((1, 11600m, false));
        delMes.Limite.ShouldBe(Dia.AddMonths(2).AddDays(-1));
        delMes.Casillas.Select(c => c.Clave).ShouldBe(["nomina", "isr", "imss", "infonavit", "iva"]);
        mes.DelMes.Single(c => c.ContratoId == presta.Id).Casillas.ShouldHaveSingleItem().Clave.ShouldBe("entrega");
        mes.Avisos.ShouldContain(a => a.Contains("Vigilancia de prueba") && a.Contains("falta el folio"));
        mes.Informe.ShouldNotBeNull().Casillas.Select(c => c.Clave).ShouldBe(["icsoe", "sisub"]);

        await ComoAsync(Administrador, () => _repse.MarcarAsync(_cliente, new MarcarRepseDto
        {
            ContratoId = recibe.Id, Anio = Dia.Year, Periodo = Dia.Month, Clave = "iva", Cumplida = true,
        }));
        await ComoAsync(Administrador, () => _repse.MarcarAsync(_cliente, new MarcarRepseDto
        {
            ContratoId = recibe.Id, Anio = Dia.Year, Periodo = Dia.Month, Clave = "iva", Cumplida = true,
        }));
        await Should.ThrowAsync<UserFriendlyException>(() => ComoAsync(Administrador, () => _repse.MarcarAsync(_cliente, new MarcarRepseDto
        {
            ContratoId = recibe.Id, Anio = Dia.Year, Periodo = Dia.Month, Clave = "entrega", Cumplida = true,
        })));
        mes = await ComoAsync(Administrador, () => _repse.GetDelMesAsync(_cliente, Dia.Year, Dia.Month));
        mes.DelMes.Single(c => c.ContratoId == recibe.Id).Casillas.Where(c => c.Cumplida).Select(c => c.Clave).ShouldBe(["iva"]);

        // La guía diaria sabe que la factura es de un servicio especializado que el cliente recibe.
        var operaciones = GetRequiredService<IOperacionesAppService>();
        var op = await ComoAsync(Usuario, () => operaciones.DeDocumentoAsync(_cliente, xml));
        (await ComoAsync(Usuario, () => operaciones.GetFacturaAsync(_cliente, op.Id)))!.ServicioEspecializado.ShouldBe("recibe");

        // El informe cuatrimestral lleva al beneficiario y a la trabajadora asignada con su SBC.
        var (anioInf, cuatrimestre, _) = RevisionRepse.InformeQueVence(Dia.Year, Dia.Month);
        var hoja = await ComoAsync(Administrador, () => _repse.InformeCuatrimestralAsync(_cliente, anioInf, cuatrimestre));
        var texto = Encoding.UTF8.GetString(Convert.FromBase64String(hoja.ContenidoB64));
        texto.ShouldContain(Beneficiario);
        texto.ShouldContain("XEXX010101MDFXXX01");
        texto.ShouldContain("523.97");
        texto.ShouldContain("AR1234/2025");
        hoja.Avisos.ShouldBeEmpty();

        // Recordatorio diario del despacho: el 10 de enero, ICSOE y SISUB por presentar y el registro que vence el 20.
        var recordatorio = GetRequiredService<RecordatorioRepse>();
        var enero = new DateTime(2027, 1, 10);
        (await WithUnitOfWorkAsync(() => recordatorio.AvisarAsync(enero, DateTime.UtcNow))).ShouldBe(2);
        (await WithUnitOfWorkAsync(() => recordatorio.AvisarAsync(enero, DateTime.UtcNow))).ShouldBe(0);

        await ComoAsync(Administrador, () => _repse.BorrarContratoAsync(_cliente, recibe.Id!.Value));
        (await ComoAsync(Administrador, () => _repse.GetDelMesAsync(_cliente, Dia.Year, Dia.Month))).Contratos.ShouldHaveSingleItem();
    }

    [Fact]
    public void Plazos_del_informe_cuatrimestral()
    {
        RevisionRepse.InformeQueVence(2027, 1).ShouldBe((2026, 3, new DateTime(2027, 1, 17)));
        RevisionRepse.InformeQueVence(2026, 2).ShouldBe((2026, 1, new DateTime(2026, 5, 17)));
        RevisionRepse.InformeQueVence(2026, 5).ShouldBe((2026, 1, new DateTime(2026, 5, 17)));
        RevisionRepse.InformeQueVence(2026, 6).ShouldBe((2026, 2, new DateTime(2026, 9, 17)));
        RevisionRepse.InformeQueVence(2026, 11).ShouldBe((2026, 3, new DateTime(2027, 1, 17)));
        RevisionRepse.MesesDe(2026, 2).ShouldBe((new DateTime(2026, 5, 1), new DateTime(2026, 8, 31)));
        RevisionRepse.LimiteDelMes(2026, 11).ShouldBe(new DateTime(2026, 12, 31));
    }
}
