using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using AuxiliarContable.Contabilidad;
using Shouldly;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Los sobres de la Descarga Masiva se escriben a mano en forma canónica; aquí se comprueba con la implementación
/// estándar de .NET (exc-c14n) que lo firmado es justo lo que se manda y que la firma abre con el certificado. La e.firma
/// es INVENTADA (se genera aquí); nunca se llama al SAT.
/// </summary>
public class SobresDeDescargaMasivaTests
{
    private const string Ds = "http://www.w3.org/2000/09/xmldsig#";

    private static EfirmaAbierta Efirma(string rfc = "A&B010101AB1")
    {
        var rsa = RSA.Create(2048);
        var sujeto = new X500DistinguishedName($"CN=CLIENTE DE PRUEBA, OID.2.5.4.45={rfc.Replace("&", "")} / XAXX010101HDFXXX01, C=MX");
        var solicitud = new CertificateRequest(sujeto, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = solicitud.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(4));
        return new EfirmaAbierta(X509CertificateLoader.LoadCertificate(cert.Export(X509ContentType.Cert)), rsa, rfc);
    }

    private static XmlDocument Leer(string sobre)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(sobre);
        return doc;
    }

    /// <summary>exc-c14n de un elemento EN SU LUGAR del documento (con lo que hereda), sin la firma si se pide.</summary>
    private static byte[] Canonico(XmlElement elemento, bool sinFirma)
    {
        var ns = new XmlNamespaceManager(elemento.OwnerDocument.NameTable);
        ns.AddNamespace("ds", Ds);
        var xpath = "(descendant-or-self::node() | descendant-or-self::*/@* | descendant-or-self::*/namespace::*)"
                    + (sinFirma ? "[not(ancestor-or-self::ds:Signature)]" : "");
        var nodos = elemento.SelectNodes(xpath, ns)!;
        var c14n = new XmlDsigExcC14NTransform();
        c14n.LoadInput(nodos);
        using var salida = (Stream)c14n.GetOutput(typeof(Stream));
        using var m = new MemoryStream();
        salida.CopyTo(m);
        return m.ToArray();
    }

    private static void FirmaValida(EfirmaAbierta efirma, XmlElement firmado, XmlElement firma, bool sinFirma)
    {
        var ns = new XmlNamespaceManager(firma.OwnerDocument.NameTable);
        ns.AddNamespace("ds", Ds);
        var resumen = firma.SelectSingleNode("ds:SignedInfo/ds:Reference/ds:DigestValue", ns)!.InnerText;
        Convert.ToBase64String(SHA1.HashData(Canonico(firmado, sinFirma))).ShouldBe(resumen);

        var signedInfo = (XmlElement)firma.SelectSingleNode("ds:SignedInfo", ns)!;
        var valor = Convert.FromBase64String(firma.SelectSingleNode("ds:SignatureValue", ns)!.InnerText);
        using var publica = efirma.Certificado.GetRSAPublicKey()!;
        publica.VerifyData(Canonico(signedInfo, sinFirma: false), valor, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1).ShouldBeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void La_solicitud_se_firma_tal_como_se_manda(bool emitidas)
    {
        using var efirma = Efirma();
        var pedido = new PedidoDeDescargaMasiva(emitidas, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31, 23, 59, 59));
        var doc = Leer(SobresDeDescargaMasiva.Solicitud(efirma, pedido));
        var operacion = (XmlElement)doc.GetElementsByTagName(emitidas ? "des:SolicitaDescargaEmitidos" : "des:SolicitaDescargaRecibidos")[0]!;
        var solicitud = (XmlElement)operacion.FirstChild!;
        var firma = (XmlElement)solicitud.GetElementsByTagName("Signature", Ds)[0]!;
        FirmaValida(efirma, operacion, firma, sinFirma: true);

        // El RFC con «&» viaja escapado y se lee bien; recibidas solo vigentes (regla de la versión 1.5).
        solicitud.GetAttribute("RfcSolicitante").ShouldBe("A&B010101AB1");
        solicitud.GetAttribute(emitidas ? "RfcEmisor" : "RfcReceptor").ShouldBe("A&B010101AB1");
        solicitud.GetAttribute("FechaInicial").ShouldBe("2026-10-01T00:00:00");
        solicitud.GetAttribute("FechaFinal").ShouldBe("2026-10-31T23:59:59");
        solicitud.GetAttribute("TipoSolicitud").ShouldBe("CFDI");
        solicitud.GetAttribute("EstadoComprobante").ShouldBe(emitidas ? "" : "Vigente");
        solicitud.Attributes.Cast<XmlAttribute>().Select(a => a.Name).ShouldBe(solicitud.Attributes.Cast<XmlAttribute>().Select(a => a.Name).Order(StringComparer.Ordinal));
        firma.GetElementsByTagName("X509Certificate", Ds)[0]!.InnerText.ShouldBe(Convert.ToBase64String(efirma.Certificado.RawData));
    }

    [Fact]
    public void Verificacion_y_descarga_se_firman()
    {
        using var efirma = Efirma("EJE010101AB1");
        foreach (var (sobre, nombre) in new[]
                 {
                     (SobresDeDescargaMasiva.Verificacion(efirma, "4e80345d-917f-40bb-a98f-4a73939343c5"), "des:VerificaSolicitudDescarga"),
                     (SobresDeDescargaMasiva.Descarga(efirma, "4E80345D-917F-40BB-A98F-4A73939343C5_01"), "des:PeticionDescargaMasivaTercerosEntrada"),
                 })
        {
            var doc = Leer(sobre);
            var operacion = (XmlElement)doc.GetElementsByTagName(nombre)[0]!;
            FirmaValida(efirma, operacion, (XmlElement)operacion.GetElementsByTagName("Signature", Ds)[0]!, sinFirma: true);
            ((XmlElement)operacion.FirstChild!).GetAttribute("RfcSolicitante").ShouldBe("EJE010101AB1");
        }
    }

    [Fact]
    public void La_autenticacion_firma_el_timestamp_y_apunta_al_certificado()
    {
        using var efirma = Efirma("EJE010101AB1");
        var doc = Leer(SobresDeDescargaMasiva.Autenticacion(efirma, new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc), Guid.Empty));
        var u = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
        var timestamp = (XmlElement)doc.GetElementsByTagName("Timestamp", u)[0]!;
        timestamp.InnerText.ShouldBe("2026-11-18T12:00:00.000Z2026-11-18T12:05:00.000Z");
        FirmaValida(efirma, timestamp, (XmlElement)doc.GetElementsByTagName("Signature", Ds)[0]!, sinFirma: false);

        var token = (XmlElement)doc.GetElementsByTagName("o:BinarySecurityToken")[0]!;
        token.InnerText.ShouldBe(Convert.ToBase64String(efirma.Certificado.RawData));
        var referencia = (XmlElement)doc.GetElementsByTagName("o:Reference")[0]!;
        referencia.GetAttribute("URI").ShouldBe("#" + token.GetAttribute("Id", u));
        Encoding.UTF8.GetString(Convert.FromBase64String(token.InnerText)).ShouldNotContain("PRIVATE");
    }
}
