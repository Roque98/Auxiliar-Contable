using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AuxiliarContable.MotorFiscal;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Concentrado;

/// <summary>
/// La hoja concentrado del cliente (pedido 21-nov-2026): lo primero que ve. Pestaña «Deudas fiscales»: cuánto le toca
/// pagar de cada obligación federal y estatal propia (solo las que le aplican), con la actualización y los recargos de lo
/// atrasado dentro de la obligación que corresponde; de cada cifra se baja al cálculo, del cálculo a las facturas
/// (consideradas, no consideradas y por definir) y de la factura a su porqué con fundamento y de dónde se tomó.
/// La ven el cliente y el despacho; los pagos solo los registra el despacho.
/// </summary>
public interface IConcentradoAppService : IApplicationService
{
    Task<DeudasFiscalesDto> GetDeudasFiscalesAsync(Guid organizacionId, int anio, int mes);

    /// <summary>
    /// Las facturas detrás de un renglón del cálculo: <paramref name="detalle"/> = ingresos, deducciones, iva_cobrado,
    /// iva_acreditable o retenciones. <paramref name="soloDelMes"/>: solo lo que movió el mes (si no, de enero al mes).
    /// </summary>
    Task<DetalleDeRenglonDto> GetDetalleAsync(Guid organizacionId, int anio, int mes, string detalle, bool soloDelMes);

    /// <summary>Una factura del cálculo: si contó, cuánto, por qué no (con fundamento según su régimen), qué hacer si se
    /// puede corregir, y de dónde y cuándo se tomó.</summary>
    Task<FacturaExplicadaDto> GetFacturaAsync(Guid organizacionId, int anio, int mes, string uuid);

    /// <summary>
    /// Pestañas contables: <paramref name="seccion"/> = deudas_contables (pasivos que no son impuestos: NIF C-9, C-19,
    /// D-1, D-3), activos, capital o ingresos_contables. Cada rubro es una cuenta del código agrupador con su saldo al
    /// cierre del mes, de la balanza de apertura más las pólizas de cada mes.
    /// </summary>
    Task<RubrosContablesDto> GetRubrosAsync(Guid organizacionId, int anio, int mes, string seccion);

    /// <summary>Una cuenta (rubro «201» o subcuenta «201.01»): saldos, de quién es el saldo y sus pólizas.</summary>
    Task<CuentaContableDto> GetCuentaAsync(Guid organizacionId, int anio, int mes, string cuenta);

    Task<PagoDeObligacionDto> RegistrarPagoAsync(Guid organizacionId, RegistrarPagoDto dto);

    Task BorrarPagoAsync(Guid organizacionId, Guid pagoId);
}

public class DeudasFiscalesDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }

    /// <summary>¿Hay cálculo de ISR e IVA guardado?</summary>
    public bool Calculado { get; set; }

    public DateTime? CalculadoUtc { get; set; }

    /// <summary>Entró algo nuevo y se está recalculando.</summary>
    public bool Actualizando { get; set; }

    public string? Error { get; set; }

    /// <summary>El despacho puede registrar pagos.</summary>
    public bool PuedeRegistrarPagos { get; set; }

    public string? Regimen { get; set; }

    /// <summary>Cómo se calcula el ISR: pm (ingresos × coeficiente), flujo (acumulado), resico_pf, arrendamiento o
    /// plataformas (mensual). Sirve para saber si el detalle es del mes o de enero al mes.</summary>
    public string? ClaseDelCalculo { get; set; }

    /// <summary>Lo que hay que pagar hoy de todas las obligaciones, con actualización y recargos.</summary>
    public decimal TotalAPagar { get; set; }

    /// <summary>Solo las que le aplican al cliente.</summary>
    public List<ObligacionFiscalDto> Obligaciones { get; set; } = [];

    /// <summary>Las que no le aplican y por qué (para el despacho).</summary>
    public List<string> NoAplican { get; set; } = [];

    public List<string> Avisos { get; set; } = [];

    /// <summary>
    /// Obligaciones y beneficios locales que el despacho aceptó de las leyes que subió el cliente («Mis leyes»): licencias,
    /// protección civil, anuncios, estímulos… Sin importe calculado: se muestran con su periodicidad, fecha y fundamento.
    /// </summary>
    public List<ObligacionLocalDto> ObligacionesLocales { get; set; } = [];

    /// <summary>De dónde salió la información: facturas, descargas, nómina…</summary>
    public FuenteDelCalculoDto Fuente { get; set; } = new();
}

