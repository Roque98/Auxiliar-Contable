using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Boveda;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Credenciales;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Descarga Masiva con la e.firma del cliente: se pide al SAT, se revisa hasta que arma los paquetes, se bajan y solo
/// entran los UUID que faltan (y solo del RFC del cliente); el impuesto del mes queda por actualizar. Al cerrar el mes se
/// pide sola para quien no mandó nada. Cada uso de la e.firma queda en su bitácora. La e.firma es INVENTADA y el SAT es
/// el de prueba (<see cref="ServicioDescargaMasivaDePrueba"/>).
/// </summary>
public class DescargaMasivaIntegrationTests : ConJerarquiaTestBase
{
    private const string Rfc = "GODE561231GR8";
    private const string Contrasena = "Llave-De-Prueba-1";
    private const string Venta = "7A9619FF-8B86-D011-B42D-00C04FC96501";
    private const string Ajena = "7A9619FF-8B86-D011-B42D-00C04FC96502";
    private const string Compra = "7A9619FF-8B86-D011-B42D-00C04FC96503";

    private readonly IDescargaDelSatAppService _descarga;
    private readonly DescargadorMasivo _descargador;
    private readonly ServicioDescargaMasivaDePrueba _sat;
    private Guid _cliente;

    /// <summary>El mes anterior: el que se cierra.</summary>
    private static DateTime MesAnterior => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-1);

    public DescargaMasivaIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _descarga = GetRequiredService<IDescargaDelSatAppService>();
        _descargador = GetRequiredService<DescargadorMasivo>();
        _sat = (ServicioDescargaMasivaDePrueba)GetRequiredService<IServicioDescargaMasiva>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _sat.Reiniciar();
        ((MotorFiscalDePrueba)GetRequiredService<IMotorFiscal>()).Reiniciar();
        _cliente = await ClienteAsync("Herrería Lupita", conEfirma: true);
    }

    private async Task<Guid> ClienteAsync(string nombre, bool conEfirma)
    {
        var cliente = await CrearOrganizacionAsync(nombre);
        await WithUnitOfWorkAsync(() => GetRequiredService<MembresiasDeOrganizacion>().AgregarAsync(cliente, Usuario.Id, RolEnOrganizacion.Miembro));
        var perfil = GetRequiredService<IPerfilFiscalAppService>();
        await ComoAsync(Administrador, () => perfil.ResponderAsync(cliente, new ResponderDatoDelPerfilDto { Campo = CamposDelPerfil.Rfc, Valor = Rfc }));
        await ComoAsync(Administrador, () => perfil.ResponderAsync(cliente, new ResponderDatoDelPerfilDto
        {
            Campo = CamposDelPerfil.Regimenes, Valor = JsonSerializer.Serialize(new[] { "612" }),
        }));
        if (conEfirma)
        {
            var (cer, key) = Efirma();
            await ComoAsync(Administrador, () => GetRequiredService<ICredencialesAppService>().GuardarEfirmaAsync(cliente, new GuardarEfirmaDto
            {
                CerBase64 = Convert.ToBase64String(cer), KeyBase64 = Convert.ToBase64String(key), Contrasena = Contrasena, Consentimiento = true,
            }));
        }

        return cliente;
    }

    private static (byte[] Cer, byte[] Key) Efirma()
    {
        using var rsa = RSA.Create(2048);
        var sujeto = new X500DistinguishedName($"CN=HERRERIA LUPITA, OID.2.5.4.45={Rfc} / XAXX010101HDFXXX01, C=MX");
        var solicitud = new CertificateRequest(sujeto, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var desde = DateTimeOffset.UtcNow.AddDays(-1);
        using var cert = solicitud.Create(sujeto, X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1),
            desde, desde.AddYears(4), Encoding.ASCII.GetBytes("00001000000712345678"));
        return (cert.Export(X509ContentType.Cert), rsa.ExportEncryptedPkcs8PrivateKey(Contrasena,
            new PbeParameters(PbeEncryptionAlgorithm.TripleDes3KeyPkcs12, HashAlgorithmName.SHA1, 2048)));
    }

    private static string Xml(string uuid, string emisor, string receptor, DateTime dia) =>
        $"""<?xml version="1.0" encoding="UTF-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" Fecha="{dia:yyyy-MM-dd}T10:00:00" Total="1160.00" TipoDeComprobante="I"><cfdi:Emisor Rfc="{emisor}" Nombre="E"/><cfdi:Receptor Rfc="{receptor}" Nombre="R"/><cfdi:Conceptos><cfdi:Concepto Descripcion="Reja"/></cfdi:Conceptos><cfdi:Complemento><tfd:TimbreFiscalDigital UUID="{uuid}"/></cfdi:Complemento></cfdi:Comprobante>""";

    private static byte[] Zip(params (string Nombre, string Contenido)[] archivos)
    {
        using var m = new MemoryStream();
        using (var zip = new ZipArchive(m, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (nombre, contenido) in archivos)
            {
                using var e = zip.CreateEntry(nombre).Open();
                e.Write(Encoding.UTF8.GetBytes(contenido));
            }
        }

        return m.ToArray();
    }

    private Task<DescargaMasivaDto> EstadoAsync(Guid cliente) => ComoAsync(Administrador, () => _descarga.GetDescargaMasivaAsync(cliente));

    [Fact]
    public async Task Se_pide_se_espera_al_sat_y_solo_entra_lo_que_falta()
    {
        (await EstadoAsync(_cliente)).Problema.ShouldBeNull();
        var m = MesAnterior;
        var r = await ComoAsync(Administrador, () => _descarga.SolicitarDescargaMasivaAsync(_cliente, new SolicitarDescargaMasivaDto { Anio = m.Year, Mes = m.Month }));
        r.Solicitudes.Count.ShouldBe(2);
        r.Solicitudes.ShouldAllBe(s => s.Estado == "EnProceso" && !s.Automatica);
        _sat.Pedidos.Count.ShouldBe(2);
        _sat.Pedidos.ShouldAllBe(p => p.Rfc == Rfc && p.Pedido.Desde == m && p.Pedido.Hasta == m.AddMonths(1).AddSeconds(-1));
        _sat.Pedidos.Count(p => p.Pedido.Emitidas).ShouldBe(1);

        // Lo mismo otra vez mientras está en proceso: no se repite (el SAT limita las solicitudes iguales).
        await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Administrador, () => _descarga.SolicitarDescargaMasivaAsync(_cliente, new SolicitarDescargaMasivaDto { Anio = m.Year, Mes = m.Month })));

        // El SAT todavía la arma.
        (await ComoAsync(Administrador, () => _descarga.AvanzarDescargaMasivaAsync(_cliente))).Solicitudes.ShouldAllBe(s => s.Estado == "EnProceso");

        // Ya la terminó: un paquete con una venta del cliente, una factura ajena y una compra que ya teníamos.
        await ComoAsync(Usuario, () => GetRequiredService<IBovedaAppService>().SubirAsync(_cliente, TipoDeDocumento.Cfdi,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(Xml(Compra, "AAA010101AAA", Rfc, m))), Compra + ".xml", "application/xml"),
            m.Year, m.Month));
        _sat.Estado = 3;
        _sat.Paquetes["PAQ_01"] = Zip(
            (Venta + ".xml", Xml(Venta, Rfc, "XAXX010101000", m.AddDays(4))),
            (Ajena + ".xml", Xml(Ajena, "AAA010101AAA", "BBB010101BBB", m.AddDays(4))),
            (Compra + ".xml", Xml(Compra, "AAA010101AAA", Rfc, m)));
        var fin = await ComoAsync(Administrador, () => _descarga.AvanzarDescargaMasivaAsync(_cliente));
        fin.Aviso.ShouldBe("Llegaron 1 facturas nuevas.");
        fin.Solicitudes.ShouldAllBe(s => s.Estado == "Descargada" && s.PaquetesBajados == 1);
        fin.Solicitudes.Sum(s => s.Nuevas).ShouldBe(1);
        fin.Solicitudes.Sum(s => s.Rechazadas).ShouldBe(2);

        var documentos = await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<Documento, Guid>>().GetListAsync(d => d.OrganizacionId == _cliente));
        documentos.Select(d => d.Uuid).OrderBy(u => u).ShouldBe(new[] { Venta, Compra }.OrderBy(u => u));

        // El impuesto del mes se mueve solo y la bitácora dice que la plataforma usó la e.firma (sin el dato).
        (await ComoAsync(Administrador, () => GetRequiredService<IImpuestosAlDiaAppService>().GetAsync(_cliente, m.Year, m.Month))).Actualizando.ShouldBeTrue();
        var usos = await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<UsoDeCredencial, Guid>>().GetListAsync(u => u.OrganizacionId == _cliente));
        usos.Count(u => u.Accion == AccionSobreCredencial.UsarEnDescargaMasiva).ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Sin_efirma_o_con_el_sat_caido_se_dice_y_no_se_guarda_nada()
    {
        var sinEfirma = await ClienteAsync("Sin e.firma", conEfirma: false);
        (await EstadoAsync(sinEfirma)).Problema.ShouldBe("El cliente no tiene su e.firma guardada en Credenciales.");
        var m = MesAnterior;
        await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Administrador, () => _descarga.SolicitarDescargaMasivaAsync(sinEfirma, new SolicitarDescargaMasivaDto { Anio = m.Year, Mes = m.Month })));

        _sat.Caido = true;
        var e = await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Administrador, () => _descarga.SolicitarDescargaMasivaAsync(_cliente, new SolicitarDescargaMasivaDto { Anio = m.Year, Mes = m.Month })));
        e.Message.ShouldContain("El SAT no responde");
        (await EstadoAsync(_cliente)).Solicitudes.ShouldBeEmpty();

        // Si en el rango no hay nada, el SAT lo dice al pedir y queda así (no se revisa más).
        _sat.Caido = false;
        _sat.CodigoDeSolicitud = "5004";
        var r = await ComoAsync(Administrador, () => _descarga.SolicitarDescargaMasivaAsync(_cliente, new SolicitarDescargaMasivaDto { Anio = m.Year, Mes = m.Month, Recibidas = false }));
        r.Solicitudes.Single().Estado.ShouldBe("SinFacturas");
    }

    [Fact]
    public async Task Al_cerrar_el_mes_se_pide_sola_para_quien_no_mando_nada()
    {
        var m = MesAnterior;
        var conFacturas = await ClienteAsync("Sí mandó", conEfirma: true);
        await ComoAsync(Usuario, () => GetRequiredService<IBovedaAppService>().SubirAsync(conFacturas, TipoDeDocumento.Cfdi,
            new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(Xml(Compra, "AAA010101AAA", Rfc, m))), Compra + ".xml", "application/xml"),
            m.Year, m.Month));

        // Antes del día del cierre no se pide nada.
        (await _descargador.PedirCierreDeMesAsync(m.AddMonths(1).AddDays(DescargadorMasivo.DiaDelCierre - 2))).ShouldBe(0);

        var cierre = m.AddMonths(1).AddDays(DescargadorMasivo.DiaDelCierre - 1).AddHours(6);
        (await _descargador.PedirCierreDeMesAsync(cierre)).ShouldBeGreaterThanOrEqualTo(1);
        var nuestras = (await EstadoAsync(_cliente)).Solicitudes;
        nuestras.Count.ShouldBe(2);
        nuestras.ShouldAllBe(s => s.Automatica && s.Anio == m.Year && s.Mes == m.Month);
        (await EstadoAsync(conFacturas)).Solicitudes.ShouldBeEmpty();

        // Al otro día no se vuelve a pedir.
        await _descargador.PedirCierreDeMesAsync(cierre.AddDays(1));
        (await EstadoAsync(_cliente)).Solicitudes.Count.ShouldBe(2);
    }
}
