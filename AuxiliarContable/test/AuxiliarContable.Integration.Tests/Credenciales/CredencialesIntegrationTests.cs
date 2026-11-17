using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Credenciales;
using AuxiliarContable.Identity;
using AuxiliarContable.Organizaciones;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Credenciales del cliente: la e.firma se comprueba al guardarla (la contraseña abre la llave y la llave es de ese
/// certificado), lo secreto queda cifrado, verlo o bajar el .key pide NIP (o la contraseña de la cuenta), los fallos
/// bloquean, solo el administrador del cliente y el despacho las administran, y cada uso queda en la bitácora.
/// La e.firma es INVENTADA: se genera aquí, igual que las del SAT (PKCS#8 cifrado con 3DES).
/// </summary>
public class CredencialesIntegrationTests : ConJerarquiaTestBase
{
    private const string Rfc = "EJE010101AB1";
    private const string Contrasena = "Mi-Llave-2026";

    private readonly ICredencialesAppService _credenciales;
    private Guid _cliente;
    private Guid _otroCliente;
    private IdentityUser _lector = null!;

    public CredencialesIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _credenciales = GetRequiredService<ICredencialesAppService>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _cliente = await CrearOrganizacionAsync("Panadería Lupita");
        _otroCliente = await CrearOrganizacionAsync("Otro cliente");
        _lector = await CrearUsuarioAsync("ana", AuxiliarRoles.Usuario);
        await WithUnitOfWorkAsync(async () =>
        {
            var membresias = GetRequiredService<MembresiasDeOrganizacion>();
            await membresias.AgregarAsync(_cliente, Usuario.Id, RolEnOrganizacion.Administrador);
            await membresias.AgregarAsync(_cliente, _lector.Id, RolEnOrganizacion.Lector);
        });
    }

    /// <summary>Una e.firma de prueba: (.cer, .key cifrado con la contraseña).</summary>
    private static (byte[] Cer, byte[] Key) Efirma(string contrasena = Contrasena, int diasDeVigencia = 4 * 365)
    {
        using var rsa = RSA.Create(2048);
        var sujeto = new X500DistinguishedName($"CN=PANADERIA LUPITA, OID.2.5.4.45={Rfc} / XAXX010101HDFXXX01, C=MX");
        var solicitud = new CertificateRequest(sujeto, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var desde = DateTimeOffset.UtcNow.AddDays(-1);
        using var cert = solicitud.Create(sujeto, X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1),
            desde, desde.AddDays(diasDeVigencia), Encoding.ASCII.GetBytes("00001000000712345678"));
        var key = rsa.ExportEncryptedPkcs8PrivateKey(contrasena,
            new PbeParameters(PbeEncryptionAlgorithm.TripleDes3KeyPkcs12, HashAlgorithmName.SHA1, 2048));
        return (cert.Export(X509ContentType.Cert), key);
    }

    private static GuardarEfirmaDto Guardar((byte[] Cer, byte[] Key) e, string contrasena = Contrasena, bool consentimiento = true) => new()
    {
        CerBase64 = Convert.ToBase64String(e.Cer),
        KeyBase64 = Convert.ToBase64String(e.Key),
        Contrasena = contrasena,
        Consentimiento = consentimiento,
    };

    private Task PonerNipAsync(IdentityUser quien, string nip = "4821") =>
        ComoAsync(quien, () => _credenciales.PonerNipAsync(new PonerNipDto { Nip = nip, ContrasenaDeCuenta = Clave }));

    [Fact]
    public async Task La_efirma_se_comprueba_se_guarda_cifrada_y_dice_su_vigencia()
    {
        var e = Efirma();

        await Should.ThrowAsync<BusinessException>(() =>
            ComoAsync(Usuario, () => _credenciales.GuardarEfirmaAsync(_cliente, Guardar(e, consentimiento: false))));
        (await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _credenciales.GuardarEfirmaAsync(_cliente, Guardar(e, "otra")))))
            .Message.ShouldContain("contraseña no abre");
        (await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _credenciales.GuardarEfirmaAsync(_cliente, Guardar((e.Cer, Efirma().Key))))))
            .Message.ShouldContain("no corresponde");

        var r = await ComoAsync(Usuario, () => _credenciales.GuardarEfirmaAsync(_cliente, Guardar(e)));

        r.PuedeAdministrar.ShouldBeTrue();
        r.ConsentimientoUtc.ShouldNotBeNull();
        r.Efirma!.Rfc.ShouldBe(Rfc);
        r.Efirma.NumeroDeCertificado.ShouldBe("00001000000712345678");
        r.Efirma.DiasParaVencer.ShouldBeGreaterThan(1400);
        r.Efirma.PorVencer.ShouldBeFalse();

        // En la base no hay ni el .cer, ni el .key, ni la contraseña en claro.
        await WithUnitOfWorkAsync(async () =>
        {
            var guardada = await GetRequiredService<IRepository<CredencialesDelCliente, Guid>>().GetAsync(c => c.OrganizacionId == _cliente);
            guardada.CerCifrado!.SequenceEqual(e.Cer).ShouldBeFalse();
            guardada.KeyCifrado!.SequenceEqual(e.Key).ShouldBeFalse();
            Encoding.UTF8.GetString(guardada.ContrasenaDeEfirmaCifrada!).ShouldNotContain(Contrasena);
        });
    }

    [Fact]
    public async Task Por_vencer_se_avisa()
    {
        var r = await ComoAsync(Administrador, () => _credenciales.GuardarEfirmaAsync(_cliente, Guardar(Efirma(diasDeVigencia: 20))));

        r.Efirma!.PorVencer.ShouldBeTrue();
        r.Efirma.DiasParaVencer.ShouldBeLessThanOrEqualTo(CredencialesConsts.DiasDeAviso);
    }

    [Fact]
    public async Task Ver_la_contrasena_y_bajar_la_llave_piden_NIP_y_el_cer_no()
    {
        var e = Efirma();
        await ComoAsync(Usuario, () => _credenciales.GuardarEfirmaAsync(_cliente, Guardar(e)));

        (await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _credenciales.RevelarAsync(_cliente, new RevelarCredencialDto { Tipo = TipoDeCredencial.Efirma }))))
            .Message.ShouldContain("contraseña de tu cuenta");
        await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _credenciales.PonerNipAsync(new PonerNipDto { Nip = "4821", ContrasenaDeCuenta = "mala" })));
        await PonerNipAsync(Usuario);
        (await ComoAsync(Usuario, () => _credenciales.GetAsync(_cliente))).TieneNip.ShouldBeTrue();

        var revelada = await ComoAsync(Usuario, () =>
            _credenciales.RevelarAsync(_cliente, new RevelarCredencialDto { Tipo = TipoDeCredencial.Efirma, Nip = "4821" }));
        revelada.Contrasena.ShouldBe(Contrasena);

        var key = await ComoAsync(Usuario, () =>
            _credenciales.DescargarAsync(_cliente, new DescargarCredencialDto { Archivo = "key", Nip = "4821" }));
        Convert.FromBase64String(key.ContenidoBase64).ShouldBe(e.Key);
        key.Nombre.ShouldEndWith(".key");
        await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _credenciales.DescargarAsync(_cliente, new DescargarCredencialDto { Archivo = "key" })));

        var cer = await ComoAsync(Usuario, () => _credenciales.DescargarAsync(_cliente, new DescargarCredencialDto { Archivo = "cer" }));
        Convert.FromBase64String(cer.ContenidoBase64).ShouldBe(e.Cer);

        // La contraseña de la cuenta también sirve en vez del NIP.
        (await ComoAsync(Administrador, () =>
            _credenciales.RevelarAsync(_cliente, new RevelarCredencialDto { Tipo = TipoDeCredencial.Efirma, ContrasenaDeCuenta = Clave })))
            .Contrasena.ShouldBe(Contrasena);
    }

    [Fact]
    public async Task Cinco_fallos_bloquean_aunque_despues_se_acierte()
    {
        await ComoAsync(Usuario, () => _credenciales.GuardarCiecAsync(_cliente,
            new GuardarCiecDto { Rfc = Rfc.ToLowerInvariant(), Contrasena = "ciec-123", Consentimiento = true }));
        await PonerNipAsync(Usuario);

        for (var i = 0; i < CredencialesConsts.MaxIntentos; i++)
        {
            await Should.ThrowAsync<UserFriendlyException>(() =>
                ComoAsync(Usuario, () => _credenciales.RevelarAsync(_cliente, new RevelarCredencialDto { Tipo = TipoDeCredencial.Ciec, Nip = "0000" })));
        }

        (await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(Usuario, () => _credenciales.RevelarAsync(_cliente, new RevelarCredencialDto { Tipo = TipoDeCredencial.Ciec, Nip = "4821" }))))
            .Message.ShouldContain("Demasiados intentos");
    }

    [Fact]
    public async Task La_CIEC_se_guarda_con_su_RFC_y_se_revela()
    {
        var r = await ComoAsync(Administrador, () => _credenciales.GuardarCiecAsync(_cliente,
            new GuardarCiecDto { Rfc = Rfc.ToLowerInvariant(), Contrasena = "ciec-123", Consentimiento = true }));
        r.Ciec!.Rfc.ShouldBe(Rfc);
        r.Efirma.ShouldBeNull();

        await PonerNipAsync(Administrador, "9090");
        (await ComoAsync(Administrador, () =>
            _credenciales.RevelarAsync(_cliente, new RevelarCredencialDto { Tipo = TipoDeCredencial.Ciec, Nip = "9090" })))
            .Contrasena.ShouldBe("ciec-123");

        var borrada = await ComoAsync(Administrador, () => _credenciales.BorrarAsync(_cliente, TipoDeCredencial.Ciec));
        borrada.Ciec.ShouldBeNull();
    }

    [Fact]
    public async Task Quien_solo_ve_al_cliente_ve_la_vigencia_pero_no_las_contrasenas_y_lo_ajeno_no_existe()
    {
        await ComoAsync(Usuario, () => _credenciales.GuardarEfirmaAsync(_cliente, Guardar(Efirma())));
        await PonerNipAsync(_lector);

        var vista = await ComoAsync(_lector, () => _credenciales.GetAsync(_cliente));
        vista.Efirma.ShouldNotBeNull();
        vista.PuedeAdministrar.ShouldBeFalse();
        await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(_lector, () => _credenciales.RevelarAsync(_cliente, new RevelarCredencialDto { Tipo = TipoDeCredencial.Efirma, Nip = "4821" })));
        await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(_lector, () => _credenciales.DescargarAsync(_cliente, new DescargarCredencialDto { Archivo = "cer" })));
        await Should.ThrowAsync<UserFriendlyException>(() =>
            ComoAsync(_lector, () => _credenciales.GuardarEfirmaAsync(_cliente, Guardar(Efirma()))));

        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () => _credenciales.GetAsync(_otroCliente)));
    }

    [Fact]
    public async Task Cada_uso_queda_en_la_bitacora_sin_el_dato_y_el_Developer_sale_como_equipo_tecnico()
    {
        await ComoAsync(Usuario, () => _credenciales.GuardarEfirmaAsync(_cliente, Guardar(Efirma())));
        await PonerNipAsync(Developer, "1111");
        await ComoAsync(Developer, () =>
            _credenciales.RevelarAsync(_cliente, new RevelarCredencialDto { Tipo = TipoDeCredencial.Efirma, Nip = "1111" }));

        var bitacora = await ComoAsync(Usuario, () => _credenciales.GetBitacoraAsync(_cliente));

        bitacora.Select(u => u.Accion).ShouldBe([AccionSobreCredencial.VerContrasena, AccionSobreCredencial.Guardar]);
        bitacora[0].Quien.ShouldBe("Equipo técnico");
        bitacora[1].Quien.ShouldBe("carlos");
    }

    [Fact]
    public void El_servicio_no_entra_al_registro_de_auditoria()
    {
        // ABP guarda los parámetros de cada petición auditada: aquí llegarían contraseñas y llaves.
        typeof(CredencialesAppService).GetCustomAttribute<DisableAuditingAttribute>().ShouldNotBeNull();
    }
}
