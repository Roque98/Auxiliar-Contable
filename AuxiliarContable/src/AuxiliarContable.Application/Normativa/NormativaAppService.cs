using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AuxiliarContable.Normativa;

/// <summary>Ver <see cref="INormativaAppService"/>.</summary>
[Authorize]
public class NormativaAppService : AuxiliarContableAppService, INormativaAppService
{
    /// <summary>Lo mismo que leyesmx/vocab_fiscal.py DISPARADORES (el motor ignora lo que no conoce).</summary>
    public static readonly (string Clave, string Descripcion)[] ListaDeDisparadores =
    [
        ("trabajadores", "Tiene trabajadores o paga sueldos y asimilados"),
        ("honorarios_pagados", "Paga honorarios o servicios a personas físicas"),
        ("arrendamiento", "Da o toma en renta bienes"),
        ("enajena_inmuebles", "Vende o compra inmuebles"),
        ("enajena_acciones", "Vende o compra acciones o partes sociales"),
        ("dividendos", "Reparte o recibe dividendos"),
        ("intereses", "Paga o cobra intereses; préstamos"),
        ("extranjero", "Opera con residentes en el extranjero"),
        ("moneda_extranjera", "Factura, cobra o paga en moneda extranjera"),
        ("importa", "Importa mercancías"),
        ("exporta", "Exporta bienes o servicios"),
        ("immex", "Programa IMMEX, maquila o depósito fiscal"),
        ("plataformas", "Vende o presta servicios por plataformas digitales"),
        ("servicios_digitales_extranjeros", "Recibe servicios digitales de proveedores extranjeros"),
        ("partes_relacionadas", "Opera con partes relacionadas"),
        ("donativos", "Da o recibe donativos; donataria"),
        ("sector_primario", "Actividades agrícolas, ganaderas, silvícolas o pesqueras"),
        ("construccion", "Construcción"),
        ("autotransporte", "Autotransporte de carga o pasajeros"),
        ("salud", "Servicios de salud"),
        ("educacion", "Servicios educativos"),
        ("alimentos", "Enajena alimentos o bebidas"),
        ("bebidas_alcohol_tabaco", "Bebidas alcohólicas, cerveza, tabacos, bebidas saborizadas"),
        ("combustibles", "Combustibles"),
        ("vehiculos", "Automóviles"),
        ("subcontratacion", "Subcontratación o servicios especializados (REPSE)"),
        ("efectivo", "Pagos o cobros en efectivo"),
        ("actividad_vulnerable", "Actividades vulnerables (antilavado)"),
        ("franja_fronteriza", "Región fronteriza"),
        ("perdidas_fiscales", "Pérdidas fiscales por amortizar"),
        ("saldo_a_favor", "Saldos a favor, devoluciones o compensaciones"),
        ("activo_fijo", "Compra de activo fijo"),
        ("regularizacion", "Declaraciones atrasadas, multas o recargos"),
        ("medios_defensa", "Recursos, juicios o acuerdos conclusivos"),
    ];

    private readonly IRepository<AnalisisNormativo, Guid> _analisis;
    private readonly Autorizador _autorizador;

    public NormativaAppService(IRepository<AnalisisNormativo, Guid> analisis, Autorizador autorizador)
    {
        _analisis = analisis;
        _autorizador = autorizador;
    }

    public async Task<AnalisisNormativoDto> PedirAsync(Guid organizacionId, PedirAnalisisNormativoDto input)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        var pregunta = (input.Pregunta ?? "").Trim();
        if (pregunta.Length < 10)
        {
            throw new UserFriendlyException("Describe la operación o la pregunta con un poco más de detalle.");
        }

        var tipo = input.ContraparteTipoDePersona?.Trim().ToLowerInvariant();
        if (tipo is not (null or "" or "fisica" or "moral"))
        {
            throw new UserFriendlyException("La contraparte es persona «fisica» o «moral».");
        }

