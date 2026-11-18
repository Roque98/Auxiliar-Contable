using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using AuxiliarContable.Contabilidad;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// El servicio web «Descarga Masiva de CFDI y Retenciones» del SAT, versión 1.5. Nada de lo que va o viene se registra
/// en el log (ni el token, ni los XML); solo los códigos de estatus llegan a la base.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IServicioDescargaMasiva))]
public class ServicioDescargaMasivaSat : IServicioDescargaMasiva, ITransientDependency
{
    public const string NombreHttpClient = "sat-descarga-masiva";

    private const string UrlAutenticacion = "https://cfdidescargamasivasolicitud.clouda.sat.gob.mx/Autenticacion/Autenticacion.svc";
    private const string UrlSolicitud = "https://cfdidescargamasivasolicitud.clouda.sat.gob.mx/SolicitaDescargaService.svc";
    private const string UrlVerificacion = "https://cfdidescargamasivasolicitud.clouda.sat.gob.mx/VerificaSolicitudDescargaService.svc";
    private const string UrlDescarga = "https://cfdidescargamasiva.clouda.sat.gob.mx/DescargaMasivaService.svc";

    /// <summary>Un paquete del SAT trae hasta miles de XML; base64 en el sobre. Más que esto no se lee.</summary>
    private const long MaxBytesDeRespuesta = 300L * 1024 * 1024;

    private readonly IHttpClientFactory _clientes;

    public ServicioDescargaMasivaSat(IHttpClientFactory clientes)
    {
        _clientes = clientes;
    }

    public bool EstaConfigurado => true;

    public async Task<string> AutenticarAsync(EfirmaAbierta efirma, CancellationToken cancellationToken = default)
    {
        var sobre = SobresDeDescargaMasiva.Autenticacion(efirma, DateTime.UtcNow, Guid.NewGuid());
        var xml = await EnviarAsync(UrlAutenticacion, "http://DescargaMasivaTerceros.gob.mx/IAutenticacion/Autentica", sobre, null, cancellationToken);
        return Elemento(xml, "AutenticaResult")?.Value.Trim() is { Length: > 0 } token
            ? token
            : throw Rechazo(xml, "El SAT no aceptó la e.firma del cliente");
    }

    public async Task<SolicitudAceptada> SolicitarAsync(string token, EfirmaAbierta efirma, PedidoDeDescargaMasiva pedido, CancellationToken cancellationToken = default)
    {
        var operacion = pedido.Emitidas ? "SolicitaDescargaEmitidos" : "SolicitaDescargaRecibidos";
        var xml = await EnviarAsync(UrlSolicitud, $"{SobresDeDescargaMasiva.NsDescarga}/ISolicitaDescargaService/{operacion}",
            SobresDeDescargaMasiva.Solicitud(efirma, pedido), token, cancellationToken);
        var r = Elemento(xml, operacion + "Result") ?? throw Rechazo(xml, "El SAT no contestó la solicitud");
        return new SolicitudAceptada(Atributo(r, "IdSolicitud"), Atributo(r, "CodEstatus") ?? "", Atributo(r, "Mensaje"));
    }

    public async Task<AvanceDeSolicitud> VerificarAsync(string token, EfirmaAbierta efirma, string idSolicitud, CancellationToken cancellationToken = default)
    {
        var xml = await EnviarAsync(UrlVerificacion, $"{SobresDeDescargaMasiva.NsDescarga}/IVerificaSolicitudDescargaService/VerificaSolicitudDescarga",
            SobresDeDescargaMasiva.Verificacion(efirma, idSolicitud), token, cancellationToken);
        var r = Elemento(xml, "VerificaSolicitudDescargaResult")
                ?? throw Rechazo(xml, "El SAT no contestó la verificación");
        return new AvanceDeSolicitud(
            Atributo(r, "CodEstatus") ?? "",
            int.TryParse(Atributo(r, "EstadoSolicitud"), out var estado) ? estado : 0,
            Atributo(r, "CodigoEstadoSolicitud"),
            int.TryParse(Atributo(r, "NumeroCFDIs"), out var n) ? n : 0,
            Atributo(r, "Mensaje"),
            r.Elements().Where(e => e.Name.LocalName == "IdsPaquetes").Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList());
    }

