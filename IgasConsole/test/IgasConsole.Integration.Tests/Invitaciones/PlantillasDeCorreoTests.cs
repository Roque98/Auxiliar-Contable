using System.Collections.Generic;
using System.Threading.Tasks;
using IgasConsole.Emails;
using Shouldly;
using Volo.Abp.TextTemplating;
using Xunit;

namespace IgasConsole;

/// <summary>
/// Las plantillas de correo se renderizan de verdad (Scriban): un error de sintaxis en un
/// .tpl no lo ve el compilador, y saldría a la luz con el primer correo real.
/// </summary>
public class PlantillasDeCorreoTests : IntegrationTestBase
{
    private readonly ITemplateRenderer _plantillas;

    public PlantillasDeCorreoTests(PostgresFixture fixture) : base(fixture)
    {
        _plantillas = GetRequiredService<ITemplateRenderer>();
    }

    private static Dictionary<string, object> Contexto() => new()
    {
        ["preencabezado"] = "Resumen corto",
        ["logo_url"] = "https://api.igas.test/images/logo/zia/zia-logotipo.png",
        ["app_url"] = "https://igas.test",
        ["app_url_texto"] = "igas.test"
    };

    [Fact]
    public async Task La_invitacion_sale_con_el_layout_de_ZIA_y_escapa_lo_que_escribe_la_gente()
    {
        var html = await _plantillas.RenderAsync(
            IgasEmailTemplates.Invitacion,
            new { quien_invita = "Tío <b>Pepe</b>", rol = "Administrador", enlace = "https://igas.test/registro#t=abc", dias = 7 },
            cultureName: "es",
            globalContext: Contexto());

        html.ShouldContain("<html lang=\"es\"");
        html.ShouldContain("https://api.igas.test/images/logo/zia/zia-logotipo.png");
        html.ShouldContain("Resumen corto");
        html.ShouldContain("https://igas.test/registro#t=abc");
        html.ShouldContain("&lt;b&gt;Pepe&lt;/b&gt;");
        html.ShouldNotContain("<b>Pepe");
        html.ShouldContain("<strong>Administrador</strong>");
        html.ShouldContain("7 días");
    }

    [Fact]
    public async Task Sin_quien_invita_ni_contexto_sigue_saliendo_bien()
    {
        var html = await _plantillas.RenderAsync(
            IgasEmailTemplates.Invitacion,
            new { quien_invita = (string?)null, rol = "Usuario", enlace = "https://igas.test/registro#t=x", dias = 7 },
            cultureName: "es");

        html.ShouldContain("Te invitaron");
        html.ShouldContain(">ZIA</span>");
    }

    [Fact]
    public async Task El_de_recuperar_contrasena_es_el_de_ZIA_en_espanol()
    {
        var html = await _plantillas.RenderAsync(
            IgasEmailTemplates.RestablecerContrasena,
            new { link = "https://igas.test/account/reset-password?x=1" },
            cultureName: "es",
            globalContext: Contexto());

        html.ShouldContain("Restablece tu contraseña");
        html.ShouldContain("https://igas.test/account/reset-password?x=1");
        html.ShouldContain("border-top:4px solid #1a7f7f");
    }

    [Fact]
    public async Task El_codigo_sale_grande_y_con_su_vigencia()
    {
        var html = await _plantillas.RenderAsync(
            IgasEmailTemplates.Codigo,
            new { titulo = "Tu código de acceso", intro = "Escribe este código.", codigo = "482915", minutos = 5, aviso = "¿No eres tú?" },
            cultureName: "es",
            globalContext: Contexto());

        if (System.Environment.GetEnvironmentVariable("IGAS_VOLCAR_CORREOS") is { Length: > 0 } carpeta)
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(carpeta, "correo-codigo.html"), html);
        }

        html.ShouldContain("482915");
        html.ShouldContain("Vence en 5 minutos");
        html.ShouldContain("de acceso"); // «ó» sale como &#243; (html.escape), que se ve igual.
        html.ShouldContain("border-top:4px solid #1a7f7f");
    }
}
