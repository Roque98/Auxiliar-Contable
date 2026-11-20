using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Bancos;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Nomina;
using AuxiliarContable.Normativa;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using IdentityUser = Volo.Abp.Identity.IdentityUser;
using IdentityUserManager = Volo.Abp.Identity.IdentityUserManager;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Hace una vuelta de <see cref="ConciliacionIntegral"/> (la llama <c>ConciliacionIntegralWorker</c>): con los permisos de
/// quien la pidió, para cada mes abierto del año hasta el elegido corre, en orden, todo lo que la plataforma ya sabe hacer:
/// facturas del SAT (descarga masiva con la e.firma), su estatus, las operaciones, tickets y control interno (Claude), el
/// banco, el amarre, la interpretación, la nómina, las pólizas, los impuestos (con los cobros PPD que demuestra el banco),
/// los estados financieros con notas y las alertas normativas. Un paso que falla no detiene a los demás: queda anotado.
/// </summary>
public class ConciliadorIntegral : ITransientDependency
{
    private static readonly (string Clave, string Titulo)[] PasosDelMes =
    [
        ("facturas", "Facturas: operaciones del mes y su estatus en el SAT"),
        ("documentos", "Tickets y control interno (Claude)"),
        ("banco", "Estados de cuenta: conciliación del banco"),
        ("amarre", "Amarre del banco con las facturas"),
        ("interpretacion", "Interpretación de las operaciones (Claude)"),
        ("nomina", "Nómina del mes"),
        ("contabilidad", "Pólizas y balanza"),
        ("impuestos", "Cálculo de impuestos"),
        ("estados", "Estados financieros y notas"),
        ("normativa", "Alertas normativas"),
    ];

    private readonly IRepository<ConciliacionIntegral, Guid> _vueltas;
    private readonly IRepository<Operacion, Guid> _operaciones;
    private readonly IUnitOfWorkManager _unidades;
    private readonly IClock _reloj;
    private readonly IdentityUserManager _usuarios;
    private readonly IUserClaimsPrincipalFactory<IdentityUser> _principales;
    private readonly ICurrentPrincipalAccessor _principal;
    private readonly ICurrentTenant _inquilino;
    private readonly IAbpLazyServiceProvider _servicios;

    public ILogger<ConciliadorIntegral> Logger { get; set; } = NullLogger<ConciliadorIntegral>.Instance;

    public ConciliadorIntegral(
        IRepository<ConciliacionIntegral, Guid> vueltas,
        IRepository<Operacion, Guid> operaciones,
        IUnitOfWorkManager unidades,
        IClock reloj,
        IdentityUserManager usuarios,
        IUserClaimsPrincipalFactory<IdentityUser> principales,
        ICurrentPrincipalAccessor principal,
        ICurrentTenant inquilino,
        IAbpLazyServiceProvider servicios)
    {
        _vueltas = vueltas;
        _operaciones = operaciones;
        _unidades = unidades;
        _reloj = reloj;
        _usuarios = usuarios;
        _principales = principales;
        _principal = principal;
        _inquilino = inquilino;
        _servicios = servicios;
    }

    private T S<T>() where T : class => _servicios.LazyGetRequiredService<T>();

    /// <summary>Hace la siguiente vuelta pedida (una a la vez). -> si hizo alguna.</summary>
    public async Task<bool> ProcesarAsync()
    {
        Guid? id;
        using (var uow = _unidades.Begin(requiresNew: true))
        {
            id = (await _vueltas.GetQueryableAsync())
                .Where(v => v.Estado == EstadoDeConciliacionIntegral.Pendiente)
                .OrderBy(v => v.PedidaUtc).Select(v => (Guid?)v.Id).FirstOrDefault();
            await uow.CompleteAsync();
        }

        if (id == null)
        {
            return false;
        }

        await EjecutarAsync(id.Value);
        return true;
    }

