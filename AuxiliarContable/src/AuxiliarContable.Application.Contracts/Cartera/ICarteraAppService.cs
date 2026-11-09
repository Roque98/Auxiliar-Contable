using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Cartera;

/// <summary>
/// La cartera del despacho: todos los clientes de un vistazo, con lo que piden atención en el
/// mes. Solo el equipo (permiso <c>Organizaciones</c>); las métricas, solo Owner y Developer.
/// </summary>
public interface ICarteraAppService : IApplicationService
{
    Task<List<ClienteDeCarteraDto>> GetClientesAsync(int anio, int mes);

    Task<MetricasDelDespachoDto> GetMetricasAsync(int anio, int mes);
}

public enum SemaforoDeCliente
{
    AlDia = 0,
    Atencion = 1,
    Urgente = 2,
}

public class ClienteDeCarteraDto
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Rfc { get; set; }

    /// <summary>Porcentaje de datos del perfil con valor.</summary>
    public int PerfilCompleto { get; set; }

    public int DatosPorConfirmar { get; set; }
    public int DocumentosDelMes { get; set; }
    public int FacturasDelMes { get; set; }
    public int EstadosDeCuentaDelMes { get; set; }

    public DateTime? UltimoDocumentoUtc { get; set; }
    public SemaforoDeCliente Semaforo { get; set; }

    /// <summary>Lo que hay que hacer, en palabras («Falta el estado de cuenta del mes»).</summary>
    public List<string> Pendientes { get; set; } = [];
}

public class MetricasDelDespachoDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public int Clientes { get; set; }
    public int ClientesConPerfilCompleto { get; set; }
    public int ClientesAlDia { get; set; }
    public int DocumentosDelMes { get; set; }
    public int FacturasDelMes { get; set; }
    public int EstadosDeCuentaDelMes { get; set; }
    public int MensajesDelChat { get; set; }
    public int PersonasEnElChat { get; set; }

    /// <summary>Tokens del chat en el mes (entrada + salida): lo que se paga a Anthropic por conversar.</summary>
    public long TokensDelChat { get; set; }
}
