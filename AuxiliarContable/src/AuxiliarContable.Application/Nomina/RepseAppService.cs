using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Nomina;

/// <summary>Ver <see cref="IRepseAppService"/>.</summary>
[Authorize]
public class RepseAppService : AuxiliarContableAppService, IRepseAppService
{
    private readonly IRepository<ContratoEspecializado, Guid> _contratos;
    private readonly IRepository<CumplimientoRepse, Guid> _cumplimientos;
    private readonly IRepository<Trabajador, Guid> _trabajadores;
    private readonly IRepository<PeriodoDeNomina, Guid> _periodos;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly RevisionRepse _revision;
    private readonly Autorizador _autorizador;

    public RepseAppService(
        IRepository<ContratoEspecializado, Guid> contratos,
        IRepository<CumplimientoRepse, Guid> cumplimientos,
        IRepository<Trabajador, Guid> trabajadores,
        IRepository<PeriodoDeNomina, Guid> periodos,
        IRepository<PerfilFiscal, Guid> perfiles,
        RevisionRepse revision,
        Autorizador autorizador)
    {
        _contratos = contratos;
        _cumplimientos = cumplimientos;
        _trabajadores = trabajadores;
        _periodos = periodos;
        _perfiles = perfiles;
        _revision = revision;
        _autorizador = autorizador;
    }

    public async Task<RepseDelMesDto> GetDelMesAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        if (mes is < 1 or > 12 || anio is < 2021 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }

