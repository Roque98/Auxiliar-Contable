using System;
using System.Threading.Tasks;
using AuxiliarContable.MotorFiscal;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Bancos;

/// <summary>
/// Estados de cuenta y conciliación bancaria de un cliente. La consola NO los lee: toma los archivos
/// de la bóveda (descifrados) y se los manda al motor fiscal (<c>motores-fiscales</c>, el trabajo ya
/// validado del despacho). El resultado se calcula cada vez y no se guarda: la fuente de verdad son
/// los archivos. Permiso: ver al cliente (lo ajeno = 404).
/// </summary>
public interface IBancosAppService : IApplicationService
{
    /// <summary>Lee un estado de cuenta de la bóveda: movimientos y si cuadra con lo que imprime el banco.</summary>
    Task<LecturaDeEstados> LeerEstadoDeCuentaAsync(Guid organizacionId, Guid documentoId);

    /// <summary>Los estados de cuenta del mes contra las facturas (XML) del mismo mes.</summary>
    Task<ConciliacionDelMesDto> ConciliarAsync(Guid organizacionId, int anio, int mes);
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

    public ResultadoDeConciliacion Resultado { get; set; } = new();
}
