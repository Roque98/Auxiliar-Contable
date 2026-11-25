using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Concentrado;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.LeyesDelCliente;

/// <summary>Ver <see cref="ICoberturaMunicipalAppService"/>. Solo el equipo del despacho (ve a todos los clientes).</summary>
[Authorize]
public partial class CoberturaMunicipalAppService : AuxiliarContableAppService, ICoberturaMunicipalAppService
{
    private const string MotorCaido = "No pudimos consultar la cobertura ahora; intenta en unos minutos.";

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
        await SoloEquipoAsync();
        var dto = new CoberturaMunicipalDto();
        string json;
        try
        {
            json = await _motor.CoberturaMunicipalAsync();
        }
        catch (MotorFiscalNoDisponibleException)
        {
            return new CoberturaMunicipalDto { Aviso = MotorCaido };
        }

        LeerCobertura(json, dto);

        var (nombres, pedidos, sinMunicipio) = await ClientesAsync();
        if (pedidos.Count > 0)
        {
            try
            {
                foreach (var x in await MunicipiosAsync(pedidos))
                {
                    if (x.Cobertura == "cubierto" || !nombres.TryGetValue(x.Id, out var org))
                    {
                        continue;
                    }

                    dto.Clientes.Add(new ClienteSinCoberturaDto
                    {
                        OrganizacionId = org.Id, Nombre = org.Nombre, Estado = x.Estado, Municipio = x.Municipio,
                        Cobertura = x.Municipio == null ? "sin_municipio" : x.Cobertura,
                    });
                }
            }
            catch (Exception ex) when (ex is MotorFiscalNoDisponibleException or JsonException or KeyNotFoundException)
            {
                dto.Aviso = "No pudimos revisar el municipio de los clientes ahora; intenta en unos minutos.";
            }
        }

