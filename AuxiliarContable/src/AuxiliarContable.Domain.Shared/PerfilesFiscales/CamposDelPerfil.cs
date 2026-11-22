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

    /// <summary>Uno de los valores de <see cref="CampoDelPerfil.Opciones"/>.</summary>
    Opcion = 4,
}

public enum GrupoDeCampos
{
    Identificacion = 0,
    Fiscal = 1,
    Operacion = 2,
}

public sealed record CampoDelPerfil(string Clave, string Etiqueta, TipoDeCampo Tipo, GrupoDeCampos Grupo, bool VieneEnLaCsf,
    IReadOnlyList<OpcionDelCampo>? Opciones = null);

/// <summary>Un valor posible de un campo de opción: lo que se guarda y cómo se lee.</summary>
public sealed record OpcionDelCampo(string Valor, string Etiqueta);

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

    /// <summary>Disparadores de las leyes estatales (catálogo del motor): con local abierto tocan protección civil, residuos,
    /// tabaco…; con inmuebles, catastro y condominio; con vehículos, tránsito y movilidad; si vende al gobierno, obra pública,
    /// adquisiciones y responsabilidades.</summary>
    public const string TieneEstablecimiento = "tiene_establecimiento";

    public const string TieneInmuebles = "tiene_inmuebles";
    public const string TieneVehiculos = "tiene_vehiculos";
    public const string ContrataConGobierno = "contrata_gobierno";

    /// <summary>Decide a qué cuentas van las compras (inventario, materia prima, costo del servicio o gasto).</summary>
    public const string TipoDeEmpresa = "tipo_empresa";

    /// <summary>Cómo acumula el costo un fabricante o constructora (producción en proceso por orden, por proceso…).</summary>
    public const string SistemaDeCostos = "sistema_costos";

    /// <summary>Perpetuo: el costo se registra en cada venta. Analítico: por compras e inventario final.</summary>
    public const string SistemaDeInventarios = "sistema_inventarios";

    /// <summary>
    /// Personas morales del régimen general: el coeficiente de utilidad de su última declaración anual (art. 14 LISR),
    /// con el que se calcula el pago provisional. Entre 0 y 1, p. ej. 0.1834.
    /// </summary>
    public const string CoeficienteDeUtilidad = "coeficiente_utilidad";

    /// <summary>
    /// Plataformas tecnológicas (régimen 625): si optó por que las retenciones de la plataforma sean pago definitivo
    /// (art. 113-B LISR, 18-M LIVA) o hace pagos provisionales. Cambia el IVA del mes y la anual; hay que preguntarlo.
    /// </summary>
    public const string PagoDePlataformas = "plataformas_pago";

    /// <summary>Qué hace por la plataforma: decide la tasa del art. 113-A LISR (25-VI LIF).</summary>
    public const string ActividadEnPlataformas = "plataformas_actividad";

    /// <summary>Patrón: su registro patronal ante el IMSS (11 caracteres).</summary>
    public const string RegistroPatronal = "registro_patronal";

    /// <summary>Patrón: prima de riesgo de trabajo vigente, en % (de 0.5 a 15; art. 72 LSS).</summary>
    public const string PrimaDeRiesgo = "prima_riesgo";

    /// <summary>Patrón: guía de la subdelegación del IMSS (5 dígitos), para los archivos del IDSE.</summary>
    public const string GuiaImss = "guia_imss";

    /// <summary>Patrón: zona del salario mínimo (general o frontera norte).</summary>
    public const string ZonaSalarioMinimo = "zona_salario_minimo";

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
        new(TieneEstablecimiento, "Tiene local, oficina o bodega abierta", TipoDeCampo.SiNo, GrupoDeCampos.Operacion, false),
        new(TieneInmuebles, "Es dueño de inmuebles", TipoDeCampo.SiNo, GrupoDeCampos.Operacion, false),
        new(TieneVehiculos, "Tiene vehículos o presta servicios de transporte", TipoDeCampo.SiNo, GrupoDeCampos.Operacion, false),
        new(ContrataConGobierno, "Vende o presta servicios al gobierno", TipoDeCampo.SiNo, GrupoDeCampos.Operacion, false),
        new(TipoDeEmpresa, "Tipo de empresa", TipoDeCampo.Opcion, GrupoDeCampos.Operacion, false,
        [
            new("comercial", "Comercial: compra y vende sin transformar"),
            new("servicios", "Servicios"),
            new("fabricante", "Fabricante: transforma materia prima"),
            new("constructora", "Constructora"),
            new("agropecuaria", "Agropecuaria"),
        ]),
        new(SistemaDeCostos, "Sistema de costos", TipoDeCampo.Opcion, GrupoDeCampos.Operacion, false,
        [
            new("ninguno", "No lleva costeo"),
            new("ordenes", "Por órdenes de producción (u obra)"),
            new("procesos", "Por procesos"),
            new("estandar", "Estándar"),
        ]),
        new(SistemaDeInventarios, "Sistema de inventarios", TipoDeCampo.Opcion, GrupoDeCampos.Operacion, false,
        [
            new("perpetuo", "Perpetuo: registra el costo en cada venta"),
            new("analitico", "Analítico: por compras e inventario final"),
        ]),
        new(CoeficienteDeUtilidad, "Coeficiente de utilidad (personas morales)", TipoDeCampo.Texto, GrupoDeCampos.Fiscal, false),
        new(RegistroPatronal, "Registro patronal (IMSS)", TipoDeCampo.Texto, GrupoDeCampos.Fiscal, false),
        new(PrimaDeRiesgo, "Prima de riesgo de trabajo (%)", TipoDeCampo.Texto, GrupoDeCampos.Fiscal, false),
        new(GuiaImss, "Guía de la subdelegación del IMSS", TipoDeCampo.Texto, GrupoDeCampos.Fiscal, false),
        new(ZonaSalarioMinimo, "Zona del salario mínimo", TipoDeCampo.Opcion, GrupoDeCampos.Fiscal, false,
        [
            new("general", "Resto del país"),
            new("frontera", "Zona libre de la frontera norte"),
        ]),
        new(PagoDePlataformas, "Plataformas tecnológicas (625): ¿pago definitivo o provisional?", TipoDeCampo.Opcion, GrupoDeCampos.Fiscal, false,
        [
            new("definitivo", "Pago definitivo: las retenciones de la plataforma son el pago (art. 113-B)"),
            new("provisional", "Pagos provisionales: en la anual acumula todo y deduce"),
        ]),
        new(ActividadEnPlataformas, "Plataformas tecnológicas (625): ¿qué hace por la plataforma?", TipoDeCampo.Opcion, GrupoDeCampos.Fiscal, false,
        [
            new("transporte", "Transporte de pasajeros o entrega de bienes (2.1 %)"),
            new("hospedaje", "Hospedaje (4 %)"),
            new("bienes_servicios", "Venta de bienes u otros servicios (2.5 %)"),
        ]),
    ];

    public static CampoDelPerfil? Buscar(string clave) => Todos.FirstOrDefault(c => c.Clave == clave);
}
