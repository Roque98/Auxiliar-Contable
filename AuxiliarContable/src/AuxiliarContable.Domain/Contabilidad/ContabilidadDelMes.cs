using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// La contabilidad de un mes, al día: cada vez que entra o cambia algo del mes (facturas, estados de cuenta, tickets,
/// efectivo y crédito, operaciones, la conciliación) se marca «por actualizar» y, después de un momento sin cambios, se
/// concilia el banco si hace falta, se amarra lo seguro y se rehacen las pólizas. Con eso la carpeta de control interno
/// (que se arma al vuelo con la conciliación) también queda al día. Pedido 17-nov-2026: «lo mismo para la contabilidad
/// y para armar la carpeta de control interno».
/// </summary>
public class ContabilidadDelMes : AuditedAggregateRoot<Guid>
{
    public const int MaxTextoLength = 500;

    public Guid OrganizacionId { get; protected set; }
    public int Anio { get; protected set; }
    public int Mes { get; protected set; }

    public bool PorActualizar { get; protected set; }

    /// <summary>Cuándo se pidió la última actualización: se espera un momento sin cambios antes de hacerla.</summary>
    public DateTime PedidoUtc { get; protected set; }

    public DateTime? ActualizadaUtc { get; protected set; }

    /// <summary>Qué se hizo la última vez («se concilió el banco · 4 movimientos amarrados · pólizas listas»).</summary>
    public string? Resumen { get; protected set; }

    /// <summary>Por qué no se pudo (falta el RFC, el motor no respondió…).</summary>
    public string? Error { get; protected set; }

    protected ContabilidadDelMes()
    {
    }

    public ContabilidadDelMes(Guid id, Guid organizacionId, int anio, int mes, DateTime pedidoUtc) : base(id)
    {
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new ArgumentException("Mes no válido.");
        }

        OrganizacionId = organizacionId;
        Anio = anio;
        Mes = mes;
        PorActualizar = true;
        PedidoUtc = pedidoUtc;
    }

    public void Pedir(DateTime utc)
    {
        PorActualizar = true;
        PedidoUtc = utc;
    }

    /// <summary>
    /// <paramref name="pedidoLeido"/>: el <see cref="PedidoUtc"/> que había al empezar. Si mientras tanto cambió algo, sigue
    /// «por actualizar».
    /// </summary>
    public void Terminar(string? resumen, DateTime utc, DateTime pedidoLeido)
    {
        Resumen = Corto(resumen);
        Error = null;
        ActualizadaUtc = utc;
        PorActualizar = PedidoUtc > pedidoLeido;
    }

    /// <summary>
    /// Algo que no se arregla solo (falta el RFC…): no se reintenta hasta que vuelva a cambiar algo del mes.
    /// </summary>
    public void Detener(string error, DateTime pedidoLeido)
    {
        Error = Corto(error);
        PorActualizar = PedidoUtc > pedidoLeido;
    }

    /// <summary>El motor no respondió: sigue por actualizar y se reintenta después de la espera.</summary>
    public void Fallar(string error, DateTime utc)
    {
        Error = Corto(error);
        PorActualizar = true;
        PedidoUtc = utc;
    }

    private static string? Corto(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim().Length > MaxTextoLength ? texto.Trim()[..MaxTextoLength] : texto.Trim();
}
