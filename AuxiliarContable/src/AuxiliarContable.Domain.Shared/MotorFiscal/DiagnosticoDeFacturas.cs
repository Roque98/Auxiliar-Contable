using System.Collections.Generic;

namespace AuxiliarContable.MotorFiscal;

// El diagnóstico de facturas del motor (/cfdi/diagnosticar en motores-fiscales; la hoja «union» del Armador):
// cada factura con lo que la salda (complementos de pago, notas de crédito), y si no cuadra, qué pasó.
// La consola no lo reinterpreta ni lo guarda: lo muestra en el cierre del mes.

public class DiagnosticoDeFacturas
{
    /// <summary>Las que emitió el cliente: si algo está mal, lo corrige él.</summary>
    public LadoDelDiagnostico Emitidas { get; set; } = new();

    /// <summary>Las que recibió: si algo está mal, lo corrige su proveedor.</summary>
    public LadoDelDiagnostico Recibidas { get; set; } = new();

    public List<ErrorDeArchivo> Errores { get; set; } = [];

    /// <summary>Lo que el diagnóstico no pudo saber (p. ej. qué está cancelado en el SAT).</summary>
    public string? Nota { get; set; }
}

public class LadoDelDiagnostico
{
    public int ApartadosQueCuadran { get; set; }
    public List<ApartadoPorRevisar> PorRevisar { get; set; } = [];
    public SueltosDelDiagnostico Sueltos { get; set; } = new();
}

/// <summary>Facturas, pagos y notas de crédito que se tocan entre sí, y lo que no cuadra entre ellos.</summary>
public class ApartadoPorRevisar
{
    public string? Fecha { get; set; }

    /// <summary>Positiva: falta por saldar; negativa: se aplicó de más.</summary>
    public decimal Diferencia { get; set; }

    public List<CfdiDelDiagnostico> Facturas { get; set; } = [];
    public List<CfdiDelDiagnostico> Pagos { get; set; } = [];
    public List<CfdiDelDiagnostico> Egresos { get; set; } = [];

    /// <summary>Qué pasó, en frases cortas (cada CFDI se nombra por los primeros 8 caracteres de su UUID).</summary>
    public List<string> Avisos { get; set; } = [];
}

public class SueltosDelDiagnostico
{
    /// <summary>PPD sin ningún complemento ni nota: puede ser que aún no se cobre, o que el complemento falte.</summary>
    public List<CfdiDelDiagnostico> FacturasPpdSinNada { get; set; } = [];

    public List<CfdiDelDiagnostico> PagosSinDocumento { get; set; } = [];
    public List<CfdiDelDiagnostico> EgresosSinRelacion { get; set; } = [];
}

public class CfdiDelDiagnostico
{
    public string Uuid { get; set; } = "";

    /// <summary>Ingreso, Egreso o Pago.</summary>
    public string? Tipo { get; set; }

    public string? Fecha { get; set; }
    public string? SerieFolio { get; set; }
    public string? RfcEmisor { get; set; }
    public string? RfcReceptor { get; set; }
    public string? NombreEmisor { get; set; }
    public string? NombreReceptor { get; set; }

    /// <summary>PUE o PPD.</summary>
    public string? Metodo { get; set; }

    public decimal? Total { get; set; }

    /// <summary>Se relaciona, pero no está en lo que se envió (otro mes, o no se ha descargado).</summary>
    public bool Falta { get; set; }
}
