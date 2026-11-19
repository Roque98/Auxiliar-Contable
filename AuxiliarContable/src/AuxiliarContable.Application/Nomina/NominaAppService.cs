using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Nomina;

/// <summary>Ver <see cref="INominaAppService"/>.</summary>
[Authorize]
public class NominaAppService : AuxiliarContableAppService, INominaAppService
{
    private static readonly string[] Periodicidades = ["semanal", "catorcenal", "quincenal", "mensual"];

    private readonly IRepository<Trabajador, Guid> _trabajadores;
    private readonly IRepository<PeriodoDeNomina, Guid> _periodos;
    private readonly IRepository<MovimientoAfiliatorio, Guid> _movimientos;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly GestorDePeriodos _cierres;
    private readonly IMotorFiscal _motor;
    private readonly Autorizador _autorizador;

    public NominaAppService(
        IRepository<Trabajador, Guid> trabajadores,
        IRepository<PeriodoDeNomina, Guid> periodos,
        IRepository<MovimientoAfiliatorio, Guid> movimientos,
        IRepository<PerfilFiscal, Guid> perfiles,
        GestorDePeriodos cierres,
        IMotorFiscal motor,
        Autorizador autorizador)
    {
        _trabajadores = trabajadores;
        _periodos = periodos;
        _movimientos = movimientos;
        _perfiles = perfiles;
        _cierres = cierres;
        _motor = motor;
        _autorizador = autorizador;
    }