        var r = await _revision.RevisarAsync(organizacionId, anio, mes);
        r.PuedeEditar = await PuedeAsync(organizacionId, AccionEnOrganizacion.Subir);
        return r;
    }

    public async Task<ContratoEspecializadoDto> GuardarContratoAsync(Guid organizacionId, GuardarContratoEspecializadoDto dto)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        var c = dto.Id is { } id
            ? await _contratos.FirstOrDefaultAsync(x => x.Id == id && x.OrganizacionId == organizacionId)
              ?? throw new EntityNotFoundException(typeof(ContratoEspecializado), id)
            : new ContratoEspecializado(GuidGenerator.Create(), organizacionId);
        c.Describir(dto.Rol, dto.ContraparteRfc, dto.ContraparteNombre, dto.Objeto, dto.Inicio, dto.Fin, dto.TrabajadoresAproximados,
            dto.RegistroRepse, dto.RegistroVigenteHasta);
        var pedidos = dto.Trabajadores ?? [];
        var suyos = pedidos.Count == 0
            ? []
            : (await _trabajadores.GetListAsync(t => t.OrganizacionId == organizacionId && pedidos.Contains(t.Id))).Select(t => t.Id).ToList();
        c.Asignar(pedidos.Where(suyos.Contains));
        if (dto.Id == null)
        {
            await _contratos.InsertAsync(c, autoSave: true);
        }
        else
        {
            await _contratos.UpdateAsync(c, autoSave: true);
        }

        return RevisionRepse.Describir(c);
    }

    public async Task BorrarContratoAsync(Guid organizacionId, Guid contratoId)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        var c = await _contratos.FirstOrDefaultAsync(x => x.Id == contratoId && x.OrganizacionId == organizacionId)
                ?? throw new EntityNotFoundException(typeof(ContratoEspecializado), contratoId);
        await _cumplimientos.DeleteAsync(x => x.OrganizacionId == organizacionId && x.ContratoId == contratoId);
        await _contratos.DeleteAsync(c);
    }

    public async Task MarcarAsync(Guid organizacionId, MarcarRepseDto dto)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        if (dto.ContratoId is { } contratoId)
        {
            var c = await _contratos.FirstOrDefaultAsync(x => x.Id == contratoId && x.OrganizacionId == organizacionId)
                    ?? throw new EntityNotFoundException(typeof(ContratoEspecializado), contratoId);
            var claves = c.EsPrestador ? RevisionRepse.DelPrestador : RevisionRepse.DelContratista;
            if (dto.Periodo is < 1 or > 12 || claves.All(x => x.Clave != dto.Clave))
            {
                throw new UserFriendlyException("Casilla no válida.");
            }
        }
        else if (dto.Periodo is < 1 or > 3 || RevisionRepse.DelInforme.All(x => x.Clave != dto.Clave))
        {
            throw new UserFriendlyException("Casilla no válida.");
        }

        var existentes = await _cumplimientos.GetListAsync(x => x.OrganizacionId == organizacionId && x.ContratoId == dto.ContratoId
                                                                && x.Anio == dto.Anio && x.Periodo == dto.Periodo && x.Clave == dto.Clave);
        if (dto.Cumplida && existentes.Count == 0)
        {
            await _cumplimientos.InsertAsync(new CumplimientoRepse(GuidGenerator.Create(), organizacionId, dto.ContratoId, dto.Anio, dto.Periodo, dto.Clave));
        }
        else if (!dto.Cumplida && existentes.Count > 0)
        {
            await _cumplimientos.DeleteManyAsync(existentes);
        }
    }

    public async Task<InformeCuatrimestralDto> InformeCuatrimestralAsync(Guid organizacionId, int anio, int cuatrimestre)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        if (cuatrimestre is < 1 or > 3)
        {
            throw new UserFriendlyException("El cuatrimestre es 1 (enero-abril), 2 (mayo-agosto) o 3 (septiembre-diciembre).");
        }

        var (desde, hasta) = RevisionRepse.MesesDe(anio, cuatrimestre);
        var contratos = (await _contratos.GetListAsync(c => c.OrganizacionId == organizacionId && c.Rol == ContratoEspecializado.Presta))
            .Where(c => c.VigenteEn(desde, hasta)).OrderBy(c => c.ContraparteNombre).ToList();
        var avisos = new List<string>();
        if (contratos.Count == 0)
        {
            avisos.Add($"El cliente no tiene contratos de servicios especializados como prestador vigentes en {RevisionRepse.NombreDeMeses(anio, cuatrimestre)}.");
        }

        var perfil = await _perfiles.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId);
        string Campo(string clave) => perfil?.Dato(clave)?.Valor?.Trim() ?? "";
        var trabajadores = (await _trabajadores.GetListAsync(t => t.OrganizacionId == organizacionId)).ToDictionary(t => t.Id);
        var sbc = await SbcAsync(organizacionId, hasta);

        var csv = new StringBuilder();
        void Fila(params object?[] celdas) => csv.AppendLine(string.Join(",", celdas.Select(Celda)));
        Fila("INFORMACIÓN CUATRIMESTRAL DE SERVICIOS ESPECIALIZADOS", RevisionRepse.NombreDeMeses(anio, cuatrimestre));
        Fila("Para capturar en el ICSOE (IMSS, art. 15-A LSS) y el SISUB (INFONAVIT, art. 29 Bis LINFONAVIT). Vence el",
            RevisionRepse.LimiteDelInforme(anio, cuatrimestre).ToString("yyyy-MM-dd"));
        Fila();
        Fila("PRESTADOR");
        Fila("RFC", "Nombre o razón social", "Domicilio", "Registro patronal", "Folio REPSE");
        var folios = string.Join(" / ", contratos.Select(c => c.RegistroRepse).Where(f => f != null).Distinct());
        Fila(Campo(CamposDelPerfil.Rfc).ToUpperInvariant(), Campo(CamposDelPerfil.Nombre), Campo(CamposDelPerfil.DomicilioFiscal),
            Campo(CamposDelPerfil.RegistroPatronal), folios);
        if (folios.Length == 0 && contratos.Count > 0)
        {
            avisos.Add("Falta el folio del registro REPSE: el informe lleva copia simple del registro de la STPS (art. 15-A LSS, fracción III).");
        }

        Fila();
        Fila("CONTRATOS");
        Fila("Beneficiario RFC", "Beneficiario", "Objeto", "Inicio", "Fin", "Celebrado en el cuatrimestre", "Trabajadores aproximados", "Trabajadores asignados");
        foreach (var c in contratos)
        {
            Fila(c.ContraparteRfc, c.ContraparteNombre, c.Objeto, c.Inicio.ToString("yyyy-MM-dd"), c.Fin?.ToString("yyyy-MM-dd") ?? "",
                c.Inicio >= desde && c.Inicio <= hasta ? "Sí" : "No", c.TrabajadoresAproximados, c.Asignados().Count);
        }

        Fila();
        Fila("TRABAJADORES");
        Fila("Beneficiario RFC", "Nombre", "CURP", "NSS", "Salario base de cotización");
        foreach (var c in contratos)
        {
            foreach (var id in c.Asignados())
            {
                if (!trabajadores.TryGetValue(id, out var t) || !t.ActivoEn(desde, hasta))
                {
                    continue;
                }

                var s = sbc.GetValueOrDefault(id) ?? t.SalarioDiarioIntegrado;
                if (s == null)
                {
                    avisos.Add($"{t.NombreCompleto}: sin salario base de cotización (no hay nómina calculada del cuatrimestre ni SDI capturado).");
                }

                if (string.IsNullOrEmpty(t.Curp) || string.IsNullOrEmpty(t.Nss))
                {
                    avisos.Add($"{t.NombreCompleto}: le falta la CURP o el NSS.");
                }

                Fila(c.ContraparteRfc, t.NombreCompleto, t.Curp ?? "", t.Nss ?? "", s?.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
            }
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return new InformeCuatrimestralDto
        {
            Nombre = $"repse-{anio}-c{cuatrimestre}.csv",
            ContenidoB64 = Convert.ToBase64String(bytes),
            Avisos = avisos,
        };
    }

    /// <summary>El SBC de cada trabajador en la última nómina calculada que termina a más tardar en <paramref name="hasta"/>.</summary>
    private async Task<Dictionary<Guid, decimal?>> SbcAsync(Guid organizacionId, DateTime hasta)
    {
        var periodos = (await _periodos.GetListAsync(p => p.OrganizacionId == organizacionId && p.Fin <= hasta && p.Resultado != null))
            .OrderByDescending(p => p.Fin).Take(12).ToList();
        var r = new Dictionary<Guid, decimal?>();
        foreach (var p in periodos)
        {
            var calculo = JsonSerializer.Deserialize<NominaDelMotor>(p.Resultado!);
            foreach (var t in calculo?.Trabajadores ?? [])
            {
                if (Guid.TryParse(t.Id, out var id) && !r.ContainsKey(id) && t.Sbc > 0)
                {
                    r[id] = t.Sbc;
                }
            }
        }

        return r;
    }

    private static string Celda(object? v)
    {
        var s = v switch
        {
            null => "",
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            _ => v.ToString() ?? "",
        };
        // Lo que empieza con = + - @ se abriría en Excel como fórmula.
        if (s.Length > 0 && "=+-@".Contains(s[0]))
        {
            s = "'" + s;
        }

        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
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
            throw new AbpAuthorizationException("Los servicios especializados los lleva el despacho.");
        }
    }
}
