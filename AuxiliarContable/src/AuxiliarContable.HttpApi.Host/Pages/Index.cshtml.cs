using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Volo.Abp.AspNetCore.Mvc.UI.RazorPages;

namespace AuxiliarContable.Pages;

/// <summary>
/// La raíz de la API (<c>https://api.…/</c>) no es un sitio: redirige a la consola.
///
/// ⚠️ Antes era la portada de la plantilla de ABP («Iniciar sesión», idiomas y la lista de
/// aplicaciones de OpenIddict con sus direcciones, pública). Quien terminaba ahí —por ejemplo,
/// tras el error 400 de un doble envío del login— iniciaba sesión en la API y se quedaba en esa
/// portada en vez de volver a la consola: parecía que producción estaba rota.
/// </summary>
public class IndexModel : AbpPageModel
{
    private readonly IConfiguration _configuracion;

    public IndexModel(IConfiguration configuracion)
    {
        _configuracion = configuracion;
    }

    public IActionResult OnGet()
    {
        var consola = _configuracion["App:AngularUrl"];
        return string.IsNullOrWhiteSpace(consola) ? Page() : Redirect(consola.TrimEnd('/') + "/");
    }
}
