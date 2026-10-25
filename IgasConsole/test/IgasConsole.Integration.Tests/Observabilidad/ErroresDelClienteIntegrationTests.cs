using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Observabilidad;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Validation;
using Xunit;

namespace IgasConsole;

/// <summary>
/// Los errores de la web y la app van al log (y con él a OpenObserve). Lo que se prueba aquí: que se
/// aceptan, que pasar el tope por minuto no rompe al cliente (se descarta en silencio) y que un
/// mensaje gigante se rechaza por validación. La sesión obligatoria la pone [Authorize]: en las
/// pruebas la autorización es «todo permitido».
/// </summary>
public class ErroresDelClienteIntegrationTests : ConJerarquiaTestBase
{
    private readonly IErroresDelClienteAppService _errores;

    public ErroresDelClienteIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _errores = GetRequiredService<IErroresDelClienteAppService>();
    }

    [Fact]
    public async Task Se_registran_y_pasar_el_tope_no_rompe_al_cliente()
    {
        foreach (var i in Enumerable.Range(0, ErroresDelClienteConsts.MaxPorMinuto + 5))
        {
            await ComoAsync(Usuario, () => _errores.RegistrarAsync(new ErrorDelClienteDto
            {
                Origen = "web",
                Mensaje = "TypeError: no se pudo leer «x» " + i,
                Pila = "at Componente (main.js:1:2)",
                Ruta = "/escritorios",
                Version = "abc123",
            }));
        }
    }

    [Fact]
    public async Task Un_mensaje_gigante_se_rechaza()
    {
        await Should.ThrowAsync<AbpValidationException>(() =>
            ComoAsync(Usuario, () => _errores.RegistrarAsync(new ErrorDelClienteDto { Origen = "app", Mensaje = new string('x', ErroresDelClienteConsts.MaxMensaje + 1) })));
    }
}
