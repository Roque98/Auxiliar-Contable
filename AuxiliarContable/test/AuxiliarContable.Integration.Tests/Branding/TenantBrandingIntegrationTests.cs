using System;
using System.IO;
using System.Threading.Tasks;
using AuxiliarContable.Branding;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Marca por inquilino contra PostgreSQL real.
///
/// <para>Lo que de verdad se vigila aquí es que <b>un inquilino no pueda repintarle la
/// pantalla a otro</b>. Los ajustes de ABP ya son por inquilino de serie, así que el código
/// que lo consigue es… ninguno: no hay nada escrito que se pueda leer para comprobarlo, y
/// por eso hace falta un test. El día que alguien cambie <c>SetForTenantOrGlobalAsync</c>
/// por <c>SetGlobalAsync</c> —que compila igual, y en una instalación de un solo inquilino
/// se comporta igual— la marca pasaría a ser común sin que nada fallara.</para>
/// </summary>
public class TenantBrandingIntegrationTests : IntegrationTestBase
{
    private readonly ITenantBrandingAppService _marca;
    private readonly ICurrentTenant _inquilinoActual;

    private static readonly Guid InquilinoA = Guid.Parse("aaaaaaaa-1111-1111-1111-11111111111a");
    private static readonly Guid InquilinoB = Guid.Parse("bbbbbbbb-1111-1111-1111-11111111111b");

