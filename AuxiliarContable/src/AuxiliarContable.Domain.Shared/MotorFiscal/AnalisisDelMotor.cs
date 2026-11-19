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

/// <summary>Una operación como se le manda al motor para contabilizarla (motor /contabilidad/polizas).</summary>
public class OperacionParaContabilizar
{
    public string Id { get; set; } = "";
    public string Fecha { get; set; } = "";

    /// <summary>entrada, salida.</summary>
    public string Sentido { get; set; } = "";

    public decimal? Importe { get; set; }
    public string Descripcion { get; set; } = "";
    public string? Contraparte { get; set; }
    public PataParaContabilizar Factura { get; set; } = new();
    public PataParaContabilizar Transaccion { get; set; } = new();
    public PataParaContabilizar Control { get; set; } = new();

    /// <summary>Lo de la revisión que el despacho ya aceptó (códigos).</summary>
    public List<string> Aceptados { get; set; } = [];

    /// <summary>Lo que interpretó Claude, si está al día con sus fuentes (las pólizas lo usan con cuidado).</summary>
    public InterpretacionDeOperacion? Interpretacion { get; set; }
}

public class PataParaContabilizar
{
    /// <summary>ligada, no_hay, pendiente.</summary>
    public string Estado { get; set; } = "pendiente";

    public string? Uuid { get; set; }

    /// <summary>Solo transacción: banco, efectivo, credito.</summary>
    public string? Forma { get; set; }

    public string? Referencia { get; set; }

    /// <summary>Solo transacción: el importe y la fecha del movimiento del banco, si lo es.</summary>
    public decimal? Importe { get; set; }

    public string? Fecha { get; set; }

    /// <summary>Solo a crédito: ya se pagó.</summary>
    public bool? Saldado { get; set; }

    public AnalisisDeDocumento? Analisis { get; set; }
}

public class PerfilParaContabilizar
{
    public string Rfc { get; set; } = "";
    public string? TipoEmpresa { get; set; }
    public string? SistemaCostos { get; set; }
    public string? SistemaInventarios { get; set; }
    public string? Giro { get; set; }
}

/// <summary>Lo que devuelve el motor al contabilizar el mes.</summary>
public class ResultadoContable
{
    public string Version { get; set; } = "";
    public List<PolizaContable> Polizas { get; set; } = [];
    public List<RenglonDeRevision> Revision { get; set; } = [];
    public List<CuentaDeBalanza> Balanza { get; set; } = [];
    public List<string> PendientesDeCierre { get; set; } = [];
    public List<ErrorDeArchivo> Errores { get; set; } = [];
}

public class PolizaContable
{
    public string Operacion { get; set; } = "";

    /// <summary>Ingresos, Egresos, Diario.</summary>
    public string Tipo { get; set; } = "";

    public int Numero { get; set; }
    public string Fecha { get; set; } = "";
    public string Concepto { get; set; } = "";
    public string? Uuid { get; set; }
    public string? Categoria { get; set; }
    public List<MovimientoContable> Movimientos { get; set; } = [];

    /// <summary>RFC de la contraparte, total y forma de pago del CFDI (para la contabilidad electrónica).</summary>
    public string? Rfc { get; set; }

    public decimal? Total { get; set; }
    public string? Forma { get; set; }
}

public class MovimientoContable
{
    public string Cuenta { get; set; } = "";
    public string Nombre { get; set; } = "";
    public decimal Debe { get; set; }
    public decimal Haber { get; set; }
    public string Texto { get; set; } = "";
}

public class RenglonDeRevision
{
    /// <summary>Null = del mes completo (p. ej. falta el tipo de empresa).</summary>
    public string? Operacion { get; set; }

    public string Codigo { get; set; } = "";

    /// <summary>detiene, aviso, aceptado.</summary>
    public string Nivel { get; set; } = "";

    public string Mensaje { get; set; } = "";
}

public class CuentaDeBalanza
{
    public string Cuenta { get; set; } = "";
    public string Nombre { get; set; } = "";
    public decimal Debe { get; set; }
    public decimal Haber { get; set; }
}

/// <summary>Lo que el cierre del mes necesita para el costo de ventas (motor: inventario_inicial, inventario_final, costo_vendido).</summary>
public class DatosDeCierre
{
    /// <summary>Inventario al iniciar el mes (el final del mes anterior).</summary>
    public decimal? InventarioInicial { get; set; }

    /// <summary>Inventario analítico: el que quedó al terminar el mes (conteo físico valuado).</summary>
    public decimal? InventarioFinal { get; set; }

    /// <summary>Inventario perpetuo: el costo de lo vendido según el almacén.</summary>
    public decimal? CostoVendido { get; set; }
}

