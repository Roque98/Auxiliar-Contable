using System.Collections.Generic;
using System.Linq;

namespace AuxiliarContable.PerfilesFiscales;

/// <summary>De dónde salió un dato del perfil. Se enseña siempre junto al dato (visión: §Principios de IA).</summary>
public enum OrigenDelDato
{
    /// <summary>Leído de la Constancia de Situación Fiscal.</summary>
    ExtraidoDeCsf = 1,

    /// <summary>Lo dijo el cliente (en la entrevista o corrigiéndolo a mano).</summary>
    RespondidoPorElCliente = 2,

    /// <summary>Lo dedujo el asistente o el sistema. Nunca se da por bueno sin confirmar.</summary>
    Inferido = 3,

    /// <summary>Lo puso o corrigió alguien del despacho.</summary>
    CapturadoPorElDespacho = 4,
}

public enum TipoDeCampo
{
    Texto = 0,
    Fecha = 1,
    SiNo = 2,

    /// <summary>Lista JSON de objetos (regímenes, obligaciones, actividades) o de textos (bancos).</summary>
    Lista = 3,
}

public enum GrupoDeCampos
{
    Identificacion = 0,
    Fiscal = 1,
    Operacion = 2,
}

public sealed record CampoDelPerfil(string Clave, string Etiqueta, TipoDeCampo Tipo, GrupoDeCampos Grupo, bool VieneEnLaCsf);

/// <summary>
/// El catálogo de lo que se guarda del cliente. Las claves son ESTABLES (van en la base y en
/// las herramientas del asistente): una etiqueta se puede cambiar, una clave no.
/// </summary>
public static class CamposDelPerfil
{
    public const string TipoDePersona = "tipo_persona";
    public const string Rfc = "rfc";
    public const string Nombre = "nombre";
    public const string Curp = "curp";
    public const string CodigoPostal = "codigo_postal";
    public const string DomicilioFiscal = "domicilio_fiscal";
    public const string FechaInicioDeOperaciones = "fecha_inicio_operaciones";
    public const string EstatusEnElPadron = "estatus_padron";
    public const string Regimenes = "regimenes";
    public const string Obligaciones = "obligaciones";
    public const string ActividadesEconomicas = "actividades_economicas";
    public const string FechaDeEmisionDeLaCsf = "fecha_emision_csf";
    public const string Giro = "giro";
    public const string EmiteFacturas = "emite_facturas";
    public const string Bancos = "bancos";
    public const string TieneTrabajadores = "tiene_trabajadores";
    public const string ManejoDeGastos = "manejo_de_gastos";

    public const int MaxValorLength = 8000;

    public static readonly IReadOnlyList<CampoDelPerfil> Todos =
    [
        new(TipoDePersona, "Tipo de persona", TipoDeCampo.Texto, GrupoDeCampos.Identificacion, true),
        new(Rfc, "RFC", TipoDeCampo.Texto, GrupoDeCampos.Identificacion, true),
        new(Nombre, "Nombre o razón social", TipoDeCampo.Texto, GrupoDeCampos.Identificacion, true),
        new(Curp, "CURP", TipoDeCampo.Texto, GrupoDeCampos.Identificacion, true),
        new(CodigoPostal, "Código postal fiscal", TipoDeCampo.Texto, GrupoDeCampos.Identificacion, true),
        new(DomicilioFiscal, "Domicilio fiscal", TipoDeCampo.Texto, GrupoDeCampos.Identificacion, true),
        new(FechaInicioDeOperaciones, "Inicio de operaciones", TipoDeCampo.Fecha, GrupoDeCampos.Fiscal, true),
        new(EstatusEnElPadron, "Estatus en el padrón", TipoDeCampo.Texto, GrupoDeCampos.Fiscal, true),
        new(Regimenes, "Regímenes fiscales", TipoDeCampo.Lista, GrupoDeCampos.Fiscal, true),
        new(Obligaciones, "Obligaciones", TipoDeCampo.Lista, GrupoDeCampos.Fiscal, true),
        new(ActividadesEconomicas, "Actividades económicas", TipoDeCampo.Lista, GrupoDeCampos.Fiscal, true),
        new(FechaDeEmisionDeLaCsf, "Fecha de la constancia", TipoDeCampo.Fecha, GrupoDeCampos.Fiscal, true),
        new(Giro, "Giro o a qué se dedica", TipoDeCampo.Texto, GrupoDeCampos.Operacion, false),
        new(EmiteFacturas, "Emite facturas", TipoDeCampo.SiNo, GrupoDeCampos.Operacion, false),
        new(Bancos, "Bancos que usa", TipoDeCampo.Lista, GrupoDeCampos.Operacion, false),
        new(TieneTrabajadores, "Tiene trabajadores", TipoDeCampo.SiNo, GrupoDeCampos.Operacion, false),
        new(ManejoDeGastos, "Cómo maneja sus gastos", TipoDeCampo.Texto, GrupoDeCampos.Operacion, false),
    ];

    public static CampoDelPerfil? Buscar(string clave) => Todos.FirstOrDefault(c => c.Clave == clave);
}
