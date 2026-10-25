using IgasConsole.Acceso;
using Shouldly;
using Xunit;

namespace IgasConsole.Acceso;

/// <summary>
/// El segmento bajo el que se esconden el panel del host y el formulario de login.
///
/// <para>Se prueba por lo mismo que <c>RaizDelEspacioTests</c>: la URL que sale de aquí acaba
/// registrada como <c>redirect_uri</c> en OpenIddict, que compara de forma <b>exacta</b>, y la
/// otra mitad de esa cadena la construye el TypeScript (<c>raizDelHost</c> en
/// <c>ruta-de-inquilino.ts</c>). Si las dos se separan, el único que se queda sin poder entrar
/// es el superadministrador — y el error ocurre en el servidor de autenticación, donde no se
/// ve desde el navegador.</para>
/// </summary>
public class AccesoConstsTests
{
    [Theory]
    [InlineData("g7k2p", "g7k2p")]
    // Llega de una variable de entorno escrita a mano: las barras y los espacios sobrantes
    // son el descuido más probable, y con ellos la ruta registrada no sería la real.
    [InlineData("  g7k2p  ", "g7k2p")]
    [InlineData("/g7k2p/", "g7k2p")]
    [InlineData("panel-oculto_2", "panel-oculto_2")]
    public void Normaliza_lo_que_sirve(string valor, string esperado)
    {
        AccesoConsts.Normalizar(valor).ShouldBe(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // Demasiado corto para que adivinarlo cueste algo.
    [InlineData("ab")]
    // ⚠️ Lista BLANCA de caracteres, no limpieza de los peligrosos. Este valor se convierte
    // en una plantilla de ruta de Razor Pages y en un prefijo de URL: una barra, una llave o
    // un interrogante por medio no darían un error claro, darían una ruta distinta de la que
    // nadie quiso — y el panel quedaría en un sitio que ni su dueño sabe.
    [InlineData("con/barra")]
    [InlineData("con espacio")]
    [InlineData("{plantilla}")]
    [InlineData("con?query")]
    [InlineData("acentuadó")]
    public void Rechaza_lo_que_no_sirve(string? valor)
    {
        // Cadena vacía —y no una excepción— porque un segmento mal escrito en la
        // configuración dejaría el servicio sin arrancar: la avería sería peor que el
        // problema. Se cae al comportamiento por defecto, que funciona.
        AccesoConsts.Normalizar(valor).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("http://localhost:4201", "g7k2p", "http://localhost:4201/g7k2p")]
    [InlineData("http://localhost:4201/", "g7k2p", "http://localhost:4201/g7k2p")]
    [InlineData("https://mi.sitio.com", "panel_x", "https://mi.sitio.com/panel_x")]
    public void Construye_la_raiz_del_host(string raiz, string segmento, string esperado)
    {
        AccesoConsts.RaizDelHost(raiz, segmento).ShouldBe(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    public void Sin_segmento_valido_el_host_se_queda_en_la_raiz(string? segmento)
    {
        // Es el interruptor de emergencia: borrar la variable de entorno y reiniciar tiene
        // que devolver la instalación a su comportamiento de siempre, sin desplegar nada.
        AccesoConsts.RaizDelHost("https://x/", segmento).ShouldBe("https://x");
    }

    [Fact]
    public void NUNCA_termina_en_barra()
    {
        // OpenIddict compara exacto: `/g7k2p` y `/g7k2p/` son dos URIs distintas, y el
        // navegador manda la primera.
        AccesoConsts.RaizDelHost("http://x", "g7k2p").ShouldNotEndWith("/");
        AccesoConsts.RaizDelHost("http://x", null).ShouldNotEndWith("/");
    }

    [Theory]
    [InlineData("g7k2p", true)]
    [InlineData("", false)]
    [InlineData("ab", false)]
    public void Las_opciones_solo_ocultan_con_un_segmento_valido(string valor, bool oculto)
    {
        var opciones = new AccesoOptions { SegmentoDelHost = valor };

        opciones.EstaOculto.ShouldBe(oculto);
    }
}
