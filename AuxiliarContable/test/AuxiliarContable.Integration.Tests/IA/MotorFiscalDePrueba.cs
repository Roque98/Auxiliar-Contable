using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.MotorFiscal;

namespace AuxiliarContable;

/// <summary>
/// El motor fiscal de los tests: devuelve lo que el test programe y apunta qué se le preguntó.
/// Nunca se llama al servicio Python desde los tests.
/// </summary>
public class MotorFiscalDePrueba : IMotorFiscal
{
    public bool EstaConfigurado { get; set; } = true;

    /// <summary>Simula el motor caído: cada consulta lanza como lo haría el cliente HTTP.</summary>
    public bool Caido { get; set; }

    public List<ResultadoDeLey> Resultados { get; } = [];

    public List<(string Consulta, string? Ley)> Consultas { get; } = [];

    /// <summary>Lo que «lee» o «concilia» el motor; el test lo arma.</summary>
    public LecturaDeEstados Lectura { get; set; } = new();

    public ResultadoDeConciliacion Conciliacion { get; set; } = new();

    /// <summary>Lo que «lee» de una constancia. Por defecto: no es constancia (los datos se capturan a mano).</summary>
    public ConstanciaLeida Constancia { get; set; } = new();

    /// <summary>Qué archivos le llegaron (nombre) y con qué RFC: lo que el test comprueba.</summary>
    public List<string> EstadosRecibidos { get; } = [];

    public List<string> FacturasRecibidas { get; } = [];

    public string? RfcRecibido { get; private set; }

    /// <summary>Lo que «encuentra» la librería de sentencias y qué se le buscó (situación, fuente).</summary>
    public List<SentenciaDelMotor> Sentencias { get; } = [];

    public List<(string Consulta, string? Fuente)> ConsultasDeSentencias { get; } = [];

    public EstadoDeSentencias EstadoSentencias { get; set; } = new();

    /// <summary>Cuántas veces se pidió actualizar la librería.</summary>
    public int Actualizaciones { get; private set; }

    /// <summary>Lo que «Claude» contesta por nombre de archivo (sin entrada = un análisis vacío de confianza baja).</summary>
    public Dictionary<string, AnalisisDeDocumento> Analisis { get; } = [];

    public List<string> Analizados { get; } = [];

    /// <summary>Lo que «Claude» propone al amarrar; recibe movimientos y candidatas.</summary>
    public Func<IReadOnlyList<MovimientoParaAmarrar>, IReadOnlyList<CandidataParaAmarrar>, SugerenciasDeAmarre>? Amarres { get; set; }

    public int PeticionesDeAmarre { get; private set; }

    /// <summary>Lo que el motor «contabiliza»; recibe lo que se le mandó.</summary>
    public Func<PerfilParaContabilizar, IReadOnlyList<OperacionParaContabilizar>, IReadOnlyList<ArchivoParaElMotor>, ResultadoContable>? Contabilidad { get; set; }

    public IReadOnlyList<OperacionParaContabilizar> Contabilizadas { get; private set; } = [];

    public string? PeriodoRecibido { get; private set; }

    /// <summary>Lo que se mandó a la contabilidad electrónica.</summary>
    public (AperturaParaElMotor? Apertura, IReadOnlyList<MesParaElMotor> Meses)? Electronica { get; private set; }

    public DatosDeCierre? CierreRecibido { get; private set; }

    /// <summary>Lo que se mandó al cálculo de pagos provisionales.</summary>
    public (string Rfc, IReadOnlyList<string> Regimenes, string Periodo, IReadOnlyList<string> Facturas, IReadOnlyList<string> Cancelados)? Provisionales { get; private set; }

    public decimal? CoeficienteRecibido { get; private set; }

    public int LlamadasDeProvisionales { get; private set; }

