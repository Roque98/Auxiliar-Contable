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
        LeyesLeidas.Clear();
        LeyesAnalizadas.Clear();
        AnalisisDeLeyes = AnalisisDeLeyesPorDefecto;
        PreguntasDeLeyes.Clear();
        CatalogosPedidos.Clear();
        DomiciliosPedidos.Clear();
        Analisis.Clear();
        Analizados.Clear();
        Amarres = null;
        PeticionesDeAmarre = 0;
        Contabilidad = null;
        Contabilizadas = [];
        Electronica = null;
        CatalogoRecibido = null;
        Gasto = [];
        Incidencias = null;
        IncidenciasPedidas = null;
        NominaContabilizada = null;
        Provisionales = null;
        AdeudosRecibidos.Clear();
        ExpedientesPedidos.Clear();
        TarifasPedidas.Clear();
        LeyesLeidasSeguidas.Clear();
        PerfilesDeExpediente.Clear();
        DetalleDeProvisionales = null;
        TodosLosMeses = false;
        IepsDelMes = null;
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
        string periodo, DatosDeCierre? cierre, IReadOnlyList<NominaParaContabilizar>? nomina = null, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        Contabilizadas = operaciones;
        NominaContabilizada = nomina;
        PeriodoRecibido = periodo;
        CierreRecibido = cierre;
        return Task.FromResult(Contabilidad?.Invoke(perfil, operaciones, facturas) ?? new ResultadoContable());
    }

    public Task<ContabilidadElectronica> ContabilidadElectronicaAsync(
        string rfc, int anio, int mes, AperturaParaElMotor? apertura, IReadOnlyList<MesParaElMotor> meses,
        ArchivoParaElMotor? catalogo = null, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        Electronica = (apertura, meses);
        CatalogoRecibido = catalogo;
        return Task.FromResult(new ContabilidadElectronica
        {
            Balanza = [new RenglonDeBalanza { Cuenta = "102.01", Nivel = 2, SaldoFinal = 1 }],
            Archivos = [new ArchivoXml { Nombre = rfc + $"{anio:0000}{mes:00}BN.xml", Tipo = "BN", ContenidoB64 = "PEJhbGFuemEvPg==" }],
            Vencimiento = "2026-12-03",
        });
    }

    /// <summary>Los periodos de nómina que llegaron a la última contabilización.</summary>
    public IReadOnlyList<NominaParaContabilizar>? NominaContabilizada { get; private set; }

    /// <summary>Lo que devuelve <see cref="LeerIncidenciasAsync"/> (null = sin nada leído) y lo que se le mandó.</summary>
    public IncidenciasLeidas? Incidencias { get; set; }

    public (IReadOnlyList<ArchivoParaElMotor> Archivos, IReadOnlyList<(string Id, string Nombre)> Trabajadores)? IncidenciasPedidas { get; private set; }

    public Task<IncidenciasLeidas> LeerIncidenciasAsync(IReadOnlyList<ArchivoParaElMotor> archivos, IReadOnlyList<(string Id, string Nombre)> trabajadores,
        DateTime inicio, DateTime fin, CancellationToken cancellationToken = default)
    {
        IncidenciasPedidas = (archivos, trabajadores);
        return Task.FromResult(Incidencias ?? new IncidenciasLeidas());
    }

    /// <summary>Lo que devuelve <see cref="GastoDeClaudeAsync"/>.</summary>
    public List<GastoDeClaude> Gasto { get; set; } = [];

    public Task<List<GastoDeClaude>> GastoDeClaudeAsync(DateTime desde, DateTime hasta, CancellationToken cancellationToken = default) =>
        Caido ? throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.") : Task.FromResult(Gasto);

    /// <summary>El catálogo del cliente que llegó a la contabilidad electrónica (null = código agrupador).</summary>
    public ArchivoParaElMotor? CatalogoRecibido { get; private set; }

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

    public List<(PerfilParaNormativa Perfil, OperacionParaNormativa Operacion)> NormativaRecibida { get; } = [];

    public List<OperacionParaNormativa> ExpedientesPedidos { get; } = [];

    public List<PerfilParaNormativa> PerfilesDeExpediente { get; } = [];

    public Task<string> ExpedienteNormativoAsync(PerfilParaNormativa perfil, OperacionParaNormativa operacion,
        CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        ExpedientesPedidos.Add(operacion);
        PerfilesDeExpediente.Add(perfil);
        return Task.FromResult(
            "{\"chispazos\":[{\"disparador\":\"extranjero\",\"pregunta\":\"¿Califica como exportación?\","
            + "\"fuentes\":[{\"clave\":\"LIVA\",\"articulo\":\"29\",\"url\":\"\"}]}],\"avisos\":[]}");
    }

    public List<(int Dias, decimal? Base, int? Mes)> TarifasPedidas { get; } = [];

    public List<(string Clave, int Desde)> LeyesLeidasSeguidas { get; } = [];

    public Task<string> LeerLeyAsync(string clave, int desde, int n, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        LeyesLeidasSeguidas.Add((clave, desde));
        return Task.FromResult(
            "{\"clave\":\"RMF-A8\",\"nombre\":\"Anexo 8 de la RMF 2026\",\"total\":9,\"siguiente\":4,"
            + "\"fragmentos\":[{\"texto\":\"B. Tarifas aplicables a retenciones … calculada en días … 0.01 27.78 0.00 1.92\"}]}");
    }

    public Task<string> TarifaIsrAsync(int dias, decimal? baseGravable, int? mes, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        TarifasPedidas.Add((dias, baseGravable, mes));
        return Task.FromResult(
            "{\"periodo\":\"semanal\",\"dias\":7,\"fundamento\":\"Art. 96 LISR y Anexo 8 de la RMF 2026\","
            + "\"tarifa\":[{\"limite_inferior\":0.01,\"limite_superior\":194.46,\"cuota_fija\":0.0,\"porcentaje_excedente\":1.92}],"
            + "\"calculo\":{\"isr_tarifa\":189.36,\"subsidio_para_el_empleo\":123.34,\"isr_a_retener\":66.02}}");
    }

    public Task<string> AnalizarNormativaAsync(PerfilParaNormativa perfil, OperacionParaNormativa operacion,
        CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        NormativaRecibida.Add((perfil, operacion));
        return Task.FromResult(
            "{\"analisis\":{\"conclusion\":\"Tasa 0% si se cumplen los requisitos.\",\"confianza\":\"media\"},\"expediente\":{}}");
    }

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

    /// <summary>Lo que recibió el último cálculo de nómina.</summary>
    public (PatronParaNomina Patron, PeriodoParaNomina Periodo, IReadOnlyList<TrabajadorParaNomina> Trabajadores)? NominaRecibida { get; private set; }

    public Task<NominaDelMotor> CalcularNominaAsync(
        PatronParaNomina patron, PeriodoParaNomina periodo, IReadOnlyList<TrabajadorParaNomina> trabajadores,
        CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        NominaRecibida = (patron, periodo, trabajadores);
        return Task.FromResult(new NominaDelMotor
        {
            Version = "prueba",
            Trabajadores = [.. trabajadores.Select(t => new NominaDeTrabajador { Id = t.Id, Nombre = t.Nombre, Neto = t.SalarioDiario * periodo.Dias })],
            Totales = new TotalesDeNomina { Neto = trabajadores.Sum(t => t.SalarioDiario * periodo.Dias) },
        });
    }

    public IReadOnlyList<MovimientoParaIdse>? IdseRecibido { get; private set; }

    public Task<ArchivosDeIdse> ArchivosIdseAsync(
        string registroPatronal, string? guia, string? zona, IReadOnlyList<MovimientoParaIdse> movimientos,
        CancellationToken cancellationToken = default)
    {
        IdseRecibido = movimientos;
        return Task.FromResult(new ArchivosDeIdse
        {
            Archivos = [.. movimientos.GroupBy(m => m.Tipo).Select(g => new ArchivoDeIdse { Nombre = g.Key + ".txt", Tipo = g.Key, Renglones = g.Count(), ContenidoB64 = "QQ==" })],
        });
    }

    public Task<RecibosDeNomina> RecibosDeNominaAsync(
        PatronParaRecibos patron, PeriodoParaNomina periodo, string periodicidad, NominaDelMotor calculo,
        IReadOnlyList<TrabajadorParaRecibo> trabajadores, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RecibosDeNomina
        {
            Recibos = [.. trabajadores.Select(t => new ReciboDeNomina { Id = t.Id, Nombre = t.Nombre, Archivo = t.Id + ".xml", Faltan = t.Curp == null ? ["CURP"] : [] })],
            ZipB64 = "UEs=",
            Validos = trabajadores.Count(t => t.Curp != null),
            ConErrores = trabajadores.Count(t => t.Curp == null),
        });

    public Task<CuotasDeNomina> CuotasDeNominaAsync(IReadOnlyList<NominaDelMotor> calculos, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CuotasDeNomina { TotalMensual = 100 * calculos.Count, Mensuales = [new CuotaDelMes { Ramo = "Invalidez y vida", Total = 100 * calculos.Count }] });

    public (IReadOnlyList<TrabajadorParaSua> Trabajadores, IReadOnlyList<MovimientoParaSua> Movimientos, IReadOnlyList<IncidenciaParaSua> Incidencias)? SuaRecibido { get; private set; }

    public Task<ArchivosDeSua> ArchivosSuaAsync(
        string registroPatronal, string? zona, int anio, int mes, IReadOnlyList<TrabajadorParaSua> trabajadores,
        IReadOnlyList<MovimientoParaSua> movimientos, IReadOnlyList<IncidenciaParaSua> incidencias,
        CancellationToken cancellationToken = default)
    {
        SuaRecibido = (trabajadores, movimientos, incidencias);
        return Task.FromResult(new ArchivosDeSua
        {
            ZipB64 = "UEs=",
            Archivos = [new ArchivoDeSua { Nombre = "1 SUA trabajadores.txt", Renglones = trabajadores.Count }],
        });
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


    public List<CobroDelBanco> CobrosDelBancoRecibidos { get; private set; } = [];

    public Task<PagosProvisionales> PagosProvisionalesAsync(
        string rfc, IReadOnlyList<string> regimenes, string periodo, string? nombre, IReadOnlyList<ArchivoParaElMotor> facturas,
        IReadOnlyList<string> cancelados, decimal? coeficienteDeUtilidad, bool conExcel, string? plataformas = null,
        string? actividadDePlataformas = null, IReadOnlyList<CobroDelBanco>? cobrosDelBanco = null,
        CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        CobrosDelBancoRecibidos = cobrosDelBanco?.ToList() ?? [];
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
            Detalle = DetalleDeProvisionales,
        }).ContinueWith(t =>
        {
            if (IepsDelMes != null)
            {
                var m = t.Result.Meses[^1];
                (m.IepsCausado, m.IepsAcreditable, m.IepsAPagar, m.IepsSaldoAFavor) = (IepsDelMes, 0, IepsDelMes, 0);
                m.RenglonesIeps = [new RenglonDelPapel { Concepto = "(=) IEPS a pagar", Importe = IepsDelMes, Tipo = "resultado" }];
            }

            if (TodosLosMeses)
            {
                var mes = int.Parse(periodo[5..]);
                t.Result.Meses.InsertRange(0, Enumerable.Range(1, mes - 1).Select(k => new MesDeImpuestos
                {
                    Periodo = $"{periodo[..4]}-{k:00}", IsrAPagar = 100, IvaAPagar = 160, IvaCobrado = 160, Metodo = "art. 106",
                }));
            }

            return t.Result;
        });
    }

    /// <summary>Lo que se le pidió a /impuestos/accesorios.</summary>
    public List<AdeudoParaElMotor> AdeudosRecibidos { get; } = [];

    /// <summary>El pago provisional trae de enero al mes (100 de ISR y 160 de IVA cada mes anterior).</summary>
    public bool TodosLosMeses { get; set; }

    /// <summary>El IEPS a pagar del mes que se calcula (null: sin IEPS en las facturas).</summary>
    public decimal? IepsDelMes { get; set; }

    /// <summary>Con qué detalle contesta el pago provisional (null: sin detalle).</summary>
    public DetalleDelCalculo? DetalleDeProvisionales { get; set; }

    public Task<AccesoriosDelMotor> AccesoriosAsync(IReadOnlyList<AdeudoParaElMotor> adeudos, DateTime? pago = null,
        CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        AdeudosRecibidos.AddRange(adeudos);
        // 1 % de actualización y 2.07 % de recargos por mes de atraso (lo de verdad lo prueba el motor)
        return Task.FromResult(new AccesoriosDelMotor
        {
            Pago = (pago ?? DateTime.Today).ToString("yyyy-MM-dd"), Tasa = 0.0207m,
            Adeudos = adeudos.Select(a => new AccesorioDelMotor
            {
                Clave = a.Clave, Importe = a.Importe, Limite = a.Limite.ToString("yyyy-MM-dd"), Meses = 1, Factor = 1.01m,
                Actualizacion = Math.Round(a.Importe * 0.01m, 2), Recargos = Math.Round(a.Importe * 1.01m * 0.0207m, 2),
                Total = Math.Round(a.Importe + a.Importe * 0.01m + a.Importe * 1.01m * 0.0207m, 2), Fundamento = "arts. 17-A y 21 CFF",
            }).ToList(),
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

    /// <summary>Leyes que «tiene leídas» el motor (por SHA-256) y lo que se le pidió analizar o preguntar.</summary>
    public HashSet<string> LeyesLeidas { get; } = [];

    public List<(IReadOnlyList<string> Documentos, IReadOnlyDictionary<string, object?> Perfil, IReadOnlyDictionary<string, object?> Sistema)>
        LeyesAnalizadas { get; } = [];

    /// <summary>El JSON que devuelve el análisis de leyes; el test lo puede cambiar.</summary>
    public string AnalisisDeLeyes { get; set; } = AnalisisDeLeyesPorDefecto;

    public const string AnalisisDeLeyesPorDefecto =
        "{\"resumen\":\"Ley de hacienda de prueba.\",\"no_encontrado\":[],\"hallazgos\":[" +
        "{\"tipo\":\"valor\",\"tema\":\"isn\",\"titulo\":\"Tasa del ISN\",\"clave\":\"isn.tasa\",\"valor\":\"2.5 %\",\"valor_numerico\":0.025," +
        "\"valor_del_sistema\":\"3 %\",\"condicion\":\"\",\"periodicidad\":\"mensual\",\"fecha_limite\":\"\",\"articulo\":\"3\",\"etiqueta\":\"Artículo 3\"," +
        "\"pagina\":2,\"cita\":\"aplicando la tasa del 2.5% sobre el total\",\"explicacion\":\"Paga 2.5 %.\"}," +
        "{\"tipo\":\"valor\",\"tema\":\"isn\",\"titulo\":\"Día de pago\",\"clave\":\"isn.dia\",\"valor\":\"día 12\",\"valor_numerico\":12," +
        "\"valor_del_sistema\":\"17\",\"condicion\":\"\",\"periodicidad\":\"mensual\",\"fecha_limite\":\"día 12 del mes siguiente\",\"articulo\":\"4\"," +
        "\"etiqueta\":\"Artículo 4\",\"pagina\":2,\"cita\":\"a más tardar el día 12 del mes siguiente\",\"explicacion\":\"Paga el 12.\"}," +
        "{\"tipo\":\"obligacion\",\"tema\":\"proteccion_civil\",\"titulo\":\"Dictamen de protección civil\",\"clave\":\"\",\"valor\":\"20 UMA\"," +
        "\"valor_numerico\":null,\"valor_del_sistema\":\"\",\"condicion\":\"aforo mayor a 50\",\"periodicidad\":\"anual\",\"fecha_limite\":\"31 de marzo\"," +
        "\"articulo\":\"5\",\"etiqueta\":\"Artículo 5\",\"pagina\":3,\"cita\":\"deberán obtener anualmente el dictamen de protección civil\",\"explicacion\":\"…\"}]}";

    public List<string> PreguntasDeLeyes { get; } = [];

    public Task<LecturaDeLeyDelMotor> IndexarLeyAsync(ArchivoParaElMotor ley, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(ley.Contenido));
        LeyesLeidas.Add(sha);
        return Task.FromResult(new LecturaDeLeyDelMotor
        {
            Documento = sha, Estado = "lista", Paginas = 4, PaginasOcr = 1, PaginasLeidas = 1, ArticulosTotal = 7,
            Titulo = "LEY DE HACIENDA DEL ESTADO DE PRUEBA",
        });
    }

    public Task<LecturaDeLeyDelMotor?> EstadoDeLeyAsync(string sha256, CancellationToken cancellationToken = default) =>
        Caido ? throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.")
            : Task.FromResult(LeyesLeidas.Contains(sha256) ? new LecturaDeLeyDelMotor { Documento = sha256, Estado = "lista", ArticulosTotal = 7 } : null);

    public Task<string> AnalizarLeyesAsync(IReadOnlyList<string> documentos, IReadOnlyDictionary<string, object?> perfil,
        IReadOnlyDictionary<string, object?> sistema, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        if (documentos.Any(d => !LeyesLeidas.Contains(d)))
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal respondió 404.") { Data = { ["Codigo"] = 404 } };
        }

        LeyesAnalizadas.Add((documentos, perfil, sistema));
        return Task.FromResult(AnalisisDeLeyes);
    }

    public Task<string> PreguntarLeyesAsync(IReadOnlyList<string> documentos, string pregunta, IReadOnlyDictionary<string, object?> perfil,
        CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        PreguntasDeLeyes.Add(pregunta);
        return Task.FromResult("{\"encontrado\":true,\"respuesta\":\"Hasta el día 12.\",\"falta\":\"\",\"citas\":[{\"documento\":\""
                               + documentos[0] + "\",\"articulo\":\"4\",\"etiqueta\":\"Artículo 4\",\"pagina\":2,\"cita\":\"a más tardar el día 12\"}]}");
    }

    /// <summary>Los perfiles con que se pidió el catálogo estatal.</summary>
    public List<(IReadOnlyDictionary<string, object?> Perfil, string? Estado)> CatalogosPedidos { get; } = [];

    public Task<string> LeyesEstatalesDelClienteAsync(IReadOnlyDictionary<string, object?> perfil, string? estado,
        CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        CatalogosPedidos.Add((perfil, estado));
        return Task.FromResult(
            "{\"estado\":\"" + estado + "\",\"nombre_estado\":\"Estado de prueba\",\"municipio\":\"Guadalajara\",\"municipio_inegi\":\"14039\",\"cobertura_municipio\":\"cubierto\"," +
            "\"ficha_municipio\":{\"historico\":\"Gaceta municipal de prueba.\",\"fuentes\":[{\"url\":\"https://gaceta.ejemplo.gob.mx/\",\"que\":\"Gaceta\"}]},\"disparadores\":[\"siempre\",\"trabajadores\"]," +
            "\"ficha\":{\"historico\":\"Periódico oficial de prueba.\",\"fuentes\":[{\"url\":\"https://leyes.ejemplo.gob.mx/\",\"que\":\"Portal\"}]}," +
            "\"bloques\":{\"D\":\"Ambiental\",\"E\":\"Fiscal\",\"MC\":\"Municipal · Protección civil\"},\"no_aplican\":[],\"resumen\":{\"local\":1,\"pendiente\":1}," +
            "\"aplican\":[{\"id\":\"proteccion_civil\",\"nombre\":\"Ley de protección civil\",\"bloque\":\"D\",\"motivos\":[\"tiene local\"]," +
            "\"pide\":\"Programa interno\",\"temas\":[],\"anual\":false,\"ficha\":{\"estatus\":\"local\",\"nombre\":\"Ley Estatal de Protección Civil de Prueba\"," +
            "\"url\":\"https://leyes.ejemplo.gob.mx/pc.pdf\",\"ultima_reforma\":\"2026-01-01\",\"disponible\":true}}," +
            "{\"id\":\"mejora_regulatoria\",\"nombre\":\"Ley de mejora regulatoria\",\"bloque\":\"A\",\"motivos\":[\"todos\"],\"pide\":\"Trámites\"," +
            "\"temas\":[],\"anual\":false,\"ficha\":{\"estatus\":\"pendiente\",\"disponible\":false," +
            "\"lo_tratan\":[{\"nombre\":\"Bando de Policía y Gobierno\",\"donde\":\"arts. 27, 126\",\"id\":\"m_bando\"},{\"donde\":\"sin nombre: se ignora\"}]}}," +
            "{\"id\":\"m_proteccion_civil\",\"nombre\":\"Reglamento municipal de protección civil\",\"bloque\":\"MC\",\"ambito\":\"municipal\"," +
            "\"clasificacion\":\"actividad\",\"motivos\":[\"tiene local\"],\"pide\":\"Visto bueno\",\"temas\":[],\"anual\":false," +
            "\"ficha\":{\"estatus\":\"local\",\"nombre\":\"Reglamento de Protección Civil de Guadalajara\",\"disponible\":true}}]}");
    }

    private const string BalanceDePrueba = "\"universo\":40,\"tenemos\":3,\"no_aplica\":36,\"no_aplica_motivo\":{\"A\":30,\"B\":6}," +
                                           "\"no_encontrado\":1,\"sin_revisar\":0,\"avance_pct\":97.5,\"completo\":false";

    public Task<string> CoberturaMunicipalAsync(CancellationToken cancellationToken = default) =>
        Caido ? throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.")
            : Task.FromResult("{\"criterio\":\"Criterio de prueba\",\"elegidos\":2,\"completos\":0,\"universo_municipal\":40,\"universo_estatal\":72," +
                              "\"motivos_fuera\":{\"chico\":\"Menos de 30 000 habitantes\"}," +
                              "\"totales\":{\"cubierto\":1,\"seleccionado\":1,\"no_cubierto\":1}," +
                              "\"estados\":[{\"estado\":\"JAL\",\"nombre\":\"Jalisco\",\"municipios\":3,\"elegidos\":2,\"poblacion_cubierta_pct\":50.0," +
                              "\"poblacion_seleccionada_pct\":80.0,\"estatal\":{\"universo\":72,\"tenemos\":60,\"no_aplica\":12," +
                              "\"no_aplica_motivo\":{\"A\":4,\"B\":8},\"no_encontrado\":0,\"sin_revisar\":0,\"avance_pct\":100.0,\"completo\":true," +
                              "\"revisado\":\"2026-11-22\"}," +
                              "\"cubierto\":[{\"cvegeo\":\"14039\",\"nombre\":\"Guadalajara\",\"poblacion\":1385629,\"elegido\":true," + BalanceDePrueba + "}]," +
                              "\"seleccionado\":[{\"cvegeo\":\"14120\",\"nombre\":\"Zapopan\",\"poblacion\":1,\"elegido\":true}]," +
                              "\"no_cubierto\":[{\"cvegeo\":\"14001\",\"nombre\":\"Acatic\",\"poblacion\":23000,\"elegido\":false,\"motivo\":\"chico\"}]}]}");

    public Task<string> ExpedienteDeCoberturaAsync(string estado, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        if (estado != "JAL")
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal respondió 404.") { Data = { ["Codigo"] = 404 } };
        }

        const string norma = "{\"id\":\"m_rastro\",\"nombre\":\"Reglamento de rastro\",\"bloque\":\"MD\",\"clase\":\"no_aplica\"," +
                             "\"motivo\":\"B) No existe en este lugar o la cubre otra norma\",\"nota\":\"No tiene rastro municipal\",\"url\":\"javascript:x\"}";
        return Task.FromResult("{\"estado\":\"JAL\",\"nombre\":\"Jalisco\",\"revisado\":\"2026-11-22\",\"estatal\":{\"universo\":72,\"tenemos\":60," +
                               "\"no_aplica\":12,\"no_aplica_motivo\":{\"A\":4,\"B\":8},\"no_encontrado\":0,\"sin_revisar\":0,\"avance_pct\":100.0," +
                               "\"completo\":true,\"normas\":[]},\"municipios\":[{\"cvegeo\":\"14039\",\"nombre\":\"Guadalajara\",\"poblacion\":1385629," +
                               "\"nivel\":\"cubierto\"," + BalanceDePrueba + ",\"normas\":[" + norma + "]}]}");
    }

    public List<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DomiciliosPedidos { get; } = [];

    public Task<string> MunicipiosDeAsync(IReadOnlyList<IReadOnlyDictionary<string, object?>> domicilios, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        DomiciliosPedidos.Add(domicilios);
        var filas = domicilios.Select(d => (d["domicilio"] as string ?? "").Contains("Guadalajara")
            ? $"{{\"id\":\"{d["id"]}\",\"estado\":\"JAL\",\"municipio\":\"Guadalajara\",\"cvegeo\":\"14039\",\"carpeta\":\"guadalajara\",\"cobertura\":\"cubierto\"}}"
            : $"{{\"id\":\"{d["id"]}\",\"estado\":\"{d["estado"]}\",\"municipio\":\"Acatic\",\"cvegeo\":\"14001\",\"carpeta\":\"acatic\",\"cobertura\":\"no_cubierto\"}}");
        return Task.FromResult("[" + string.Join(",", filas) + "]");
    }

    public Task<LecturaDeLeyDelMotor> IndexarLeyEstatalAsync(string estado, string ley, string? municipio = null, CancellationToken cancellationToken = default)
    {
        if (Caido)
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.");
        }

        if (ley != "proteccion_civil" && !(ley == "m_proteccion_civil" && municipio == "14039"))
        {
            throw new MotorFiscalNoDisponibleException("El motor fiscal respondió 404.") { Data = { ["Codigo"] = 404 } };
        }

        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(estado + "/" + municipio + "/" + ley)));
        LeyesLeidas.Add(sha);
        return Task.FromResult(new LecturaDeLeyDelMotor
        {
            Documento = sha, Estado = "lista", Paginas = 60, ArticulosTotal = 200, NombreOficial = "Ley Estatal de Protección Civil de Prueba",
            UltimaReforma = "2026-01-01",
        });
    }
}
