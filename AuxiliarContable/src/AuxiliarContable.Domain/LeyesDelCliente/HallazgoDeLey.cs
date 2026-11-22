using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.LeyesDelCliente;

/// <summary>
/// Algo que el motor encontró en una ley del cliente, con su cita literal ya verificada contra el artículo: un valor que
/// difiere del sistema (tasa del ISN, día límite, recargos), una obligación o trámite local (licencia, protección civil,
/// anuncios…), un beneficio o un aviso. No cuenta hasta que el despacho lo acepta; el valor aceptado reemplaza al del
/// sistema solo para este cliente.
/// </summary>
public class HallazgoDeLey : CreationAuditedEntity<Guid>
{
    public Guid OrganizacionId { get; protected set; }
    public Guid LeyId { get; protected set; }

    /// <summary>valor, obligacion, beneficio o aviso.</summary>
    public string Tipo { get; protected set; } = "aviso";

    public string? Tema { get; protected set; }
    public string Titulo { get; protected set; } = string.Empty;

    /// <summary>isn.tasa, isn.dia o recargos.tasa si reemplaza un valor del sistema; null si no.</summary>
    public string? Clave { get; protected set; }

    public string? Valor { get; protected set; }
    public decimal? ValorNumerico { get; protected set; }
    public string? ValorDelSistema { get; protected set; }
    public string? Condicion { get; protected set; }
    public string? Periodicidad { get; protected set; }
    public string? FechaLimite { get; protected set; }
    public string Articulo { get; protected set; } = string.Empty;
    public string? Etiqueta { get; protected set; }
    public int? Pagina { get; protected set; }
    public string Cita { get; protected set; } = string.Empty;
    public string? Explicacion { get; protected set; }

    public DecisionDeHallazgo Decision { get; protected set; }
    public Guid? DecididoPor { get; protected set; }
    public DateTime? DecididoUtc { get; protected set; }

    protected HallazgoDeLey()
    {
    }

    public HallazgoDeLey(Guid id, Guid organizacionId, Guid leyId, string tipo, string? tema, string titulo, string? clave, string? valor,
        decimal? valorNumerico, string? valorDelSistema, string? condicion, string? periodicidad, string? fechaLimite, string articulo,
        string? etiqueta, int? pagina, string cita, string? explicacion) : base(id)
    {
        OrganizacionId = organizacionId;
        LeyId = leyId;
        Tipo = tipo is "valor" or "obligacion" or "beneficio" ? tipo : "aviso";
        Tema = LeyDelCliente.Corto(tema, 60);
        Titulo = LeyDelCliente.Corto(titulo, 300) ?? "(sin título)";
        Clave = clave is LeyesDelClienteConsts.IsnTasa or LeyesDelClienteConsts.IsnDia or LeyesDelClienteConsts.RecargosTasa ? clave : null;
        Valor = LeyDelCliente.Corto(valor, 200);
        ValorNumerico = Clave == null ? null : valorNumerico;
        ValorDelSistema = LeyDelCliente.Corto(valorDelSistema, 200);
        Condicion = LeyDelCliente.Corto(condicion, 500);
        Periodicidad = LeyDelCliente.Corto(periodicidad, 20);
        FechaLimite = LeyDelCliente.Corto(fechaLimite, 200);
        Articulo = LeyDelCliente.Corto(articulo, 60) ?? "";
        Etiqueta = LeyDelCliente.Corto(etiqueta, 120);
        Pagina = pagina;
        Cita = LeyDelCliente.Corto(cita, 2000) ?? "";
        Explicacion = LeyDelCliente.Corto(explicacion, 2000);
        Decision = DecisionDeHallazgo.Propuesto;
    }

    /// <summary>Para no repetir un hallazgo ya decidido cuando la ley se vuelve a analizar. El número va sin ceros de más:
    /// la base lo devuelve con 6 decimales («0.025000») y el motor sin ellos («0.025»).</summary>
    public string Llave => $"{Clave}|{Articulo}|{(Clave != null ? ValorNumerico?.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture) : Titulo)}";

    public void Decidir(bool aceptar, Guid? quien, DateTime utc)
    {
        Decision = aceptar ? DecisionDeHallazgo.Aceptado : DecisionDeHallazgo.Rechazado;
        DecididoPor = quien;
        DecididoUtc = utc;
    }

    public void Reemplazar(DateTime utc)
    {
        Decision = DecisionDeHallazgo.Reemplazado;
        DecididoUtc = utc;
    }
}
