using System.Collections.Generic;

namespace AuxiliarContable.MotorFiscal;

/// <summary>
/// Lo del cliente que necesita el análisis normativo (motor: <c>/normativa/analizar</c>). El nombre sólo sirve para
/// detectar la forma jurídica (S.A. de C.V., A.C.…) y el motor no se lo pasa a Claude; el RFC no viaja.
/// </summary>
public class PerfilParaNormativa
{
    public string? TipoPersona { get; set; }
    public string? Nombre { get; set; }
    public List<string> Regimenes { get; set; } = [];
    public List<string> Actividades { get; set; } = [];
    public string? CodigoPostal { get; set; }
    public string? TieneTrabajadores { get; set; }
    public List<string> Disparadores { get; set; } = [];
    public int? Ejercicio { get; set; }
}

public class ContraparteParaNormativa
{
    public string? TipoPersona { get; set; }
    public string? Residencia { get; set; }
}

public class OperacionParaNormativa
{
    public string Descripcion { get; set; } = "";

    /// <summary>AAAA-MM-DD: fija el ejercicio cuya normativa aplica.</summary>
    public string? Fecha { get; set; }

    public ContraparteParaNormativa? Contraparte { get; set; }
    public List<string> Disparadores { get; set; } = [];
}