    public async Task EjecutarAsync(Guid id)
    {
        ConciliacionIntegral vuelta;
        IdentityUser? usuario;
        using (var uow = _unidades.Begin(requiresNew: true))
        {
            vuelta = await _vueltas.GetAsync(id);
            vuelta.Iniciar(_reloj.Now.ToUniversalTime());
            await _vueltas.UpdateAsync(vuelta, autoSave: true);
            usuario = await _usuarios.FindByIdAsync(vuelta.UsuarioId.ToString());
            await uow.CompleteAsync();
        }

        if (usuario == null)
        {
            await GuardarAsync(id, v => v.Fallar("El usuario que pidió la conciliación ya no existe.", _reloj.Now.ToUniversalTime()));
            return;
        }

        try
        {
            using (_inquilino.Change(usuario.TenantId))
            {
                ClaimsPrincipal yo;
                using (var uow = _unidades.Begin(requiresNew: true))
                {
                    yo = await _principales.CreateAsync(usuario);
                    await uow.CompleteAsync();
                }

                using (_principal.Change(yo))
                {
                    await RecorrerAsync(vuelta);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "La conciliación integral {Id} no terminó", id);
            await GuardarAsync(id, v => v.Fallar("No se pudo terminar la conciliación: " + ex.Message, _reloj.Now.ToUniversalTime()));
        }
    }

    private async Task RecorrerAsync(ConciliacionIntegral vuelta)
    {
        var org = vuelta.OrganizacionId;
        var periodos = S<GestorDePeriodos>();

        // Del primer mes abierto del año hasta el elegido: un cobro de hoy actualiza la factura PPD de un mes anterior.
        var desde = vuelta.Mes;
        while (desde > 1 && !await EnUowAsync(() => periodos.EstaCerradoAsync(org, vuelta.Anio, desde - 1)))
        {
            desde--;
        }

        var pasos = new List<PasoDeConciliacionDto>
        {
            new() { Clave = "sat", Titulo = "Descarga de facturas del SAT con la e.firma" },
        };
        for (var m = desde; m <= vuelta.Mes; m++)
        {
            pasos.AddRange(PasosDelMes.Select(p => new PasoDeConciliacionDto
            {
                Periodo = $"{vuelta.Anio:0000}-{m:00}", Clave = p.Clave, Titulo = p.Titulo,
            }));
        }

        var meses = new List<MesConciliadoDto>();
        await GuardarPasosAsync(vuelta.Id, pasos);

        await PasoAsync(vuelta.Id, pasos, pasos[0], () => DescargarDelSatAsync(org, vuelta.Anio, vuelta.Mes));

        for (var m = desde; m <= vuelta.Mes; m++)
        {
            var periodo = $"{vuelta.Anio:0000}-{m:00}";
            var mes = m;
            var resumen = new MesConciliadoDto { Anio = vuelta.Anio, Mes = m };
            meses.Add(resumen);
            PasoDeConciliacionDto P(string clave) => pasos.First(p => p.Periodo == periodo && p.Clave == clave);

            if (await EnUowAsync(() => periodos.EstaCerradoAsync(org, vuelta.Anio, mes)))
            {
                foreach (var p in pasos.Where(p => p.Periodo == periodo))
                {
                    p.Estado = "omitido";
                    p.Resumen = "El mes está cerrado: se queda como se cerró.";
                }

                await GuardarPasosAsync(vuelta.Id, pasos);
                continue;
            }

            await PasoAsync(vuelta.Id, pasos, P("facturas"), () => FacturasAsync(org, vuelta.Anio, mes));
            await PasoAsync(vuelta.Id, pasos, P("documentos"), () => DocumentosAsync(org, vuelta.Anio, mes));
            await PasoAsync(vuelta.Id, pasos, P("banco"), () => BancoAsync(org, vuelta.Anio, mes));
            await PasoAsync(vuelta.Id, pasos, P("amarre"), () => AmarreAsync(org, vuelta.Anio, mes));
            await PasoAsync(vuelta.Id, pasos, P("interpretacion"), () => InterpretacionAsync(org, vuelta.Anio, mes));
            await PasoAsync(vuelta.Id, pasos, P("nomina"), () => NominaAsync(org, vuelta.Anio, mes));
            await PasoAsync(vuelta.Id, pasos, P("contabilidad"), () => ContabilidadAsync(org, vuelta.Anio, mes, resumen));
            await PasoAsync(vuelta.Id, pasos, P("impuestos"), () => ImpuestosAsync(org, vuelta.Anio, mes, resumen));
            await PasoAsync(vuelta.Id, pasos, P("estados"), () => EstadosAsync(org, vuelta.Anio, mes, resumen));
            await PasoAsync(vuelta.Id, pasos, P("normativa"), () => NormativaAsync(org, vuelta.Anio, mes, resumen));
        }

        await GuardarAsync(vuelta.Id, v => v.Terminar(
            JsonSerializer.Serialize(pasos), JsonSerializer.Serialize(meses), _reloj.Now.ToUniversalTime()));
    }

    // ------------------------------------------------------------------ pasos
    /// <summary>Un resultado de paso: estado (hecho, aviso, omitido) y resumen.</summary>
    private sealed record Hecho(string Estado, string Resumen);

    private static Hecho Bien(string r) => new("hecho", r);
    private static Hecho Aviso(string r) => new("aviso", r);
    private static Hecho Omitido(string r) => new("omitido", r);

    private async Task PasoAsync(Guid id, List<PasoDeConciliacionDto> pasos, PasoDeConciliacionDto paso, Func<Task<Hecho>> hacer)
    {
        paso.Estado = "trabajando";
        await GuardarPasosAsync(id, pasos);
        try
        {
            using var uow = _unidades.Begin(requiresNew: true);
            var r = await hacer();
            await uow.CompleteAsync();
            paso.Estado = r.Estado;
            paso.Resumen = r.Resumen;
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            paso.Estado = "error";
            paso.Resumen = "El motor o Claude no respondieron: " + ex.Message;
        }
        catch (Exception ex) when (ex is UserFriendlyException or BusinessException or Volo.Abp.Authorization.AbpAuthorizationException)
        {
            paso.Estado = "aviso";
            paso.Resumen = ex.Message;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Paso {Paso} de la conciliación integral", paso.Clave);
            paso.Estado = "error";
            paso.Resumen = "No se pudo: " + ex.Message;
        }

        await GuardarPasosAsync(id, pasos);
    }

    private async Task<Hecho> DescargarDelSatAsync(Guid org, int anio, int mes)
    {
        var sat = S<DescargadorMasivo>();
        var problema = await sat.ProblemaConLaEfirmaAsync(org);
        var vuelta = await sat.AvanzarAsync(org);
        var importadas = vuelta.Terminadas > 0 ? $" Se importaron {vuelta.Terminadas} paquete(s) que ya estaban listos." : "";
        if (problema != null)
        {
            return Omitido($"Sin descarga masiva: {problema}{importadas}");
        }

        var pedidas = await sat.PedirAsync(org, anio, mes, emitidas: true, recibidas: true, automatica: false);
        return Bien((pedidas.Count == 0
            ? $"Ya hay una solicitud en proceso para {mes:00}/{anio}."
            : $"Se pidieron al SAT las emitidas y recibidas de {mes:00}/{anio}; el SAT tarda de minutos a horas: lo que llegue entra al volver a conciliar.")
            + importadas);
    }

    private async Task<Hecho> FacturasAsync(Guid org, int anio, int mes)
    {
        var desde = new DateTime(anio, mes, 1);
        await S<RegistroDeOperaciones>().AgregarLoQueFaltaAsync(org, desde, desde.AddMonths(1));
        var r = await S<RevisorDeEstatusSat>().RevisarMesAsync(org, anio, mes);
        var texto = $"Estatus revisado de {r.Revisadas} factura(s)";
        if (r.Canceladas > 0)
        {
            texto += $", {r.Canceladas} cancelada(s)";
        }

        if (r.Faltan > 0)
        {
            texto += $"; faltan {r.Faltan} (el SAT dejó de responder o el tope de la vuelta)";
        }

        return r.Faltan > 0 || r.Fallaron > 0 ? Aviso(texto + ".") : Bien(texto + ".");
    }

    private async Task<Hecho> DocumentosAsync(Guid org, int anio, int mes)
    {
        var mesa = S<IMesaDeTrabajoAppService>();
        int analizados = 0, completadas = 0, faltan = 0;
        var avisos = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var r = await mesa.AnalizarAsync(org, anio, mes);
            analizados += r.Analizados;
            completadas += r.Completadas;
            faltan = r.Faltan;
            avisos.AddRange(r.Avisos);
            if (r.Analizados == 0 || r.Faltan == 0)
            {
                break;
            }
        }

        if (analizados == 0 && faltan == 0)
        {
            return Bien("No hay tickets ni documentos nuevos por leer.");
        }

        var texto = $"{analizados} documento(s) leído(s), {completadas} operación(es) completada(s)"
                    + (faltan > 0 ? $"; faltan {faltan} (se leen al volver a conciliar)" : "") + ".";
        return avisos.Count > 0 || faltan > 0 ? Aviso(texto + " " + string.Join(" ", avisos.Distinct().Take(3))) : Bien(texto);
    }

