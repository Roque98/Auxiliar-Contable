using System;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using AuxiliarContable.Documentos;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// El servicio SOAP público del SAT (ConsultaCFDIService). La expresión es la del código QR: ?re=…&amp;rr=…&amp;tt=…&amp;id=….
/// Solo salen al SAT los datos que ya trae la factura (RFC, total y UUID); nada se registra en el log.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IVerificadorDeCfdi))]
public class VerificadorDeCfdiSat : IVerificadorDeCfdi, ITransientDependency
{
    public const string NombreHttpClient = "sat-verificacion";
    private const string Url = "https://consultaqr.facturaelectronica.sat.gob.mx/ConsultaCFDIService.svc";
    private const string Accion = "http://tempuri.org/IConsultaCFDIService/Consulta";

    private readonly IHttpClientFactory _clientes;

    public VerificadorDeCfdiSat(IHttpClientFactory clientes)
    {
        _clientes = clientes;
    }

    public bool EstaConfigurado => true;

    public async Task<EstatusDelSat> ConsultarAsync(string rfcEmisor, string rfcReceptor, string total, string uuid, CancellationToken cancellationToken = default)
    {
        var expresion = $"?re={rfcEmisor}&rr={rfcReceptor}&tt={total}&id={uuid}";
        var cuerpo = "<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:tem=\"http://tempuri.org/\">"
                     + "<soapenv:Header/><soapenv:Body><tem:Consulta><tem:expresionImpresa>" + SecurityElement.Escape(expresion)
                     + "</tem:expresionImpresa></tem:Consulta></soapenv:Body></soapenv:Envelope>";
        using var peticion = new HttpRequestMessage(HttpMethod.Post, Url)
        {
            Content = new StringContent(cuerpo, Encoding.UTF8, "text/xml"),
        };
        peticion.Headers.Add("SOAPAction", $"\"{Accion}\"");
        try
        {
            using var respuesta = await _clientes.CreateClient(NombreHttpClient).SendAsync(peticion, cancellationToken);
            var texto = await respuesta.Content.ReadAsStringAsync(cancellationToken);
            if (!respuesta.IsSuccessStatusCode)
            {
                throw new VerificadorNoDisponibleException($"El SAT respondió {(int)respuesta.StatusCode}.");
            }

            var xml = XDocument.Parse(texto);
            string? Valor(string nombre) => xml.Descendants().FirstOrDefault(e => e.Name.LocalName == nombre)?.Value?.Trim() is { Length: > 0 } v ? v : null;
            var estado = Valor("Estado") ?? throw new VerificadorNoDisponibleException("El SAT no dijo el estado de la factura.");
            return new EstatusDelSat(estado, Valor("EstatusCancelacion"), Valor("EsCancelable"), Valor("CodigoEstatus"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or XmlException)
        {
            throw new VerificadorNoDisponibleException("El servicio de verificación del SAT no responde.", ex);
        }
    }
}