        var conocidos = ListaDeDisparadores.Select(d => d.Clave).ToHashSet();
        var disparadores = input.Disparadores.Where(conocidos.Contains).Distinct().ToList();
        var fila = new AnalisisNormativo(GuidGenerator.Create(), organizacionId, pregunta, input.FechaDeLaOperacion?.Date,
            tipo, input.ContraparteResidencia?.Trim().ToUpperInvariant(), string.Join(",", disparadores), Clock.Now.ToUniversalTime());
        await _analisis.InsertAsync(fila, autoSave: true);
        return Dto(fila, conResultado: false);
    }

    public async Task<List<AnalisisNormativoDto>> GetConsultasAsync(Guid organizacionId)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        var filas = await AsyncExecuter.ToListAsync((await _analisis.GetQueryableAsync())
            .Where(x => x.OrganizacionId == organizacionId).OrderByDescending(x => x.CreationTime).Take(100));
        return [.. filas.Select(f => Dto(f, conResultado: false))];
    }

    public async Task<AnalisisNormativoDto> GetAnalisisAsync(Guid organizacionId, Guid id)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        var fila = await _analisis.FirstOrDefaultAsync(x => x.Id == id && x.OrganizacionId == organizacionId)
                   ?? throw new EntityNotFoundException(typeof(AnalisisNormativo), id);
        return Dto(fila, conResultado: true);
    }

    public List<DisparadorDto> GetDisparadores() =>
        [.. ListaDeDisparadores.Select(d => new DisparadorDto { Clave = d.Clave, Descripcion = d.Descripcion })];

    private static AnalisisNormativoDto Dto(AnalisisNormativo f, bool conResultado) => new()
    {
        Id = f.Id,
        Pregunta = f.Pregunta,
        FechaDeLaOperacion = f.FechaDeLaOperacion,
        ContraparteTipoDePersona = f.ContraparteTipoDePersona,
        ContraparteResidencia = f.ContraparteResidencia,
        Disparadores = [.. (f.Disparadores ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)],
        Estado = f.Estado.ToString(),
        Conclusion = f.Conclusion,
        Confianza = f.Confianza,
        Error = f.Error,
        CreationTime = f.CreationTime,
        ListoUtc = f.ListoUtc,
        Resultado = conResultado ? f.Resultado : null,
    };

    private async Task ExigirEquipoAsync(Guid organizacionId, AccionEnOrganizacion accion)
    {
        await _autorizador.ExigirAsync(organizacionId, accion);
        if (!(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new AbpAuthorizationException("El análisis normativo es del despacho.");
        }
    }
}

/// <summary>
/// Hace los análisis pedidos (lo llama <c>AnalisisNormativoWorker</c>): arma el perfil del cliente con su perfil
/// fiscal —tipo de persona, regímenes, actividades de la constancia, código postal, trabajadores— y llama al motor.
/// El RFC no viaja; el nombre sólo sirve al motor para la forma jurídica.
/// </summary>
public class AnalizadorNormativo : ITransientDependency
{
    private readonly IRepository<AnalisisNormativo, Guid> _analisis;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly IMotorFiscal _motor;
    private readonly IUnitOfWorkManager _unidades;
    private readonly IClock _reloj;
    private readonly ILogger<AnalizadorNormativo> _log;

    public AnalizadorNormativo(IRepository<AnalisisNormativo, Guid> analisis, IRepository<PerfilFiscal, Guid> perfiles,
        IMotorFiscal motor, IUnitOfWorkManager unidades, IClock reloj, ILogger<AnalizadorNormativo> log)
    {
        _analisis = analisis;
        _perfiles = perfiles;
        _motor = motor;
        _unidades = unidades;
        _reloj = reloj;
        _log = log;
    }

