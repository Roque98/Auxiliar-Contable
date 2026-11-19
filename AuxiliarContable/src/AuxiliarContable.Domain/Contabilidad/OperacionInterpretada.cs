using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Lo que Claude interpretó de una operación (factura, ticket y banco juntos): qué es el objeto y qué es para el cliente
/// —tratamiento fiscal, juicio contable, alertas y preguntas—. Una por operación. Se pide desde la mesa y la hace un trabajo
/// en segundo plano (tarda); <see cref="Huella"/> son las fuentes con que se hizo: si cambian, ya no está al día.
/// Pedido 18-nov-2026: «falta que intervenga en la interpretación».
/// </summary>
public class OperacionInterpretada : AuditedAggregateRoot<Guid>
{
    public const int HuellaLength = 32;
    public const int MaxErrorLength = 500;

    /// <summary>Si Claude no respondió, se espera esto antes de volver a intentar.</summary>
    public static readonly TimeSpan EsperaTrasFalla = TimeSpan.FromMinutes(10);

    public Guid OrganizacionId { get; protected set; }
    public Guid OperacionId { get; protected set; }
    public int Anio { get; protected set; }
    public int Mes { get; protected set; }

    /// <summary>Está en la fila para interpretarse (a partir de <see cref="PedidaUtc"/>).</summary>
    public bool Pedida { get; protected set; }

    public DateTime PedidaUtc { get; protected set; }

    /// <summary>Las fuentes con que se interpretó (ver <c>InterpretadorDeOperaciones.Huella</c>).</summary>
    public string? Huella { get; protected set; }

    /// <summary>El JSON de <c>InterpretacionDelMotor</c>.</summary>
    public string? Resultado { get; protected set; }

    public DateTime? InterpretadaUtc { get; protected set; }
    public string? Error { get; protected set; }

    protected OperacionInterpretada()
    {
    }

    public OperacionInterpretada(Guid id, Guid organizacionId, Guid operacionId, int anio, int mes) : base(id)
    {
        OrganizacionId = organizacionId;
        OperacionId = operacionId;
        Anio = anio;
        Mes = mes;
    }

    public void Pedir(DateTime utc)
    {
        Pedida = true;
        PedidaUtc = utc;
    }

    public void Guardar(string huella, string resultado, DateTime utc)
    {
        Huella = huella;
        Resultado = resultado;
        InterpretadaUtc = utc;
        Pedida = false;
        Error = null;
    }

    /// <summary>Claude no respondió: sigue en la fila y se reintenta después de <see cref="EsperaTrasFalla"/>.</summary>
    public void Fallar(string error, DateTime utc)
    {
        Error = Corto(error);
        PedidaUtc = utc + EsperaTrasFalla;
    }

    /// <summary>Algo que no se arregla solo (la operación ya no existe, falta el RFC): sale de la fila.</summary>
    public void Detener(string error)
    {
        Error = Corto(error);
        Pedida = false;
    }

    private static string? Corto(string? t) =>
        string.IsNullOrWhiteSpace(t) ? null : t.Trim().Length > MaxErrorLength ? t.Trim()[..MaxErrorLength] : t.Trim();
}
