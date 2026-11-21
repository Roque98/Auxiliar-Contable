using System.Collections.Generic;

namespace AuxiliarContable.MotorFiscal;

// Pago provisional de ISR e IVA solo con los CFDI (/impuestos/provisionales en motores-fiscales): el papel de trabajo de
// impuestos del Armador (Excel con fórmulas, tarifa del Anexo 8) y, por mes del ejercicio, lo que mueve el impuesto.

/// <summary>
/// Una factura PPD que la conciliación del banco muestra cobrada (o pagada) sin su complemento de pago: entra al
/// cálculo por flujo en la fecha del banco (arts. 102 y 113-E LISR, 1-B LIVA) y se avisa que falta el complemento.
/// </summary>
public class CobroDelBanco
{
    public string Uuid { get; set; } = "";

    /// <summary>AAAA-MM-DD del movimiento del banco.</summary>
    public string Fecha { get; set; } = "";

    /// <summary>En la moneda de la factura.</summary>
    public decimal Importe { get; set; }

    /// <summary>emitida (cobro) o recibida (pago).</summary>
    public string Lado { get; set; } = "emitida";
}

public class PagosProvisionales
{
    public RegimenDelCalculo Regimen { get; set; } = new();

    /// <summary>De enero al mes que se calcula.</summary>
    public List<MesDeImpuestos> Meses { get; set; } = [];

    public List<string> Avisos { get; set; } = [];

    /// <summary>Lo que el cálculo da por hecho (PTU y pérdidas en cero, pagos anteriores = los calculados…).</summary>
    public List<string> Supuestos { get; set; } = [];

    public List<ErrorDeArchivo> Errores { get; set; } = [];

    /// <summary>El papel de trabajo (.xlsx) en base64.</summary>
    public ArchivoDelMotor? Archivo { get; set; }

    /// <summary>Factura por factura, lo que entró al cálculo del ejercicio hasta el mes y lo que no, con su porqué.</summary>
    public DetalleDelCalculo? Detalle { get; set; }
}

public class DetalleDelCalculo
{
    /// <summary>pm (lo facturado × coeficiente), flujo, resico_pf, arrendamiento o plataformas.</summary>
    public string Clase { get; set; } = "";

    public List<FacturaDelCalculo> Ingresos { get; set; } = [];
    public List<FacturaDelCalculo> Deducciones { get; set; } = [];
}

public class FacturaDelCalculo
{
    public string Uuid { get; set; } = "";
    public string Fecha { get; set; } = "";
    public string Rfc { get; set; } = "";
    public string Nombre { get; set; } = "";

    /// <summary>Ingreso, Egreso o Nómina (el renglón de los recibos de nómina).</summary>
    public string Tipo { get; set; } = "";

    public string Metodo { get; set; } = "";
    public string Forma { get; set; } = "";
    public string Uso { get; set; } = "";
    public decimal Total { get; set; }

    /// <summary>Sin IVA; negativa en las notas de crédito.</summary>
    public decimal Base { get; set; }

    public decimal Iva { get; set; }

    /// <summary>Ingresos: lo que se acumuló (cobrado, o facturado en personas morales).</summary>
    public decimal Acumulable { get; set; }

    public decimal IvaCobrado { get; set; }

    /// <summary>Deducciones: lo que se dedujo.</summary>
    public decimal Deducible { get; set; }

    public decimal IvaAcreditable { get; set; }
    public decimal RetIsr { get; set; }
    public decimal RetIva { get; set; }
    public decimal ProporcionNoDeducible { get; set; }

    /// <summary>Ingresos: cobrada, por_cobrar, cobro_parcial, facturada, nota_de_credito, no_cuenta. Deducciones:
    /// considerada, no_considerada, parcial, por_definir, pago_parcial.</summary>
    public string Estado { get; set; } = "";

    public List<MotivoDelCalculo> Motivos { get; set; } = [];

    /// <summary>Meses (AAAA-MM) en que contó.</summary>
    public List<string> Meses { get; set; } = [];

    /// <summary>Lo que movió en cada mes: base, iva, deducible, iva_acreditable, ret_isr, ret_iva.</summary>
    public Dictionary<string, Dictionary<string, decimal>> PorMes { get; set; } = [];

    public string Concepto { get; set; } = "";
    public int? Recibos { get; set; }
}

public class MotivoDelCalculo
{
    public string Regla { get; set; } = "";

