using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AuxiliarContable.MotorFiscal;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Lo que Claude interpretó de cada operación del mes (factura, ticket y banco juntos): qué es el objeto, el tratamiento
/// fiscal, el juicio contable, alertas y preguntas para el cliente. Se pide y se hace en segundo plano. Solo el despacho.
/// </summary>
public interface IInterpretacionesAppService : IApplicationService
{
    Task<InterpretacionesDelMesDto> GetDelMesAsync(Guid organizacionId, int anio, int mes);

    /// <summary>
    /// Pone en la fila las operaciones del mes que no están al día (o solo <paramref name="operacionId"/>, aunque lo esté):
    /// las interpreta un trabajo en segundo plano, de dos en dos por minuto.
    /// </summary>
    Task<InterpretacionesDelMesDto> PedirAsync(Guid organizacionId, int anio, int mes, Guid? operacionId);
}

public class InterpretacionesDelMesDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }

    /// <summary>Operaciones con fuentes que no están interpretadas o ya no están al día, y no están en la fila.</summary>
    public int Pendientes { get; set; }

    /// <summary>En la fila, esperando a Claude.</summary>
    public int EnLaFila { get; set; }

    public List<InterpretacionDeOperacionDto> Operaciones { get; set; } = [];
}

public class InterpretacionDeOperacionDto
{
    public Guid OperacionId { get; set; }
    public bool EnLaFila { get; set; }

    /// <summary>Se interpretó con las fuentes que tiene hoy.</summary>
    public bool AlDia { get; set; }

    public DateTime? InterpretadaUtc { get; set; }
    public string? Error { get; set; }
    public List<string> Fuentes { get; set; } = [];
    public InterpretacionDeOperacion? Interpretacion { get; set; }
}
