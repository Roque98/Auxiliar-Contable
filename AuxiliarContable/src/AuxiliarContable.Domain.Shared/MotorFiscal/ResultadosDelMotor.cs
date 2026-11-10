using System.Collections.Generic;

namespace AuxiliarContable.MotorFiscal;

// Lo que devuelve el motor fiscal (servicio/bancos.py en motores-fiscales), tal cual. Van en
// Domain.Shared porque son a la vez el resultado de IMotorFiscal y lo que la API enseña: la consola
// no los reinterpreta ni los guarda, solo los muestra (y el chat los resume).

/// <summary>Uno o varios estados de cuenta leídos.</summary>
public class LecturaDeEstados
{
    public List<CuentaLeida> Cuentas { get; set; } = [];

    /// <summary>Archivos que no se pudieron leer (PDF dañado, escaneo ilegible…): no detienen a los demás.</summary>
    public List<ErrorDeArchivo> Errores { get; set; } = [];
}

public class CuentaLeida
{
    public string Archivo { get; set; } = "";
    public string Banco { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Titular { get; set; } = "";
    public string Rfc { get; set; } = "";
    public string Cuenta { get; set; } = "";
    public string Clabe { get; set; } = "";
    public string? PeriodoInicial { get; set; }
    public string? PeriodoFinal { get; set; }
    public List<MovimientoLeido> Movimientos { get; set; } = [];

    /// <summary>Lo calculado contra lo que imprime el banco (saldo inicial, depósitos, retiros, saldo final…).</summary>
    public List<RenglonDeCuadre> Cuadre { get; set; } = [];

    public List<HallazgoDeLectura> Hallazgos { get; set; } = [];

    /// <summary>El banco imprimió totales y todos coinciden. Sin nada impreso no se puede afirmar: false.</summary>
    public bool Cuadra { get; set; }

    /// <summary>Cada movimiento quedó comprobado con el saldo impreso.</summary>
    public bool TodosSeguros { get; set; }

    /// <summary>El método de siempre no la pudo comprobar y la leyó Claude (claude -p en el motor).</summary>
    public bool LeidaConClaude { get; set; }
}

public class MovimientoLeido
{
    public int? N { get; set; }
    public string? Fecha { get; set; }
    public string Concepto { get; set; } = "";
    public string Referencia { get; set; } = "";
    public string Rastreo { get; set; } = "";
    public string Contraparte { get; set; } = "";
    public string? RfcContraparte { get; set; }

    /// <summary>«abono» (entra dinero) o «cargo» (sale).</summary>
    public string Naturaleza { get; set; } = "";

    public decimal Importe { get; set; }
    public decimal? Saldo { get; set; }
    public bool Seguro { get; set; }
    public string Observaciones { get; set; } = "";

    /// <summary>Solo en la conciliación: a qué cuenta pertenece y qué se le encontró.</summary>
    public string? Cuenta { get; set; }

    public string? Banco { get; set; }
    public ConciliacionDeMovimiento? Conciliacion { get; set; }
}

public class RenglonDeCuadre
{
    public string Concepto { get; set; } = "";
    public decimal? Declarado { get; set; }
    public decimal? Calculado { get; set; }
    public decimal? Diferencia { get; set; }

    /// <summary>null = el banco no imprime ese dato.</summary>
    public bool? Ok { get; set; }

    public string Pista { get; set; } = "";
}

public class HallazgoDeLectura
{
    /// <summary>Índice del movimiento (desde 0) o null si es de la cuenta.</summary>
    public int? Movimiento { get; set; }

    /// <summary>«error» o «aviso».</summary>
    public string Nivel { get; set; } = "";

    public string Mensaje { get; set; } = "";
    public string Sugerencia { get; set; } = "";
}

public class ErrorDeArchivo
{
    public string Archivo { get; set; } = "";
    public string Error { get; set; } = "";
}

/// <summary>Estados de cuenta contra las facturas (XML) del periodo.</summary>
public class ResultadoDeConciliacion
{
    /// <summary>Cada cuenta con su cuadre (sin los movimientos: van en <see cref="Movimientos"/>).</summary>
    public List<CuentaLeida> Cuentas { get; set; } = [];

    public List<MovimientoLeido> Movimientos { get; set; } = [];
    public List<FacturaDelMotor> Facturas { get; set; } = [];
    public List<FacturaDelMotor> FacturasSinMovimiento { get; set; } = [];
    public List<AlertaDeConciliacion> Alertas { get; set; } = [];
    public List<ErrorDeArchivo> Errores { get; set; } = [];
}

public class ConciliacionDeMovimiento
{
    /// <summary>conciliado, combinacion (varias facturas o varios movimientos), traspaso, sin_factura o pendiente.</summary>
    public string Estado { get; set; } = "";

    /// <summary>UUID de las facturas que le corresponden.</summary>
    public List<string> Facturas { get; set; } = [];

    public decimal? Probabilidad { get; set; }
    public string Motivo { get; set; } = "";
}

public class FacturaDelMotor
{
    public string? Uuid { get; set; }

    /// <summary>«Emitidos» (el cliente la emitió) o «Recibidos».</summary>
    public string? Naturaleza { get; set; }

    public string? Tercero { get; set; }
    public string? RfcTercero { get; set; }
    public string? SerieFolio { get; set; }
    public string? TipoCfdi { get; set; }
    public string? Fecha { get; set; }
    public decimal? Importe { get; set; }
    public string? Forma { get; set; }
    public string? Clase { get; set; }

    /// <summary>Solo en las facturas sin movimiento: por qué no apareció en el banco.</summary>
    public string? Motivo { get; set; }
}

/// <summary>Lo que el motor leyó de una Constancia de Situación Fiscal (el PDF del SAT, con texto).</summary>
public class ConstanciaLeida
{
    /// <summary>false: no es una constancia o no tiene texto (foto, escaneo).</summary>
    public bool EsConstancia { get; set; }

    public string Rfc { get; set; } = "";

    /// <summary>«fisica» o «moral» (por el largo del RFC).</summary>
    public string TipoPersona { get; set; } = "";

    public string Nombre { get; set; } = "";
    public string CodigoPostal { get; set; } = "";
    public string Entidad { get; set; } = "";

    /// <summary>Los vigentes, «612 · Personas Físicas con Actividades Empresariales y Profesionales».</summary>
    public List<string> Regimenes { get; set; } = [];

    public List<string> Obligaciones { get; set; } = [];

    /// <summary>Las vigentes, «Panificación tradicional (100 %)».</summary>
    public List<string> Actividades { get; set; } = [];
}

public class AlertaDeConciliacion
{
    public string? Uuid { get; set; }
    public int? Movimiento { get; set; }
    public string Mensaje { get; set; } = "";
}