    public async Task<PaqueteDelSat> DescargarAsync(string token, EfirmaAbierta efirma, string idPaquete, CancellationToken cancellationToken = default)
    {
        var xml = await EnviarAsync(UrlDescarga, $"{SobresDeDescargaMasiva.NsDescarga}/IDescargaMasivaTercerosService/Descargar",
            SobresDeDescargaMasiva.Descarga(efirma, idPaquete), token, cancellationToken);
        var respuesta = Elemento(xml, "respuesta");
        var paquete = Elemento(xml, "Paquete")?.Value;
        byte[]? zip = null;
        if (!string.IsNullOrWhiteSpace(paquete))
        {
            try
            {
                zip = Convert.FromBase64String(paquete.Trim());
            }
            catch (FormatException)
            {
                zip = null;
            }
        }

        return new PaqueteDelSat(respuesta is null ? "" : Atributo(respuesta, "CodEstatus") ?? "", respuesta is null ? null : Atributo(respuesta, "Mensaje"), zip);
    }

    private async Task<XDocument> EnviarAsync(string url, string accion, string sobre, string? token, CancellationToken cancellationToken)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(sobre, Encoding.UTF8, "text/xml"),
        };
        peticion.Headers.TryAddWithoutValidation("SOAPAction", $"\"{accion}\"");
        if (token != null)
        {
            peticion.Headers.TryAddWithoutValidation("Authorization", $"WRAP access_token=\"{token}\"");
        }

        try
        {
            using var respuesta = await _clientes.CreateClient(NombreHttpClient)
                .SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (respuesta.Content.Headers.ContentLength > MaxBytesDeRespuesta)
            {
                throw new DescargaMasivaNoDisponibleException("La respuesta del SAT es demasiado grande.");
            }

            var texto = await respuesta.Content.ReadAsStringAsync(cancellationToken);
            XDocument xml;
            try
            {
                xml = XDocument.Parse(texto);
            }
            catch (XmlException)
            {
                throw new DescargaMasivaNoDisponibleException($"El SAT respondió {(int)respuesta.StatusCode} sin un XML.");
            }

            // Un SOAP Fault viene con 500: se devuelve para que quien llama diga qué falló.
            if (!respuesta.IsSuccessStatusCode && Elemento(xml, "Fault") is null)
            {
                throw new DescargaMasivaNoDisponibleException($"El SAT respondió {(int)respuesta.StatusCode}.");
            }

            return xml;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new DescargaMasivaNoDisponibleException("El servicio de descarga masiva del SAT no responde.", ex);
        }
    }

    /// <summary>Un SOAP Fault es un «no» del SAT (firma, certificado…): se dice. Sin Fault, el SAT no está bien.</summary>
    private static Exception Rechazo(XDocument xml, string que) =>
        Elemento(xml, "Fault") is not null
            ? new UserFriendlyException(que + Falla(xml) + ".")
            : new DescargaMasivaNoDisponibleException(que + ".");

    private static string Falla(XDocument xml) =>
        Elemento(xml, "faultstring")?.Value.Trim() is { Length: > 0 } f ? $" ({(f.Length > 200 ? f[..200] : f)})" : "";

    private static XElement? Elemento(XDocument xml, string nombre) => xml.Descendants().FirstOrDefault(e => e.Name.LocalName == nombre);

    private static string? Atributo(XElement e, string nombre) =>
        e.Attributes().FirstOrDefault(a => a.Name.LocalName == nombre)?.Value.Trim() is { Length: > 0 } v ? v : null;
}
