using System.Collections.Generic;

namespace AuxiliarContable.MotorFiscal;

/// <summary>Nómina de un periodo (motor /nomina/calcular).</summary>
public class NominaDelMotor
{
    public string Version { get; set; } = "";
    public List<NominaDeTrabajador> Trabajadores { get; set; } = [];
    public TotalesDeNomina Totales { get; set; } = new();
    public IsnDeNomina Isn { get; set; } = new();
    public List<MovimientoDePoliza> Poliza { get; set; } = [];
    public List<string> Avisos { get; set; } = [];
}

public class NominaDeTrabajador
{
    public string? Id { get; set; }
    public string? Nombre { get; set; }
    public string? Rfc { get; set; }
    public string? Nss { get; set; }
    public int Dias { get; set; }
    public int DiasPagados { get; set; }
    public int Faltas { get; set; }
    public int Incapacidad { get; set; }
    public bool SalarioMinimo { get; set; }
    public List<PercepcionDeNomina> Percepciones { get; set; } = [];
    public List<DeduccionDeNomina> Deducciones { get; set; } = [];
    public decimal TotalPercepciones { get; set; }
    public decimal Gravado { get; set; }
    public decimal Exento { get; set; }
    public decimal Isr { get; set; }
    public decimal Subsidio { get; set; }
    public decimal IsrRetenido { get; set; }
    public decimal FactorIntegracion { get; set; }
    public decimal Sdi { get; set; }
    public decimal Sbc { get; set; }
    public List<CuotaDeImss> Cuotas { get; set; } = [];
    public decimal ImssObrero { get; set; }
    public decimal ImssPatron { get; set; }
    public decimal TotalDeducciones { get; set; }
    public decimal Neto { get; set; }
    public List<RenglonDelPapel> Renglones { get; set; } = [];
    public List<string> Avisos { get; set; } = [];
    public List<string> Supuestos { get; set; } = [];
}

public class PercepcionDeNomina
{
    public string Concepto { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string ClaveSat { get; set; } = "";
    public decimal Importe { get; set; }
    public decimal Gravado { get; set; }
    public decimal Exento { get; set; }
    public bool IntegraSbc { get; set; }

    /// <summary>Solo horas extra: [c_TipoHoras, horas, importe] para el nodo HorasExtra del CFDI.</summary>
    public List<List<System.Text.Json.JsonElement>>? Horas { get; set; }

    public int? DiasConHoras { get; set; }
}

public class DeduccionDeNomina
{
    public string Concepto { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string ClaveSat { get; set; } = "";
    public decimal Importe { get; set; }
}

public class CuotaDeImss
{
    public string Ramo { get; set; } = "";
    public decimal Base { get; set; }
    public decimal Patron { get; set; }
    public decimal Obrero { get; set; }

    /// <summary>imss, sar, infonavit.</summary>
    public string Grupo { get; set; } = "";
}

public class TotalesDeNomina
{
    public decimal TotalPercepciones { get; set; }
    public decimal Gravado { get; set; }
    public decimal Exento { get; set; }
    public decimal Isr { get; set; }
    public decimal Subsidio { get; set; }
    public decimal IsrRetenido { get; set; }
    public decimal ImssObrero { get; set; }
    public decimal ImssPatron { get; set; }
    public decimal InfonavitPatron { get; set; }
    public decimal SarPatron { get; set; }
    public decimal TotalDeducciones { get; set; }
    public decimal Neto { get; set; }
}

public class IsnDeNomina
{
    public string? Estado { get; set; }
    public decimal? Tasa { get; set; }
    public decimal Importe { get; set; }
    public string Fundamento { get; set; } = "";
    public bool Verificado { get; set; }
}

public class MovimientoDePoliza
{
    public string Cuenta { get; set; } = "";
    public decimal Debe { get; set; }
    public decimal Haber { get; set; }
}

/// <summary>Lo del patrón que pide el cálculo.</summary>
public class PatronParaNomina
{
    public string Rfc { get; set; } = "";
    public string? Estado { get; set; }
    public string? CodigoPostal { get; set; }

    /// <summary>general o frontera.</summary>
    public string? Zona { get; set; }

    /// <summary>Prima de riesgo de trabajo de la empresa, en %.</summary>
    public decimal? PrimaRiesgo { get; set; }

    /// <summary>Tasa del ISN (fracción) que el despacho aceptó de la ley que subió el cliente («Mis leyes»); manda sobre la
    /// tabla del sistema. Null = la del sistema.</summary>
    public decimal? IsnTasa { get; set; }

    public string? IsnFundamento { get; set; }
}

public class PeriodoParaNomina
{
    public string Inicio { get; set; } = "";
    public string Fin { get; set; } = "";
    public int Dias { get; set; }
    public int Mes { get; set; }
}

/// <summary>Las incidencias de un trabajador en un periodo (lo que reporta el cliente).</summary>
public class IncidenciasDeNomina
{
    public int Faltas { get; set; }

    /// <summary>Primer día de las faltas (para el ausentismo del SUA); sin él va la fecha de inicio del periodo.</summary>
    public System.DateTime? InicioFaltas { get; set; }