    public async Task<NominaDelClienteDto> GetDelClienteAsync(Guid organizacionId, int anio)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        var desde = new DateTime(anio, 1, 1);
        var hasta = desde.AddYears(1);
        var ts = await _trabajadores.GetListAsync(t => t.OrganizacionId == organizacionId);
        var ps = await _periodos.GetListAsync(p => p.OrganizacionId == organizacionId && p.Inicio >= desde && p.Inicio < hasta);
        var (patron, falta) = await PatronAsync(organizacionId);
        var nombres = ts.ToDictionary(t => t.Id, t => t.NombreCompleto);
        var ms = await _movimientos.GetListAsync(m => m.OrganizacionId == organizacionId);
        return new NominaDelClienteDto
        {
            PuedeEditar = await PuedeAsync(organizacionId, AccionEnOrganizacion.Subir),
            FaltaEnElPerfil = falta,
            Trabajadores = [.. ts.OrderBy(t => t.FechaBaja != null).ThenBy(t => t.Nombre).Select(Describir)],
            Periodos = [.. ps.OrderByDescending(p => p.Inicio).Select(p => Describir(p, conDetalle: false))],
            Movimientos = [.. ms.OrderBy(m => m.PresentadoUtc != null).ThenByDescending(m => m.Fecha).Take(200).Select(m => new MovimientoAfiliatorioDto
            {
                Id = m.Id, TrabajadorId = m.TrabajadorId, Nombre = nombres.GetValueOrDefault(m.TrabajadorId) ?? "", Tipo = m.Tipo, Fecha = m.Fecha,
                SalarioDiario = m.SalarioDiario, CausaBaja = m.CausaBaja, PresentadoUtc = m.PresentadoUtc,
            })],
        };
    }

    public async Task<TrabajadorDto> GuardarTrabajadorAsync(Guid organizacionId, GuardarTrabajadorDto dto)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        var t = dto.Id is { } id
            ? await _trabajadores.FirstOrDefaultAsync(x => x.Id == id && x.OrganizacionId == organizacionId)
              ?? throw new EntityNotFoundException(typeof(Trabajador), id)
            : new Trabajador(GuidGenerator.Create(), organizacionId);
        var (antes, antesSdi) = (t.SalarioDiario, t.SalarioDiarioIntegrado);
        t.Describir(dto.Nombre, dto.Rfc, dto.Curp, dto.Nss, dto.FechaIngreso, dto.SalarioDiario, dto.SalarioDiarioIntegrado,
            dto.DiasAguinaldo, dto.PrimaVacacionalPct, dto.CreditoInfonavitTipo, dto.CreditoInfonavitValor, dto.Fonacot);
        t.Identificar(dto.PrimerApellido, dto.SegundoApellido, dto.CodigoPostal, dto.NumeroEmpleado, dto.Umf);
        if (dto.Id == null)
        {
            await _trabajadores.InsertAsync(t, autoSave: true);
            await AvisarAsync(t, "alta", t.FechaIngreso, null);
        }
        else
        {
            await _trabajadores.UpdateAsync(t, autoSave: true);
            if (t.FechaBaja == null && (antes != t.SalarioDiario || antesSdi != t.SalarioDiarioIntegrado))
            {
                await AvisarAsync(t, "modificacion", Clock.Now.Date, null);
            }
        }

        return Describir(t);
    }

    public async Task<TrabajadorDto> DarDeBajaAsync(Guid organizacionId, Guid trabajadorId, DateTime? fecha, string? causa)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        var t = await _trabajadores.FirstOrDefaultAsync(x => x.Id == trabajadorId && x.OrganizacionId == organizacionId)
                ?? throw new EntityNotFoundException(typeof(Trabajador), trabajadorId);
        if (fecha is { } f)
        {
            var c = (causa ?? "2").Trim().ToUpperInvariant();
            if (c.Length != 1 || !"123456789A".Contains(c))
            {
                throw new UserFriendlyException("La causa de la baja es del 1 al 9 o A (catálogo del IMSS).");
            }

            t.DarDeBaja(f);
            await AvisarAsync(t, "baja", f, c);
        }
        else
        {
            t.Reingresar();
            await AvisarAsync(t, "alta", Clock.Now.Date, null);
        }

        await _trabajadores.UpdateAsync(t, autoSave: true);
        return Describir(t);
    }

    public async Task<ArchivosDeIdse> ArchivosIdseAsync(Guid organizacionId, List<Guid>? ids = null)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        var perfil = await _perfiles.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId);
        string? Campo(string clave) => perfil?.Dato(clave)?.Valor?.Trim();
        var rp = Campo(CamposDelPerfil.RegistroPatronal)
                 ?? throw new UserFriendlyException("Falta el registro patronal en el perfil fiscal del cliente.");
        var ms = (await _movimientos.GetListAsync(m => m.OrganizacionId == organizacionId && m.PresentadoUtc == null))
            .Where(m => ids == null || ids.Count == 0 || ids.Contains(m.Id)).ToList();
        if (ms.Count == 0)
        {
            throw new UserFriendlyException("No hay movimientos pendientes de presentar en el IDSE.");
        }

        var tIds = ms.Select(m => m.TrabajadorId).Distinct().ToList();
        var ts = (await _trabajadores.GetListAsync(t => tIds.Contains(t.Id))).ToDictionary(t => t.Id);
        var lista = ms.Where(m => ts.ContainsKey(m.TrabajadorId)).Select(m =>
        {
            var t = ts[m.TrabajadorId];
            return new MovimientoParaIdse
            {
                Id = m.Id.ToString(), Tipo = m.Tipo, Fecha = m.Fecha.ToString("yyyy-MM-dd"), Nss = t.Nss, Curp = t.Curp,
                PrimerApellido = t.PrimerApellido, SegundoApellido = t.SegundoApellido, Nombres = t.Nombre,
                SalarioDiario = m.SalarioDiario, Sdi = m.SalarioDiarioIntegrado, FechaIngreso = t.FechaIngreso.ToString("yyyy-MM-dd"),
                DiasAguinaldo = t.DiasAguinaldo, PrimaVacacionalPct = t.PrimaVacacionalPct, Umf = t.Umf, Clave = t.NumeroEmpleado,
                CausaBaja = m.CausaBaja,
            };
        }).ToList();
        try
        {
            return await _motor.ArchivosIdseAsync(rp, Campo(CamposDelPerfil.GuiaImss), Campo(CamposDelPerfil.ZonaSalarioMinimo), lista);
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor no pudo armar los archivos del IDSE");
            throw new UserFriendlyException("El motor fiscal no está disponible en este momento. Intenta de nuevo en unos minutos.");
        }
    }

    public async Task MarcarPresentadosAsync(Guid organizacionId, List<Guid> ids)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        var ahora = Clock.Now.ToUniversalTime();
        foreach (var m in await _movimientos.GetListAsync(m => m.OrganizacionId == organizacionId && ids.Contains(m.Id) && m.PresentadoUtc == null))
        {
            m.MarcarPresentado(ahora);
            await _movimientos.UpdateAsync(m, autoSave: true);
        }
    }

    public async Task<RecibosDeNomina> RecibosAsync(Guid organizacionId, Guid periodoId)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        var p = await PeriodoAsync(organizacionId, periodoId);
        if (p.Resultado == null)
        {
            throw new UserFriendlyException("Primero calcula la nómina del periodo.");
        }

        var calculo = JsonSerializer.Deserialize<NominaDelMotor>(p.Resultado)!;
        var perfil = await _perfiles.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId);
        string? Campo(string clave) => perfil?.Dato(clave)?.Valor?.Trim();
        var ids = calculo.Trabajadores.Select(w => Guid.TryParse(w.Id, out var g) ? g : Guid.Empty).ToList();
        var ts = (await _trabajadores.GetListAsync(t => ids.Contains(t.Id))).ToDictionary(t => t.Id.ToString());
        var patron = new PatronParaRecibos
        {
            Rfc = Campo(CamposDelPerfil.Rfc)?.ToUpperInvariant() ?? throw new UserFriendlyException("Falta el RFC del cliente en su perfil fiscal."),
            Nombre = Campo(CamposDelPerfil.Nombre), Regimen = RegimenPrincipal(Campo(CamposDelPerfil.Regimenes)),
            Cp = Campo(CamposDelPerfil.CodigoPostal), RegistroPatronal = Campo(CamposDelPerfil.RegistroPatronal),
        };
        var datos = ts.Values.Select(t => new TrabajadorParaRecibo
        {
            Id = t.Id.ToString(), Nombre = t.NombreCompleto, Rfc = t.Rfc, Curp = t.Curp, Nss = t.Nss, Cp = t.CodigoPostal,
            FechaIngreso = t.FechaIngreso.ToString("yyyy-MM-dd"), NumEmpleado = t.NumeroEmpleado,
        }).ToList();
        try
        {
            return await _motor.RecibosDeNominaAsync(patron,
                new PeriodoParaNomina { Inicio = p.Inicio.ToString("yyyy-MM-dd"), Fin = p.Fin.ToString("yyyy-MM-dd"), Dias = p.Dias, Mes = p.Fin.Month },
                p.Periodicidad, calculo, datos);
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor no pudo armar los recibos de nómina");
            throw new UserFriendlyException("El motor fiscal no está disponible en este momento. Intenta de nuevo en unos minutos.");
        }
    }

    public async Task<CuotasDelMesDto> CuotasDelMesAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }

        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1);
        var ps = await _periodos.GetListAsync(p => p.OrganizacionId == organizacionId && p.Fin >= desde && p.Fin < hasta);
        var calculados = ps.Where(p => p.Resultado != null).Select(p => JsonSerializer.Deserialize<NominaDelMotor>(p.Resultado!)!).ToList();
        var dto = new CuotasDelMesDto { Anio = anio, Mes = mes, Periodos = calculados.Count, SinCalcular = ps.Count - calculados.Count };
        if (calculados.Count > 0)
        {
            try
            {
                dto.Cuotas = await _motor.CuotasDeNominaAsync(calculados);
            }
            catch (MotorFiscalNoDisponibleException ex)
            {
                Logger.LogWarning(ex, "El motor no pudo sumar las cuotas");
                throw new UserFriendlyException("El motor fiscal no está disponible en este momento. Intenta de nuevo en unos minutos.");
            }
        }

        return dto;
    }

    private async Task AvisarAsync(Trabajador t, string tipo, DateTime fecha, string? causa) =>
        await _movimientos.InsertAsync(new MovimientoAfiliatorio(GuidGenerator.Create(), t.OrganizacionId, t.Id, tipo, fecha,
            t.SalarioDiario, t.SalarioDiarioIntegrado, causa), autoSave: true);

    /// <summary>La clave del régimen principal (601, 612…) de lo que dice el perfil (claves o nombres).</summary>
    private static string? RegimenPrincipal(string? regimenes)
    {
        if (string.IsNullOrWhiteSpace(regimenes))
        {
            return null;
        }

        var clave = System.Text.RegularExpressions.Regex.Match(regimenes, @"\b6\d\d\b");
        if (clave.Success)
        {
            return clave.Value;
        }

        return Sat.Regimenes.FirstOrDefault(r => regimenes.Contains(r.Value, StringComparison.OrdinalIgnoreCase)).Key;
    }

    public async Task<PeriodoDeNominaDto> CrearPeriodoAsync(Guid organizacionId, CrearPeriodoDto dto)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        if (!Periodicidades.Contains(dto.Periodicidad))
        {
            throw new UserFriendlyException("La periodicidad es semanal, catorcenal, quincenal o mensual.");
        }

        await _cierres.ExigirAbiertoAsync(organizacionId, dto.Fin);
        var inicio = dto.Inicio.Date;
        if (await _periodos.AnyAsync(p => p.OrganizacionId == organizacionId && p.Inicio == inicio && p.Periodicidad == dto.Periodicidad))
        {
            throw new UserFriendlyException("Ya hay un periodo " + dto.Periodicidad + " que empieza ese día.");
        }

        var p = await _periodos.InsertAsync(new PeriodoDeNomina(GuidGenerator.Create(), organizacionId, dto.Inicio, dto.Fin, dto.Periodicidad), autoSave: true);
        return await DetalleAsync(p);
    }

    public async Task<PeriodoDeNominaDto> GetPeriodoAsync(Guid organizacionId, Guid periodoId)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        return await DetalleAsync(await PeriodoAsync(organizacionId, periodoId));
    }

    public async Task<PeriodoDeNominaDto> GuardarIncidenciasAsync(Guid organizacionId, Guid periodoId, List<IncidenciasDeTrabajadorDto> incidencias)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        var p = await PeriodoAsync(organizacionId, periodoId);
        await _cierres.ExigirAbiertoAsync(organizacionId, p.Fin);
        foreach (var i in incidencias ?? [])
        {
            var x = i.Incidencias;
            if (x.Faltas < 0 || x.Incapacidad < 0 || x.Faltas + x.Incapacidad > p.Dias || x.HorasExtraDobles < 0 || x.HorasExtraTriples < 0
                || x.HorasJornada is <= 0 or > 24)
            {
                throw new UserFriendlyException($"Incidencias de {i.Nombre ?? "un trabajador"}: faltas e incapacidad no pasan de los días del periodo y las horas no son negativas.");
            }
        }

        p.PonerIncidencias(JsonSerializer.Serialize((incidencias ?? []).ToDictionary(i => i.TrabajadorId, i => i.Incidencias)));
        await _periodos.UpdateAsync(p, autoSave: true);
        return await DetalleAsync(p);
    }

    public async Task<PeriodoDeNominaDto> CalcularAsync(Guid organizacionId, Guid periodoId)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        var p = await PeriodoAsync(organizacionId, periodoId);
        await _cierres.ExigirAbiertoAsync(organizacionId, p.Fin);
        var (patron, _) = await PatronAsync(organizacionId);
        if (string.IsNullOrWhiteSpace(patron.Rfc))
        {
            throw new UserFriendlyException("Falta el RFC del cliente en su perfil fiscal.");
        }

        var activos = (await _trabajadores.GetListAsync(t => t.OrganizacionId == organizacionId)).Where(t => t.ActivoEn(p.Inicio, p.Fin)).ToList();
        if (activos.Count == 0)
        {
            throw new UserFriendlyException("No hay trabajadores activos en el periodo: dalos de alta primero.");
        }

        var inc = Incidencias(p);
        var lista = activos.Select(t =>
        {
            var i = inc.GetValueOrDefault(t.Id) ?? new IncidenciasDeNomina();
            return new TrabajadorParaNomina
            {
                Id = t.Id.ToString(), Nombre = t.NombreCompleto, Rfc = t.Rfc, Nss = t.Nss, FechaIngreso = t.FechaIngreso.ToString("yyyy-MM-dd"),
                SalarioDiario = t.SalarioDiario, Sdi = t.SalarioDiarioIntegrado, DiasAguinaldo = t.DiasAguinaldo,
                PrimaVacacionalPct = t.PrimaVacacionalPct, Incidencias = i, OtrasPercepciones = i.OtrasPercepciones,
                OtrasDeducciones = i.OtrasDeducciones,
                CreditoInfonavit = t.CreditoInfonavitTipo == null ? null : new CreditoInfonavitParaNomina { Tipo = t.CreditoInfonavitTipo, Valor = t.CreditoInfonavitValor ?? 0 },
                Fonacot = t.Fonacot,
            };
        }).ToList();
        NominaDelMotor r;
        try
        {
            r = await _motor.CalcularNominaAsync(patron,
                new PeriodoParaNomina { Inicio = p.Inicio.ToString("yyyy-MM-dd"), Fin = p.Fin.ToString("yyyy-MM-dd"), Dias = p.Dias, Mes = p.Fin.Month },
                lista);
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor no pudo calcular la nómina");
            throw new UserFriendlyException("El motor fiscal no está disponible en este momento. Intenta de nuevo en unos minutos.");
        }

        p.Guardar(JsonSerializer.Serialize(r), Clock.Now.ToUniversalTime());
        await _periodos.UpdateAsync(p, autoSave: true);
        return await DetalleAsync(p);
    }

    public async Task BorrarPeriodoAsync(Guid organizacionId, Guid periodoId)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        var p = await PeriodoAsync(organizacionId, periodoId);
        await _cierres.ExigirAbiertoAsync(organizacionId, p.Fin);
        await _periodos.DeleteAsync(p, autoSave: true);
    }

    private async Task<PeriodoDeNomina> PeriodoAsync(Guid organizacionId, Guid periodoId) =>
        await _periodos.FirstOrDefaultAsync(x => x.Id == periodoId && x.OrganizacionId == organizacionId)
        ?? throw new EntityNotFoundException(typeof(PeriodoDeNomina), periodoId);

    private static Dictionary<Guid, IncidenciasDeNomina> Incidencias(PeriodoDeNomina p) =>
        string.IsNullOrWhiteSpace(p.Incidencias) ? [] : JsonSerializer.Deserialize<Dictionary<Guid, IncidenciasDeNomina>>(p.Incidencias) ?? [];

    private async Task<PeriodoDeNominaDto> DetalleAsync(PeriodoDeNomina p)
    {
        var dto = Describir(p, conDetalle: true);
        var inc = Incidencias(p);
        var ts = (await _trabajadores.GetListAsync(t => t.OrganizacionId == p.OrganizacionId)).Where(t => t.ActivoEn(p.Inicio, p.Fin));
        dto.Incidencias = [.. ts.OrderBy(t => t.Nombre).Select(t => new IncidenciasDeTrabajadorDto
        {
            TrabajadorId = t.Id, Nombre = t.NombreCompleto, Incidencias = inc.GetValueOrDefault(t.Id) ?? new IncidenciasDeNomina(),
        })];
        return dto;
    }

    private static PeriodoDeNominaDto Describir(PeriodoDeNomina p, bool conDetalle)
    {
        var r = p.Resultado == null ? null : JsonSerializer.Deserialize<NominaDelMotor>(p.Resultado);
        return new PeriodoDeNominaDto
        {
            Id = p.Id, Inicio = p.Inicio, Fin = p.Fin, Periodicidad = p.Periodicidad, Dias = p.Dias, CalculadoUtc = p.CalculadoUtc,
            Resultado = conDetalle ? r : null, Neto = r?.Totales.Neto,
        };
    }

    private static TrabajadorDto Describir(Trabajador t) => new()
    {
        Id = t.Id, Nombre = t.Nombre, PrimerApellido = t.PrimerApellido, SegundoApellido = t.SegundoApellido,
        CodigoPostal = t.CodigoPostal, NumeroEmpleado = t.NumeroEmpleado, Umf = t.Umf, Rfc = t.Rfc, Curp = t.Curp, Nss = t.Nss, FechaIngreso = t.FechaIngreso, FechaBaja = t.FechaBaja,
        SalarioDiario = t.SalarioDiario, SalarioDiarioIntegrado = t.SalarioDiarioIntegrado, DiasAguinaldo = t.DiasAguinaldo,
        PrimaVacacionalPct = t.PrimaVacacionalPct, CreditoInfonavitTipo = t.CreditoInfonavitTipo, CreditoInfonavitValor = t.CreditoInfonavitValor,
        Fonacot = t.Fonacot,
    };

    /// <summary>Lo del patrón para el cálculo, y lo que le falta a su perfil.</summary>
    private async Task<(PatronParaNomina Patron, List<string> Falta)> PatronAsync(Guid organizacionId)
    {
        var perfil = await _perfiles.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId);
        string? Campo(string clave) => perfil?.Dato(clave)?.Valor?.Trim();
        var patron = new PatronParaNomina
        {
            Rfc = Campo(CamposDelPerfil.Rfc)?.ToUpperInvariant() ?? "",
            CodigoPostal = Campo(CamposDelPerfil.CodigoPostal),
            Zona = Campo(CamposDelPerfil.ZonaSalarioMinimo),
            PrimaRiesgo = decimal.TryParse(Campo(CamposDelPerfil.PrimaDeRiesgo), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var pr) ? pr : null,
        };
        var falta = new List<string>();
        if (string.IsNullOrWhiteSpace(Campo(CamposDelPerfil.RegistroPatronal)))
        {
            falta.Add("registro patronal");
        }

        if (patron.PrimaRiesgo == null)
        {
            falta.Add("prima de riesgo de trabajo");
        }

        if (patron.CodigoPostal == null)
        {
            falta.Add("código postal fiscal (para el impuesto sobre nóminas)");
        }

        return (patron, falta);
    }

    private async Task<bool> PuedeAsync(Guid organizacionId, AccionEnOrganizacion accion)
    {
        try
        {
            await _autorizador.ExigirAsync(organizacionId, accion);
            return true;
        }
        catch (AbpAuthorizationException)
        {
            return false;
        }
    }

    private async Task ExigirEquipoAsync(Guid organizacionId, AccionEnOrganizacion accion)
    {
        await _autorizador.ExigirAsync(organizacionId, accion);
        if (!(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new AbpAuthorizationException("La nómina es del despacho.");
        }
    }
}