    public async Task<int> ProcesarAsync(int maximo = 1)
    {
        List<Guid> ids;
        using (var uow = _unidades.Begin(requiresNew: true))
        {
            var ahora = _reloj.Now.ToUniversalTime();
            ids = [.. (await _analisis.GetQueryableAsync())
                .Where(x => x.Estado == EstadoDelAnalisis.Pendiente && x.PedidoUtc <= ahora)
                .OrderBy(x => x.PedidoUtc).Select(x => x.Id).Take(maximo)];
            await uow.CompleteAsync();
        }

        var hechos = 0;
        foreach (var id in ids)
        {
            try
            {
                using var uow = _unidades.Begin(requiresNew: true);
                var fila = await _analisis.GetAsync(id);
                try
                {
                    await AnalizarAsync(fila);
                    hechos++;
                }
                catch (MotorFiscalNoDisponibleException ex)
                {
                    fila.Fallar("El motor o Claude no respondieron: " + ex.Message, _reloj.Now.ToUniversalTime());
                }
                catch (Exception ex) when (ex is UserFriendlyException or BusinessException)
                {
                    fila.Detener(ex.Message);
                }

                await _analisis.UpdateAsync(fila, autoSave: true);
                await uow.CompleteAsync();
            }
            catch (AbpDbConcurrencyException)
            {
                // Otra petición la tocó: se intenta en la siguiente vuelta.
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "No se pudo hacer el análisis normativo {Id}", id);
            }
        }

        return hechos;
    }

    private async Task AnalizarAsync(AnalisisNormativo fila)
    {
        var perfil = await PerfilAsync(fila.OrganizacionId, fila.FechaDeLaOperacion?.Year);
        var operacion = new OperacionParaNormativa
        {
            Descripcion = fila.Pregunta,
            Fecha = fila.FechaDeLaOperacion?.ToString("yyyy-MM-dd"),
            Contraparte = fila.ContraparteTipoDePersona == null && fila.ContraparteResidencia == null
                ? null
                : new ContraparteParaNormativa { TipoPersona = fila.ContraparteTipoDePersona, Residencia = fila.ContraparteResidencia },
            Disparadores = [.. (fila.Disparadores ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)],
        };
        var json = await _motor.AnalizarNormativaAsync(perfil, operacion);
        string? conclusion = null, confianza = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("analisis", out var a))
            {
                conclusion = a.TryGetProperty("conclusion", out var c) ? c.GetString() : null;
                confianza = a.TryGetProperty("confianza", out var k) ? k.GetString() : null;
            }
        }
        catch (JsonException)
        {
            throw new UserFriendlyException("El motor devolvió un análisis que no se pudo leer.");
        }

        fila.Terminar(json, conclusion, confianza, _reloj.Now.ToUniversalTime());
    }

    public async Task<PerfilParaNormativa> PerfilAsync(Guid organizacionId, int? ejercicio)
    {
        var p = await _perfiles.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId)
                ?? throw new UserFriendlyException("El cliente no tiene perfil fiscal: súbele su constancia de situación fiscal.");
        string? Campo(string clave) => p.Dato(clave)?.Valor?.Trim();
        // Las listas del perfil se guardan como arreglo JSON; un texto suelto se parte por renglón o «;».
        static List<string> Lista(string? v)
        {
            if (string.IsNullOrWhiteSpace(v))
            {
                return [];
            }

            try
            {
                using var json = JsonDocument.Parse(v);
                if (json.RootElement.ValueKind == JsonValueKind.Array)
                {
                    return [.. json.RootElement.EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                        .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim())];
                }
            }
            catch (JsonException)
            {
                // texto suelto
            }

            return [.. v.Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        }

        var giro = Campo(CamposDelPerfil.Giro);
        var actividades = Lista(Campo(CamposDelPerfil.ActividadesEconomicas));
        if (actividades.Count == 0 && !string.IsNullOrWhiteSpace(giro))
        {
            actividades.Add(giro);
        }

        return new PerfilParaNormativa
        {
            TipoPersona = Campo(CamposDelPerfil.TipoDePersona),
            Nombre = Campo(CamposDelPerfil.Nombre),
            Regimenes = Lista(Campo(CamposDelPerfil.Regimenes)),
            Actividades = actividades,
            CodigoPostal = Campo(CamposDelPerfil.CodigoPostal),
            TieneTrabajadores = Campo(CamposDelPerfil.TieneTrabajadores),
            Ejercicio = ejercicio,
        };
    }
}
