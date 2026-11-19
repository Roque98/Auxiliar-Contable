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
    public int Incapacidad { get; set; }
    public decimal HorasJornada { get; set; } = 8;
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
