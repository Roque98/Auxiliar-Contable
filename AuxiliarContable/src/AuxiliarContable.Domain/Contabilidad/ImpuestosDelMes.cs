using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// El pago provisional de ISR e IVA de un mes, al día: cada vez que entra, cambia o se borra una factura del cliente se
/// marca «por actualizar» y el actualizador lo recalcula solo (después de un momento sin cambios, para no recalcular
/// por cada XML de un paquete). Pedido 17-nov-2026: «cada que se vayan agregando facturas se vaya actualizando».
/// </summary>
public class ImpuestosDelMes : AuditedAggregateRoot<Guid>
{
    public const int MaxErrorLength = 500;
    public const int MaxHuellaLength = 64;

    public Guid OrganizacionId { get; protected set; }

    public int Anio { get; protected set; }

    public int Mes { get; protected set; }

    /// <summary>JSON de <c>PagosProvisionales</c> (sin el Excel). Null = aún no se calcula.</summary>
    public string? Resultado { get; protected set; }

    public DateTime? CalculadoUtc { get; protected set; }

    public bool PorActualizar { get; protected set; }

    /// <summary>Cuándo se pidió el último recálculo: se espera un momento sin cambios antes de hacerlo.</summary>
    public DateTime PedidoUtc { get; protected set; }

    /// <summary>Por qué falló el último intento (el motor no respondió…); se reintenta solo.</summary>
    public string? Error { get; protected set; }

    public int Facturas { get; protected set; }
    public int Canceladas { get; protected set; }
    public int Danadas { get; protected set; }
    public int SinEstatus { get; protected set; }

    /// <summary>RFC, regímenes y coeficiente con los que se calculó: si cambian en el perfil, hay que recalcular.</summary>
    public string? HuellaDelPerfil { get; protected set; }

    protected ImpuestosDelMes()
    {
    }

    public ImpuestosDelMes(Guid id, Guid organizacionId, int anio, int mes, DateTime pedidoUtc) : base(id)
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
    /// <paramref name="pedidoLeido"/>: el <see cref="PedidoUtc"/> que había al empezar a calcular. Si mientras tanto llegó
    /// otra factura, sigue «por actualizar».
    /// </summary>
    public void Guardar(string resultado, int facturas, int canceladas, int danadas, int sinEstatus, string huellaDelPerfil,
        DateTime utc, DateTime pedidoLeido)
    {
        Resultado = Check.NotNullOrWhiteSpace(resultado, nameof(resultado));
        Facturas = facturas;
        Canceladas = canceladas;
        Danadas = danadas;
        SinEstatus = sinEstatus;
        HuellaDelPerfil = huellaDelPerfil.Length <= MaxHuellaLength ? huellaDelPerfil : huellaDelPerfil[..MaxHuellaLength];
        CalculadoUtc = utc;
        Error = null;
        PorActualizar = PedidoUtc > pedidoLeido;
    }

    /// <summary>Sigue por actualizar; se reintenta después de la espera.</summary>
    public void Fallar(string error, DateTime utc)
    {
        Error = error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
        PorActualizar = true;
        PedidoUtc = utc;
    }
}
