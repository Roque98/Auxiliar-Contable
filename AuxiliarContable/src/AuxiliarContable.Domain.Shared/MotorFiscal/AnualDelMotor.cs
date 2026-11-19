using System.Collections.Generic;

namespace AuxiliarContable.MotorFiscal;

/// <summary>
/// Declaración anual de ISR del ejercicio con los CFDI (motor /impuestos/anual): persona física art. 152, RESICO 113-F,
/// persona moral art. 9; renglón por renglón con su fundamento.
/// </summary>
public class AnualDelMotor
{
    public int Ejercicio { get; set; }

    /// <summary>Cómo se calculó («Persona física (art. 152 LISR)»…).</summary>
    public string Tipo { get; set; } = "";

    public string? Regimen { get; set; }
    public List<RenglonDelPapel> Renglones { get; set; } = [];
    public decimal IsrDelEjercicio { get; set; }
    public decimal ACargo { get; set; }
    public decimal AFavor { get; set; }

    /// <summary>Personas morales: el coeficiente de utilidad para los pagos del año siguiente (art. 14-I).</summary>
    public decimal? CoeficienteSiguiente { get; set; }

    public List<DeduccionPersonal> DeduccionesPersonales { get; set; } = [];
    public List<string> Avisos { get; set; } = [];
    public List<string> Supuestos { get; set; } = [];

    /// <summary>AAAA-MM-DD: personas morales el 31 de marzo (art. 9), personas físicas el 30 de abril (art. 150).</summary>
    public string Vence { get; set; } = "";

    public List<ErrorDeArchivo> Errores { get; set; } = [];

    /// <summary>Los CFDI de retenciones del ejercicio que usó (plataformas, intereses, dividendos…).</summary>
    public List<RetencionDeAnual> Retenciones { get; set; } = [];
}

public class RetencionDeAnual
{
    public string? Uuid { get; set; }

    /// <summary>plataformas, intereses, dividendos u otra.</summary>
    public string Tipo { get; set; } = "";

    public string Descripcion { get; set; } = "";
    public string RfcEmisor { get; set; } = "";
    public string NombreEmisor { get; set; } = "";
    public string? Periodo { get; set; }
    public decimal MontoOperacion { get; set; }
    public decimal IsrRetenido { get; set; }
    public decimal IvaRetenido { get; set; }
}

/// <summary>Un CFDI con uso D01-D10 pagado en el año (art. 151).</summary>
public class DeduccionPersonal
{
    public string Uso { get; set; } = "";
    public string Concepto { get; set; } = "";
    public string? Rfc { get; set; }
    public string Nombre { get; set; } = "";
    public string Fecha { get; set; } = "";
    public decimal Importe { get; set; }
    public string? Uuid { get; set; }

    /// <summary>Pagada en efectivo: no es deducible.</summary>
    public bool Efectivo { get; set; }
}

/// <summary>Lo que la anual necesita y no viene en los CFDI. Null = no se capturó.</summary>
public class CapturaDeAnual
{
    public decimal? PtuPagada { get; set; }
    public decimal? PerdidasAnteriores { get; set; }
    public decimal? UmaAnual { get; set; }
    public decimal? InventarioInicial { get; set; }
    public decimal? InventarioFinal { get; set; }
    public decimal? SueldosGravados { get; set; }
    public decimal? IsrRetenidoSueldos { get; set; }

    /// <summary>Lo que de verdad se pagó de pagos provisionales; null = los que salen del cálculo.</summary>
    public decimal? PagosProvisionales { get; set; }

    /// <summary>Para el motor: solo lo capturado, con los nombres del servicio.</summary>
    public Dictionary<string, decimal> ParaElMotor()
    {
        var d = new Dictionary<string, decimal>();
        void Poner(string clave, decimal? valor)
        {
            if (valor.HasValue)
            {
                d[clave] = valor.Value;
            }
        }

        Poner("ptu_pagada", PtuPagada);
        Poner("perdidas_anteriores", PerdidasAnteriores);
        Poner("uma_anual", UmaAnual);
        Poner("inventario_inicial", InventarioInicial);
        Poner("inventario_final", InventarioFinal);
        Poner("sueldos_gravados", SueldosGravados);
        Poner("isr_retenido_sueldos", IsrRetenidoSueldos);
        Poner("pagos_provisionales", PagosProvisionales);
        return d;
    }
}
