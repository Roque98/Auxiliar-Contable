using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.MotorFiscal;

/// <summary>
/// El motor fiscal (repositorio <c>motores-fiscales</c>, hecho por el contador): un servicio Python
/// que escucha solo en 127.0.0.1 del mismo servidor. Ver <c>MotorFiscalHttp</c> en el host.
///
/// <para>La consola NO reimplementa su lógica: le pregunta. Lo que devuelve son datos con su
/// fuente (ley, artículo, reforma) y se le enseñan así a la persona.</para>
/// </summary>
public interface IMotorFiscal
{
    /// <summary>¿Hay URL del motor? Sin ella cada uso lanza <see cref="MotorFiscalNoDisponibleException"/>.</summary>
    bool EstaConfigurado { get; }

    /// <summary>
    /// Busca en leyes, reglamentos, reglas del SAT y NIF. <paramref name="ley"/> son las siglas
    /// del ordenamiento (LISR, CFF, LIVA, RMF…) para acotar; null busca en todos. <paramref name="desde"/> salta
    /// los primeros (la pantalla pide de página en página). Las siglas que la ley no usa («RESICO») las traduce el motor.
    /// </summary>
    Task<IReadOnlyList<ResultadoDeLey>> BuscarEnLeyesAsync(
        string consulta, string? ley = null, int cuantos = 5, int desde = 0, CancellationToken cancellationToken = default);

    /// <summary>Lee estados de cuenta (PDF o foto) y los cuadra contra lo que imprime el banco.</summary>
    Task<LecturaDeEstados> LeerEstadosDeCuentaAsync(
        IReadOnlyList<ArchivoParaElMotor> archivos, CancellationToken cancellationToken = default);

    /// <summary>
    /// Estados de cuenta contra las facturas (XML) del periodo. <paramref name="rfcDelCliente"/> decide qué
    /// facturas son emitidas (el cliente es el emisor) y cuáles recibidas.
    /// </summary>
    Task<ResultadoDeConciliacion> ConciliarAsync(
        string rfcDelCliente, IReadOnlyList<ArchivoParaElMotor> estadosDeCuenta, IReadOnlyList<ArchivoParaElMotor> facturas,
        CancellationToken cancellationToken = default);

    /// <summary>Lee una Constancia de Situación Fiscal (el PDF que se descarga del SAT).</summary>
    Task<ConstanciaLeida> LeerConstanciaAsync(ArchivoParaElMotor constancia, CancellationToken cancellationToken = default);

