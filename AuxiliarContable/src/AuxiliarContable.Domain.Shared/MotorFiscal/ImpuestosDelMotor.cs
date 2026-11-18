using System.Collections.Generic;

namespace AuxiliarContable.MotorFiscal;

// Pago provisional de ISR e IVA solo con los CFDI (/impuestos/provisionales en motores-fiscales): el papel de trabajo de
// impuestos del Armador (Excel con fórmulas, tarifa del Anexo 8) y, por mes del ejercicio, lo que mueve el impuesto.

public class PagosProvisionales
{
    public RegimenDelCalculo Regimen { get; set; } = new();

    /// <summary>De enero al mes que se calcula.</summary>
    public List<MesDeImpuestos> Meses { get; set; } = [];

    public List<string> Avisos { get; set; } = [];

    public List<ErrorDeArchivo> Errores { get; set; } = [];

    /// <summary>El papel de trabajo (.xlsx) en base64.</summary>
    public ArchivoDelMotor? Archivo { get; set; }
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
}

public class ArchivoDelMotor
{
    public string Nombre { get; set; } = "";
    public string ContenidoB64 { get; set; } = "";
}