    private async Task<Hecho> BancoAsync(Guid org, int anio, int mes)
    {
        var c = await S<IBancosAppService>().ConciliarAsync(org, anio, mes);
        var movs = c.Resultado.Movimientos.Count;
        var sinFactura = c.Resultado.FacturasSinMovimiento.Count;
        return Bien($"{c.EstadosDeCuenta} estado(s) de cuenta, {movs} movimiento(s), {c.Facturas} factura(s); "
                    + $"{sinFactura} factura(s) sin movimiento del banco.");
    }

    private async Task<Hecho> AmarreAsync(Guid org, int anio, int mes)
    {
        var r = await S<IMesaDeTrabajoAppService>().AmarrarBancoAsync(org, anio, mes);
        var texto = $"{r.PorUuid + r.PorRegla + r.PorClaude} movimiento(s) amarrado(s) (UUID {r.PorUuid}, regla {r.PorRegla}, "
                    + $"Claude {r.PorClaude}); {r.OperacionesNuevas} operación(es) nueva(s) del banco.";
        return r.Sugerencias.Count > 0 || r.Avisos.Count > 0
            ? Aviso(texto + (r.Sugerencias.Count > 0 ? $" {r.Sugerencias.Count} sugerencia(s) por confirmar en la mesa." : ""))
            : Bien(texto);
    }

