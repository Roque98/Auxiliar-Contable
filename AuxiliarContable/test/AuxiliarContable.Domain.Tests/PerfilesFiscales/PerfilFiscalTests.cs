using System;
using AuxiliarContable.PerfilesFiscales;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace AuxiliarContable;

/// <summary>Las reglas del perfil: el origen de cada dato y que lo confirmado no se pisa.</summary>
public class PerfilFiscalTests
{
    private static readonly DateTime Ahora = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Csf = Guid.NewGuid();

    private static PerfilFiscal Perfil() => new(Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void Lo_extraido_queda_sin_confirmar_y_apunta_a_su_documento()
    {
        var perfil = Perfil();

        perfil.Proponer(CamposDelPerfil.Rfc, "SAT970701NN3", OrigenDelDato.ExtraidoDeCsf, Csf, Ahora).ShouldBeTrue();

        var dato = perfil.Dato(CamposDelPerfil.Rfc)!;
        dato.Confirmado.ShouldBeFalse();
        dato.Origen.ShouldBe(OrigenDelDato.ExtraidoDeCsf);
        dato.DocumentoId.ShouldBe(Csf);
    }

    [Fact]
    public void Una_extraccion_no_pisa_lo_confirmado_y_lo_avisa()
    {
        var perfil = Perfil();
        perfil.Responder(CamposDelPerfil.Giro, "Consultoría", OrigenDelDato.RespondidoPorElCliente, Ahora);

        perfil.Proponer(CamposDelPerfil.Giro, "Restaurante", OrigenDelDato.Inferido, null, Ahora).ShouldBeFalse();

        perfil.Dato(CamposDelPerfil.Giro)!.Valor.ShouldBe("Consultoría");
    }

    [Fact]
    public void Una_extraccion_igual_a_lo_confirmado_no_es_diferencia()
    {
        var perfil = Perfil();
        perfil.Responder(CamposDelPerfil.CodigoPostal, "06600", OrigenDelDato.CapturadoPorElDespacho, Ahora);

        perfil.Proponer(CamposDelPerfil.CodigoPostal, "06600", OrigenDelDato.ExtraidoDeCsf, Csf, Ahora).ShouldBeTrue();
    }

    [Fact]
    public void Lo_extraido_sin_confirmar_si_se_actualiza()
    {
        var perfil = Perfil();
        perfil.Proponer(CamposDelPerfil.Nombre, "VIEJO", OrigenDelDato.ExtraidoDeCsf, Csf, Ahora);

        perfil.Proponer(CamposDelPerfil.Nombre, "NUEVO", OrigenDelDato.ExtraidoDeCsf, Csf, Ahora).ShouldBeTrue();

        perfil.Dato(CamposDelPerfil.Nombre)!.Valor.ShouldBe("NUEVO");
    }

    [Fact]
    public void Confirmar_da_por_bueno_lo_extraido_sin_cambiar_su_origen()
    {
        var perfil = Perfil();
        perfil.Proponer(CamposDelPerfil.Rfc, "SAT970701NN3", OrigenDelDato.ExtraidoDeCsf, Csf, Ahora);

        perfil.Confirmar(CamposDelPerfil.Rfc, Ahora);

        var dato = perfil.Dato(CamposDelPerfil.Rfc)!;
        dato.Confirmado.ShouldBeTrue();
        dato.Origen.ShouldBe(OrigenDelDato.ExtraidoDeCsf);
    }

    [Fact]
    public void Confirmar_lo_de_la_constancia_no_toca_lo_inferido_ni_lo_ya_confirmado()
    {
        var perfil = Perfil();
        perfil.Proponer(CamposDelPerfil.Rfc, "SAT970701NN3", OrigenDelDato.ExtraidoDeCsf, Csf, Ahora);
        perfil.Proponer(CamposDelPerfil.CodigoPostal, "06600", OrigenDelDato.ExtraidoDeCsf, Csf, Ahora);
        perfil.Proponer(CamposDelPerfil.Giro, "Consultoría", OrigenDelDato.Inferido, null, Ahora);

        perfil.ConfirmarLoDeLaConstancia(Ahora).ShouldBe(2);

        perfil.Dato(CamposDelPerfil.Rfc)!.Confirmado.ShouldBeTrue();
        perfil.Dato(CamposDelPerfil.CodigoPostal)!.Confirmado.ShouldBeTrue();
        perfil.Dato(CamposDelPerfil.Giro)!.Confirmado.ShouldBeFalse();
        perfil.ConfirmarLoDeLaConstancia(Ahora).ShouldBe(0);
    }

    [Fact]
    public void No_se_confirma_lo_que_no_existe_ni_se_guardan_campos_fuera_del_catalogo()
    {
        var perfil = Perfil();

        Should.Throw<BusinessException>(() => perfil.Confirmar(CamposDelPerfil.Rfc, Ahora))
            .Code.ShouldBe("AuxiliarContable:DatoDelPerfilNoExiste");
        Should.Throw<BusinessException>(() => perfil.Responder("color_favorito", "azul", OrigenDelDato.RespondidoPorElCliente, Ahora))
            .Code.ShouldBe("AuxiliarContable:CampoDelPerfilDesconocido");
    }

    [Fact]
    public void Cada_metodo_solo_admite_su_origen()
    {
        var perfil = Perfil();

        Should.Throw<ArgumentException>(() => perfil.Proponer(CamposDelPerfil.Giro, "x", OrigenDelDato.RespondidoPorElCliente, null, Ahora));
        Should.Throw<ArgumentException>(() => perfil.Responder(CamposDelPerfil.Giro, "x", OrigenDelDato.ExtraidoDeCsf, Ahora));
    }

    [Fact]
    public void Los_avisos_de_una_CSF_vieja_no_pisan_los_de_la_nueva()
    {
        var perfil = Perfil();
        var vieja = Guid.NewGuid();
        var nueva = Guid.NewGuid();
        perfil.EmpezarLecturaDeCsf(vieja);
        perfil.EmpezarLecturaDeCsf(nueva);

        perfil.TerminarLecturaDeCsf(vieja, ["de la vieja"]);
        perfil.AvisosDeLaCsf.ShouldBeNull();

        perfil.TerminarLecturaDeCsf(nueva, ["de la nueva"]);
        perfil.AvisosDeLaCsf.ShouldBe("de la nueva");
    }
}