    public int Incapacidad { get; set; }

    /// <summary>Del certificado de incapacidad del IMSS: folio (2 letras y 6 números) y fecha de inicio.</summary>
    public string? FolioIncapacidad { get; set; }

    public System.DateTime? InicioIncapacidad { get; set; }
    public decimal HorasJornada { get; set; } = 8;

    /// <summary>
    /// Horas ordinarias a la semana (0 = no se capturaron). El motor avisa si pasan del máximo del año: 48 en 2026, 46 en
    /// 2027… 40 en 2030 (LFT 59, transitorio Segundo DOF 01-05-2026).
    /// </summary>
    public decimal HorasSemana { get; set; }

    /// <summary>diurna (8 h), nocturna (7) o mixta (7.5), LFT 61. Null = diurna.</summary>
    public string? TipoJornada { get; set; }

    public decimal HorasExtraDobles { get; set; }
    public decimal HorasExtraTriples { get; set; }
    public decimal DiasDescansoTrabajados { get; set; }
    public decimal FestivosTrabajados { get; set; }
    public decimal DomingosTrabajados { get; set; }
    public decimal Aguinaldo { get; set; }
    public decimal PrimaVacacional { get; set; }
    public List<ConceptoLibre> OtrasPercepciones { get; set; } = [];
    public List<ConceptoLibre> OtrasDeducciones { get; set; } = [];
}

public class ConceptoLibre
{
    public string Concepto { get; set; } = "";
    public decimal Importe { get; set; }

    /// <summary>Solo percepciones: la parte exenta de ISR.</summary>
    public decimal Exento { get; set; }

    /// <summary>Solo percepciones: si integra al salario base de cotización.</summary>
    public bool IntegraSbc { get; set; }
}

/// <summary>Un trabajador como se le manda al motor.</summary>
public class TrabajadorParaNomina
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? Rfc { get; set; }
    public string? Nss { get; set; }
    public string? FechaIngreso { get; set; }
    public decimal SalarioDiario { get; set; }
    public decimal? Sdi { get; set; }
    public int DiasAguinaldo { get; set; }
    public decimal PrimaVacacionalPct { get; set; }
    public IncidenciasDeNomina Incidencias { get; set; } = new();
    public List<ConceptoLibre> OtrasPercepciones { get; set; } = [];
    public List<ConceptoLibre> OtrasDeducciones { get; set; } = [];
    public CreditoInfonavitParaNomina? CreditoInfonavit { get; set; }
    public decimal? Fonacot { get; set; }
}

public class CreditoInfonavitParaNomina
{
    public string Tipo { get; set; } = "";
    public decimal Valor { get; set; }
}

/// <summary>Un movimiento afiliatorio como se le manda al motor (/nomina/idse).</summary>
public class MovimientoParaIdse
{
    public string Id { get; set; } = "";

    /// <summary>alta, modificacion, baja.</summary>
    public string Tipo { get; set; } = "";

    public string Fecha { get; set; } = "";
    public string? Nss { get; set; }
    public string? Curp { get; set; }
    public string? PrimerApellido { get; set; }
    public string? SegundoApellido { get; set; }
    public string? Nombres { get; set; }
    public decimal SalarioDiario { get; set; }
    public decimal? Sdi { get; set; }
    public string? FechaIngreso { get; set; }
    public int DiasAguinaldo { get; set; } = 15;
    public decimal PrimaVacacionalPct { get; set; } = 25;
    public string? Umf { get; set; }
    public string? Clave { get; set; }
    public string? CausaBaja { get; set; }
}

public class ArchivosDeIdse
{
    public List<ArchivoDeIdse> Archivos { get; set; } = [];
    public List<ErrorDeMovimiento> Errores { get; set; } = [];
}

public class ArchivoDeIdse
{
    public string Nombre { get; set; } = "";

    /// <summary>08 reingreso, 07 modificación de salario, 02 baja.</summary>
    public string Tipo { get; set; } = "";

    public int Renglones { get; set; }
    public string ContenidoB64 { get; set; } = "";
}

public class ErrorDeMovimiento
{
    public string? Id { get; set; }
    public string? Nombre { get; set; }
    public List<string> Errores { get; set; } = [];
}

/// <summary>Lo del patrón para los recibos (CFDI de nómina).</summary>
public class PatronParaRecibos
{
    public string Rfc { get; set; } = "";
    public string? Nombre { get; set; }
    public string? Regimen { get; set; }
    public string? Cp { get; set; }
    public string? RegistroPatronal { get; set; }
}

public class TrabajadorParaRecibo
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? Rfc { get; set; }
    public string? Curp { get; set; }
    public string? Nss { get; set; }
    public string? Cp { get; set; }
    public string? FechaIngreso { get; set; }
    public string? NumEmpleado { get; set; }
}

public class RecibosDeNomina
{
    public List<ReciboDeNomina> Recibos { get; set; } = [];
    public string ZipB64 { get; set; } = "";
    public int Validos { get; set; }
    public int ConErrores { get; set; }
}