    private async Task<Hecho> InterpretacionAsync(Guid org, int anio, int mes)
    {
        var interprete = S<InterpretadorDeOperaciones>();
        var pedidas = await interprete.PedirAsync(org, anio, mes);
        if (pedidas == 0)
        {
            return Bien("Las operaciones del mes ya están interpretadas con sus fuentes al día.");
        }

        var hechas = await interprete.ProcesarAsync(maximo: Math.Min(pedidas, 15));
        return hechas >= pedidas
            ? Bien($"{hechas} operación(es) interpretada(s).")
            : Aviso($"{hechas} de {pedidas} interpretadas; las demás siguen en la fila y entran al volver a conciliar.");
    }

    private async Task<Hecho> NominaAsync(Guid org, int anio, int mes)
    {
        var nomina = S<INominaAppService>();
        var datos = await nomina.GetDelClienteAsync(org, anio);
        if (datos.Trabajadores.Count == 0)
        {
            return Omitido("El cliente no tiene trabajadores en la nómina del despacho.");
        }

        var delMes = datos.Periodos.Where(p => p.Fin.Year == anio && p.Fin.Month == mes).ToList();
        if (delMes.Count == 0)
        {
            return Aviso("No hay periodos de nómina que terminen en el mes: créalos en «Nómina» con las incidencias.");
        }

        var calculados = 0;
        foreach (var p in delMes.Where(p => p.CalculadoUtc == null))
        {
            await nomina.CalcularAsync(org, p.Id);
            calculados++;
        }

        return Bien($"{delMes.Count} periodo(s) del mes" + (calculados > 0 ? $", {calculados} calculado(s) ahora." : ", ya calculados."));
    }

