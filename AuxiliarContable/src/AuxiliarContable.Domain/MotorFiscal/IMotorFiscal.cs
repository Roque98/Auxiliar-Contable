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
    /// del ordenamiento (LISR, CFF, LIVA, RMF…) para acotar; null busca en todos.
    /// </summary>
    Task<IReadOnlyList<ResultadoDeLey>> BuscarEnLeyesAsync(
        string consulta, string? ley = null, int cuantos = 5, CancellationToken cancellationToken = default);

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
        string consulta, string? ley = null, int cuantos = 5, CancellationToken cancellationToken = default)
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
}