        dto.Clientes.AddRange(sinMunicipio.Select(o => new ClienteSinCoberturaDto
        {
            OrganizacionId = o.Id, Nombre = o.Nombre, Estado = o.Estado, Cobertura = "sin_municipio",
        }));
        dto.Clientes = [.. dto.Clientes.OrderBy(c => c.Cobertura switch
        {
            "no_cubierto" => 0, "sin_fuente" => 1, "sin_municipio" => 2, "parcial" => 3, _ => 4,
        }).ThenBy(c => c.Nombre)];
        return dto;
    }

    public async Task<ExpedienteDeEstadoDto> GetExpedienteAsync(string estado)
    {
        await SoloEquipoAsync();
        estado = (estado ?? "").Trim().ToUpperInvariant();
        if (!ClaveDeEstado().IsMatch(estado))
        {
            throw new EntityNotFoundException(typeof(ExpedienteDeEstadoDto), estado);
        }

        string json;
        try
        {
            json = await _motor.ExpedienteDeCoberturaAsync(estado);
        }
        catch (MotorFiscalNoDisponibleException ex) when (ex.Data["Codigo"] is 404)
        {
            throw new EntityNotFoundException(typeof(ExpedienteDeEstadoDto), estado);
        }
        catch (MotorFiscalNoDisponibleException)
        {
            throw new Volo.Abp.UserFriendlyException(MotorCaido);
        }

        return LeerExpediente(json);
    }

    public async Task<PrioridadDelVigiaDto> GetPrioridadDelVigiaAsync()
    {
        await SoloEquipoAsync();
        var (_, pedidos, sinMunicipio) = await ClientesAsync();
        var dto = new PrioridadDelVigiaDto { SinMunicipio = sinMunicipio.Count };
        if (pedidos.Count == 0)
        {
            return dto;
        }

        try
        {
            var filas = await MunicipiosAsync(pedidos);
            dto.SinMunicipio += filas.Count(f => f.Carpeta == null);
            dto.Municipios = [.. filas.Where(f => f.Carpeta != null && f.Estado != null).Select(f => $"{f.Estado}/{f.Carpeta}")
                .Distinct().Order(StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is MotorFiscalNoDisponibleException or JsonException or KeyNotFoundException)
        {
            dto.Aviso = MotorCaido;
        }

        return dto;
    }

    private async Task SoloEquipoAsync()
    {
        if (!(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new AbpAuthorizationException("El mapa de cobertura es para el equipo del despacho.");
        }
    }

    private sealed record Cliente(Guid Id, string Nombre, string? Estado);

    private sealed record MunicipioDeCliente(string Id, string? Estado, string? Municipio, string? Carpeta, string Cobertura);

    /// <summary>Clientes con estado (por el domicilio) y, del domicilio, solo el final sin números (como en Mis leyes): lo
    /// único que viaja al motor.</summary>
    private async Task<(Dictionary<string, (Guid Id, string Nombre)> Nombres, List<IReadOnlyDictionary<string, object?>> Pedidos,
        List<Cliente> SinMunicipio)> ClientesAsync()
    {
        var orgs = await AsyncExecuter.ToListAsync((await _organizaciones.GetQueryableAsync()).OrderBy(o => o.Nombre).Select(o => new { o.Id, o.Nombre }));
        var perfiles = (await _perfiles.GetListAsync()).ToDictionary(p => p.OrganizacionId);
        var pedidos = new List<IReadOnlyDictionary<string, object?>>();
        var sinMunicipio = new List<Cliente>();
        foreach (var o in orgs)
        {
            var dom = perfiles.GetValueOrDefault(o.Id)?.Dato(CamposDelPerfil.DomicilioFiscal)?.Valor;
            var estado = IsnPorEstado.Buscar(dom)?.Clave;
            var corto = ProcesadorDeLeyesDelCliente.DomicilioSinCalle(dom);
            if (estado == null || corto == null)
            {
                sinMunicipio.Add(new Cliente(o.Id, o.Nombre, estado));
                continue;
            }

            pedidos.Add(new Dictionary<string, object?> { ["id"] = o.Id.ToString(), ["estado"] = estado, ["domicilio"] = corto });
        }

        return (orgs.ToDictionary(o => o.Id.ToString(), o => (o.Id, o.Nombre)), pedidos, sinMunicipio);
    }

    private async Task<List<MunicipioDeCliente>> MunicipiosAsync(List<IReadOnlyDictionary<string, object?>> pedidos)
    {
        using var d = JsonDocument.Parse(await _motor.MunicipiosDeAsync(pedidos));
        return [.. d.RootElement.EnumerateArray().Select(x => new MunicipioDeCliente(x.GetProperty("id").GetString() ?? "",
            Txt(x, "estado"), Txt(x, "municipio"), Txt(x, "carpeta"), Txt(x, "cobertura") ?? "no_cubierto"))];
    }

    /// <summary>El JSON del motor (<c>/leyes-estatales/cobertura</c>) al DTO.</summary>
    public static void LeerCobertura(string json, CoberturaMunicipalDto dto)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            dto.Criterio = Txt(r, "criterio");
            dto.MinimoCubierto = Num(r, "minimo_cubierto");
            dto.Elegidos = Num(r, "elegidos");
            dto.Completos = Num(r, "completos");
            dto.UniversoMunicipal = Num(r, "universo_municipal");
            dto.UniversoEstatal = Num(r, "universo_estatal");
            if (r.TryGetProperty("motivos_fuera", out var mf) && mf.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in mf.EnumerateObject())
                {
                    dto.MotivosFuera[p.Name] = p.Value.GetString() ?? "";
                }
            }

            if (r.TryGetProperty("totales", out var t))
            {
                dto.Cubiertos = Num(t, "cubierto");
                dto.Parciales = Num(t, "parcial");
                dto.SinFuente = Num(t, "sin_fuente");
                dto.EnProceso = Num(t, "seleccionado");
                dto.NoCubiertos = Num(t, "no_cubierto");
            }

            foreach (var e in Arreglo(r, "estados"))
            {
                var est = new CoberturaDeEstadoDto
                {
                    Estado = Txt(e, "estado") ?? "", Nombre = Txt(e, "nombre") ?? "", Municipios = Num(e, "municipios"),
                    Elegidos = Num(e, "elegidos"),
                    PoblacionCubiertaPct = Dbl(e, "poblacion_cubierta_pct"), PoblacionSeleccionadaPct = Dbl(e, "poblacion_seleccionada_pct"),
                    Cubiertos = Nombres(e, "cubierto"), Parciales = Nombres(e, "parcial"), SinFuente = Nombres(e, "sin_fuente"),
                    EnProceso = Nombres(e, "seleccionado"), NoCubiertos = Nombres(e, "no_cubierto"),
                };
                if (e.TryGetProperty("estatal", out var be))
                {
                    LeerBalance(be, est.Estatal);
                }

                foreach (var (clave, nivel) in new[] { ("cubierto", "cubierto"), ("parcial", "parcial"), ("sin_fuente", "sin_fuente") })
                {
                    foreach (var m in Arreglo(e, clave))
                    {
                        var x = new MunicipioDeCoberturaDto
                        {
                            Cvegeo = Txt(m, "cvegeo") ?? "", Nombre = Txt(m, "nombre") ?? "", Poblacion = Num(m, "poblacion"), Nivel = nivel,
                        };
                        LeerBalance(m, x);
                        est.ConFicha.Add(x);
                    }
                }

                est.ConFicha = [.. est.ConFicha.OrderByDescending(m => m.Poblacion)];
                est.NoElegidos = [.. Arreglo(e, "no_cubierto").Select(m => new MunicipioFueraDto
                {
                    Nombre = Txt(m, "nombre") ?? "", Poblacion = Num(m, "poblacion"), Motivo = Txt(m, "motivo") ?? "",
                })];
                dto.Estados.Add(est);
            }
        }
        catch (JsonException)
        {
            dto.Aviso = "La cobertura respondió algo que no se pudo leer.";
        }
    }

    /// <summary>El JSON del motor (<c>/leyes-estatales/cobertura/{estado}</c>) al DTO.</summary>
    public static ExpedienteDeEstadoDto LeerExpediente(string json)
    {
        using var d = JsonDocument.Parse(json);
        var r = d.RootElement;
        var dto = new ExpedienteDeEstadoDto { Estado = Txt(r, "estado") ?? "", Nombre = Txt(r, "nombre") ?? "", Revisado = Txt(r, "revisado") };
        if (r.TryGetProperty("estatal", out var be))
        {
            LeerBalance(be, dto.Estatal);
            dto.Estatal.Normas = Normas(be);
        }

        foreach (var m in Arreglo(r, "municipios"))
        {
            var x = new MunicipioConNormasDto
            {
                Cvegeo = Txt(m, "cvegeo") ?? "", Nombre = Txt(m, "nombre") ?? "", Poblacion = Num(m, "poblacion"), Nivel = Txt(m, "nivel"),
            };
            LeerBalance(m, x);
            x.Normas = Normas(m);
            dto.Municipios.Add(x);
        }

        return dto;
    }

    private static void LeerBalance(JsonElement e, BalanceDto b)
    {
        b.Universo = Num(e, "universo");
        b.Tenemos = Num(e, "tenemos");
        b.NoAplica = Num(e, "no_aplica");
        b.NoEncontrado = Num(e, "no_encontrado");
        b.SinRevisar = Num(e, "sin_revisar");
        b.AvancePct = Dbl(e, "avance_pct") ?? 0;
        b.Completo = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("completo", out var c) && c.ValueKind == JsonValueKind.True;
        b.Revisado = Txt(e, "revisado");
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("no_aplica_motivo", out var m))
        {
            b.NoAplicaPorOtraAutoridad = Num(m, "A");
            b.NoAplicaPorNoExistir = Num(m, "B");
        }
    }

    private static List<NormaDeCoberturaDto> Normas(JsonElement e) =>
        [.. Arreglo(e, "normas").Select(n => new NormaDeCoberturaDto
        {
            Id = Txt(n, "id") ?? "", Nombre = Txt(n, "nombre"), Bloque = Txt(n, "bloque"), Clase = Txt(n, "clase") ?? "",
            Motivo = Txt(n, "motivo"), Titulo = Txt(n, "titulo"), Nota = Txt(n, "nota"), UltimaReforma = Txt(n, "ultima_reforma"),
            Url = Txt(n, "url") is { } u && u.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? u : null,
            LoTratan = LeyesDelClienteAppService.LoTratan(n),
        })];

    private static IEnumerable<JsonElement> Arreglo(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

    private static List<string> Nombres(JsonElement e, string k) =>
        [.. Arreglo(e, k).Select(m => m.ValueKind == JsonValueKind.String ? m.GetString() : Txt(m, "nombre")).Where(n => n != null).Select(n => n!)];

    private static string? Txt(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Num(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static double? Dbl(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    [GeneratedRegex("^[A-Z]{2,5}$")]
    private static partial Regex ClaveDeEstado();
}
