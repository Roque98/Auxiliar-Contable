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

    public void Reiniciar()
    {
        Analisis.Clear();
        Analizados.Clear();
        Amarres = null;
        PeticionesDeAmarre = 0;
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