    private async Task<Hecho> ContabilidadAsync(Guid org, int anio, int mes, MesConciliadoDto resumen)
    {
        var polizas = await S<Contabilizador>().ContabilizarAsync(org, anio, mes, cierre: null, conservarCierre: true);
        await S<ContabilidadAlDia>().HechoAsync(org, anio, mes);
        var r = JsonSerializer.Deserialize<ResultadoContable>(polizas.Resultado) ?? new ResultadoContable();
        resumen.Polizas = r.Polizas.Count;
        resumen.Detenidas = r.Revision.Where(x => x.Nivel == "detiene").Select(x => x.Operacion ?? "").Distinct().Count();
        var sinComplemento = r.Revision.Count(x => x.Codigo == "PPD_SIN_COMPLEMENTO");
        if (sinComplemento > 0)
        {
            resumen.Avisos.Add($"{sinComplemento} factura(s) PPD pagadas sin complemento de pago: se toman en impuestos por la fecha del banco; pide el complemento.");
        }

        var texto = $"{resumen.Polizas} póliza(s)" + (resumen.Detenidas > 0 ? $"; {resumen.Detenidas} operación(es) detenida(s) por revisar en la mesa" : "") + ".";
        return resumen.Detenidas > 0 ? Aviso(texto) : Bien(texto);
    }

    private async Task<Hecho> ImpuestosAsync(Guid org, int anio, int mes, MesConciliadoDto resumen)
    {
        var calc = S<CalculadoraDeImpuestos>();
        var ahora = _reloj.Now.ToUniversalTime();
        var fila = await calc.FilaAsync(org, anio, mes, ahora);
        var c = await calc.CalcularAsync(org, anio, mes, conExcel: false);
        await calc.GuardarAsync(fila, c, fila.PedidoUtc);
        var delMes = c.Resultado.Meses.FirstOrDefault(x => x.Periodo == $"{anio:0000}-{mes:00}");
        resumen.IsrAPagar = delMes?.IsrAPagar;
        resumen.IvaAPagar = delMes?.IvaAPagar;
        resumen.IvaAFavor = delMes?.IvaSaldoAFavor;
        resumen.Avisos.AddRange(c.Resultado.Avisos.Take(4));
        return Bien($"ISR a pagar {Pesos(delMes?.IsrAPagar)}, IVA a pagar {Pesos(delMes?.IvaAPagar)}"
                    + (delMes?.IvaSaldoAFavor > 0 ? $" (saldo a favor {Pesos(delMes.IvaSaldoAFavor)})" : "")
                    + $", con {c.Facturas} factura(s) del ejercicio.");
    }

    private async Task<Hecho> EstadosAsync(Guid org, int anio, int mes, MesConciliadoDto resumen)
    {
        var ce = await S<IMesaDeTrabajoAppService>().ContabilidadElectronicaAsync(org, anio, mes);
        var ef = ce.Resultado.Estados;
        if (ef == null)
        {
            return Aviso("No se pudieron armar los estados financieros: " + string.Join(" ", ce.Resultado.Avisos.Take(2)));
        }

        resumen.UtilidadDelEjercicio = ef.Resultado.FirstOrDefault(r => r.Concepto.StartsWith("= Utilidad"))?.Importe;
        resumen.EstadosPreliminares = ef.Preliminar;
        resumen.Notas = ef.Notas.Count;
        var texto = $"Utilidad del ejercicio {Pesos(resumen.UtilidadDelEjercicio)}; {ef.Notas.Count} nota(s) en borrador y "
                    + $"{ef.Revelaciones.Count} revelación(es) por verificar"
                    + (ef.Cuadre is { } q && q != 0 ? $"; el balance no cuadra por {Pesos(q)}" : "") + ".";
        return ef.Preliminar || ef.Cuadre is not (null or 0)
            ? Aviso(texto + (ef.Preliminar ? " Preliminares: " + string.Join(" ", ef.Advertencias.Take(2)) : ""))
            : Bien(texto);
    }

