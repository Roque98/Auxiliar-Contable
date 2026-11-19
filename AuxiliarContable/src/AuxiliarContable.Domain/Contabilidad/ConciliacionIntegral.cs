using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Una vuelta del botón «Conciliar» (pedido 19-nov-2026: «un botón mágico que cada que se apriete todo lo que hemos hecho
/// hasta ahora se active y concilie la información»). Va de los meses abiertos del año hasta el que se eligió y, en cada
/// uno: facturas del SAT, estatus, tickets y control interno, banco, amarre, interpretación, nómina, pólizas, impuestos,
/// estados financieros con notas y alertas normativas. La hace un trabajo en segundo plano como el usuario que la pidió
/// (mismos permisos); <see cref="Pasos"/> se va llenando para que la pantalla enseñe el avance.
/// </summary>
public class ConciliacionIntegral : CreationAuditedAggregateRoot<Guid>
{
    public const int MaxErrorLength = 500;

    public Guid OrganizacionId { get; protected set; }

    /// <summary>El mes que se eligió: se concilia desde el primer mes abierto del año hasta éste.</summary>
    public int Anio { get; protected set; }

    public int Mes { get; protected set; }

    /// <summary>Quien la pidió: el trabajo corre con sus permisos.</summary>
    public Guid UsuarioId { get; protected set; }

    public EstadoDeConciliacionIntegral Estado { get; protected set; }
    public DateTime PedidaUtc { get; protected set; }
    public DateTime? IniciadaUtc { get; protected set; }
    public DateTime? TerminadaUtc { get; protected set; }

    /// <summary>JSON: los pasos de cada mes con su estado y resumen.</summary>
    public string Pasos { get; protected set; } = "[]";

    /// <summary>JSON: lo que quedó de cada mes (impuestos, utilidad, pendientes, alertas normativas).</summary>
    public string? Resultado { get; protected set; }

    public string? Error { get; protected set; }

    protected ConciliacionIntegral()
    {
    }

    public ConciliacionIntegral(Guid id, Guid organizacionId, int anio, int mes, Guid usuarioId, DateTime utc) : base(id)
    {
        OrganizacionId = organizacionId;
        Anio = anio;
        Mes = mes;
        UsuarioId = usuarioId;
        Estado = EstadoDeConciliacionIntegral.Pendiente;
        PedidaUtc = utc;
    }

    public void Iniciar(DateTime utc)
    {
        Estado = EstadoDeConciliacionIntegral.Trabajando;
        IniciadaUtc = utc;
    }

    public void Avanzar(string pasos) => Pasos = pasos;

    public void Terminar(string pasos, string resultado, DateTime utc)
    {
        Pasos = pasos;
        Resultado = resultado;
        Estado = EstadoDeConciliacionIntegral.Lista;
        TerminadaUtc = utc;
        Error = null;
    }

    public void Fallar(string error, DateTime utc)
    {
        Estado = EstadoDeConciliacionIntegral.Error;
        TerminadaUtc = utc;
        error = error.Trim();
        Error = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
    }
}

public enum EstadoDeConciliacionIntegral
{
    Pendiente = 0,
    Trabajando = 1,
    Lista = 2,
    Error = 3,
}
