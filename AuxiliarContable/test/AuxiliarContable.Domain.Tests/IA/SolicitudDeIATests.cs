using System;
using AuxiliarContable.IA;
using Shouldly;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Lo que se rechaza ANTES de llamar al modelo: un envío que la API va a rechazar cuesta tiempo
/// y no le explica nada a quien subió el archivo.
/// </summary>
public class SolicitudDeIATests
{
    private static readonly EsquemaDeSalida Esquema =
        new("leer", "Datos", """{"type":"object","properties":{}}""");

    [Theory]
    [InlineData("image/heic")]
    [InlineData("image/tiff")]
    [InlineData("application/zip")]
    public void Una_imagen_que_el_modelo_no_lee_se_rechaza(string tipo)
    {
        Should.Throw<ArgumentException>(() => ParteDeContenido.Imagen([1, 2, 3], tipo))
            .Message.ShouldContain("image/jpeg");
    }

    [Fact]
    public void Archivo_elige_pdf_o_imagen_por_el_tipo()
    {
        ParteDeContenido.Archivo([1], "application/PDF").ShouldBeOfType<PdfDeContenido>();
        ParteDeContenido.Archivo([1], "image/png").ShouldBeOfType<ImagenDeContenido>();
    }

    [Fact]
    public void Una_imagen_de_mas_de_5_MB_se_rechaza()
    {
        Should.Throw<ArgumentException>(() => ParteDeContenido.Imagen(new byte[IAConsts.MaxBytesPorImagen + 1], "image/png"));
        ParteDeContenido.Imagen(new byte[IAConsts.MaxBytesPorImagen], "image/png").ShouldNotBeNull();
    }

    [Fact]
    public void La_peticion_entera_cuenta_en_base64()
    {
        // 25 MB en bruto son ~33 MB en base64: no caben aunque el archivo «pese» menos de 32.
        var pdf = ParteDeContenido.Pdf(new byte[25 * 1024 * 1024]);

        Should.Throw<ArgumentException>(() => new SolicitudDeIA("p", "i", Esquema, [pdf]))
            .Message.ShouldContain("32 MB");
    }

    [Fact]
    public void Una_solicitud_sin_contenido_se_rechaza()
    {
        Should.Throw<ArgumentException>(() => new SolicitudDeIA("p", "i", Esquema, []));
    }

    [Theory]
    [InlineData("con espacios")]
    [InlineData("acentuación")]
    [InlineData("")]
    public void El_nombre_del_esquema_tiene_que_valer_como_nombre_de_herramienta(string nombre)
    {
        Should.Throw<ArgumentException>(() => new EsquemaDeSalida(nombre, "d", """{"type":"object"}"""));
    }

    [Theory]
    [InlineData("""{"type":"array"}""")]
    [InlineData("""{"properties":{}}""")]
    [InlineData("[]")]
    public void El_esquema_tiene_que_ser_un_objeto(string esquema)
    {
        Should.Throw<ArgumentException>(() => new EsquemaDeSalida("leer", "d", esquema));
    }

    [Fact]
    public void Un_esquema_que_no_es_json_se_rechaza()
    {
        Should.Throw<ArgumentException>(() => new EsquemaDeSalida("leer", "d", "{type: object"));
    }

    [Fact]
    public void Sin_host_el_modelo_no_esta_configurado_y_lanza()
    {
        var modelo = new ModeloDeIASinConfigurar();

        modelo.EstaConfigurado.ShouldBeFalse();
        Should.Throw<ErrorDeIAException>(() => modelo.ExtraerAsync<object>(new SolicitudDeIA("p", "i", Esquema, [ParteDeContenido.Texto("x")])))
            .Motivo.ShouldBe(MotivoDeErrorDeIA.NoConfigurado);
    }
}
