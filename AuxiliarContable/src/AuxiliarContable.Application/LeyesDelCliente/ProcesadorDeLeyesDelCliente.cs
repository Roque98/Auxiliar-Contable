using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Concentrado;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Normativa;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AuxiliarContable.LeyesDelCliente;

/// <summary>
/// Lo que hace el trabajador de «Mis leyes» en cada vuelta: manda al motor las leyes por leer y pregunta cómo va su OCR,
/// analiza las que cambiaron de huella (perfil, ejercicio o valores del sistema) y contesta preguntas. Una llamada larga
/// a la vez: cada análisis es un claude -p en el servidor del motor.
/// </summary>
public class ProcesadorDeLeyesDelCliente : ITransientDependency
{
    private static readonly TimeSpan EsperaDeLectura = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan EsperaTrasFalla = TimeSpan.FromMinutes(10);

    private readonly IRepository<LeyDelCliente, Guid> _leyes;
    private readonly IRepository<HallazgoDeLey, Guid> _hallazgos;
    private readonly IRepository<PreguntaSobreLeyes, Guid> _preguntas;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly AnalizadorNormativo _normativa;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly IMotorFiscal _motor;
    private readonly IUnitOfWorkManager _unidades;
    private readonly IGuidGenerator _guids;
    private readonly IClock _reloj;
    private readonly ILogger<ProcesadorDeLeyesDelCliente> _log;

    public ProcesadorDeLeyesDelCliente(IRepository<LeyDelCliente, Guid> leyes, IRepository<HallazgoDeLey, Guid> hallazgos,
        IRepository<PreguntaSobreLeyes, Guid> preguntas, IRepository<Documento, Guid> documentos, AlmacenDeDocumentos almacen,
        AnalizadorNormativo normativa, IRepository<PerfilFiscal, Guid> perfiles, IMotorFiscal motor, IUnitOfWorkManager unidades, IGuidGenerator guids, IClock reloj,
        ILogger<ProcesadorDeLeyesDelCliente> log)
    {
        _leyes = leyes;
        _hallazgos = hallazgos;
        _preguntas = preguntas;
        _documentos = documentos;
        _almacen = almacen;
        _normativa = normativa;
        _perfiles = perfiles;
        _motor = motor;
        _unidades = unidades;
        _guids = guids;
        _reloj = reloj;
        _log = log;
    }

    private DateTime Ahora => _reloj.Now.ToUniversalTime();

    /// <summary>-> cuántas cosas se movieron (lecturas, análisis, preguntas).</summary>
    public async Task<int> ProcesarAsync()
    {
        if (!_motor.EstaConfigurado)
        {
            return 0;
        }

        var hechos = 0;
        List<Guid> porLeer, listas, preguntas;
        using (var uow = _unidades.Begin(requiresNew: true))
        {
            var ahora = Ahora;
            var q = await _leyes.GetQueryableAsync();
            porLeer = [.. q.Where(x => (x.Lectura == LecturaDeLey.Pendiente || x.Lectura == LecturaDeLey.Leyendo) && x.ProximoIntentoUtc <= ahora)
                .OrderBy(x => x.ProximoIntentoUtc).Select(x => x.Id).Take(5)];
            preguntas = [.. (await _preguntas.GetQueryableAsync()).Where(x => x.Estado == EstadoDePregunta.Pendiente && x.PedidoUtc <= ahora)
                .OrderBy(x => x.PedidoUtc).Select(x => x.Id).Take(1)];
            await uow.CompleteAsync();
        }

        foreach (var id in porLeer)
        {
            hechos += await ConUnidadAsync(id, LeerAsync);
        }

        // Las listas se buscan después de leer: la que se acaba de leer se analiza en la misma vuelta.
        using (var uow = _unidades.Begin(requiresNew: true))
        {
            var ahora = Ahora;
            listas = [.. (await _leyes.GetQueryableAsync()).Where(x => x.Lectura == LecturaDeLey.Lista && x.ProximoIntentoUtc <= ahora)
                .OrderBy(x => x.ProximoIntentoUtc).Select(x => x.Id).Take(200)];
            await uow.CompleteAsync();
        }

        // Una sola llamada larga por vuelta: la primera ley cuya huella cambió.
        foreach (var id in listas)
        {
            var r = await ConUnidadAsync(id, AnalizarSiTocaAsync);
            hechos += r;
            if (r > 0)
            {
                break;
            }
        }

        foreach (var id in preguntas)
        {
            hechos += await ConPreguntaAsync(id);
        }

        return hechos;
    }

