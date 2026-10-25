using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.Notifications.Brevo;

/// <summary>
/// Las tres llamadas a la API v3 de Brevo que usa la aplicación. Endpoints y campos
/// verificados contra la documentación oficial (developers.brevo.com) el 2026-09-27:
/// <list type="bullet">
/// <item>Correo: <c>POST smtp/email</c> → 201 con <c>messageId</c>.</item>
/// <item>SMS: <c>POST transactionalSMS/send</c> (el antiguo <c>transactionalSMS/sms</c>
/// está obsoleto) → 201.</item>
/// <item>WhatsApp: <c>POST whatsapp/sendMessage</c> con <c>templateId</c> o <c>text</c>
/// → 201. La documentación no describe variables de plantilla, así que no se mandan.</item>
/// </list>
///
/// <para>Un fallo LANZA, con el código y el cuerpo que devolvió Brevo. Quien llama suele ser
/// un trabajo en segundo plano, y lanzar es lo que hace que ABP lo reintente. La API key va
/// en la cabecera y nunca en el mensaje de error.</para>
/// </summary>
public class BrevoClient : ITransientDependency
{
    public const string NombreHttpClient = "Brevo";

    private readonly IHttpClientFactory _fabrica;
    private readonly BrevoOptions _opciones;

    public BrevoClient(IHttpClientFactory fabrica, IOptions<BrevoOptions> opciones)
    {
        _fabrica = fabrica;
        _opciones = opciones.Value;
    }

    public Task EnviarCorreoAsync(string para, string asunto, string cuerpo, bool esHtml)
    {
        if (string.IsNullOrWhiteSpace(_opciones.SenderEmail))
        {
            throw new AbpException("Falta Brevo:SenderEmail: sin remitente Brevo rechaza el correo.");
        }

        return EnviarAsync("smtp/email", new CorreoBrevo
        {
            Sender = new Contacto { Email = _opciones.SenderEmail, Name = _opciones.SenderName },
            To = [new Contacto { Email = para }],
            Subject = asunto,
            HtmlContent = esHtml ? cuerpo : null,
            TextContent = esHtml ? null : cuerpo,
        });
    }

    public Task EnviarSmsAsync(string numero, string texto)
    {
        return EnviarAsync("transactionalSMS/send", new SmsBrevo
        {
            Sender = _opciones.SmsSender,
            Recipient = numero,
            Content = texto,
            Type = "transactional",
            UnicodeEnabled = true,
        });
    }

    public Task EnviarWhatsAppAsync(string numero, long? plantillaId, string? texto)
    {
        if (string.IsNullOrWhiteSpace(_opciones.WhatsAppSenderNumber))
        {
            throw new AbpException("Falta Brevo:WhatsAppSenderNumber: no hay número remitente de WhatsApp.");
        }

        return EnviarAsync("whatsapp/sendMessage", new WhatsAppBrevo
        {
            SenderNumber = _opciones.WhatsAppSenderNumber,
            ContactNumbers = [numero],
            TemplateId = plantillaId,
            Text = plantillaId.HasValue ? null : texto,
        });
    }

    private async Task EnviarAsync<T>(string ruta, T cuerpo)
    {
        if (!_opciones.TieneCredenciales)
        {
            throw new AbpException("Brevo no está configurado (falta Brevo:ApiKey).");
        }

        var cliente = _fabrica.CreateClient(NombreHttpClient);

        using var peticion = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_opciones.BaseUrl), ruta))
        {
            Content = JsonContent.Create(cuerpo),
        };
        peticion.Headers.Add("api-key", _opciones.ApiKey);
        peticion.Headers.Add("accept", "application/json");

        using var respuesta = await cliente.SendAsync(peticion);

        if (!respuesta.IsSuccessStatusCode)
        {
            var detalle = await respuesta.Content.ReadAsStringAsync();
            throw new AbpException(
                $"Brevo respondió {(int)respuesta.StatusCode} a {ruta}: {Recortar(detalle)}");
        }
    }

    private static string Recortar(string texto) => texto.Length <= 500 ? texto : texto[..500] + "…";

    private sealed class Contacto
    {
        [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }
    }

    private sealed class CorreoBrevo
    {
        [JsonPropertyName("sender")] public Contacto Sender { get; set; } = new();
        [JsonPropertyName("to")] public Contacto[] To { get; set; } = [];
        [JsonPropertyName("subject")] public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("htmlContent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? HtmlContent { get; set; }

        [JsonPropertyName("textContent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TextContent { get; set; }
    }

    private sealed class SmsBrevo
    {
        [JsonPropertyName("sender")] public string Sender { get; set; } = string.Empty;
        [JsonPropertyName("recipient")] public string Recipient { get; set; } = string.Empty;
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
        [JsonPropertyName("type")] public string Type { get; set; } = "transactional";
        [JsonPropertyName("unicodeEnabled")] public bool UnicodeEnabled { get; set; }
    }

    private sealed class WhatsAppBrevo
    {
        [JsonPropertyName("senderNumber")] public string SenderNumber { get; set; } = string.Empty;
        [JsonPropertyName("contactNumbers")] public string[] ContactNumbers { get; set; } = [];

        [JsonPropertyName("templateId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? TemplateId { get; set; }

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; set; }
    }
}
