using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Nomina;

/// <summary>
/// Un periodo de nómina de un cliente (semana, catorcena, quincena o mes): las incidencias de cada trabajador y el último
/// cálculo del motor (percepciones, ISR, IMSS, INFONAVIT, ISN, neto y póliza).
/// </summary>
public class PeriodoDeNomina : FullAuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }
    public DateTime Inicio { get; protected set; }
    public DateTime Fin { get; protected set; }

    /// <summary>semanal, catorcenal, quincenal, mensual.</summary>
    public string Periodicidad { get; protected set; } = "";

    public int Dias { get; protected set; }

    /// <summary>JSON: {trabajadorId: {faltas, incapacidad, horas_extra_dobles…}}.</summary>
    public string? Incidencias { get; protected set; }

    /// <summary>JSON del motor (/nomina/calcular).</summary>
    public string? Resultado { get; protected set; }

    public DateTime? CalculadoUtc { get; protected set; }

    protected PeriodoDeNomina()
    {
    }

    public PeriodoDeNomina(Guid id, Guid organizacionId, DateTime inicio, DateTime fin, string periodicidad) : base(id)
    {
        if (fin.Date < inicio.Date || (fin.Date - inicio.Date).TotalDays > 30)
        {
            throw new UserFriendlyException("El periodo de nómina va de una fecha a otra igual o posterior, de 31 días como máximo.");
        }

        OrganizacionId = organizacionId;
        Inicio = inicio.Date;
        Fin = fin.Date;
        Periodicidad = Check.NotNullOrWhiteSpace(periodicidad, nameof(periodicidad), 20);
        Dias = (int)(Fin - Inicio).TotalDays + 1;
    }

    public void PonerIncidencias(string json)
    {
        Incidencias = json;
        Resultado = null;          // ya no corresponde a lo capturado
        CalculadoUtc = null;
    }

    public void Guardar(string resultado, DateTime utc)
    {
        Resultado = Check.NotNullOrWhiteSpace(resultado, nameof(resultado));
        CalculadoUtc = utc;
    }
}