    private async Task<int> ConUnidadAsync(Guid id, Func<LeyDelCliente, Task<int>> paso)
    {
        try
        {
            using var uow = _unidades.Begin(requiresNew: true);
            var ley = await _leyes.FindAsync(id);
            if (ley == null)
            {
                return 0;
            }

            var r = await paso(ley);
            await _leyes.UpdateAsync(ley, autoSave: true);
            await uow.CompleteAsync();
            return r;
        }
        catch (AbpDbConcurrencyException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Mis leyes: no se pudo procesar la ley {Id}", id);
            return 0;
        }
    }

    private async Task<int> LeerAsync(LeyDelCliente ley)
    {
        try
        {
            LecturaDeLeyDelMotor? r = null;
            if (ley.Lectura == LecturaDeLey.Leyendo)
            {
                r = await _motor.EstadoDeLeyAsync(ley.Sha256);
            }

            if (r == null)
            {
                var doc = await _documentos.GetAsync(ley.DocumentoId);
                r = await _motor.IndexarLeyAsync(new ArchivoParaElMotor(ley.NombreDelArchivo, await _almacen.LeerAsync(doc)));
            }

            switch (r.Estado)
            {
                case "lista":
                    ley.Leida(r.Paginas, r.PaginasOcr, r.ArticulosTotal, r.Titulo, Ahora);
                    break;
                case "error":
                    ley.NoSePudoLeer(r.Error ?? "El motor no pudo leer el PDF.");
                    break;
                default:
                    ley.Leyendo(r.Paginas, r.PaginasOcr, r.PaginasLeidas, Ahora + EsperaDeLectura);
                    break;
            }

            return 1;
        }
        catch (MotorFiscalNoDisponibleException ex) when (ex.Data["Codigo"] is 422)
        {
            ley.NoSePudoLeer(ex.Data["Detalle"] as string ?? "El motor no pudo leer el PDF.");
            return 1;
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            ley.LecturaFallida("El motor no respondió: " + ex.Message, Ahora + EsperaTrasFalla);
            return 0;
        }
    }