public class ReciboDeNomina
{
    public string? Id { get; set; }
    public string? Nombre { get; set; }
    public string Archivo { get; set; } = "";
    public List<string> Errores { get; set; } = [];
    public List<string> Faltan { get; set; } = [];
}

public class CuotasDeNomina
{
    public List<CuotaDelMes> Mensuales { get; set; } = [];
    public List<CuotaDelMes> Bimestrales { get; set; } = [];
    public decimal TotalMensual { get; set; }
    public decimal TotalBimestral { get; set; }
}

public class CuotaDelMes
{
    public string Ramo { get; set; } = "";
    public decimal Patron { get; set; }
    public decimal Obrero { get; set; }
    public decimal Total { get; set; }
}

/// <summary>Un trabajador como se le manda al motor para el SUA (/nomina/sua).</summary>
public class TrabajadorParaSua
{
    public string Id { get; set; } = "";
    public string? Nss { get; set; }
    public string? Rfc { get; set; }
    public string? Curp { get; set; }
    public string? PrimerApellido { get; set; }
    public string? SegundoApellido { get; set; }
    public string? Nombres { get; set; }
    public string FechaIngreso { get; set; } = "";
    public decimal SalarioDiario { get; set; }
    public decimal? Sdi { get; set; }
    public int DiasAguinaldo { get; set; } = 15;
    public decimal PrimaVacacionalPct { get; set; } = 25;
    public string? Clave { get; set; }
    public string? Cp { get; set; }
    public string? Umf { get; set; }
    public CreditoParaSua? Credito { get; set; }
}

public class CreditoParaSua
{
    public string? Numero { get; set; }
    public string? Inicio { get; set; }

    /// <summary>porcentaje, cuota_fija o veces.</summary>
    public string Tipo { get; set; } = "";

    public decimal Valor { get; set; }
}

public class MovimientoParaSua
{
    public string TrabajadorId { get; set; } = "";

    /// <summary>alta, modificacion, baja.</summary>
    public string Tipo { get; set; } = "";

    public string Fecha { get; set; } = "";
    public decimal SalarioDiario { get; set; }
    public decimal? Sdi { get; set; }
    public string? FechaIngreso { get; set; }
    public int DiasAguinaldo { get; set; } = 15;
    public decimal PrimaVacacionalPct { get; set; } = 25;
}

public class IncidenciaParaSua
{
    public string TrabajadorId { get; set; } = "";
    public string PeriodoInicio { get; set; } = "";
    public int Faltas { get; set; }
    public string? InicioFaltas { get; set; }
    public int Incapacidad { get; set; }
    public string? InicioIncapacidad { get; set; }
    public string? FolioIncapacidad { get; set; }
}

public class ArchivosDeSua
{
    public string ZipB64 { get; set; } = "";
    public List<ArchivoDeSua> Archivos { get; set; } = [];
    public List<ErrorDeMovimiento> Errores { get; set; } = [];
    public List<string> Avisos { get; set; } = [];
}

public class ArchivoDeSua
{
    public string Nombre { get; set; } = "";
    public int Renglones { get; set; }
}

/// <summary>
/// Un periodo de nómina ya calculado que termina en el mes que se contabiliza: su póliza (provisión) entra al mes y sus
/// netos sirven para reconocer los pagos del banco y los CFDI de nómina (motor /contabilidad/polizas, «nomina»).
/// </summary>
public class NominaParaContabilizar
{
    /// <summary>«2026-10-01 al 2026-10-15».</summary>
    public string Periodo { get; set; } = "";

    /// <summary>AAAA-MM-DD.</summary>
    public string Fin { get; set; } = "";

    public List<MovimientoDePoliza> Poliza { get; set; } = [];
    public List<NetoDeTrabajador> Trabajadores { get; set; } = [];
}

public class NetoDeTrabajador
{
    public string? Nombre { get; set; }
    public string? Rfc { get; set; }
    public decimal Neto { get; set; }
}

/// <summary>Lo que el motor leyó de la lista de raya o el reporte de incidencias (/nomina/incidencias/leer).</summary>
public class IncidenciasLeidas
{
    public bool Legible { get; set; }
    public string? PeriodoInicio { get; set; }
    public string? PeriodoFin { get; set; }
    public List<IncidenciaLeida> Trabajadores { get; set; } = [];
    public List<string> Dudas { get; set; } = [];
}

public class IncidenciaLeida
{
    /// <summary>Como viene en el documento.</summary>
    public string Nombre { get; set; } = "";

    /// <summary>El trabajador del despacho al que se parece (el id que se le mandó); null si a ninguno.</summary>
    public string? TrabajadorId { get; set; }

    public decimal Parecido { get; set; }
    public decimal Faltas { get; set; }
    public decimal Incapacidad { get; set; }
    public decimal HorasExtraDobles { get; set; }
    public decimal HorasExtraTriples { get; set; }
    public decimal HorasSemana { get; set; }
    public decimal DomingosTrabajados { get; set; }
    public decimal DiasDescansoTrabajados { get; set; }
    public decimal FestivosTrabajados { get; set; }
    public string Nota { get; set; } = "";
}
