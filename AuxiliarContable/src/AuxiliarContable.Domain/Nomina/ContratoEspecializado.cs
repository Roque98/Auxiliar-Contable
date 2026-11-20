using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Nomina;

/// <summary>
/// Un contrato de servicios especializados o de ejecución de obras especializadas (REPSE) de un cliente, como prestador
/// (<see cref="Presta"/>: pone a sus trabajadores al servicio de otro) o como beneficiario (<see cref="Recibe"/>). Con él se
/// arma la revisión del mes (los documentos del art. 27-V LISR y 5-II LIVA) y el informe cuatrimestral al IMSS y al
/// INFONAVIT (art. 15-A LSS, 29 Bis LINFONAVIT). Pedido 20-nov-2026.
/// </summary>
public class ContratoEspecializado : FullAuditedAggregateRoot<Guid>
{
    public const string Presta = "presta";
    public const string Recibe = "recibe";
    public const int MaxNombreLength = 200;
    public const int MaxObjetoLength = 1000;

    public Guid OrganizacionId { get; protected set; }

    /// <summary><see cref="Presta"/> o <see cref="Recibe"/>.</summary>
    public string Rol { get; protected set; } = Presta;

    /// <summary>El beneficiario (si el cliente presta) o el contratista (si recibe).</summary>
    public string ContraparteRfc { get; protected set; } = "";

    public string ContraparteNombre { get; protected set; } = "";

    /// <summary>Objeto del contrato (art. 14 LFT).</summary>
    public string Objeto { get; protected set; } = "";

    public DateTime Inicio { get; protected set; }
    public DateTime? Fin { get; protected set; }

    /// <summary>Número aproximado de trabajadores que participarán (art. 14 LFT).</summary>
    public int TrabajadoresAproximados { get; protected set; }

    /// <summary>Folio del registro ante la STPS de quien presta el servicio (el cliente o su contratista).</summary>
    public string? RegistroRepse { get; protected set; }

    /// <summary>Hasta cuándo vale el registro: se renueva cada tres años (art. 15 LFT).</summary>
    public DateTime? RegistroVigenteHasta { get; protected set; }

    /// <summary>Solo si el cliente presta: ids de sus trabajadores asignados, separados por coma.</summary>
    public string? Trabajadores { get; protected set; }

    protected ContratoEspecializado()
    {
    }

    public ContratoEspecializado(Guid id, Guid organizacionId) : base(id)
    {
        OrganizacionId = organizacionId;
    }

    public bool EsPrestador => Rol == Presta;

    public void Describir(string rol, string contraparteRfc, string contraparteNombre, string objeto, DateTime inicio, DateTime? fin,
        int trabajadoresAproximados, string? registroRepse, DateTime? registroVigenteHasta)
    {
        if (rol is not (Presta or Recibe))
        {
            throw new UserFriendlyException("El cliente presta el servicio o lo recibe.");
        }

        var rfc = (contraparteRfc ?? "").Trim().ToUpperInvariant();
        if (rfc.Length is < 12 or > 13)
        {
            throw new UserFriendlyException("El RFC de la otra parte tiene 12 o 13 caracteres.");
        }

        if (fin != null && fin.Value.Date < inicio.Date)
        {
            throw new UserFriendlyException("El contrato termina antes de empezar.");
        }

        Rol = rol;
        ContraparteRfc = rfc;
        ContraparteNombre = Check.NotNullOrWhiteSpace(contraparteNombre, nameof(contraparteNombre), MaxNombreLength).Trim();
        Objeto = Check.NotNullOrWhiteSpace(objeto, nameof(objeto), MaxObjetoLength).Trim();
        Inicio = inicio.Date;
        Fin = fin?.Date;
        TrabajadoresAproximados = Math.Clamp(trabajadoresAproximados, 0, 100_000);
        var folio = registroRepse?.Trim().ToUpperInvariant();
        RegistroRepse = string.IsNullOrEmpty(folio) ? null : folio.Length > 30 ? folio[..30] : folio;
        RegistroVigenteHasta = registroVigenteHasta?.Date;
        if (!EsPrestador)
        {
            Trabajadores = null;
        }
    }

    public void Asignar(IEnumerable<Guid> trabajadores)
    {
        var ids = trabajadores.Distinct().ToList();
        Trabajadores = EsPrestador && ids.Count > 0 ? string.Join(",", ids) : null;
    }

    public List<Guid> Asignados() =>
        [.. (Trabajadores ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => Guid.TryParse(t, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty)];

    /// <summary>¿Estuvo vigente algún día entre <paramref name="desde"/> y <paramref name="hasta"/> (incluidos)?</summary>
    public bool VigenteEn(DateTime desde, DateTime hasta) => Inicio <= hasta.Date && (Fin == null || Fin.Value >= desde.Date);
}
