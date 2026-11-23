using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Concentrado;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.LeyesDelCliente;

/// <summary>Ver <see cref="ICoberturaMunicipalAppService"/>. Solo el equipo del despacho (ve a todos los clientes).</summary>
[Authorize]
public class CoberturaMunicipalAppService : AuxiliarContableAppService, ICoberturaMunicipalAppService
{
    private readonly IRepository<Organizacion, Guid> _organizaciones;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly IMotorFiscal _motor;
    private readonly Autorizador _autorizador;

    public CoberturaMunicipalAppService(IRepository<Organizacion, Guid> organizaciones, IRepository<PerfilFiscal, Guid> perfiles,
        IMotorFiscal motor, Autorizador autorizador)
    {
        _organizaciones = organizaciones;
        _perfiles = perfiles;
        _motor = motor;
        _autorizador = autorizador;
    }

    public async Task<CoberturaMunicipalDto> GetReporteAsync()
    {
        if (!(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new AbpAuthorizationException("El reporte de cobertura es para el equipo del despacho.");
        }

        var dto = new CoberturaMunicipalDto();
        string json;
        try
        {
            json = await _motor.CoberturaMunicipalAsync();
        }
        catch (MotorFiscalNoDisponibleException)
        {
            return new CoberturaMunicipalDto { Aviso = "No pudimos consultar la cobertura ahora; intenta en unos minutos." };
        }

        LeerCobertura(json, dto);

        // Clientes: estado por el domicilio y, del domicilio, solo el final sin números (como en Mis leyes).
        var orgs = await AsyncExecuter.ToListAsync((await _organizaciones.GetQueryableAsync()).OrderBy(o => o.Nombre).Select(o => new { o.Id, o.Nombre }));
        var perfiles = (await _perfiles.GetListAsync()).ToDictionary(p => p.OrganizacionId);
        var pedidos = new List<IReadOnlyDictionary<string, object?>>();
        var sinMunicipio = new List<ClienteSinCoberturaDto>();
        foreach (var o in orgs)
        {
            var dom = perfiles.GetValueOrDefault(o.Id)?.Dato(CamposDelPerfil.DomicilioFiscal)?.Valor;
            var estado = IsnPorEstado.Buscar(dom)?.Clave;
            var corto = ProcesadorDeLeyesDelCliente.DomicilioSinCalle(dom);
            if (estado == null || corto == null)
            {
                sinMunicipio.Add(new ClienteSinCoberturaDto { OrganizacionId = o.Id, Nombre = o.Nombre, Estado = estado, Cobertura = "sin_municipio" });
                continue;
            }

            pedidos.Add(new Dictionary<string, object?> { ["id"] = o.Id.ToString(), ["estado"] = estado, ["domicilio"] = corto });
        }

        var nombres = orgs.ToDictionary(o => o.Id.ToString(), o => (o.Id, o.Nombre));
        if (pedidos.Count > 0)
        {
            try
            {
                using var d = JsonDocument.Parse(await _motor.MunicipiosDeAsync(pedidos));
                foreach (var x in d.RootElement.EnumerateArray())
                {
                    var id = x.GetProperty("id").GetString() ?? "";
                    var cob = Txt(x, "cobertura") ?? "no_cubierto";
                    if (cob == "cubierto" || !nombres.TryGetValue(id, out var org))
                    {
                        continue;
                    }

                    var mun = Txt(x, "municipio");
                    dto.Clientes.Add(new ClienteSinCoberturaDto
                    {
                        OrganizacionId = org.Id, Nombre = org.Nombre, Estado = Txt(x, "estado"), Municipio = mun,
                        Cobertura = mun == null ? "sin_municipio" : cob,
                    });
                }
            }
            catch (Exception ex) when (ex is MotorFiscalNoDisponibleException or JsonException or KeyNotFoundException)
            {
                dto.Aviso = "No pudimos revisar el municipio de los clientes ahora; intenta en unos minutos.";
            }
        }

        dto.Clientes.AddRange(sinMunicipio);
        dto.Clientes = [.. dto.Clientes.OrderBy(c => c.Cobertura == "no_cubierto" ? 0 : c.Cobertura == "sin_municipio" ? 1 : 2).ThenBy(c => c.Nombre)];
        return dto;
    }

    /// <summary>El JSON del motor (<c>/leyes-estatales/cobertura</c>) al DTO.</summary>
    public static void LeerCobertura(string json, CoberturaMunicipalDto dto)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            dto.Criterio = Txt(r, "criterio");
            if (r.TryGetProperty("totales", out var t))
            {
                dto.Cubiertos = Num(t, "cubierto");
                dto.EnProceso = Num(t, "seleccionado");
                dto.NoCubiertos = Num(t, "no_cubierto");
            }

            if (r.TryGetProperty("estados", out var es) && es.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in es.EnumerateArray())
                {
                    static List<string> Nombres(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array
                        ? [.. v.EnumerateArray().Select(m => Txt(m, "nombre")).Where(n => n != null).Select(n => n!)]
                        : [];
                    dto.Estados.Add(new CoberturaDeEstadoDto
                    {
                        Estado = Txt(e, "estado") ?? "", Nombre = Txt(e, "nombre") ?? "", Municipios = Num(e, "municipios"),
                        PoblacionCubiertaPct = Dbl(e, "poblacion_cubierta_pct"), PoblacionSeleccionadaPct = Dbl(e, "poblacion_seleccionada_pct"),
                        Cubiertos = Nombres(e, "cubierto"), EnProceso = Nombres(e, "seleccionado"), NoCubiertos = Nombres(e, "no_cubierto"),
                    });
                }
            }
        }
        catch (JsonException)
        {
            dto.Aviso = "La cobertura respondió algo que no se pudo leer.";
        }
    }

    private static string? Txt(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Num(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static double? Dbl(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