    /// <summary>Lo que se le manda al motor del cliente (sin RFC ni nombre) y lo que hoy usa el sistema.</summary>
    public async Task<(Dictionary<string, object?> Perfil, Dictionary<string, object?> Sistema, string Huella)> PreguntaAsync(LeyDelCliente ley)
    {
        var anio = _reloj.Now.Year;
        var p = await _normativa.PerfilAsync(ley.OrganizacionId, anio);
        var domicilio = (await _perfiles.FirstOrDefaultAsync(x => x.OrganizacionId == ley.OrganizacionId))
            ?.Dato(CamposDelPerfil.DomicilioFiscal)?.Valor;
        var estado = ley.Estado ?? IsnPorEstado.Buscar(domicilio)?.Clave;
        var perfil = new Dictionary<string, object?>
        {
            ["tipo_persona"] = p.TipoPersona,
            ["regimenes"] = p.Regimenes,
            ["actividades"] = p.Actividades,
            ["giro"] = p.Actividades.FirstOrDefault(),
            ["tiene_trabajadores"] = p.TieneTrabajadores,
            ["estado"] = estado,
            ["municipio"] = ley.Municipio,
            ["ejercicio"] = anio,
        };
        var sistema = new Dictionary<string, object?>();
        if (IsnPorEstado.Buscar(estado, anio) is { } regla)
        {
            sistema["isn_dia"] = regla.Dia;
            sistema["isn_fundamento_plazo"] = regla.FundamentoPlazo;
            sistema["recargos_tasa"] = regla.Tasa;
            sistema["recargos_fundamento"] = regla.FundamentoRecargos;
        }

        var huella = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new object[] { ley.Sha256, perfil, sistema }))));
        return (perfil, sistema, huella);
    }

    private async Task<int> AnalizarSiTocaAsync(LeyDelCliente ley)
    {
        (Dictionary<string, object?> perfil, Dictionary<string, object?> sistema, string huella) q;
        try
        {
            q = await PreguntaAsync(ley);
        }
        catch (UserFriendlyException)
        {
            return 0;       // sin perfil fiscal no hay contra qué analizar; se intenta cuando lo tenga
        }

        if (!ley.TocaAnalizar(q.huella, Ahora))
        {
            return 0;
        }

        ley.Analizando(q.huella);
        try
        {
            var json = await _motor.AnalizarLeyesAsync([ley.Sha256], q.perfil, q.sistema);
            await GuardarHallazgosAsync(ley, json);
            return 1;
        }
        catch (MotorFiscalNoDisponibleException ex) when (ex.Data["Codigo"] is 404)
        {
            ley.VolverALeer(Ahora);
            return 1;
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            ley.AnalisisFallido("El motor o Claude no respondieron: " + ex.Message, Ahora + EsperaTrasFalla);
            return 1;
        }
        catch (JsonException)
        {
            ley.AnalisisFallido("El motor devolvió un análisis que no se pudo leer.", Ahora + EsperaTrasFalla);
            return 1;
        }
    }

    private async Task GuardarHallazgosAsync(LeyDelCliente ley, string json)
    {
        using var d = JsonDocument.Parse(json);
        var raiz = d.RootElement;
        static string? S(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static decimal? N(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;

        var existentes = await _hallazgos.GetListAsync(h => h.LeyId == ley.Id);
        // Los propuestos se reemplazan por los nuevos; lo que el despacho ya decidió se queda.
        await _hallazgos.DeleteManyAsync(existentes.Where(h => h.Decision == DecisionDeHallazgo.Propuesto), autoSave: true);
        var decididos = existentes.Where(h => h.Decision != DecisionDeHallazgo.Propuesto).Select(h => h.Llave).ToHashSet();
        if (raiz.TryGetProperty("hallazgos", out var lista) && lista.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in lista.EnumerateArray())
            {
                var clave = S(x, "clave");
                var h = new HallazgoDeLey(_guids.Create(), ley.OrganizacionId, ley.Id, S(x, "tipo") ?? "aviso", S(x, "tema"), S(x, "titulo") ?? "",
                    string.IsNullOrEmpty(clave) ? null : clave, S(x, "valor"), N(x, "valor_numerico"), S(x, "valor_del_sistema"),
                    S(x, "condicion"), S(x, "periodicidad"), S(x, "fecha_limite"), S(x, "articulo") ?? "", S(x, "etiqueta"),
                    x.TryGetProperty("pagina", out var pg) && pg.ValueKind == JsonValueKind.Number ? pg.GetInt32() : null,
                    S(x, "cita") ?? "", S(x, "explicacion"));
                if (!string.IsNullOrEmpty(h.Cita) && decididos.Add(h.Llave))
                {
                    await _hallazgos.InsertAsync(h);
                }
            }
        }

        ley.Analizada(ley.HuellaEnCurso!, S(raiz, "resumen"), json, Ahora);
    }

    private async Task<int> ConPreguntaAsync(Guid id)
    {
        try
        {
            using var uow = _unidades.Begin(requiresNew: true);
            var p = await _preguntas.GetAsync(id);
            var leyes = await _leyes.GetListAsync(x => x.OrganizacionId == p.OrganizacionId && x.Lectura != LecturaDeLey.Error);
            if (leyes.Count == 0)
            {
                p.Detener("No hay leyes subidas que se puedan leer.");
            }
            else if (leyes.Any(l => l.Lectura != LecturaDeLey.Lista))
            {
                p.Esperar(Ahora + EsperaDeLectura);     // se contesta cuando terminen de leerse todas
            }
            else
            {
                try
                {
                    var perfil = (await PreguntaAsync(leyes[0])).Perfil;
                    perfil.Remove("municipio");
                    p.Contestar(await _motor.PreguntarLeyesAsync([.. leyes.Select(l => l.Sha256)], p.Pregunta, perfil), Ahora);
                }
                catch (UserFriendlyException)
                {
                    p.Contestar(await _motor.PreguntarLeyesAsync([.. leyes.Select(l => l.Sha256)], p.Pregunta, new Dictionary<string, object?>()), Ahora);
                }
                catch (MotorFiscalNoDisponibleException ex) when (ex.Data["Codigo"] is 404)
                {
                    foreach (var l in leyes)
                    {
                        l.VolverALeer(Ahora);
                        await _leyes.UpdateAsync(l);
                    }

                    p.Esperar(Ahora + EsperaDeLectura);
                }
                catch (MotorFiscalNoDisponibleException ex)
                {
                    p.Fallar("El motor o Claude no respondieron: " + ex.Message, Ahora + EsperaTrasFalla);
                }
            }

            await _preguntas.UpdateAsync(p, autoSave: true);
            await uow.CompleteAsync();
            return 1;
        }
        catch (AbpDbConcurrencyException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Mis leyes: no se pudo contestar la pregunta {Id}", id);
            return 0;
        }
    }
}
