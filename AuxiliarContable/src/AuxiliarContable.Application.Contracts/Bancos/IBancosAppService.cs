using System;
using System.Threading.Tasks;
using AuxiliarContable.MotorFiscal;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Bancos;

/// <summary>
/// Estados de cuenta y conciliación bancaria de un cliente. La consola NO los lee: toma los archivos
/// de la bóveda (descifrados) y se los manda al motor fiscal (<c>motores-fiscales</c>, el trabajo ya
/// validado del despacho). Se guarda la última conciliación de cada mes para el cierre, pero la
/// fuente de verdad son los archivos: si cambian, se vuelve a conciliar. Permiso: ver al cliente
/// (lo ajeno = 404).
/// </summary>
public interface IBancosAppService : IApplicationService
{
    /// <summary>Lee un estado de cuenta de la bóveda: movimientos y si cuadra con lo que imprime el banco.</summary>
    Task<LecturaDeEstados> LeerEstadoDeCuentaAsync(Guid organizacionId, Guid documentoId);

    /// <summary>Los estados de cuenta del mes contra las facturas (XML) del mismo mes.</summary>
    Task<ConciliacionDelMesDto> ConciliarAsync(Guid organizacionId, int anio, int mes);

    /// <summary>
    /// Las facturas del mes con sus complementos de pago y notas de crédito: lo que no cuadra y qué pasó (p. ej. un
    /// complemento que paga la factura PUE en vez de la PPD). Se mandan también las de los meses anteriores para ver
    /// la historia de cada PPD, pero solo se reporta lo que toca el mes. No se guarda: se calcula al pedirlo.
    /// </summary>
    Task<DiagnosticoDeFacturas> DiagnosticarFacturasAsync(Guid organizacionId, int anio, int mes);

    /// <summary>
    /// «Mis facturas»: las del mes clasificadas (emitidas o recibidas, tipo, PPD por definir, inversiones, y qué es cada
    /// compra para el giro del cliente según su perfil). Con la historia de 3 meses para el estado de las PPD.
    /// </summary>
    Task<FacturasClasificadas> ClasificarFacturasAsync(Guid organizacionId, int anio, int mes);
}

public class ConciliacionDelMesDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }

    /// <summary>El RFC del perfil fiscal con que se separan emitidas y recibidas.</summary>
    public string Rfc { get; set; } = "";

    /// <summary>Cuántos archivos de la bóveda entraron.</summary>
    public int EstadosDeCuenta { get; set; }

    public int Facturas { get; set; }

    public DateTime ConciliadaUtc { get; set; }

    public ResultadoDeConciliacion Resultado { get; set; } = new();
}
