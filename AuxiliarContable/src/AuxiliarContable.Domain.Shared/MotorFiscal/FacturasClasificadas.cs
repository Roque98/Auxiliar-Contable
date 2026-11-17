using System.Collections.Generic;

namespace AuxiliarContable.MotorFiscal;

// «Mis facturas» (motor /cfdi/clasificar): cada factura del mes leída con el código del Armador y clasificada; en
// compras, la categoría de cada concepto según el giro del cliente (librería producto × giro). La consola la muestra.

public class FacturasClasificadas
{
    /// <summary>La actividad de la librería con que se clasificó (sale del giro del perfil); null si no se conoce.</summary>
    public ActividadDelGiro? Actividad { get; set; }

    public List<FacturaClasificada> Facturas { get; set; } = [];
    public List<ErrorDeArchivo> Errores { get; set; } = [];
}

public class ActividadDelGiro
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
}

public class FacturaClasificada
{
    public string Uuid { get; set; } = "";
    public string? Archivo { get; set; }

    /// <summary>emitida o recibida.</summary>
    public string Lado { get; set; } = "";

    /// <summary>Ingreso, Egreso, Pago, Nómina o Traslado.</summary>
    public string? Tipo { get; set; }

    public string? Fecha { get; set; }
    public string? SerieFolio { get; set; }
    public string? RfcContraparte { get; set; }
    public string? NombreContraparte { get; set; }
    public decimal? Total { get; set; }

    /// <summary>PUE o PPD.</summary>
    public string? Metodo { get; set; }

    public string? Forma { get; set; }
    public string? Uso { get; set; }

    /// <summary>Solo PPD: por_definir (sin complemento), parcial o pagada.</summary>
    public string? EstadoDePago { get; set; }

    public decimal? Pagado { get; set; }

    /// <summary>venta, complemento_de_pago, nota_de_credito, nomina, traslado, inversion, deduccion_personal, mercancia,
    /// insumo_directo, gasto_indirecto, gasto_operacion, ajeno o sin_clasificar.</summary>
    public string Categoria { get; set; } = "";

    public List<ConceptoClasificado> Conceptos { get; set; } = [];
}

public class ConceptoClasificado
{
    public string? Clave { get; set; }
    public string? Descripcion { get; set; }
    public decimal? Importe { get; set; }
    public string? Categoria { get; set; }
    public string? Certeza { get; set; }
    public string? Fundamento { get; set; }
}