public class ObligacionLocalDto
{
    public string Titulo { get; set; } = "";
    public string? Valor { get; set; }
    public string? Condicion { get; set; }
    public string? Periodicidad { get; set; }
    public string? FechaLimite { get; set; }
    public string Fundamento { get; set; } = "";

    /// <summary>obligacion o beneficio.</summary>
    public string Tipo { get; set; } = "";
}

public class FuenteDelCalculoDto
{
    public int Facturas { get; set; }
    public int Canceladas { get; set; }

    /// <summary>Última factura que entró a la bóveda.</summary>
    public DateTime? UltimaFacturaUtc { get; set; }

    public int PeriodosDeNomina { get; set; }
    public List<string> Supuestos { get; set; } = [];
}

public class ObligacionFiscalDto
{
    /// <summary>isr, iva, ieps, ret_isr_salarios, ret_isr, ret_iva, imss, infonavit, isn.</summary>
    public string Clave { get; set; } = "";

    public string Nombre { get; set; } = "";

    /// <summary>federal o estatal.</summary>
    public string Nivel { get; set; } = "federal";

    /// <summary>Qué es, en una línea.</summary>
    public string Descripcion { get; set; } = "";

    /// <summary>«noviembre de 2026» o «5.º bimestre de 2026».</summary>
    public string Periodo { get; set; } = "";

    public DateTime? Limite { get; set; }

    /// <summary>La fecha límite es la usual y depende de la ley del estado.</summary>
    public bool LimiteAproximado { get; set; }

    /// <summary>Lo que resulta del periodo (null si no se pudo calcular).</summary>
    public decimal? Impuesto { get; set; }

    public decimal AFavor { get; set; }

    /// <summary>Lo que ya se registró pagado del periodo.</summary>
    public decimal Pagado { get; set; }

    /// <summary>Meses anteriores del ejercicio que no se pagaron a tiempo.</summary>
    public List<AdeudoAnteriorDto> Adeudos { get; set; } = [];

    public decimal Actualizacion { get; set; }
    public decimal Recargos { get; set; }

    /// <summary>Lo que falta pagar hoy: lo del periodo menos lo pagado, más lo atrasado con sus accesorios.</summary>
    public decimal Total { get; set; }

    /// <summary>por_pagar, vencido, pagado, a_favor, sin_impuesto o sin_calcular.</summary>
    public string Estado { get; set; } = "";

    /// <summary>El cálculo renglón por renglón; los que traen <c>Detalle</c> se pueden abrir.</summary>
    public List<RenglonDelPapel> Renglones { get; set; } = [];

    public string Fundamento { get; set; } = "";
    public string? Nota { get; set; }
    public List<PagoDeObligacionDto> Pagos { get; set; } = [];
}

public class AdeudoAnteriorDto
{
    public int Mes { get; set; }
    public string Periodo { get; set; } = "";
    public decimal Impuesto { get; set; }
    public decimal Pagado { get; set; }
    public DateTime Limite { get; set; }
    public int MesesDeAtraso { get; set; }
    public decimal Factor { get; set; }
    public decimal Actualizacion { get; set; }
    public decimal Recargos { get; set; }
    public decimal Total { get; set; }
    public string Fundamento { get; set; } = "";
}

public class PagoDeObligacionDto
{
    public Guid Id { get; set; }
    public int Anio { get; set; }
    public int Mes { get; set; }
    public string Clave { get; set; } = "";
    public decimal Importe { get; set; }
    public DateTime FechaDePago { get; set; }
    public string? Nota { get; set; }
}

public class RegistrarPagoDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public string Clave { get; set; } = "";
    public decimal Importe { get; set; }
    public DateTime FechaDePago { get; set; }
    public string? Nota { get; set; }
}

public class DetalleDeRenglonDto
{
    public string Detalle { get; set; } = "";
    public string Titulo { get; set; } = "";

    /// <summary>Qué cuenta y qué no en este renglón, según el régimen.</summary>
    public string Explicacion { get; set; } = "";

    public bool SoloDelMes { get; set; }

    /// <summary>Grupos: consideradas, no_consideradas, por_definir (en ingresos: cobradas, por_cobrar…).</summary>
    public List<GrupoDeFacturasDto> Grupos { get; set; } = [];
}