    public void Reiniciar()
    {
        Analisis.Clear();
        Analizados.Clear();
        Amarres = null;
        PeticionesDeAmarre = 0;
        Contabilidad = null;
        Contabilizadas = [];
        Electronica = null;
        Provisionales = null;
        CoeficienteRecibido = null;
        LlamadasDeProvisionales = 0;
        Interpretaciones = 0;
        InterpretacionRecibida = null;
        EstaConfigurado = true;
        Caido = false;
        Resultados.Clear();
        Consultas.Clear();
        Lectura = new LecturaDeEstados();
        Conciliacion = new ResultadoDeConciliacion();
        Constancia = new ConstanciaLeida();
        EstadosRecibidos.Clear();
        FacturasRecibidas.Clear();
        RfcRecibido = null;
        Sentencias.Clear();
        Diagnostico = new DiagnosticoDeFacturas();
        Clasificadas = new FacturasClasificadas();
        GiroRecibido = null;
        FacturasDiagnosticadas.Clear();
        ArchivosDelPeriodo = null;
        ConsultasDeSentencias.Clear();
        EstadoSentencias = new EstadoDeSentencias();
        Actualizaciones = 0;
    }

    public Task<IReadOnlyList<ResultadoDeLey>> BuscarEnLeyesAsync(
        string consulta, string? ley = null, int cuantos = 5, int desde = 0, CancellationToken cancellationToken = default)
    {
        Consultas.Add((consulta, ley));
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        return Task.FromResult<IReadOnlyList<ResultadoDeLey>>(Resultados.Skip(desde).Take(cuantos).ToArray());
    }

    public Task<LecturaDeEstados> LeerEstadosDeCuentaAsync(
        IReadOnlyList<ArchivoParaElMotor> archivos, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        EstadosRecibidos.AddRange(archivos.Select(a => a.Nombre));
        return Task.FromResult(Lectura);
    }

    public Task<ResultadoDeConciliacion> ConciliarAsync(
        string rfcDelCliente, IReadOnlyList<ArchivoParaElMotor> estadosDeCuenta, IReadOnlyList<ArchivoParaElMotor> facturas,
        CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        RfcRecibido = rfcDelCliente;
        EstadosRecibidos.AddRange(estadosDeCuenta.Select(a => a.Nombre));
        FacturasRecibidas.AddRange(facturas.Select(a => a.Nombre));
        return Task.FromResult(Conciliacion);
    }

    public Task<ConstanciaLeida> LeerConstanciaAsync(ArchivoParaElMotor constancia, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        return Task.FromResult(Constancia);
    }

    /// <summary>Lo que «diagnostica» el motor; el test lo arma.</summary>
    public DiagnosticoDeFacturas Diagnostico { get; set; } = new();

    /// <summary>Qué archivos le llegaron al diagnóstico, en orden, y cuántos eran del periodo.</summary>
    public List<string> FacturasDiagnosticadas { get; } = [];

    public int? ArchivosDelPeriodo { get; private set; }

    public Task<DiagnosticoDeFacturas> DiagnosticarFacturasAsync(
        string rfcDelCliente, IReadOnlyList<ArchivoParaElMotor> facturas, int archivosDelPeriodo,
        CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        RfcRecibido = rfcDelCliente;
        FacturasDiagnosticadas.AddRange(facturas.Select(f => f.Nombre));
        ArchivosDelPeriodo = archivosDelPeriodo;
        return Task.FromResult(Diagnostico);
    }

    /// <summary>Lo que «clasifica» el motor; el test lo arma. Y con qué giro se le pidió.</summary>
    public FacturasClasificadas Clasificadas { get; set; } = new();

    public string? GiroRecibido { get; private set; }

    public Task<AnalisisDeDocumento> AnalizarDocumentoAsync(
        ArchivoParaElMotor archivo, string? rfcDelCliente, string? giro, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        Analizados.Add(archivo.Nombre);
        GiroRecibido = giro;
        return Task.FromResult(Analisis.TryGetValue(archivo.Nombre, out var a)
            ? a
            : new AnalisisDeDocumento { Legible = false, Tipo = "otro", Confianza = "baja", RelacionConGiro = "no_aplica" });
    }

    public Task<ResultadoContable> ContabilizarAsync(
        PerfilParaContabilizar perfil, IReadOnlyList<OperacionParaContabilizar> operaciones, IReadOnlyList<ArchivoParaElMotor> facturas,
        string periodo, DatosDeCierre? cierre, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        Contabilizadas = operaciones;
        PeriodoRecibido = periodo;
        CierreRecibido = cierre;
        return Task.FromResult(Contabilidad?.Invoke(perfil, operaciones, facturas) ?? new ResultadoContable());
    }