/// <summary>Un saldo de apertura: deudor o acreedor.</summary>
public class SaldoDeApertura
{
    public string Cuenta { get; set; } = "";
    public decimal Deudor { get; set; }
    public decimal Acreedor { get; set; }
}

public class AperturaParaElMotor
{
    /// <summary>AAAA-MM: los saldos son al iniciar ese mes.</summary>
    public string Periodo { get; set; } = "";

    public List<SaldoDeApertura> Saldos { get; set; } = [];
}

public class MesParaElMotor
{
    public string Periodo { get; set; } = "";
    public List<PolizaContable> Polizas { get; set; } = [];
}

/// <summary>Balanza del mes y XML de la contabilidad electrónica (motor /contabilidad/electronica).</summary>
public class ContabilidadElectronica
{
    public List<RenglonDeBalanza> Balanza { get; set; } = [];
    public List<ArchivoXml> Archivos { get; set; } = [];
    public string Vencimiento { get; set; } = "";
    public List<string> Avisos { get; set; } = [];

    /// <summary>Estado de situación financiera y de resultados del ejercicio hasta el mes (NIF B-6 y B-3).</summary>
    public EstadosFinancieros? Estados { get; set; }

    /// <summary>Cargos y abonos del mes por cuenta (los mismos de la balanza): el auxiliar de cada cuenta.</summary>
    public List<MovimientoDeAuxiliar> Movimientos { get; set; } = [];
}

public class MovimientoDeAuxiliar
{
    public string Cuenta { get; set; } = "";
    public string Fecha { get; set; } = "";

    /// <summary>Folio de la póliza (I-2026-10-0001).</summary>
    public string Poliza { get; set; } = "";

    public string Tipo { get; set; } = "";
    public string Concepto { get; set; } = "";
    public string Texto { get; set; } = "";
    public decimal Debe { get; set; }
    public decimal Haber { get; set; }
    public string? Uuid { get; set; }
    public string? Rfc { get; set; }
}

public class EstadosFinancieros
{
    /// <summary>AAAA-MM: el estado de resultados va de este mes…</summary>
    public string Desde { get; set; } = "";

    /// <summary>…a este; el de situación financiera es al cierre de este.</summary>
    public string Hasta { get; set; } = "";

    public List<RenglonDeEstado> Resultado { get; set; } = [];
    public List<RenglonDeEstado> Situacion { get; set; } = [];

    /// <summary>Activo − (pasivo + capital): debe ser 0.</summary>
    public decimal? Cuadre { get; set; }

    /// <summary>Faltan saldos iniciales o el costo de lo vendido: así se rotulan.</summary>
    public bool Preliminar { get; set; }

    public List<string> Advertencias { get; set; } = [];
    public List<RazonFinanciera> Razones { get; set; } = [];

    /// <summary>Borrador de notas (NIF B-6, B-3 y particulares) con las cifras del cliente.</summary>
    public List<NotaDeLosEstados> Notas { get; set; } = [];

    /// <summary>Revelaciones obligatorias de las NIF que aplican por los rubros con saldo (lista de verificación).</summary>
    public List<RevelacionNif> Revelaciones { get; set; } = [];
}

public class NotaDeLosEstados
{
    public string Titulo { get; set; } = "";
    public string Texto { get; set; } = "";
}

public class RevelacionNif
{
    public string Norma { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Parrafo { get; set; } = "";
    public string Texto { get; set; } = "";
}

public class RenglonDeEstado
{
    public string Concepto { get; set; } = "";
    public decimal? Importe { get; set; }
    public string Fuente { get; set; } = "";

    /// <summary>Solo en el de situación financiera: titulo, rubro, subtotal o total.</summary>
    public string? Tipo { get; set; }
}

public class RazonFinanciera
{
    public string Clave { get; set; } = "";
    public string Nombre { get; set; } = "";
    public decimal? Valor { get; set; }
    public string Unidad { get; set; } = "";
    public string Lectura { get; set; } = "";
}

public class RenglonDeBalanza
{
    public string Cuenta { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int Nivel { get; set; }

    /// <summary>D deudora, A acreedora.</summary>
    public string Naturaleza { get; set; } = "D";

    public decimal SaldoInicial { get; set; }
    public decimal Debe { get; set; }
    public decimal Haber { get; set; }
    public decimal SaldoFinal { get; set; }
}

public class ArchivoXml
{
    public string Nombre { get; set; } = "";

    /// <summary>CT catálogo, BN balanza, PL pólizas.</summary>
    public string Tipo { get; set; } = "";

    public string ContenidoB64 { get; set; } = "";

    /// <summary>Errores contra el XSD del SAT (vacío = válido).</summary>
    public List<string> Errores { get; set; } = [];
}
