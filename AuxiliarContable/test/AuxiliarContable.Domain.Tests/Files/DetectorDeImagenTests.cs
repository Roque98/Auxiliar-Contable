using System;
using AuxiliarContable.Files;
using Shouldly;
using Xunit;

namespace AuxiliarContable.Files;

/// <summary>
/// El detector es lo único que separa "el usuario subió una foto" de "el usuario subió lo
/// que le dio la gana y dijo que era una foto", así que se prueba aparte y sin base de datos.
/// </summary>
public class DetectorDeImagenTests
{
    /// <summary>PNG real de 1x1 transparente.</summary>
    private static byte[] PngDeVerdad() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    [Fact]
    public void Reconoce_un_PNG()
    {
        DetectorDeImagen.Detectar(PngDeVerdad()).ShouldBe(FormatoDeImagen.Png);
    }

    [Fact]
    public void Reconoce_un_JPEG()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

        DetectorDeImagen.Detectar(jpeg).ShouldBe(FormatoDeImagen.Jpeg);
    }

    [Fact]
    public void Reconoce_un_WebP()
    {
        // "RIFF" + 4 bytes de tamaño (cualquiera) + "WEBP".
        byte[] webp =
        [
            0x52, 0x49, 0x46, 0x46,
            0x1A, 0x00, 0x00, 0x00,
            0x57, 0x45, 0x42, 0x50
        ];

        DetectorDeImagen.Detectar(webp).ShouldBe(FormatoDeImagen.WebP);
    }

    [Fact]
    public void Un_RIFF_que_no_es_WebP_no_cuela()
    {
        // Un WAV también empieza por "RIFF"; lo que lo distingue está en el byte 8.
        byte[] wav =
        [
            0x52, 0x49, 0x46, 0x46,
            0x1A, 0x00, 0x00, 0x00,
            0x57, 0x41, 0x56, 0x45 // "WAVE"
        ];

        DetectorDeImagen.Detectar(wav).ShouldBe(FormatoDeImagen.Desconocido);
    }

    [Fact]
    public void Un_SVG_con_script_NO_se_acepta_aunque_sea_una_imagen()
    {
        // El caso que motiva todo esto: un SVG es una imagen legítima para un navegador y
        // además puede ejecutar código. Se queda fuera por diseño.
        var svg = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

        DetectorDeImagen.Detectar(svg).ShouldBe(FormatoDeImagen.Desconocido);
    }

    [Fact]
    public void Un_archivo_mas_corto_que_la_firma_no_revienta()
    {
        // Un stream vacío o de dos bytes tiene que dar "desconocido", no una excepción de
        // índice: llega desde fuera y por tanto es un caso normal, no un imposible.
        DetectorDeImagen.Detectar([]).ShouldBe(FormatoDeImagen.Desconocido);
        DetectorDeImagen.Detectar([0xFF, 0xD8]).ShouldBe(FormatoDeImagen.Desconocido);
        DetectorDeImagen.Detectar([0x52, 0x49, 0x46, 0x46]).ShouldBe(FormatoDeImagen.Desconocido);
    }

    [Fact]
    public void El_ContentType_sale_del_formato_detectado()
    {
        DetectorDeImagen.ContentType(FormatoDeImagen.Png).ShouldBe("image/png");
        DetectorDeImagen.ContentType(FormatoDeImagen.Jpeg).ShouldBe("image/jpeg");
        DetectorDeImagen.ContentType(FormatoDeImagen.WebP).ShouldBe("image/webp");
        DetectorDeImagen.ContentType(FormatoDeImagen.Desconocido).ShouldBeNull();
    }
}
