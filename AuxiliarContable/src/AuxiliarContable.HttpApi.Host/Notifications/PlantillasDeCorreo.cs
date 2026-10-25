using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AuxiliarContable.Notifications.Brevo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.TextTemplating;

namespace AuxiliarContable.Notifications;

/// <summary>
/// Renderiza un correo con el layout de la consola (<c>Emails/Layout.tpl</c>) pasándole lo que el
/// layout necesita y el modelo no le llega: el logo y la dirección de la consola.
///
/// <para>⚠️ El logo va con URL ABSOLUTA de la API (<c>App:SelfUrl</c>). Un correo no puede
/// cargar rutas relativas, y muchos clientes bloquean imágenes incrustadas en base64. En
/// desarrollo esa URL es localhost: el correo se ve sin logo fuera de esta máquina, y está
/// bien así.</para>
/// </summary>
public class PlantillasDeCorreo : ITransientDependency
{
    private readonly ITemplateRenderer _plantillas;
    private readonly IConfiguration _configuracion;

    public PlantillasDeCorreo(ITemplateRenderer plantillas, IConfiguration configuracion)
    {
        _plantillas = plantillas;
        _configuracion = configuracion;
    }

    public Task<string> RenderAsync(string plantilla, object modelo, string preencabezado)
    {
        var api = (_configuracion["App:SelfUrl"] ?? string.Empty).TrimEnd('/');
        var consola = (_configuracion["App:AngularUrl"] ?? string.Empty).TrimEnd('/');

        var contexto = new Dictionary<string, object>
        {
            ["preencabezado"] = preencabezado,
            ["logo_url"] = api.Length > 0 ? api + "/images/logo/marca/isotipo.png" : string.Empty,
            ["app_url"] = consola,
            ["app_url_texto"] = consola.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
        };

        return _plantillas.RenderAsync(plantilla, modelo, cultureName: "es", globalContext: contexto);
    }
}

/// <summary>Hay correo de verdad si Brevo tiene API key y remitente.</summary>
// ExposeServices explícito: el nombre no sigue la convención de ABP (I + nombre de la
// clase), así que sin esto la interfaz no se registraba y el servicio no arrancaba.
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IEstadoDelCorreo))]
public class EstadoDelCorreoBrevo : IEstadoDelCorreo, ISingletonDependency
{
    private readonly IOptions<BrevoOptions> _opciones;

    public EstadoDelCorreoBrevo(IOptions<BrevoOptions> opciones)
    {
        _opciones = opciones;
    }

    public bool PuedeEnviar =>
        _opciones.Value.TieneCredenciales && !string.IsNullOrWhiteSpace(_opciones.Value.SenderEmail);
}