    public TenantBrandingIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _marca = GetRequiredService<ITenantBrandingAppService>();
        _inquilinoActual = GetRequiredService<ICurrentTenant>();
    }

    /// <summary>Ejecuta algo COMO ese inquilino (null = host).</summary>
    private Task<T> ComoAsync<T>(Guid? inquilino, Func<Task<T>> accion)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            using (_inquilinoActual.Change(inquilino))
            {
                return await accion();
            }
        });
    }

    /// <summary>PNG real de 1x1 transparente.</summary>
    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static RemoteStreamContent Archivo(
        byte[] bytes, string nombre = "logo.png", string tipo = "image/png")
    {
        return new RemoteStreamContent(new MemoryStream(bytes), nombre, tipo);
    }

    // -- Aislamiento: lo que importa -------------------------------------------------

    [Fact]
    public async Task El_nombre_de_un_inquilino_NO_lo_ve_otro()
    {
        await ComoAsync(InquilinoA, () => _marca.UpdateAsync(new UpdateBrandingDto { AppName = "Acme" }));

        var b = await ComoAsync(InquilinoB, () => _marca.GetAsync());

        // Si esto fallara, B estaría viendo "Acme" en su barra lateral y en su login.
        b.AppName.ShouldBeNull();
        b.EffectiveAppName.ShouldNotBe("Acme");
    }

    [Fact]
    public async Task El_color_de_un_inquilino_NO_lo_ve_otro()
    {
        await ComoAsync(InquilinoA, () => _marca.UpdateAsync(new UpdateBrandingDto { AccentColor = "#123456" }));

        var b = await ComoAsync(InquilinoB, () => _marca.GetAsync());

        b.AccentColor.ShouldBe(BrandingConsts.DefaultAccentColor);
    }

    [Fact]
    public async Task El_logo_de_un_inquilino_NO_lo_ve_otro()
    {
        await ComoAsync(InquilinoA, () => _marca.UploadLogoAsync(Archivo(Png())));

        var b = await ComoAsync(InquilinoB, () => _marca.GetAsync());

        // Los contenedores de BLOB de ABP son multi-tenant por defecto, así que la misma
        // clave ("logo") apunta a archivos distintos. Es justo el automatismo que este test
        // fija: declararlo IsMultiTenant = false volvería el logo común a toda la
        // instalación sin ningún otro síntoma.
        b.HasLogo.ShouldBeFalse();
        b.LogoUrl.ShouldBeNull();
    }

    // -- Herencia desde el host ------------------------------------------------------

    [Fact]
    public async Task Un_inquilino_sin_nombre_propio_hereda_el_de_la_instalacion()
    {
        await ComoAsync(null, () => _marca.UpdateAsync(new UpdateBrandingDto { AppName = "Plataforma" }));

        var a = await ComoAsync(InquilinoA, () => _marca.GetAsync());

        a.AppName.ShouldBeNull();            // no ha puesto ninguno...
        a.EffectiveAppName.ShouldBe("Plataforma");  // ...pero ve el del host
        a.InheritedAppName.ShouldBe("Plataforma");
    }

    [Fact]
    public async Task El_nombre_propio_gana_al_de_la_instalacion()
    {
        await ComoAsync(null, () => _marca.UpdateAsync(new UpdateBrandingDto { AppName = "Plataforma" }));
        await ComoAsync(InquilinoA, () => _marca.UpdateAsync(new UpdateBrandingDto { AppName = "Acme" }));

        var a = await ComoAsync(InquilinoA, () => _marca.GetAsync());

        a.EffectiveAppName.ShouldBe("Acme");
        // Y sigue sabiendo a qué volvería si lo borrase, que es lo que la pantalla usa de
        // marcador de posición.
        a.InheritedAppName.ShouldBe("Plataforma");
    }

    [Fact]
    public async Task Vaciar_el_nombre_vuelve_a_heredar_en_vez_de_dejarlo_en_blanco()
    {
        await ComoAsync(null, () => _marca.UpdateAsync(new UpdateBrandingDto { AppName = "Plataforma" }));
        await ComoAsync(InquilinoA, () => _marca.UpdateAsync(new UpdateBrandingDto { AppName = "Acme" }));

        var a = await ComoAsync(InquilinoA, () => _marca.UpdateAsync(new UpdateBrandingDto { AppName = "  " }));

        // Guardar la cadena vacía dejaría la barra lateral sin nombre. Tiene que BORRAR la
        // fila del ajuste, que es lo que hace que el proveedor caiga al valor global.
        a.AppName.ShouldBeNull();
        a.EffectiveAppName.ShouldBe("Plataforma");
    }

    [Fact]
    public async Task Sin_nada_configurado_el_nombre_es_el_de_serie()
    {
        var host = await ComoAsync(null, () => _marca.GetAsync());

        host.AppName.ShouldBeNull();
        host.IsHost.ShouldBeTrue();
        // El texto localizado del recurso, que es lo que ya enseñaba el proveedor de marca
        // antes de que existiera esta pantalla: una instalación que no la abra nunca se
        // sigue viendo igual.
        host.EffectiveAppName.ShouldBe("Auxiliar contable");
    }

    // -- Color: es un control de seguridad, no un capricho ----------------------------

    [Theory]
    [InlineData("rojo")]                       // nombre CSS
    [InlineData("#abc")]                       // forma corta
    [InlineData("rgb(1,2,3)")]                 // función
    [InlineData("#12345g")]                    // dígito no hexadecimal
    [InlineData("#123456; } body { display:none")] // inyección de CSS
    [InlineData("#12345")]                     // demasiado corto
    [InlineData("#1234567")]                   // demasiado largo
    public async Task Un_color_que_no_es_RRGGBB_se_rechaza(string malo)
    {
        // No es cosmética: este valor acaba dentro de un <style> en la pantalla de login.
        // Ver ColorDeMarca.
        await Should.ThrowAsync<UserFriendlyException>(
            ComoAsync(InquilinoA, () => _marca.UpdateAsync(new UpdateBrandingDto { AccentColor = malo })));
    }

    [Fact]
    public async Task Un_color_valido_se_guarda_en_minusculas()
    {
        var a = await ComoAsync(
            InquilinoA, () => _marca.UpdateAsync(new UpdateBrandingDto { AccentColor = "  #1B6AC9  " }));

        // Normalizado: el selector del navegador emite minúsculas, y así dos formas de
        // escribir el mismo color no se ven como cambios distintos.
        a.AccentColor.ShouldBe("#1b6ac9");
    }

    [Fact]
    public async Task Vaciar_el_color_vuelve_al_de_serie()
    {
        await ComoAsync(InquilinoA, () => _marca.UpdateAsync(new UpdateBrandingDto { AccentColor = "#1b6ac9" }));

        var a = await ComoAsync(InquilinoA, () => _marca.UpdateAsync(new UpdateBrandingDto { AccentColor = null }));

        a.AccentColor.ShouldBe(BrandingConsts.DefaultAccentColor);
    }

    // -- Logo ------------------------------------------------------------------------

    [Fact]
    public async Task Sube_el_logo_y_aparece_con_su_URL()
    {
        var bytes = Png();

        var a = await ComoAsync(InquilinoA, () => _marca.UploadLogoAsync(Archivo(bytes)));

        a.HasLogo.ShouldBeTrue();
        a.LogoSizeBytes.ShouldBe(bytes.Length);
        a.LogoVersion.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// ⚠️ La forma EXACTA de la URL está escrita dos veces: aquí en C# (para la pantalla de
    /// login, que es HTML del servidor) y en <c>logo-url.ts</c> (para el SPA, que no puede
    /// llamar a este servicio porque exige permiso). Este test es lo único que impide que
    /// se separen — si cambia una, aquí se ve.
    /// </summary>
    [Fact]
    public async Task La_URL_del_logo_lleva_el_inquilino_y_la_version()
    {
        var a = await ComoAsync(InquilinoA, () => _marca.UploadLogoAsync(Archivo(Png())));

        a.LogoUrl.ShouldBe($"/api/branding/logo?__tenant={InquilinoA:D}&v={a.LogoVersion}");
    }

    [Fact]
    public async Task La_URL_del_logo_del_host_va_SIN_inquilino()
    {
        var host = await ComoAsync(null, () => _marca.UploadLogoAsync(Archivo(Png())));

        host.LogoUrl.ShouldBe($"/api/branding/logo?v={host.LogoVersion}");
    }

    [Fact]
    public async Task Cambiar_el_logo_cambia_la_version()
    {
        var primera = await ComoAsync(InquilinoA, () => _marca.UploadLogoAsync(Archivo(Png())));

        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03];
        var segunda = await ComoAsync(
            InquilinoA, () => _marca.UploadLogoAsync(Archivo(jpeg, "otro.jpg", "image/jpeg")));

        // Si la versión no cambiara, la URL sería la misma y el navegador seguiría
        // enseñando el logo anterior desde su caché — que está marcada como inmutable.
        segunda.LogoVersion.ShouldNotBe(primera.LogoVersion);
        segunda.LogoSizeBytes.ShouldBe(jpeg.Length);
    }

    [Fact]
    public async Task Quitar_el_logo_lo_deja_sin_version_ni_URL()
    {
        await ComoAsync(InquilinoA, () => _marca.UploadLogoAsync(Archivo(Png())));

        var a = await ComoAsync(InquilinoA, () => _marca.DeleteLogoAsync());

        a.HasLogo.ShouldBeFalse();
        a.LogoUrl.ShouldBeNull();
        a.LogoSizeBytes.ShouldBe(0);
    }

    [Fact]
    public async Task Quitar_un_logo_que_no_existe_no_es_un_error()
    {
        // Es lo que pidió quien pulsó el botón, y además puede pasar con dos pestañas
        // abiertas.
        var a = await ComoAsync(InquilinoA, () => _marca.DeleteLogoAsync());

        a.HasLogo.ShouldBeFalse();
    }

    [Fact]
    public async Task Un_archivo_que_no_es_imagen_se_rechaza()
    {
        // El Content-Type dice image/png y MIENTE: es HTML con un script. Se sirve desde un
        // endpoint anónimo, así que aceptarlo sería XSS almacenado servido a cualquiera que
        // abra la pantalla de login.
        var html = System.Text.Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");

        await Should.ThrowAsync<UserFriendlyException>(
            ComoAsync(InquilinoA, () => _marca.UploadLogoAsync(Archivo(html, "logo.png", "image/png"))));
    }

    [Fact]
    public async Task Un_SVG_se_rechaza_aunque_sea_una_imagen()
    {
        // Un SVG es XML y admite <script>. Ninguna firma de bytes distingue uno inofensivo
        // de uno que no lo sea, así que queda fuera de la lista a propósito.
        var svg = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

        await Should.ThrowAsync<UserFriendlyException>(
            ComoAsync(InquilinoA, () => _marca.UploadLogoAsync(Archivo(svg, "logo.svg", "image/svg+xml"))));
    }

    [Fact]
    public async Task Un_logo_demasiado_grande_se_rechaza_aunque_MIENTA_sobre_su_tamano()
    {
        var gordo = new byte[BrandingConsts.MaxLogoBytes + 1];
        Png().CopyTo(gordo, 0); // firma válida: lo que falla es el tamaño, no el formato

        // El stream NO es buscable, así que ContentLength llega nulo — igual que con
        // Transfer-Encoding: chunked. Es el camino que prueba el tope REAL, el que se aplica
        // mientras se lee.
        var contenido = new RemoteStreamContent(
            new StreamSinLongitud(gordo), "logo.png", "image/png");

        await Should.ThrowAsync<UserFriendlyException>(
            ComoAsync(InquilinoA, () => _marca.UploadLogoAsync(contenido)));
    }

    [Fact]
    public async Task El_nombre_no_puede_pasar_del_maximo()
    {
        var largo = new string('x', BrandingConsts.MaxAppNameLength + 1);

        await Should.ThrowAsync<UserFriendlyException>(
            ComoAsync(InquilinoA, () => _marca.UpdateAsync(new UpdateBrandingDto { AppName = largo })));
    }

    /// <summary>
    /// Stream que se lee bien pero no dice cuánto ocupa. Reproduce lo que llega por HTTP
    /// cuando el cliente no manda <c>Content-Length</c>.
    /// </summary>
    private sealed class StreamSinLongitud(byte[] datos) : Stream
    {
        private readonly MemoryStream _interno = new(datos);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _interno.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
