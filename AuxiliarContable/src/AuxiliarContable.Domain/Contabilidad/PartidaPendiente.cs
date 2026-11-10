using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Lo que se decidió de un movimiento del banco que el motor no pudo ligar a una factura (la
/// «partida en conciliación» de los sistemas contables). Va aparte de la conciliación y se busca
/// por la <see cref="Clave"/> del movimiento: si se vuelve a conciliar, lo ya resuelto no se pierde.
/// </summary>
public class PartidaPendiente : AuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }

    public int Anio { get; protected set; }

    public int Mes { get; protected set; }

    /// <summary>Huella del movimiento (ver <see cref="ClaveDeMovimiento"/>).</summary>
    public string Clave { get; protected set; } = string.Empty;

    /// <summary>Fecha, concepto e importe, para enseñárselo al cliente sin volver a leer el estado de cuenta.</summary>
    public string Descripcion { get; protected set; } = string.Empty;

    public ResolucionDePartida Resolucion { get; protected set; }

    /// <summary>Lo que escribe el despacho (la pregunta al cliente o por qué se resolvió así).</summary>
    public string? Nota { get; protected set; }

    /// <summary>Lo que contestó el cliente.</summary>
    public string? Respuesta { get; protected set; }

    public DateTime? RespondidaUtc { get; protected set; }

    protected PartidaPendiente()
    {
    }

    public PartidaPendiente(Guid id, Guid organizacionId, int anio, int mes, string clave, string descripcion) : base(id)
    {
        OrganizacionId = organizacionId;
        Anio = anio;
        Mes = mes;
        Clave = Check.Length(clave, nameof(clave), ContabilidadConsts.ClaveLength, ContabilidadConsts.ClaveLength)!;
        Descripcion = Recortar(Check.NotNullOrWhiteSpace(descripcion, nameof(descripcion)), ContabilidadConsts.MaxDescripcionLength)!;
    }

    public void Resolver(ResolucionDePartida resolucion, string? nota)
    {
        Resolucion = resolucion;
        Nota = Recortar(string.IsNullOrWhiteSpace(nota) ? null : nota.Trim(), ContabilidadConsts.MaxNotaLength);
    }

    /// <summary>El cliente contesta. Queda para que el despacho decida qué es; no se resuelve sola.</summary>
    public void Responder(string respuesta, DateTime ahoraUtc)
    {
        if (ahoraUtc.Kind != DateTimeKind.Utc)
        {
            throw new AbpException("RespondidaUtc debe ser UTC (columna timestamptz).");
        }

        Respuesta = Recortar(Check.NotNullOrWhiteSpace(respuesta, nameof(respuesta)).Trim(), ContabilidadConsts.MaxRespuestaLength);
        RespondidaUtc = ahoraUtc;
    }

    /// <summary>¿Detiene el cierre? Sin resolver o esperando algo del cliente o de la factura.</summary>
    public bool Pendiente => Resolucion is ResolucionDePartida.SinResolver
        or ResolucionDePartida.PreguntarAlCliente or ResolucionDePartida.FacturaPorLlegar;

    private static string? Recortar(string? texto, int maximo) =>
        texto == null || texto.Length <= maximo ? texto : texto[..maximo];
}
