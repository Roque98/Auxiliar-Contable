using System;
using System.Threading.Tasks;
using AuxiliarContable.MotorFiscal;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// El pago provisional de ISR e IVA del mes, al día: se recalcula solo cada vez que entra, cambia o se borra una factura
/// del cliente (o cambia su régimen o coeficiente en el perfil). Solo el equipo del despacho.
/// </summary>
public interface IImpuestosAlDiaAppService : IApplicationService
{
    Task<ImpuestosAlDiaDto> GetAsync(Guid organizacionId, int anio, int mes);

    /// <summary>«Actualizar ahora»: se recalcula en la siguiente vuelta (menos de un minuto), sin esperar los 10 minutos.</summary>
    Task<ImpuestosAlDiaDto> ActualizarAhoraAsync(Guid organizacionId, int anio, int mes);
}

public class ImpuestosAlDiaDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }

    /// <summary>Hay un cálculo guardado (puede estar viejo si <see cref="Actualizando"/>).</summary>
    public bool Calculado { get; set; }

    public DateTime? CalculadoUtc { get; set; }

    /// <summary>Entró algo nuevo: se recalcula 10 minutos después de lo último que entró (o ya, con «Actualizar ahora»).</summary>
    public bool Actualizando { get; set; }

    /// <summary>Cuándo se va a recalcular, si está por actualizar.</summary>
    public DateTime? SeActualizaUtc { get; set; }

    public string? Error { get; set; }
    public int Facturas { get; set; }
    public int Canceladas { get; set; }
    public int Danadas { get; set; }
    public int SinEstatus { get; set; }
    public bool SinEntregaDelCliente { get; set; }

    /// <summary>De enero al mes, sin el Excel (se pide aparte, con «Calcular ISR e IVA»).</summary>
    public PagosProvisionales? Resultado { get; set; }

    /// <summary>El renglón del mes que se pidió.</summary>
    public MesDeImpuestos? DelMes { get; set; }
}
