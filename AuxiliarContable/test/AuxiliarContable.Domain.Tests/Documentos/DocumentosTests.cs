using System;
using System.Security.Cryptography;
using System.Text;
using AuxiliarContable.Documentos;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace AuxiliarContable;

/// <summary>El tipo real de un archivo y el cifrado de lo que va al disco.</summary>
public class DocumentosTests
{
    [Fact]
    public void El_tipo_sale_de_los_bytes_y_no_del_nombre()
    {
        DetectorDeDocumento.Detectar("%PDF-1.7\n..."u8).ShouldBe(DetectorDeDocumento.Pdf);
        DetectorDeDocumento.Detectar("\uFEFF<?xml version=\"1.0\"?><cfdi:Comprobante/>"u8).ShouldBe(DetectorDeDocumento.Xml);
        DetectorDeDocumento.Detectar("  \r\n<cfdi:Comprobante/>"u8).ShouldBe(DetectorDeDocumento.Xml);
        DetectorDeDocumento.Detectar([0xFF, 0xD8, 0xFF, 0xE0]).ShouldBe("image/jpeg");
        DetectorDeDocumento.Detectar("MZ\x90\x00 ejecutable"u8).ShouldBeNull();
        DetectorDeDocumento.Detectar("hola"u8).ShouldBeNull();
    }

    [Fact]
    public void Un_XML_con_DOCTYPE_se_rechaza_porque_ningun_CFDI_lo_trae()
    {
        DetectorDeDocumento.Detectar("<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY a \"b\">]><x/>"u8).ShouldBeNull();
    }

    [Theory]
    [InlineData(@"C:\fakepath\estado.pdf", "estado.pdf")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("  ", "documento")]
    [InlineData("con\u0000control.pdf", "concontrol.pdf")]
    public void El_nombre_se_limpia_de_rutas_y_caracteres_raros(string entrada, string esperado) =>
        GestorDeDocumentos.NombreSeguro(entrada).ShouldBe(esperado);

    private static CifradorDeDocumentos Cifrador(string? clave = null) =>
        new(Options.Create(new DocumentosOptions { ClaveDeCifrado = clave ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    [Fact]
    public void Lo_cifrado_se_descifra_igual_y_no_contiene_el_original()
    {
        var cifrador = Cifrador();
        var id = Guid.NewGuid();
        var claro = Encoding.UTF8.GetBytes("RFC SAT970701NN3 saldo 12,500.00");

        var cifrado = cifrador.Cifrar(id, claro);

        Encoding.UTF8.GetString(cifrado).ShouldNotContain("SAT970701NN3");
        cifrador.Descifrar(id, cifrado).ShouldBe(claro);
    }

    [Fact]
    public void El_archivo_de_un_documento_no_se_descifra_como_si_fuera_de_otro()
    {
        var cifrador = Cifrador();
        var cifrado = cifrador.Cifrar(Guid.NewGuid(), [1, 2, 3]);

        Should.Throw<AbpException>(() => cifrador.Descifrar(Guid.NewGuid(), cifrado));
    }

    [Fact]
    public void Un_archivo_alterado_no_se_da_por_bueno()
    {
        var cifrador = Cifrador();
        var id = Guid.NewGuid();
        var cifrado = cifrador.Cifrar(id, [1, 2, 3, 4]);
        cifrado[^1] ^= 0xFF;

        Should.Throw<AbpException>(() => cifrador.Descifrar(id, cifrado));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("no-es-base64!")]
    [InlineData("AAAA")] // 3 bytes
    public void Sin_una_clave_valida_no_se_guarda_nada(string? clave)
    {
        Should.Throw<AbpException>(() => new CifradorDeDocumentos(Options.Create(new DocumentosOptions { ClaveDeCifrado = clave })).Cifrar(Guid.NewGuid(), [1]));
    }
}
