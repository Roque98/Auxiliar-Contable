using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// La e.firma de un cliente ya descifrada, solo en memoria y solo mientras dura una vuelta de la descarga masiva.
/// Nunca se escribe, ni se registra, ni sale del servidor: al SAT solo va el certificado (público) y las firmas.
/// </summary>
public sealed class EfirmaAbierta : IDisposable
{
    public EfirmaAbierta(X509Certificate2 certificado, RSA llave, string rfc)
    {
        Certificado = certificado;
        Llave = llave;
        Rfc = rfc;
    }

    public X509Certificate2 Certificado { get; }
    public RSA Llave { get; }
    public string Rfc { get; }

    public void Dispose()
    {
        Llave.Dispose();
        Certificado.Dispose();
    }
}

/// <summary>Qué se pide al SAT: emitidas o recibidas del RFC de la e.firma, de un rango de fechas (hora de México).</summary>
public sealed record PedidoDeDescargaMasiva(bool Emitidas, DateTime Desde, DateTime Hasta);

/// <summary>Respuesta a la solicitud: 5000 = aceptada (trae IdSolicitud); 5004 = no hay facturas; otro = rechazo.</summary>
public sealed record SolicitudAceptada(string? IdSolicitud, string CodEstatus, string? Mensaje);

/// <summary>
/// Cómo va una solicitud. EstadoSolicitud: 1 aceptada, 2 en proceso, 3 terminada, 4 error, 5 rechazada, 6 vencida.
/// </summary>
public sealed record AvanceDeSolicitud(string CodEstatus, int EstadoSolicitud, string? CodigoEstadoSolicitud, int NumeroCfdis, string? Mensaje,
    IReadOnlyList<string> Paquetes);

/// <summary>Un paquete: .zip con los XML (vacío si el SAT no lo dio).</summary>
public sealed record PaqueteDelSat(string CodEstatus, string? Mensaje, byte[]? Zip);

/// <summary>
/// El servicio web oficial «Descarga Masiva de CFDI y Retenciones» del SAT (versión 1.5): se autentica con la e.firma
/// del contribuyente, sin captcha ni persona. Los cuatro pasos: autenticar (token de 5 minutos), solicitar, verificar
/// (el SAT arma los paquetes en minutos u horas) y descargar cada paquete.
/// </summary>
public interface IServicioDescargaMasiva
{
    bool EstaConfigurado { get; }

    Task<string> AutenticarAsync(EfirmaAbierta efirma, CancellationToken cancellationToken = default);

    Task<SolicitudAceptada> SolicitarAsync(string token, EfirmaAbierta efirma, PedidoDeDescargaMasiva pedido, CancellationToken cancellationToken = default);

    Task<AvanceDeSolicitud> VerificarAsync(string token, EfirmaAbierta efirma, string idSolicitud, CancellationToken cancellationToken = default);

    Task<PaqueteDelSat> DescargarAsync(string token, EfirmaAbierta efirma, string idPaquete, CancellationToken cancellationToken = default);
}

/// <summary>El SAT no contestó (red, caída, respuesta que no se entiende): se intenta en la siguiente vuelta.</summary>
public class DescargaMasivaNoDisponibleException : AbpException
{
    public DescargaMasivaNoDisponibleException(string mensaje, Exception? interna = null) : base(mensaje, interna)
    {
    }
}

/// <summary>Sin host (DbMigrator, tests) no se llama al SAT.</summary>
[Dependency(TryRegister = true)]
[ExposeServices(typeof(IServicioDescargaMasiva))]
public class ServicioDescargaMasivaSinConfigurar : IServicioDescargaMasiva, ISingletonDependency
{
    public bool EstaConfigurado => false;

    private static Exception No() => new DescargaMasivaNoDisponibleException("No hay descarga masiva del SAT en este proceso.");

    public Task<string> AutenticarAsync(EfirmaAbierta efirma, CancellationToken cancellationToken = default) => throw No();

    public Task<SolicitudAceptada> SolicitarAsync(string token, EfirmaAbierta efirma, PedidoDeDescargaMasiva pedido, CancellationToken cancellationToken = default) =>
        throw No();