    /// <summary>requisito (se puede corregir: trae <see cref="Solucion"/>), ley, inversion, indispensable, revisar o
    /// personal.</summary>
    public string Clase { get; set; } = "";

    public string Hipotesis { get; set; } = "";
    public string Articulo { get; set; } = "";
    public string Resultado { get; set; } = "";
    public string Concepto { get; set; } = "";
    public decimal? Importe { get; set; }
    public decimal? NoDeducible { get; set; }
    public string Solucion { get; set; } = "";
    public string Pregunta { get; set; } = "";
    public string? Explicacion { get; set; }
}

public class RegimenDelCalculo
{
    /// <summary>Clave del régimen que manda en el pago provisional (601, 612, 626…); null si no se supo.</summary>
    public string? Principal { get; set; }

    public List<string> Claves { get; set; } = [];

    public string? Nombre { get; set; }

    /// <summary>ISR con lo cobrado y lo pagado (personas físicas, RESICO PM); si no, lo devengado.</summary>
    public bool Flujo { get; set; }

    public string? Fuente { get; set; }
}

public class MesDeImpuestos
{
    /// <summary>AAAA-MM.</summary>
    public string Periodo { get; set; } = "";

    public decimal Facturado { get; set; }
    public decimal NotasDeCredito { get; set; }
    public decimal Cobrado { get; set; }
    public decimal ComprasFacturadas { get; set; }
    public decimal Pagado { get; set; }
    public decimal Nomina { get; set; }
    public decimal IvaCobrado { get; set; }
    public decimal IvaPagado { get; set; }
    public decimal IvaRetenidoPorClientes { get; set; }
    public decimal IsrRetenidoPorClientes { get; set; }

    /// <summary>Trasladado cobrado − retenido por los clientes − acreditable pagado (el formal está en el Excel).</summary>
    public decimal IvaEstimado { get; set; }

    public int CfdiEmitidos { get; set; }
    public int CfdiRecibidos { get; set; }

    /// <summary>Artículo con el que se calculó (art. 106, 113-E, 14…); null si el régimen no hace pago propio.</summary>
    public string? Metodo { get; set; }

    public decimal? IngresosAcumulados { get; set; }
    public decimal? DeduccionesAcumuladas { get; set; }
    public decimal DeduccionesDelMes { get; set; }
    public decimal? Base { get; set; }

    /// <summary>Null si falta un dato para calcularlo (p. ej. el coeficiente de utilidad de una persona moral).</summary>
    public decimal? IsrCausado { get; set; }

    public decimal IsrRetenido { get; set; }
    public decimal? IsrPagosAnteriores { get; set; }
    public decimal? IsrAPagar { get; set; }
    public decimal? IsrAFavor { get; set; }
    public decimal IvaAcreditable { get; set; }
    public decimal IvaResultado { get; set; }
    public decimal IvaSaldoAnterior { get; set; }
    public decimal IvaAPagar { get; set; }
    public decimal IvaSaldoAFavor { get; set; }

    /// <summary>El ISR del mes renglón por renglón, como en el papel de trabajo, con su fundamento.</summary>
    public List<RenglonDelPapel> RenglonesIsr { get; set; } = [];

    /// <summary>El IVA del mes renglón por renglón.</summary>
    public List<RenglonDelPapel> RenglonesIva { get; set; } = [];

    /// <summary>IEPS del mes (LIEPS 4o, 5o y 10); null si no hay IEPS en las facturas.</summary>
    public decimal? IepsCausado { get; set; }

    public decimal? IepsAcreditable { get; set; }
    public decimal? IepsAPagar { get; set; }
    public decimal? IepsSaldoAFavor { get; set; }
    public List<RenglonDelPapel> RenglonesIeps { get; set; } = [];
}

public class RenglonDelPapel
{
    public string Concepto { get; set; } = "";

    /// <summary>Null si falta un dato para calcularlo, o si el renglón solo explica (tipo «nota»).</summary>
    public decimal? Importe { get; set; }

    public string Fundamento { get; set; } = "";

    /// <summary>renglon, subtotal, resultado, tasa (en %), factor o nota.</summary>
    public string Tipo { get; set; } = "renglon";

    /// <summary>Qué lista del detalle explica el renglón: ingresos, deducciones, iva_cobrado, iva_acreditable o
    /// retenciones (null: no se baja).</summary>
    public string? Detalle { get; set; }
}

public class ArchivoDelMotor
{
    public string Nombre { get; set; } = "";
    public string ContenidoB64 { get; set; } = "";
}
