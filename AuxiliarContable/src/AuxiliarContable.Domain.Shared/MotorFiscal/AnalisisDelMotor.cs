using System.Collections.Generic;

namespace AuxiliarContable.MotorFiscal;

/// <summary>Lo que Claude leyó de un documento del cliente (motor /documentos/analizar).</summary>
public class AnalisisDeDocumento
{
    public bool Legible { get; set; }

    /// <summary>ticket, nota_de_venta, nota_de_compra, comprobante_de_transferencia, voucher_de_tarjeta, recibo…</summary>
    public string Tipo { get; set; } = "";

    /// <summary>AAAA-MM-DD.</summary>
    public string? Fecha { get; set; }

    public decimal? Importe { get; set; }
    public string? Folio { get; set; }
    public string? Referencia { get; set; }
    public string? Uuid { get; set; }
    public string? EmisorNombre { get; set; }
    public string? EmisorRfc { get; set; }

    /// <summary>transferencia, tarjeta, efectivo, cheque, credito, desconocida.</summary>
    public string FormaDePago { get; set; } = "";

    /// <summary>salida, entrada, desconocido.</summary>
    public string Sentido { get; set; } = "";

    public List<ConceptoAnalizado> Conceptos { get; set; } = [];
    public string QueEs { get; set; } = "";

    /// <summary>mercancia, insumo_directo, gasto_indirecto, gasto_operacion, inversion, deduccion_personal, ajeno, venta, no_aplica.</summary>
    public string RelacionConGiro { get; set; } = "";

    public string Explicacion { get; set; } = "";
    public string QuePrueba { get; set; } = "";

    /// <summary>alta, media, baja.</summary>
    public string Confianza { get; set; } = "";
}

public class ConceptoAnalizado
{
    public string Descripcion { get; set; } = "";
    public decimal? Cantidad { get; set; }
    public decimal? Importe { get; set; }
}

/// <summary>Un movimiento del banco sin amarrar, como se le manda a Claude.</summary>
public class MovimientoParaAmarrar
{
    public string Id { get; set; } = "";
    public string? Fecha { get; set; }
    public string Naturaleza { get; set; } = "";
    public decimal Importe { get; set; }
    public string Concepto { get; set; } = "";
    public string Referencia { get; set; } = "";
    public string Contraparte { get; set; } = "";
}

/// <summary>Una operación sin pago que podría ser la de un movimiento.</summary>
public class CandidataParaAmarrar
{
    public string Id { get; set; } = "";
    public string Fecha { get; set; } = "";
    public string Sentido { get; set; } = "";
    public decimal? Importe { get; set; }
    public string Descripcion { get; set; } = "";
    public string? Contraparte { get; set; }
    public string? Uuid { get; set; }
    public string? Folio { get; set; }
}

public class SugerenciasDeAmarre
{
    public List<ParDeAmarre> Pares { get; set; } = [];
}

public class ParDeAmarre
{
    public string Movimiento { get; set; } = "";
    public string Operacion { get; set; } = "";

    /// <summary>alta, media, baja.</summary>
    public string Confianza { get; set; } = "";

    public string Porque { get; set; } = "";
}