    public Task<AvanceDeSolicitud> VerificarAsync(string token, EfirmaAbierta efirma, string idSolicitud, CancellationToken cancellationToken = default) =>
        throw No();

    public Task<PaqueteDelSat> DescargarAsync(string token, EfirmaAbierta efirma, string idPaquete, CancellationToken cancellationToken = default) =>
        throw No();
}

/// <summary>
/// Los sobres SOAP firmados del servicio de descarga masiva. Se escriben ya en forma canónica (exc-c14n: atributos en
/// orden alfabético, sin espacios entre etiquetas, etiquetas vacías abiertas y cerradas): así lo que se firma es
/// exactamente lo que se manda. Firma RSA-SHA1 y resumen SHA-1, que es lo que pide el SAT.
/// </summary>
public static class SobresDeDescargaMasiva
{
    public const string NsDescarga = "http://DescargaMasivaTerceros.sat.gob.mx";
    public const string NsAutenticacion = "http://DescargaMasivaTerceros.gob.mx";
    private const string NsDsig = "http://www.w3.org/2000/09/xmldsig#";
    private const string NsUtil = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
    private const string NsSecext = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    private const string X509v3 = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3";
    private const string Base64Binary = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";
    private const string ExcC14n = "http://www.w3.org/2001/10/xml-exc-c14n#";

    /// <summary>Autenticación: firma un Timestamp de 5 minutos; el certificado viaja como BinarySecurityToken.</summary>
    public static string Autenticacion(EfirmaAbierta efirma, DateTime ahoraUtc, Guid idDelToken)
    {
        var creado = ahoraUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var vence = ahoraUtc.AddMinutes(5).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var token = "uuid-" + idDelToken.ToString("D") + "-1";
        var timestamp = $"<u:Timestamp xmlns:u=\"{NsUtil}\" u:Id=\"_0\"><u:Created>{creado}</u:Created><u:Expires>{vence}</u:Expires></u:Timestamp>";
        var keyInfo = $"<KeyInfo><o:SecurityTokenReference><o:Reference URI=\"#{token}\" ValueType=\"{X509v3}\"></o:Reference></o:SecurityTokenReference></KeyInfo>";
        var firma = Firma(efirma, timestamp, "#_0", keyInfo);
        return $"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:u=\"{NsUtil}\"><s:Header>"
               + $"<o:Security xmlns:o=\"{NsSecext}\" s:mustUnderstand=\"1\">"
               + $"<u:Timestamp u:Id=\"_0\"><u:Created>{creado}</u:Created><u:Expires>{vence}</u:Expires></u:Timestamp>"
               + $"<o:BinarySecurityToken EncodingType=\"{Base64Binary}\" ValueType=\"{X509v3}\" u:Id=\"{token}\">{Certificado(efirma)}</o:BinarySecurityToken>"
               + firma
               + $"</o:Security></s:Header><s:Body><Autentica xmlns=\"{NsAutenticacion}\"></Autentica></s:Body></s:Envelope>";
    }

    /// <summary>
    /// Solicitud de CFDI (XML) emitidos o recibidos. Las recibidas van con EstadoComprobante «Vigente»: la versión 1.5 no
    /// entrega el XML de recibidas canceladas (su estatus lo da la verificación de cada noche).
    /// </summary>
    public static string Solicitud(EfirmaAbierta efirma, PedidoDeDescargaMasiva pedido)
    {
        var atributos = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["FechaFinal"] = pedido.Hasta.ToString("yyyy-MM-dd'T'HH:mm:ss"),
            ["FechaInicial"] = pedido.Desde.ToString("yyyy-MM-dd'T'HH:mm:ss"),
            ["RfcSolicitante"] = efirma.Rfc,
            ["TipoSolicitud"] = "CFDI",
        };
        if (pedido.Emitidas)
        {
            atributos["RfcEmisor"] = efirma.Rfc;
        }
        else
        {
            atributos["RfcReceptor"] = efirma.Rfc;
            atributos["EstadoComprobante"] = "Vigente";
        }