    public Task<ContabilidadElectronica> ContabilidadElectronicaAsync(
        string rfc, int anio, int mes, AperturaParaElMotor? apertura, IReadOnlyList<MesParaElMotor> meses,
        CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        Electronica = (apertura, meses);
        return Task.FromResult(new ContabilidadElectronica
        {
            Balanza = [new RenglonDeBalanza { Cuenta = "102.01", Nivel = 2, SaldoFinal = 1 }],
            Archivos = [new ArchivoXml { Nombre = rfc + $"{anio:0000}{mes:00}BN.xml", Tipo = "BN", ContenidoB64 = "PEJhbGFuemEvPg==" }],
            Vencimiento = "2026-12-03",
        });
    }

    public (string? Pago, string? Actividad) PlataformasRecibidas { get; private set; }

    /// <summary>Cuántas veces se pidió una interpretación, y lo último que recibió.</summary>
    public int Interpretaciones { get; private set; }

    public (OperacionParaInterpretar Operacion, bool ConFactura, AnalisisDeDocumento? Control, BancoParaInterpretar? Banco)? InterpretacionRecibida { get; private set; }

    /// <summary>Lo que contesta «Claude» en las pruebas.</summary>
    public InterpretacionDeOperacion InterpretacionQueContesta { get; set; } = new()
    {
        Objeto = "Compresor de aire", Categoria = "inversion", Confianza = "alta",
        Conceptos = [new ConceptoInterpretado { Indice = 0, Categoria = "inversion", Isr = "inversion", Iva = "acreditable", Cuenta = "155.01" }],
        Alertas = [new AlertaDeInterpretacion { Tipo = "otra_interpretacion", Mensaje = "Es maquinaria", Fundamento = "art. 34 LISR" }],
    };

    public Task<InterpretacionDelMotor> InterpretarOperacionAsync(
        PerfilParaInterpretar perfil, OperacionParaInterpretar operacion, ArchivoParaElMotor? factura, AnalisisDeDocumento? control,
        BancoParaInterpretar? banco, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        Interpretaciones++;
        InterpretacionRecibida = (operacion, factura != null, control, banco);
        return Task.FromResult(new InterpretacionDelMotor { Version = "prueba", Huella = "h", Fuentes = ["factura"], Interpretacion = InterpretacionQueContesta });
    }

    /// <summary>Lo que recibió la última anual: ejercicio, archivos y lo capturado.</summary>
    public (int Anio, List<string> Facturas, IReadOnlyDictionary<string, decimal> Captura)? AnualRecibida { get; private set; }

    public Task<AnualDelMotor> AnualAsync(
        string rfc, IReadOnlyList<string> regimenes, int anio, IReadOnlyList<ArchivoParaElMotor> facturas,
        IReadOnlyList<string> cancelados, decimal? coeficienteDeUtilidad, string? plataformas, string? actividadDePlataformas,
        IReadOnlyDictionary<string, decimal> captura, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        AnualRecibida = (anio, facturas.Select(f => f.Nombre).ToList(), captura);
        return Task.FromResult(new AnualDelMotor
        {
            Ejercicio = anio,
            Tipo = "Persona física (art. 152 LISR)",
            Renglones = [new RenglonDelPapel { Concepto = "(=) ISR a cargo", Importe = 100, Tipo = "resultado" }],
            IsrDelEjercicio = 100,
            ACargo = 100,
            Vence = $"{anio + 1}-04-30",
        });
    }

    /// <summary>Lo que recibió la última DIOT: RFC, periodo, archivos, cancelados y regímenes.</summary>
    public (string Rfc, string Periodo, List<string> Facturas, IReadOnlyList<string> Cancelados, IReadOnlyList<string> Regimenes)? DiotRecibida { get; private set; }

