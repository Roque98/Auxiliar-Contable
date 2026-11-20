using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Notifications;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace AuxiliarContable.Nomina;

/// <summary>
/// El aviso diario de servicios especializados para el equipo del despacho (campana, permiso de organizaciones):
/// <list type="bullet">
/// <item>del 7 al 17 de enero, mayo y septiembre, si el ICSOE o el SISUB del cuatrimestre no están marcados;</item>
/// <item>en los últimos <see cref="DiasAntesDelFinDeMes"/> días del mes, los documentos de lo facturado el mes anterior
/// que faltan (el plazo es el último día de este mes, art. 5-II LIVA);</item>
/// <item>registros REPSE vencidos o que vencen en <see cref="DiasAntesDelRegistro"/> días.</item>
/// </list>
/// El freno es el propio aviso: el mismo título no se repite en <see cref="DiasEntreAvisos"/> días.
/// </summary>
public class RecordatorioRepse : ITransientDependency
{
    public const int DiasAntesDelInforme = 10;
    public const int DiasAntesDelFinDeMes = 7;
    public const int DiasAntesDelRegistro = 30;
    public const int DiasEntreAvisos = 3;

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-MX");

    private readonly IRepository<ContratoEspecializado, Guid> _contratos;
    private readonly IRepository<Organizacion, Guid> _organizaciones;
    private readonly INotificacionRepository _avisos;
    private readonly RevisionRepse _revision;
    private readonly IGuidGenerator _guids;

    public RecordatorioRepse(
        IRepository<ContratoEspecializado, Guid> contratos,
        IRepository<Organizacion, Guid> organizaciones,
        INotificacionRepository avisos,
        RevisionRepse revision,
        IGuidGenerator guids)
    {
        _contratos = contratos;
        _organizaciones = organizaciones;
        _avisos = avisos;
        _revision = revision;
        _guids = guids;
    }

    /// <summary>Deja los avisos que tocan hoy; devuelve cuántos.</summary>
    public async Task<int> AvisarAsync(DateTime hoy, DateTime ahoraUtc)
    {
        hoy = hoy.Date;
        var contratos = await _contratos.GetListAsync();
        var r = 0;
        foreach (var grupo in contratos.GroupBy(c => c.OrganizacionId))
        {
            var org = await _organizaciones.FindAsync(grupo.Key, includeDetails: false);
            if (org == null)
            {
                continue;
            }

            foreach (var (titulo, cuerpo) in await PendientesAsync(org, grupo.ToList(), hoy))
            {
                if (await _avisos.ExistsRecentAsync(titulo, null, ahoraUtc.AddDays(-DiasEntreAvisos)))
                {
                    continue;
                }

                await _avisos.InsertAsync(new Notificacion(
                    _guids.Create(), tenantId: null, userId: null, AuxiliarContablePermissions.Organizaciones.Default, NotificationSeverity.Warning,
                    titulo, cuerpo, $"/nomina?cliente={org.Id}", ahoraUtc));
                r++;
            }
        }

        return r;
    }

    private async Task<List<(string Titulo, string Cuerpo)>> PendientesAsync(Organizacion org, List<ContratoEspecializado> contratos, DateTime hoy)
    {
        var r = new List<(string, string)>();
        if (hoy.Month is 1 or 5 or 9 && hoy.Day <= 17 && hoy.Day > 17 - DiasAntesDelInforme && contratos.Any(c => c.EsPrestador))
        {
            var mes = await _revision.RevisarAsync(org.Id, hoy.Year, hoy.Month);
            if (mes.Informe is { } inf && inf.Casillas.Any(c => !c.Cumplida))
            {
                var faltan = string.Join(" y ", inf.Casillas.Where(c => !c.Cumplida).Select(c => c.Clave.ToUpperInvariant()));
                r.Add(($"{faltan} de {org.Nombre} vence(n) el {inf.Limite.ToString("d 'de' MMMM", Es)}",
                    $"Información de los contratos de servicios especializados de {inf.Meses} (art. 15-A LSS, 29 Bis LINFONAVIT). "
                    + "En Nómina → Servicios especializados está la hoja con los datos para capturarla."));
            }
        }

        var finDeMes = new DateTime(hoy.Year, hoy.Month, 1).AddMonths(1).AddDays(-1);
        if ((finDeMes - hoy).TotalDays < DiasAntesDelFinDeMes)
        {
            var anterior = hoy.AddMonths(-1);
            var mes = await _revision.RevisarAsync(org.Id, anterior.Year, anterior.Month);
            var faltan = mes.DelMes.Where(c => c.Facturas > 0 && c.Casillas.Any(x => !x.Cumplida)).ToList();
            if (faltan.Count > 0)
            {
                var nombreMes = anterior.ToString("MMMM 'de' yyyy", Es);
                r.Add(($"Documentos REPSE de {org.Nombre} de {nombreMes}: vencen el {finDeMes.ToString("d 'de' MMMM", Es)}",
                    string.Join("; ", faltan.Select(c => (c.Rol == ContratoEspecializado.Presta ? "entregar a " : "recabar de ") + c.Contraparte))
                    + ". Sin ellos el beneficiario no deduce el gasto (art. 27-V LISR) y debe presentar complementaria de IVA (art. 5-II LIVA)."));
            }
        }

        var porVencer = contratos.Where(c => c.RegistroVigenteHasta is { } v && v <= hoy.AddDays(DiasAntesDelRegistro) && (c.Fin == null || c.Fin >= hoy)).ToList();
        if (porVencer.Count > 0)
        {
            r.Add(($"Registro REPSE por renovar ({org.Nombre})",
                string.Join("; ", porVencer.Select(c => $"{c.ContraparteNombre} ({(c.EsPrestador ? "el del cliente" : "el del contratista")}): "
                                                       + $"{(c.RegistroVigenteHasta < hoy ? "venció" : "vence")} el {c.RegistroVigenteHasta!.Value.ToString("d 'de' MMMM 'de' yyyy", Es)}"))
                + ". El registro se renueva cada tres años (art. 15 LFT)."));
        }

        return r;
    }
}