    private async Task<Hecho> NormativaAsync(Guid org, int anio, int mes, MesConciliadoDto resumen)
    {
        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1);
        var ops = await _operaciones.GetListAsync(o => o.OrganizacionId == org && o.Fecha >= desde && o.Fecha < hasta);
        if (ops.Count == 0)
        {
            return Omitido("No hay operaciones en el mes.");
        }

        // Lo que pasó en el mes, en palabras (sin RFC): enciende los chispazos del índice de leyes.
        var descripcion = "Operaciones del mes: " + string.Join("; ", ops
            .OrderByDescending(o => o.Importe ?? 0).Take(60)
            .Select(o => $"{(o.Sentido == SentidoDeOperacion.Entrada ? "cobro" : "pago")} {o.Descripcion} {o.Contraparte}".Trim()));
        if (descripcion.Length > 1900)
        {
            descripcion = descripcion[..1900];
        }

        var perfil = await S<AnalizadorNormativo>().PerfilAsync(org, anio);
        using var alMotor = ClienteDelMotor.Para(org);
        var json = await S<IMotorFiscal>().ExpedienteNormativoAsync(perfil,
            new OperacionParaNormativa { Descripcion = descripcion, Fecha = $"{anio:0000}-{mes:00}-01" });
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("chispazos", out var chispazos))
        {
            foreach (var c in chispazos.EnumerateArray())
            {
                var fuentes = c.TryGetProperty("fuentes", out var f)
                    ? f.EnumerateArray().Select(x => $"{x.GetProperty("clave").GetString()} {(x.TryGetProperty("articulo", out var a) ? a.GetString() : "")}".Trim()).Distinct().ToList()
                    : [];
                resumen.AlertasNormativas.Add(new AlertaNormativaDto
                {
                    Disparador = c.GetProperty("disparador").GetString() ?? "",
                    Pregunta = c.GetProperty("pregunta").GetString() ?? "",
                    Fuentes = fuentes,
                });
            }
        }

        return resumen.AlertasNormativas.Count == 0
            ? Bien("Ninguna situación del mes enciende preguntas normativas adicionales.")
            : Aviso($"{resumen.AlertasNormativas.Count} pregunta(s) normativa(s) que revisar (detalle abajo); para razonarlas usa «Análisis normativo».");
    }

    // ------------------------------------------------------------- utilidades
    private static string Pesos(decimal? v) => v == null ? "—" : v.Value.ToString("$#,##0.00");

    private async Task<T> EnUowAsync<T>(Func<Task<T>> f)
    {
        using var uow = _unidades.Begin(requiresNew: true);
        var r = await f();
        await uow.CompleteAsync();
        return r;
    }

    private Task GuardarPasosAsync(Guid id, List<PasoDeConciliacionDto> pasos) =>
        GuardarAsync(id, v => v.Avanzar(JsonSerializer.Serialize(pasos)));

    private async Task GuardarAsync(Guid id, Action<ConciliacionIntegral> cambio)
    {
        for (var intento = 0; intento < 3; intento++)
        {
            try
            {
                using var uow = _unidades.Begin(requiresNew: true);
                var v = await _vueltas.GetAsync(id);
                cambio(v);
                await _vueltas.UpdateAsync(v, autoSave: true);
                await uow.CompleteAsync();
                return;
            }
            catch (AbpDbConcurrencyException)
            {
                // Se reintenta con la versión nueva.
            }
        }
    }
}
