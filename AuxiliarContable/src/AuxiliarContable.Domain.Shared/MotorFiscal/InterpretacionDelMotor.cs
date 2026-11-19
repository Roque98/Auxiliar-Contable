using System.Collections.Generic;

namespace AuxiliarContable.MotorFiscal;

/// <summary>
/// Qué es de verdad una operación y qué es para el cliente (motor /operaciones/interpretar, con claude -p): el objeto
/// concepto por concepto, si las fuentes coinciden, el tratamiento fiscal, el juicio contable, alertas y preguntas.
/// </summary>
public class InterpretacionDelMotor
{
    public string Version { get; set; } = "";
    public string Huella { get; set; } = "";

    /// <summary>factura, ticket_o_nota, movimiento_del_banco: las que había.</summary>
    public List<string> Fuentes { get; set; } = [];

    public InterpretacionDeOperacion Interpretacion { get; set; } = new();
}

public class InterpretacionDeOperacion
{
    /// <summary>En una línea, qué se compró, vendió o pagó de verdad.</summary>
    public string Objeto { get; set; } = "";

    public List<ConceptoInterpretado> Conceptos { get; set; } = [];
    public ProveedorInterpretado Proveedor { get; set; } = new();

    /// <summary>si, parcial, no, solo_hay_una.</summary>
    public string CoincidenLasFuentes { get; set; } = "";

    public List<string> Diferencias { get; set; } = [];

    /// <summary>mercancia, insumo_directo, gasto_indirecto, gasto_operacion, inversion, deduccion_personal, ajeno, venta, no_aplica.</summary>
    public string Categoria { get; set; } = "";

    public string JuicioContable { get; set; } = "";
    public string TratamientoFiscal { get; set; } = "";
    public List<AlertaDeInterpretacion> Alertas { get; set; } = [];
    public List<string> PreguntasAlCliente { get; set; } = [];

    /// <summary>alta, media, baja: las pólizas solo usan su categoría si es alta.</summary>
    public string Confianza { get; set; } = "";
}

public class ConceptoInterpretado
{
    /// <summary>El número de concepto de la factura (desde 0); -1 si no hay factura.</summary>
    public int Indice { get; set; }

    public string QueEs { get; set; } = "";

    /// <summary>¿La clave SAT y la unidad corresponden a lo que dice el concepto?</summary>
    public bool? ClaveCoincide { get; set; }

    public string Categoria { get; set; } = "";

    /// <summary>Cuenta sugerida del código agrupador.</summary>
    public string Cuenta { get; set; } = "";

    /// <summary>deducible, deducible_parcial, no_deducible, inversion, costo_de_lo_vendido, deduccion_personal, ingreso_acumulable…</summary>
    public string Isr { get; set; } = "";

    /// <summary>acreditable, acreditable_parcial, no_acreditable, trasladado, exento_o_tasa_0, no_aplica.</summary>
    public string Iva { get; set; } = "";

    public decimal? PorcentajeInversion { get; set; }
    public string Fundamento { get; set; } = "";
}

public class ProveedorInterpretado
{
    public string QueVende { get; set; } = "";
    public bool LoConozco { get; set; }
}

public class AlertaDeInterpretacion
{
    /// <summary>requisito_fiscal, no_deducible, posible_error, otra_interpretacion, falta_informacion.</summary>
    public string Tipo { get; set; } = "";

    public string Mensaje { get; set; } = "";
    public string Fundamento { get; set; } = "";
}

/// <summary>Lo del cliente que Claude necesita para interpretar.</summary>
public class PerfilParaInterpretar
{
    public string Rfc { get; set; } = "";
    public List<string> Regimenes { get; set; } = [];
    public string? Giro { get; set; }
    public string? TipoEmpresa { get; set; }
    public string? SistemaCostos { get; set; }
    public string? SistemaInventarios { get; set; }
}

public class OperacionParaInterpretar
{
    public string Fecha { get; set; } = "";

    /// <summary>entrada, salida.</summary>
    public string Sentido { get; set; } = "";

    public decimal? Importe { get; set; }
    public string Descripcion { get; set; } = "";
    public string? Contraparte { get; set; }

    /// <summary>banco, efectivo, credito.</summary>
    public string? Forma { get; set; }
}

/// <summary>El movimiento del estado de cuenta amarrado a la operación.</summary>
public class BancoParaInterpretar
{
    public string? Fecha { get; set; }
    public string Concepto { get; set; } = "";
    public string? Referencia { get; set; }
    public string? Contraparte { get; set; }
    public decimal? Importe { get; set; }
}
