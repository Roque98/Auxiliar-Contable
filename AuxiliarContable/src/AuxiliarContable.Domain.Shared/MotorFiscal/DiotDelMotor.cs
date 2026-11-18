using System.Collections.Generic;

namespace AuxiliarContable.MotorFiscal;

/// <summary>
/// DIOT del mes (motor /diot, la del Armador): un renglón por proveedor y tipo de operación, por flujo de pagos
/// (LIVA 5-III), lo que no entra y por qué, y el archivo de carga masiva de 54 campos.
/// </summary>
public class DiotDelMotor
{
    public string Periodo { get; set; } = "";
    public List<RenglonDeDiot> Renglones { get; set; } = [];
    public TotalesDeDiot Totales { get; set; } = new();
    public List<CfdiFueraDeDiot> Excluidos { get; set; } = [];
    public List<string> Avisos { get; set; } = [];

    /// <summary>Proporción del IVA acreditable (LIVA 5-V) si el cliente tiene actos exentos; null si todo es gravado.</summary>
    public decimal? Proporcion { get; set; }

    /// <summary>AAAA-MM-DD: el último día del mes siguiente (regla 4.5.1 RMF).</summary>
    public string Vencimiento { get; set; } = "";

    /// <summary>El .txt para la carga masiva; null si no hay renglones.</summary>
    public ArchivoDelMotor? Archivo { get; set; }

    public List<ErrorDeArchivo> Errores { get; set; } = [];
}

/// <summary>Los importes de un renglón de la DIOT (enteros, como los pide el SAT) y su total.</summary>
public class TotalesDeDiot
{
    public decimal Valor16 { get; set; }
    public decimal Devol16 { get; set; }
    public decimal AcrExcl16 { get; set; }
    public decimal AcrProp16 { get; set; }
    public decimal NoProp16 { get; set; }
    public decimal NoReq16 { get; set; }
    public decimal ValorFn { get; set; }
    public decimal AcrExclFn { get; set; }
    public decimal AcrPropFn { get; set; }
    public decimal ValorFs { get; set; }
    public decimal AcrExclFs { get; set; }
    public decimal AcrPropFs { get; set; }
    public decimal Tasa0 { get; set; }
    public decimal Exento { get; set; }
    public decimal NoObjeto { get; set; }
    public decimal RetIva { get; set; }
    public decimal IvaAcreditable { get; set; }
}

public class RenglonDeDiot : TotalesDeDiot
{
    /// <summary>04 proveedor nacional, 15 proveedor global.</summary>
    public string TipoTercero { get; set; } = "";

    /// <summary>02 bienes, 03 servicios profesionales, 06 uso o goce, 85 otros…</summary>
    public string TipoOperacion { get; set; } = "";

    public string TipoOperacionTexto { get; set; } = "";
    public string Rfc { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int Cfdi { get; set; }
    public List<string> Notas { get; set; } = [];
    public List<AporteDeDiot> Detalle { get; set; } = [];
}

/// <summary>De dónde sale cada peso de un renglón: una factura PUE, un complemento de pago, una nota de crédito.</summary>
public class AporteDeDiot
{
    public string Uuid { get; set; } = "";
    public string Factura { get; set; } = "";
    public string Fecha { get; set; } = "";
    public string Como { get; set; } = "";
    public decimal Base { get; set; }
    public decimal Iva { get; set; }

    /// <summary>Por qué su IVA no es acreditable (efectivo de más de $2,000, 69-B…); null si sí lo es.</summary>
    public string? NoCumple { get; set; }
}

public class CfdiFueraDeDiot
{
    public string? Uuid { get; set; }
    public string? Rfc { get; set; }
    public string Nombre { get; set; } = "";
    public string Fecha { get; set; } = "";
    public string? Tipo { get; set; }
    public decimal Total { get; set; }
    public string Motivo { get; set; } = "";
}
