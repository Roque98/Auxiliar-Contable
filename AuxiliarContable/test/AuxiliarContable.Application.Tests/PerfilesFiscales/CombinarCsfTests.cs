using System;
using System.Collections.Generic;
using System.Linq;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Cómo se juntan lo que lee el código y lo que lee el modelo de una CSF: en los
/// identificadores manda el código, y lo del modelo se valida, no se cree.
/// </summary>
public class CombinarCsfTests
{
    private static readonly DateTime Hoy = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    private static IdentificadoresDeCsf Ids(string? rfc = "GODE561231GR8", DateOnly? emision = null) =>
        new(true, rfc, "GODE561231HDFRRN09", "06600", emision ?? new DateOnly(2026, 10, 15), rfc?.Length == 12 ? "moral" : "fisica");

    private static Dictionary<string, string> Datos(List<(string Campo, string Valor)> lista) =>
        lista.ToDictionary(d => d.Campo, d => d.Valor);

    [Fact]
    public void En_el_RFC_manda_el_codigo_y_si_el_modelo_dice_otro_queda_aviso()
    {
        var avisos = new List<string>();

        var datos = Datos(ProcesadorDeCsf.Combinar(Ids(), new LecturaDelModelo { EsConstancia = true, Rfc = "GODE561231GR9" }, Hoy, avisos));

        datos[CamposDelPerfil.Rfc].ShouldBe("GODE561231GR8");
        datos[CamposDelPerfil.TipoDePersona].ShouldBe("fisica");
        avisos.ShouldContain(a => a.Contains("RFC se leyó de dos formas"));
    }

    [Fact]
    public void Una_constancia_de_mas_de_3_meses_se_senala()
    {
        var avisos = new List<string>();

        ProcesadorDeCsf.Combinar(Ids(emision: new DateOnly(2026, 6, 1)), null, Hoy, avisos);

        avisos.ShouldContain(a => a.Contains("más de 3 meses"));
    }

    [Fact]
    public void Un_regimen_fuera_del_catalogo_no_se_guarda_y_se_avisa()
    {
        var avisos = new List<string>();
        var modelo = new LecturaDelModelo
        {
            EsConstancia = true,
            Regimenes =
            [
                new RegimenLeido { Clave = "626", Descripcion = "Simplificado de Confianza", FechaInicio = "2022-01-01" },
                new RegimenLeido { Clave = "999", Descripcion = "Régimen inventado" },
            ],
        };

        var datos = Datos(ProcesadorDeCsf.Combinar(Ids(), modelo, Hoy, avisos));

        datos[CamposDelPerfil.Regimenes].ShouldContain("\"626\"");
        datos[CamposDelPerfil.Regimenes].ShouldContain("Régimen Simplificado de Confianza"); // la descripción del catálogo, no la del modelo
        datos[CamposDelPerfil.Regimenes].ShouldNotContain("999");
        avisos.ShouldContain(a => a.Contains("Régimen inventado"));
    }

    [Fact]
    public void La_CURP_solo_se_guarda_para_personas_fisicas()
    {
        var datos = Datos(ProcesadorDeCsf.Combinar(Ids(rfc: "SAT970701NN3"), null, Hoy, []));

        datos[CamposDelPerfil.TipoDePersona].ShouldBe("moral");
        datos.ShouldNotContainKey(CamposDelPerfil.Curp);
    }

    [Fact]
    public void Sin_modelo_solo_quedan_los_identificadores()
    {
        var datos = Datos(ProcesadorDeCsf.Combinar(Ids(), null, Hoy, []));

        datos.Keys.ShouldBe(
            [CamposDelPerfil.Rfc, CamposDelPerfil.TipoDePersona, CamposDelPerfil.Curp, CamposDelPerfil.CodigoPostal, CamposDelPerfil.FechaDeEmisionDeLaCsf],
            ignoreOrder: true);
    }

    [Fact]
    public void Fechas_que_no_son_AAAA_MM_DD_se_descartan_y_porcentajes_que_no_suman_100_se_avisan()
    {
        var avisos = new List<string>();
        var modelo = new LecturaDelModelo
        {
            EsConstancia = true,
            FechaInicioDeOperaciones = "01/01/2015",
            Actividades = [new ActividadLeida { Descripcion = "Consultoría", Porcentaje = 60 }, new ActividadLeida { Descripcion = "Venta", Porcentaje = 30 }],
        };

        var datos = Datos(ProcesadorDeCsf.Combinar(Ids(), modelo, Hoy, avisos));

        datos.ShouldNotContainKey(CamposDelPerfil.FechaInicioDeOperaciones);
        avisos.ShouldContain(a => a.Contains("suman 90"));
    }
}