    /// <summary>
    /// Facturas con sus complementos de pago y notas de crédito: lo que no cuadra y qué pasó. Los primeros
    /// <paramref name="archivosDelPeriodo"/> archivos son del mes; los demás (meses anteriores) solo dan la historia.
    /// </summary>
    Task<DiagnosticoDeFacturas> DiagnosticarFacturasAsync(
        string rfcDelCliente, IReadOnlyList<ArchivoParaElMotor> facturas, int archivosDelPeriodo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cada factura de los primeros <paramref name="archivosDelPeriodo"/> archivos, clasificada (lado, tipo, PPD por
    /// definir, categoría); <paramref name="giro"/> es cómo describe el cliente su negocio, para las compras.
    /// </summary>
    Task<FacturasClasificadas> ClasificarFacturasAsync(
        string rfcDelCliente, IReadOnlyList<ArchivoParaElMotor> facturas, string? giro, int archivosDelPeriodo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Un ticket, nota, comprobante o foto (OCR incluido) leído por Claude: sus datos y qué representa para el giro.
    /// <see cref="MotorFiscalNoDisponibleException"/> si el motor o claude -p no están disponibles.
    /// </summary>
    Task<AnalisisDeDocumento> AnalizarDocumentoAsync(
        ArchivoParaElMotor archivo, string? rfcDelCliente, string? giro, CancellationToken cancellationToken = default);

    /// <summary>Las operaciones del mes y sus XML -> revisión de errores, pólizas, balanza y pendientes de cierre.</summary>
    Task<ResultadoContable> ContabilizarAsync(
        PerfilParaContabilizar perfil, IReadOnlyList<OperacionParaContabilizar> operaciones, IReadOnlyList<ArchivoParaElMotor> facturas,
        string periodo, DatosDeCierre? cierre, CancellationToken cancellationToken = default);

    /// <summary>Saldos de apertura + pólizas de cada mes -> balanza acumulada del mes y XML del SAT (catálogo, balanza, pólizas).</summary>
    Task<ContabilidadElectronica> ContabilidadElectronicaAsync(
        string rfc, int anio, int mes, AperturaParaElMotor? apertura, IReadOnlyList<MesParaElMotor> meses,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pago provisional de ISR e IVA del <paramref name="periodo"/> (AAAA-MM) solo con los CFDI del ejercicio: el papel de
    /// trabajo de impuestos (Excel) y lo que mueve el impuesto mes por mes. <paramref name="cancelados"/>: UUID que el SAT
    /// dio por cancelados; <paramref name="regimenes"/>: los del perfil fiscal (nombres o claves).
    /// <paramref name="plataformas"/> y <paramref name="actividadDePlataformas"/>: régimen 625, «definitivo» o «provisional»
    /// (art. 113-B LISR) y qué hace por la plataforma (la tasa del 113-A); null si no se ha contestado.
    /// </summary>
    Task<PagosProvisionales> PagosProvisionalesAsync(
        string rfc, IReadOnlyList<string> regimenes, string periodo, string? nombre, IReadOnlyList<ArchivoParaElMotor> facturas,
        IReadOnlyList<string> cancelados, decimal? coeficienteDeUtilidad, bool conExcel, string? plataformas = null,
        string? actividadDePlataformas = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// DIOT del <paramref name="periodo"/> (AAAA-MM) con los CFDI del ejercicio y del anterior: un renglón por proveedor y
    /// tipo de operación, por flujo de pagos (LIVA 5-III), lo que no entra y el archivo de carga masiva.
    /// </summary>
    Task<DiotDelMotor> DiotAsync(
        string rfc, string periodo, IReadOnlyList<ArchivoParaElMotor> facturas, IReadOnlyList<string> cancelados,
        IReadOnlyList<string> regimenes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Declaración anual de ISR del ejercicio <paramref name="anio"/> con los CFDI del ejercicio y del anterior;
    /// <paramref name="captura"/>: lo que no viene en los CFDI (PTU, pérdidas, UMA, inventarios, sueldos, pagos).
    /// </summary>
    Task<AnualDelMotor> AnualAsync(
        string rfc, IReadOnlyList<string> regimenes, int anio, IReadOnlyList<ArchivoParaElMotor> facturas,
        IReadOnlyList<string> cancelados, decimal? coeficienteDeUtilidad, string? plataformas, string? actividadDePlataformas,
        IReadOnlyDictionary<string, decimal> captura, CancellationToken cancellationToken = default);

    /// <summary>
    /// Qué es de verdad una operación (factura, ticket y banco juntos) y qué es para el cliente, con claude -p.
    /// </summary>
    Task<InterpretacionDelMotor> InterpretarOperacionAsync(
        PerfilParaInterpretar perfil, OperacionParaInterpretar operacion, ArchivoParaElMotor? factura, AnalisisDeDocumento? control,
        BancoParaInterpretar? banco, CancellationToken cancellationToken = default);

    /// <summary>Nómina de un periodo: percepciones, ISR con subsidio, IMSS, INFONAVIT, FONACOT, ISN, neto y póliza.</summary>
    Task<NominaDelMotor> CalcularNominaAsync(
        PatronParaNomina patron, PeriodoParaNomina periodo, IReadOnlyList<TrabajadorParaNomina> trabajadores,
        CancellationToken cancellationToken = default);

    /// <summary>Archivos del IDSE (168 posiciones, uno por tipo de movimiento) y los movimientos que no pasan.</summary>
    Task<ArchivosDeIdse> ArchivosIdseAsync(
        string registroPatronal, string? guia, string? zona, IReadOnlyList<MovimientoParaIdse> movimientos,
        CancellationToken cancellationToken = default);

    /// <summary>Recibos de nómina (CFDI 4.0 + Nómina 1.2) sin sello, validados contra el XSD del SAT, en un .zip.</summary>
    Task<RecibosDeNomina> RecibosDeNominaAsync(
        PatronParaRecibos patron, PeriodoParaNomina periodo, string periodicidad, NominaDelMotor calculo,
        IReadOnlyList<TrabajadorParaRecibo> trabajadores, CancellationToken cancellationToken = default);

    /// <summary>Cuotas IMSS e INFONAVIT de varios periodos: mensuales y bimestrales.</summary>
    Task<CuotasDeNomina> CuotasDeNominaAsync(IReadOnlyList<NominaDelMotor> calculos, CancellationToken cancellationToken = default);

    /// <summary>Archivos de importación del SUA del mes (trabajadores 168, afiliatorios 80, movimientos 49) en un .zip.</summary>
    Task<ArchivosDeSua> ArchivosSuaAsync(
        string registroPatronal, string? zona, int anio, int mes, IReadOnlyList<TrabajadorParaSua> trabajadores,
        IReadOnlyList<MovimientoParaSua> movimientos, IReadOnlyList<IncidenciaParaSua> incidencias,
        CancellationToken cancellationToken = default);

    /// <summary>Movimientos del banco sin amarrar + operaciones sin pago -> parejas propuestas por Claude, con su porqué.</summary>
    Task<SugerenciasDeAmarre> SugerirAmarresAsync(
        IReadOnlyList<MovimientoParaAmarrar> movimientos, IReadOnlyList<CandidataParaAmarrar> candidatas, string? giro,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tesis y jurisprudencias del TFJA y de la SCJN sobre una situación o tema, con lo que se concluyó de cada una.
    /// <paramref name="fuente"/> (TFJA o SCJN) acota; null busca en las dos.
    /// </summary>
    Task<IReadOnlyList<SentenciaDelMotor>> BuscarSentenciasAsync(
        string consulta, string? fuente = null, int cuantos = 5, int desde = 0, CancellationToken cancellationToken = default);

    /// <summary>Cuánto hay en la librería de sentencias y cómo va su actualización.</summary>
    Task<EstadoDeSentencias> EstadoDeSentenciasAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Arranca en el motor la actualización (bajar lo nuevo y estudiarlo); no espera a que termine.
    /// Devuelve false si ya había una en curso.
    /// </summary>
    Task<bool> ActualizarSentenciasAsync(CancellationToken cancellationToken = default);
}

/// <summary>Un archivo de la bóveda, ya descifrado, para mandarlo al motor.</summary>
public record ArchivoParaElMotor(string Nombre, byte[] Contenido);

/// <summary>Un artículo (o fragmento) encontrado, con lo necesario para citarlo.</summary>
public record ResultadoDeLey(
    string Clave,
    string Ordenamiento,
    string? Articulo,
    string? Ubicacion,
    string? UltimaReformaDof,
    string? NotaDeVigencia,
    string? Url,
    string Texto);

/// <summary>El motor no está configurado, no responde o respondió con error.</summary>
public class MotorFiscalNoDisponibleException : AbpException
{
    public MotorFiscalNoDisponibleException(string mensaje, Exception? interna = null)
        : base(mensaje, interna)
    {
    }
}

/// <summary>Sin host (DbMigrator, tests) no hay motor: cualquier uso lanza.</summary>
[Dependency(TryRegister = true)]
[ExposeServices(typeof(IMotorFiscal))]
public class MotorFiscalSinConfigurar : IMotorFiscal, ISingletonDependency
{
    public bool EstaConfigurado => false;

    public Task<IReadOnlyList<ResultadoDeLey>> BuscarEnLeyesAsync(
        string consulta, string? ley = null, int cuantos = 5, int desde = 0, CancellationToken cancellationToken = default)
    {
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");
    }

    public Task<LecturaDeEstados> LeerEstadosDeCuentaAsync(
        IReadOnlyList<ArchivoParaElMotor> archivos, CancellationToken cancellationToken = default)
    {
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");
    }

    public Task<ResultadoDeConciliacion> ConciliarAsync(
        string rfcDelCliente, IReadOnlyList<ArchivoParaElMotor> estadosDeCuenta, IReadOnlyList<ArchivoParaElMotor> facturas,
        CancellationToken cancellationToken = default)
    {
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");
    }

    public Task<ConstanciaLeida> LeerConstanciaAsync(ArchivoParaElMotor constancia, CancellationToken cancellationToken = default)
    {
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");
    }

    public Task<DiagnosticoDeFacturas> DiagnosticarFacturasAsync(
        string rfcDelCliente, IReadOnlyList<ArchivoParaElMotor> facturas, int archivosDelPeriodo,
        CancellationToken cancellationToken = default)
    {
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");
    }

    public Task<FacturasClasificadas> ClasificarFacturasAsync(
        string rfcDelCliente, IReadOnlyList<ArchivoParaElMotor> facturas, string? giro, int archivosDelPeriodo,
        CancellationToken cancellationToken = default)
    {
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");
    }

    public Task<AnalisisDeDocumento> AnalizarDocumentoAsync(
        ArchivoParaElMotor archivo, string? rfcDelCliente, string? giro, CancellationToken cancellationToken = default) =>
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");

    public Task<ResultadoContable> ContabilizarAsync(
        PerfilParaContabilizar perfil, IReadOnlyList<OperacionParaContabilizar> operaciones, IReadOnlyList<ArchivoParaElMotor> facturas,
        string periodo, DatosDeCierre? cierre, CancellationToken cancellationToken = default) =>
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");

    public Task<ContabilidadElectronica> ContabilidadElectronicaAsync(
        string rfc, int anio, int mes, AperturaParaElMotor? apertura, IReadOnlyList<MesParaElMotor> meses,
        CancellationToken cancellationToken = default) =>
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");

    public Task<SugerenciasDeAmarre> SugerirAmarresAsync(
        IReadOnlyList<MovimientoParaAmarrar> movimientos, IReadOnlyList<CandidataParaAmarrar> candidatas, string? giro,
        CancellationToken cancellationToken = default) =>
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");

    public Task<PagosProvisionales> PagosProvisionalesAsync(
        string rfc, IReadOnlyList<string> regimenes, string periodo, string? nombre, IReadOnlyList<ArchivoParaElMotor> facturas,
        IReadOnlyList<string> cancelados, decimal? coeficienteDeUtilidad, bool conExcel, string? plataformas = null,
        string? actividadDePlataformas = null, CancellationToken cancellationToken = default) =>
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");

    public Task<AnualDelMotor> AnualAsync(
        string rfc, IReadOnlyList<string> regimenes, int anio, IReadOnlyList<ArchivoParaElMotor> facturas,
        IReadOnlyList<string> cancelados, decimal? coeficienteDeUtilidad, string? plataformas, string? actividadDePlataformas,
        IReadOnlyDictionary<string, decimal> captura, CancellationToken cancellationToken = default) =>
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");

    public Task<InterpretacionDelMotor> InterpretarOperacionAsync(
        PerfilParaInterpretar perfil, OperacionParaInterpretar operacion, ArchivoParaElMotor? factura, AnalisisDeDocumento? control,
        BancoParaInterpretar? banco, CancellationToken cancellationToken = default) =>
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");

    public Task<NominaDelMotor> CalcularNominaAsync(
        PatronParaNomina patron, PeriodoParaNomina periodo, IReadOnlyList<TrabajadorParaNomina> trabajadores,
        CancellationToken cancellationToken = default) =>
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");

    public Task<ArchivosDeIdse> ArchivosIdseAsync(
        string registroPatronal, string? guia, string? zona, IReadOnlyList<MovimientoParaIdse> movimientos,
        CancellationToken cancellationToken = default) =>
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");

    public Task<RecibosDeNomina> RecibosDeNominaAsync(
        PatronParaRecibos patron, PeriodoParaNomina periodo, string periodicidad, NominaDelMotor calculo,
        IReadOnlyList<TrabajadorParaRecibo> trabajadores, CancellationToken cancellationToken = default) =>
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");

    public Task<CuotasDeNomina> CuotasDeNominaAsync(IReadOnlyList<NominaDelMotor> calculos, CancellationToken cancellationToken = default) =>
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");

    public Task<ArchivosDeSua> ArchivosSuaAsync(
        string registroPatronal, string? zona, int anio, int mes, IReadOnlyList<TrabajadorParaSua> trabajadores,
        IReadOnlyList<MovimientoParaSua> movimientos, IReadOnlyList<IncidenciaParaSua> incidencias,
        CancellationToken cancellationToken = default) =>
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");

    public Task<DiotDelMotor> DiotAsync(
        string rfc, string periodo, IReadOnlyList<ArchivoParaElMotor> facturas, IReadOnlyList<string> cancelados,
        IReadOnlyList<string> regimenes, CancellationToken cancellationToken = default) =>
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");

    public Task<IReadOnlyList<SentenciaDelMotor>> BuscarSentenciasAsync(
        string consulta, string? fuente = null, int cuantos = 5, int desde = 0, CancellationToken cancellationToken = default)
    {
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");
    }

    public Task<EstadoDeSentencias> EstadoDeSentenciasAsync(CancellationToken cancellationToken = default)
    {
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");
    }

    public Task<bool> ActualizarSentenciasAsync(CancellationToken cancellationToken = default)
    {
        throw new MotorFiscalNoDisponibleException("No hay motor fiscal en este proceso.");
    }
}
