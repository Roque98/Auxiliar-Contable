using System;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Xunit;

namespace AuxiliarContable;

/// <summary>Lo que se lee y valida con código, sin IA: formatos del SAT y la parte fija de la CSF.</summary>
public class SatYCsfTests
{
    [Theory]
    [InlineData("SAT970701NN3")] // el del propio SAT
    [InlineData("CFE370814QI0")]
    [InlineData("TME840315KT6")]
    [InlineData("XAXX010101000")] // genérico
    public void El_digito_verificador_de_RFC_reales_cuadra(string rfc)
    {
        Sat.EsRfc(rfc).ShouldBeTrue();
        Sat.DigitoVerificadorCuadra(rfc).ShouldBeTrue();
    }

    [Fact]
    public void Un_RFC_con_un_caracter_cambiado_no_cuadra()
    {
        Sat.DigitoVerificadorCuadra("SAT970701NN4").ShouldBeFalse();
        Sat.DigitoVerificadorCuadra("SAT970701NM3").ShouldBeFalse();
    }

    [Theory]
    [InlineData("SAT97070NN3")]
    [InlineData("sat970701nn3")]
    [InlineData("SAT9707011NN3X")]
    [InlineData("")]
    public void No_es_RFC(string texto) => Sat.EsRfc(texto).ShouldBeFalse();

    [Fact]
    public void La_CURP_se_valida_por_formato()
    {
        Sat.EsCurp("GODE561231HDFRRN09").ShouldBeTrue();
        Sat.EsCurp("GODE561231HXXRRN09").ShouldBeFalse(); // entidad inexistente
        Sat.EsCurp("GODE561331HDFRRN09").ShouldBeFalse(); // mes 13
    }

    [Theory]
    [InlineData("A 15 DE OCTUBRE DE 2026", 2026, 10, 15)]
    [InlineData("CIUDAD DE MÉXICO A 3 DE ENERO DE 2025", 2025, 1, 3)]
    [InlineData("a 1 de septiembre de 2024", 2024, 9, 1)]
    public void Lee_la_fecha_como_la_escribe_el_SAT(string texto, int anio, int mes, int dia) =>
        Sat.FechaLarga(texto).ShouldBe(new DateOnly(anio, mes, dia));

    [Fact]
    public void Una_fecha_imposible_no_se_inventa() => Sat.FechaLarga("A 31 DE FEBRERO DE 2026").ShouldBeNull();

    [Fact]
    public void El_catalogo_de_regimenes_tiene_los_19_del_SAT()
    {
        Sat.Regimenes.Count.ShouldBe(19);
        Sat.Regimenes["626"].ShouldBe("Régimen Simplificado de Confianza");
    }

    private const string TextoDeCsf = """
        CÉDULA DE IDENTIFICACIÓN FISCAL CONSTANCIA DE SITUACIÓN FISCAL
        RFC: GODE561231GR8 idCIF: 12345678901
        Lugar y Fecha de Emisión CUAUHTEMOC , CIUDAD DE MEXICO A 15 DE OCTUBRE DE 2026
        Datos de Identificación del Contribuyente: RFC: GODE561231GR8 CURP: GODE561231HDFRRN09
        Nombre (s): EDUARDO Primer Apellido: GOMEZ Segundo Apellido: DIAZ
        Datos del domicilio registrado Código Postal:06600 Tipo de Vialidad: CALLE
        """;

    [Fact]
    public void De_la_CSF_saca_con_codigo_los_identificadores()
    {
        var ids = LectorDeCsf.Leer(TextoDeCsf);

        ids.PareceCsf.ShouldBeTrue();
        ids.Rfc.ShouldBe("GODE561231GR8");
        ids.Curp.ShouldBe("GODE561231HDFRRN09");
        ids.CodigoPostal.ShouldBe("06600");
        ids.FechaDeEmision.ShouldBe(new DateOnly(2026, 10, 15));
        ids.TipoDePersona.ShouldBe("fisica");
    }

    [Fact]
    public void Un_documento_que_no_es_CSF_no_lo_parece()
    {
        var ids = LectorDeCsf.Leer("ESTADO DE CUENTA BANCO EJEMPLO RFC: BEJ010101AB1 SALDO 1,000.00");

        ids.PareceCsf.ShouldBeFalse();
        ids.TipoDePersona.ShouldBe("moral");
    }
}