    public Task<DiotDelMotor> DiotAsync(
        string rfc, string periodo, IReadOnlyList<ArchivoParaElMotor> facturas, IReadOnlyList<string> cancelados,
        IReadOnlyList<string> regimenes, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        DiotRecibida = (rfc, periodo, facturas.Select(f => f.Nombre).ToList(), cancelados, regimenes);
        return Task.FromResult(new DiotDelMotor
        {
            Periodo = periodo,
            Renglones = [new RenglonDeDiot { TipoTercero = "04", TipoOperacion = "85", TipoOperacionTexto = "Otros", Rfc = "PRO030303CCC", Valor16 = 1000, AcrExcl16 = 160, IvaAcreditable = 160, Cfdi = 1 }],
            Totales = new TotalesDeDiot { Valor16 = 1000, AcrExcl16 = 160, IvaAcreditable = 160 },
            Vencimiento = "2026-11-30",
            Archivo = new ArchivoDelMotor { Nombre = "DIOT.txt", ContenidoB64 = "MDR8ODV8" },
        });
    }


    public Task<PagosProvisionales> PagosProvisionalesAsync(
        string rfc, IReadOnlyList<string> regimenes, string periodo, string? nombre, IReadOnlyList<ArchivoParaElMotor> facturas,
        IReadOnlyList<string> cancelados, decimal? coeficienteDeUtilidad, bool conExcel, string? plataformas = null,
        string? actividadDePlataformas = null, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        PlataformasRecibidas = (plataformas, actividadDePlataformas);
        Provisionales = (rfc, regimenes, periodo, facturas.Select(f => f.Nombre).ToList(), cancelados);
        CoeficienteRecibido = coeficienteDeUtilidad;
        LlamadasDeProvisionales++;
        return Task.FromResult(new PagosProvisionales
        {
            Regimen = new RegimenDelCalculo { Principal = "612", Claves = ["612"], Flujo = true },
            Meses =
            [
                new MesDeImpuestos
                {
                    Periodo = periodo, Cobrado = 1000 * facturas.Count, IvaCobrado = 160, IvaEstimado = 160, CfdiEmitidos = facturas.Count,
                    Metodo = "art. 106", Base = 1000 * facturas.Count, IsrCausado = 19.2m * facturas.Count, IsrAPagar = 19.2m * facturas.Count,
                    IvaAPagar = 160,
                },
            ],
            Archivo = conExcel ? new ArchivoDelMotor { Nombre = "papel de impuestos.xlsx", ContenidoB64 = "UEsDBA==" } : null,
        });
    }

    public Task<SugerenciasDeAmarre> SugerirAmarresAsync(
        IReadOnlyList<MovimientoParaAmarrar> movimientos, IReadOnlyList<CandidataParaAmarrar> candidatas, string? giro,
        CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        PeticionesDeAmarre++;
        return Task.FromResult(Amarres?.Invoke(movimientos, candidatas) ?? new SugerenciasDeAmarre());
    }

    public Task<FacturasClasificadas> ClasificarFacturasAsync(
        string rfcDelCliente, IReadOnlyList<ArchivoParaElMotor> facturas, string? giro, int archivosDelPeriodo,
        CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        RfcRecibido = rfcDelCliente;
        GiroRecibido = giro;
        FacturasDiagnosticadas.AddRange(facturas.Select(f => f.Nombre));
        ArchivosDelPeriodo = archivosDelPeriodo;
        return Task.FromResult(Clasificadas);
    }

    public Task<IReadOnlyList<SentenciaDelMotor>> BuscarSentenciasAsync(
        string consulta, string? fuente = null, int cuantos = 5, int desde = 0, CancellationToken cancellationToken = default)
    {
        ConsultasDeSentencias.Add((consulta, fuente));
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        return Task.FromResult<IReadOnlyList<SentenciaDelMotor>>(Sentencias.Skip(desde).Take(cuantos).ToArray());
    }

    public Task<EstadoDeSentencias> EstadoDeSentenciasAsync(CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        return Task.FromResult(EstadoSentencias);
    }

    /// <summary>Como el motor: si ya hay una en curso no arranca otra.</summary>
    public Task<bool> ActualizarSentenciasAsync(CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        if (EstadoSentencias.Trabajo.Corriendo)
        {
            return Task.FromResult(false);
        }

        Actualizaciones++;
        EstadoSentencias.Trabajo.Corriendo = true;
        EstadoSentencias.Trabajo.Fase = "descargando";
        return Task.FromResult(true);
    }
}
