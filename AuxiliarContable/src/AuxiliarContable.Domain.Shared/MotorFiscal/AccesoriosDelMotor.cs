using System.Collections.Generic;

namespace AuxiliarContable.MotorFiscal;

/// <summary>Actualización (CFF 17-A) y recargos (CFF 21) de lo que no se pagó a tiempo (motor /impuestos/accesorios).</summary>
public class AccesoriosDelMotor
{
    /// <summary>Día del pago con que se calculó (AAAA-MM-DD).</summary>
    public string Pago { get; set; } = "";

    /// <summary>Tasa mensual de recargos por mora.</summary>
    public decimal Tasa { get; set; }

    public List<AccesorioDelMotor> Adeudos { get; set; } = [];
}

public class AccesorioDelMotor
{
    public string Clave { get; set; } = "";
    public decimal Importe { get; set; }
    public string Limite { get; set; } = "";
    public int Meses { get; set; }
    public decimal Factor { get; set; }
    public decimal Actualizacion { get; set; }
    public decimal Recargos { get; set; }
    public decimal Total { get; set; }
    public string Fundamento { get; set; } = "";
    public string Aviso { get; set; } = "";
}

/// <summary>Lo que se debe de un periodo: se manda al motor para su actualización y recargos.</summary>
public record AdeudoParaElMotor(string Clave, decimal Importe, System.DateTime Limite);