        var operacion = pedido.Emitidas ? "SolicitaDescargaEmitidos" : "SolicitaDescargaRecibidos";
        return Operacion(efirma, operacion, "solicitud", atributos);
    }

    public static string Verificacion(EfirmaAbierta efirma, string idSolicitud) =>
        Operacion(efirma, "VerificaSolicitudDescarga", "solicitud", new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["IdSolicitud"] = idSolicitud,
            ["RfcSolicitante"] = efirma.Rfc,
        });

    public static string Descarga(EfirmaAbierta efirma, string idPaquete) =>
        Operacion(efirma, "PeticionDescargaMasivaTercerosEntrada", "peticionDescarga", new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["IdPaquete"] = idPaquete,
            ["RfcSolicitante"] = efirma.Rfc,
        });

    /// <summary>
    /// La operación se firma entera (sin la firma: «enveloped»), con el elemento de la operación como documento; la
    /// firma va dentro del elemento de la solicitud y su KeyInfo trae el certificado.
    /// </summary>
    private static string Operacion(EfirmaAbierta efirma, string operacion, string elemento, SortedDictionary<string, string> atributos)
    {
        var texto = new StringBuilder();
        foreach (var (nombre, valor) in atributos)
        {
            texto.Append(' ').Append(nombre).Append("=\"").Append(EscaparAtributo(valor)).Append('"');
        }

        var abre = $"<des:{operacion} xmlns:des=\"{NsDescarga}\"><des:{elemento}{texto}>";
        var cierra = $"</des:{elemento}></des:{operacion}>";
        var cert = efirma.Certificado;
        var keyInfo = "<KeyInfo><X509Data><X509IssuerSerial>"
                      + $"<X509IssuerName>{EscaparTexto(cert.Issuer)}</X509IssuerName>"
                      + $"<X509SerialNumber>{SerialDecimal(cert)}</X509SerialNumber>"
                      + $"</X509IssuerSerial><X509Certificate>{Certificado(efirma)}</X509Certificate></X509Data></KeyInfo>";
        var firma = Firma(efirma, abre + cierra, "", keyInfo);
        return "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Header></s:Header><s:Body>"
               + abre + firma + cierra + "</s:Body></s:Envelope>";
    }

    /// <summary>Resume <paramref name="canonico"/>, firma el SignedInfo canónico y arma el &lt;Signature&gt;.</summary>
    private static string Firma(EfirmaAbierta efirma, string canonico, string referencia, string keyInfo)
    {
        var resumen = Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(canonico)));
        var interior = $"<CanonicalizationMethod Algorithm=\"{ExcC14n}\"></CanonicalizationMethod>"
                       + $"<SignatureMethod Algorithm=\"{NsDsig}rsa-sha1\"></SignatureMethod>"
                       + $"<Reference URI=\"{referencia}\"><Transforms><Transform Algorithm=\"{ExcC14n}\"></Transform></Transforms>"
                       + $"<DigestMethod Algorithm=\"{NsDsig}sha1\"></DigestMethod><DigestValue>{resumen}</DigestValue></Reference>";
        // En forma canónica el SignedInfo declara el espacio de nombres de la firma (lo hereda de <Signature>).
        var firmado = $"<SignedInfo xmlns=\"{NsDsig}\">{interior}</SignedInfo>";
        var valor = Convert.ToBase64String(efirma.Llave.SignData(Encoding.UTF8.GetBytes(firmado), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1));
        return $"<Signature xmlns=\"{NsDsig}\"><SignedInfo>{interior}</SignedInfo><SignatureValue>{valor}</SignatureValue>{keyInfo}</Signature>";
    }

    private static string Certificado(EfirmaAbierta efirma) => Convert.ToBase64String(efirma.Certificado.RawData);

    private static string SerialDecimal(X509Certificate2 cert) =>
        new BigInteger(cert.SerialNumberBytes.Span, isUnsigned: true, isBigEndian: true).ToString();

    /// <summary>Escape de c14n en atributos: &amp;, &lt; y comillas (los RFC pueden llevar «&amp;»).</summary>
    public static string EscaparAtributo(string valor) => valor.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;");

    /// <summary>Escape de c14n en texto: &amp;, &lt; y &gt;.</summary>
    public static string EscaparTexto(string valor) => valor.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
