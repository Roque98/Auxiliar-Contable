using System;
using System.IO;
using System.Threading.Tasks;
using IgasConsole.Files;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Xunit;

namespace IgasConsole;

/// <summary>
/// Foto de perfil contra PostgreSQL real: los BLOBs se guardan en la propia base
/// (proveedor Database de ABP), así que el ciclo completo se puede comprobar de verdad.
/// </summary>
public class ProfilePictureIntegrationTests : IntegrationTestBase
{
    private readonly IProfilePictureAppService _fotos;

    public ProfilePictureIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _fotos = GetRequiredService<IProfilePictureAppService>();
    }

    /// <summary>PNG real de 1x1 transparente.</summary>
    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static RemoteStreamContent Archivo(byte[] bytes, string nombre = "foto.png", string tipo = "image/png")
    {
        return new RemoteStreamContent(new MemoryStream(bytes), nombre, tipo);
    }

    [Fact]
    public async Task Sin_foto_lo_dice_en_vez_de_fallar()
    {
        var actual = await _fotos.GetAsync();

        actual.HasPicture.ShouldBeFalse();
        actual.DataUrl.ShouldBeNull();
    }

    [Fact]
    public async Task Sube_y_recupera_la_misma_imagen()
    {
        var bytes = Png();

        await _fotos.UploadAsync(Archivo(bytes));
        var actual = await _fotos.GetAsync();

        actual.HasPicture.ShouldBeTrue();
        actual.SizeBytes.ShouldBe(bytes.Length);
        actual.DataUrl.ShouldBe($"data:image/png;base64,{Convert.ToBase64String(bytes)}");
    }

    [Fact]
    public async Task Subir_otra_vez_reemplaza_la_anterior()
    {
        await _fotos.UploadAsync(Archivo(Png()));

        // Un JPEG cualquiera: al detector le basta la firma.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03];
        await _fotos.UploadAsync(Archivo(jpeg, "otra.jpg", "image/jpeg"));

        var actual = await _fotos.GetAsync();

        // Reemplazo, no error de "ya existe" ni dos fotos conviviendo.
        actual.DataUrl.ShouldStartWith("data:image/jpeg;base64,");
        actual.SizeBytes.ShouldBe(jpeg.Length);
    }

    [Fact]
    public async Task Borrar_deja_al_usuario_sin_foto()
    {
        await _fotos.UploadAsync(Archivo(Png()));

        await _fotos.DeleteAsync();

        (await _fotos.GetAsync()).HasPicture.ShouldBeFalse();
    }

    [Fact]
    public async Task Borrar_cuando_no_hay_foto_no_es_un_error()
    {
        // Borrar algo que ya no está es justo lo que el usuario pidió.
        await Should.NotThrowAsync(() => _fotos.DeleteAsync());
    }

    [Fact]
    public async Task Un_archivo_que_no_es_imagen_se_rechaza_aunque_diga_que_lo_es()
    {
        // Content-Type "image/png" y extensión .png, pero el contenido es HTML con un
        // script. Si se aceptara y luego se devolviera al navegador, sería XSS almacenado.
        var disfrazado = System.Text.Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");

        var ex = await Should.ThrowAsync<UserFriendlyException>(
            () => _fotos.UploadAsync(Archivo(disfrazado, "inocente.png", "image/png")));

        ex.Message.ShouldContain("no es una imagen");
        (await _fotos.GetAsync()).HasPicture.ShouldBeFalse();
    }

    [Fact]
    public async Task Una_imagen_demasiado_grande_se_rechaza_por_ContentLength()
    {
        var enorme = new byte[ProfilePictureConsts.MaxBytes + 1];
        Png().CopyTo(enorme, 0); // firma válida: lo que sobra es el tamaño, no el formato

        await Should.ThrowAsync<UserFriendlyException>(() => _fotos.UploadAsync(Archivo(enorme)));

        (await _fotos.GetAsync()).HasPicture.ShouldBeFalse();
    }

    [Fact]
    public async Task Una_imagen_demasiado_grande_se_rechaza_TAMBIEN_sin_ContentLength()
    {
        // Este es el test que importa de los dos. El ContentLength lo declara quien sube y
        // puede faltar o mentir; aquí se simula justo eso con un stream no buscable, para
        // que la única defensa que quede en pie sea el tope al ir leyendo.
        var enorme = new byte[ProfilePictureConsts.MaxBytes + 1];
        Png().CopyTo(enorme, 0);

        var sinLongitud = new RemoteStreamContent(
            new StreamSinLongitud(enorme), "foto.png", "image/png");

        sinLongitud.ContentLength.ShouldBeNull(); // si esto cambiara, el test dejaría de probar nada

        await Should.ThrowAsync<UserFriendlyException>(() => _fotos.UploadAsync(sinLongitud));

        (await _fotos.GetAsync()).HasPicture.ShouldBeFalse();
    }

    /// <summary>
    /// Stream que no sabe decir cuánto mide, como el cuerpo de una petición HTTP con
    /// <c>Transfer-Encoding: chunked</c>.
    /// </summary>
    private sealed class StreamSinLongitud : Stream
    {
        private readonly MemoryStream _interno;

        public StreamSinLongitud(byte[] datos)
        {
            _interno = new MemoryStream(datos);
        }

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

        public override void Flush() => _interno.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _interno.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