public class GrupoDeFacturasDto
{
    public string Clave { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public decimal Importe { get; set; }
    public List<FacturaResumenDto> Facturas { get; set; } = [];
}

public class FacturaResumenDto
{
    public string Uuid { get; set; } = "";
    public string Fecha { get; set; } = "";
    public string Rfc { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Concepto { get; set; } = "";
    public decimal Total { get; set; }

    /// <summary>Lo que la factura aporta al renglón.</summary>
    public decimal Importe { get; set; }

    public string Estado { get; set; } = "";

    /// <summary>El primer motivo, en corto.</summary>
    public string? Motivo { get; set; }
}

public class FacturaExplicadaDto
{
    public FacturaDelCalculo Factura { get; set; } = new();

    /// <summary>ingreso o deduccion.</summary>
    public string Lado { get; set; } = "";

    /// <summary>La explicación en palabras: si contó, cuánto y por qué.</summary>
    public string Resumen { get; set; } = "";

    /// <summary>El régimen con que se revisó.</summary>
    public string? Regimen { get; set; }

    /// <summary>Dónde está el archivo y cuándo entró.</summary>
    public OrigenDeFacturaDto? Origen { get; set; }
}

public class OrigenDeFacturaDto
{
    public Guid DocumentoId { get; set; }
    public string Archivo { get; set; } = "";
    public DateTime EntroUtc { get; set; }

    /// <summary>«la subiste tú», «la subió el despacho», «se descargó del SAT»…</summary>
    public string Quien { get; set; } = "";

    public string? EstatusSat { get; set; }
    public DateTime? EstatusSatUtc { get; set; }
}

public class RubrosContablesDto
{
    public string Seccion { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Explicacion { get; set; } = "";
    public int Anio { get; set; }
    public int Mes { get; set; }
    public decimal Total { get; set; }
    public List<RubroContableDto> Rubros { get; set; } = [];
    public List<string> Avisos { get; set; } = [];

    /// <summary>¿Hay contabilidad (pólizas) de algún mes hasta este?</summary>
    public bool HayContabilidad { get; set; }

    /// <summary>Desde dónde se suman los saldos: la apertura o el primer mes con pólizas.</summary>
    public string Desde { get; set; } = "";

    public int MesesConPolizas { get; set; }
    public DateTime? UltimaContabilizacionUtc { get; set; }
}

public class RubroContableDto
{
    /// <summary>Código agrupador de primer nivel («201») o una cuenta virtual («resultado_del_ejercicio»).</summary>
    public string Clave { get; set; } = "";

    public string Nombre { get; set; } = "";

    /// <summary>«Pasivo a corto plazo», «Activo a largo plazo»…</summary>
    public string Grupo { get; set; } = "";

    /// <summary>La NIF que la rige (C-19, C-9, D-3…).</summary>
    public string Nif { get; set; } = "";

    public decimal Saldo { get; set; }
    public bool Virtual { get; set; }
    public List<SaldoDeCuentaDto> Cuentas { get; set; } = [];
}

public class SaldoDeCuentaDto
{
    public string Clave { get; set; } = "";
    public string Nombre { get; set; } = "";
    public decimal Saldo { get; set; }
}

public class CuentaContableDto
{
    public string Clave { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Nif { get; set; } = "";

    /// <summary>D (deudora) o A (acreedora).</summary>
    public string Naturaleza { get; set; } = "";

    /// <summary>Al empezar el mes que se ve.</summary>
    public decimal SaldoInicial { get; set; }

    public decimal Cargos { get; set; }
    public decimal Abonos { get; set; }
    public decimal SaldoFinal { get; set; }
    public List<SaldoDeCuentaDto> Subcuentas { get; set; } = [];

    /// <summary>De quién es el saldo (proveedor, cliente…), por RFC.</summary>
    public List<TerceroDeCuentaDto> Terceros { get; set; } = [];

    /// <summary>Las pólizas del periodo (las más recientes primero).</summary>
    public List<MovimientoDeCuentaDto> Movimientos { get; set; } = [];

    public bool MovimientosRecortados { get; set; }
}

public class TerceroDeCuentaDto
{
    public string Rfc { get; set; } = "";
    public string Nombre { get; set; } = "";
    public decimal Saldo { get; set; }
    public int Movimientos { get; set; }
}

public class MovimientoDeCuentaDto
{
    public string Fecha { get; set; } = "";
    public string Poliza { get; set; } = "";
    public string Concepto { get; set; } = "";
    public string Cuenta { get; set; } = "";
    public string Texto { get; set; } = "";
    public string? Rfc { get; set; }
    public string? Uuid { get; set; }
    public decimal Debe { get; set; }
    public decimal Haber { get; set; }

    /// <summary>El archivo de donde salió (si está en la bóveda) y cuándo entró.</summary>
    public string? Archivo { get; set; }

    public DateTime? EntroUtc { get; set; }
}
