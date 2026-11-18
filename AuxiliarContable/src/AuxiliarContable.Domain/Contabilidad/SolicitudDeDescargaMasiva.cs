using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.Timing;

namespace AuxiliarContable.Contabilidad;

/// <summary>Cómo va una solicitud de descarga masiva, desde el lado de la plataforma.</summary>
public enum EstadoDeDescargaMasiva
{
    /// <summary>El SAT la aceptó y está armando los paquetes.</summary>
    EnProceso = 0,

    /// <summary>Se bajaron todos los paquetes y sus XML ya están en la bóveda.</summary>
    Descargada = 1,

    /// <summary>El SAT dice que en ese rango no hay facturas.</summary>
    SinFacturas = 2,

    /// <summary>El SAT la rechazó, dio error o la dejó vencer (ver Mensaje).</summary>
    Rechazada = 3,
}

/// <summary>
/// Una solicitud al servicio de Descarga Masiva del SAT: emitidas o recibidas de un cliente en un rango de fechas. Se
/// guarda para seguirla (el SAT tarda de minutos a horas en armar los paquetes) y para no repetirla: el SAT solo acepta
/// dos veces los mismos parámetros «de por vida».
/// </summary>
public class SolicitudDeDescargaMasiva : CreationAuditedAggregateRoot<Guid>
{
    public const int MaxMensajeLength = 500;
    public const int MaxIdLength = 60;
    public const int MaxPaquetesLength = 2000;

    /// <summary>El SAT borra los paquetes a las 72 horas: después de eso ya no se baja.</summary>
    public static readonly TimeSpan Vigencia = TimeSpan.FromHours(72);

    public Guid OrganizacionId { get; protected set; }
    public int Anio { get; protected set; }
    public int Mes { get; protected set; }
    public bool Emitidas { get; protected set; }

    /// <summary>Rango pedido, en hora de México (así lo interpreta el SAT).</summary>
    [DisableDateTimeNormalization]
    public DateTime Desde { get; protected set; }

    [DisableDateTimeNormalization]
    public DateTime Hasta { get; protected set; }

    /// <summary>La pidió el trabajo del cierre de mes (no una persona).</summary>
    public bool Automatica { get; protected set; }

    public string? IdSolicitud { get; protected set; }
    public EstadoDeDescargaMasiva Estado { get; protected set; }
    public string? Mensaje { get; protected set; }
    public int NumeroCfdis { get; protected set; }

    /// <summary>Ids de los paquetes que dio el SAT, separados por coma, y cuántos ya se bajaron (en ese orden).</summary>
    public string? Paquetes { get; protected set; }

    public int PaquetesBajados { get; protected set; }
    public int Nuevas { get; protected set; }
    public int Repetidas { get; protected set; }
    public int Rechazadas { get; protected set; }
    public DateTime SolicitadaUtc { get; protected set; }
    public DateTime? RevisadaUtc { get; protected set; }
    public DateTime? TerminadaUtc { get; protected set; }

    protected SolicitudDeDescargaMasiva()
    {
    }

    public SolicitudDeDescargaMasiva(Guid id, Guid organizacionId, int anio, int mes, bool emitidas, DateTime desde, DateTime hasta,
        bool automatica, DateTime ahoraUtc) : base(id)
    {
        OrganizacionId = organizacionId;
        Anio = anio;
        Mes = mes;
        Emitidas = emitidas;
        Desde = DateTime.SpecifyKind(desde, DateTimeKind.Unspecified);
        Hasta = DateTime.SpecifyKind(hasta, DateTimeKind.Unspecified);
        Automatica = automatica;
        SolicitadaUtc = ahoraUtc;
        Estado = EstadoDeDescargaMasiva.EnProceso;
    }

    public bool Activa => Estado == EstadoDeDescargaMasiva.EnProceso;

    public IReadOnlyList<string> ListaDePaquetes =>
        string.IsNullOrEmpty(Paquetes) ? [] : Paquetes.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public void Aceptar(string idSolicitud, string? mensaje)
    {
        IdSolicitud = idSolicitud.Length > MaxIdLength ? idSolicitud[..MaxIdLength] : idSolicitud;
        Mensaje = Corto(mensaje);
    }

    public void Revisada(DateTime ahoraUtc, string? mensaje)
    {
        RevisadaUtc = ahoraUtc;
        Mensaje = Corto(mensaje) ?? Mensaje;
    }

    public void PonerPaquetes(IEnumerable<string> paquetes, int numeroCfdis)
    {
        var texto = string.Join(',', paquetes.Select(p => p.Trim()).Where(p => p.Length > 0));
        Paquetes = texto.Length > MaxPaquetesLength ? texto[..MaxPaquetesLength] : texto;
        NumeroCfdis = numeroCfdis;
    }

    public void PaqueteBajado(int nuevas, int repetidas, int rechazadas)
    {
        PaquetesBajados++;
        Nuevas += nuevas;
        Repetidas += repetidas;
        Rechazadas += rechazadas;
    }

    public void Terminar(EstadoDeDescargaMasiva estado, DateTime ahoraUtc, string? mensaje = null)
    {
        Estado = estado;
        TerminadaUtc = ahoraUtc;
        RevisadaUtc = ahoraUtc;
        Mensaje = Corto(mensaje) ?? Mensaje;
    }

    private static string? Corto(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim().Length > MaxMensajeLength ? texto.Trim()[..MaxMensajeLength] : texto.Trim();
}
