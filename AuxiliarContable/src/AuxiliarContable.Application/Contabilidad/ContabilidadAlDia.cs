using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Bancos;
using AuxiliarContable.MotorFiscal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Pone al día la contabilidad de los meses «por actualizar» que llevan un momento sin cambios (la llama el trabajo en
/// segundo plano del host): concilia el banco si sus archivos cambiaron, amarra lo seguro (por UUID y por regla; sin
/// Claude) y rehace las pólizas conservando lo capturado para el cierre. Cada mes en su propia unidad de trabajo.
/// </summary>
public class ContabilidadAlDia : ITransientDependency
{
    /// <summary>Sin cambios durante esto antes de actualizar: la misma espera que los impuestos al día.</summary>
    public static readonly TimeSpan Espera = ActualizadorDeImpuestos.Espera;

    private readonly IRepository<ContabilidadDelMes, Guid> _meses;
    private readonly Conciliador _conciliador;
    private readonly Contabilizador _contabilizador;
    private readonly GestorDePeriodos _periodos;
    private readonly IUnitOfWorkManager _unidades;
    private readonly IGuidGenerator _guids;
    private readonly IClock _reloj;

    public ILogger<ContabilidadAlDia> Logger { get; set; } = NullLogger<ContabilidadAlDia>.Instance;

    public ContabilidadAlDia(
        IRepository<ContabilidadDelMes, Guid> meses,
        Conciliador conciliador,
        Contabilizador contabilizador,
        GestorDePeriodos periodos,
        IUnitOfWorkManager unidades,
        IGuidGenerator guids,
        IClock reloj)
    {
        _meses = meses;
        _conciliador = conciliador;
        _contabilizador = contabilizador;
        _periodos = periodos;
        _unidades = unidades;
        _guids = guids;
        _reloj = reloj;
    }

    /// <returns>Cuántos meses se pusieron al día.</returns>
    public async Task<int> ActualizarAsync(int maximo = 2, TimeSpan? espera = null)
    {
        List<Guid> ids;
        using (var uow = _unidades.Begin(requiresNew: true))
        {
            var limite = _reloj.Now.ToUniversalTime() - (espera ?? Espera);
            ids = [.. (await _meses.GetQueryableAsync())
                .Where(m => m.PorActualizar && m.PedidoUtc <= limite)
                .OrderBy(m => m.PedidoUtc)
                .Select(m => m.Id)
                .Take(maximo)];
            await uow.CompleteAsync();
        }

        var hechos = 0;
        foreach (var id in ids)
        {
            try
            {
                // Lo que cambia la actualización (operaciones nuevas, la conciliación) no vuelve a pedirla.
                using var callado = MarcadorDeContabilidad.Callar();
                using var uow = _unidades.Begin(requiresNew: true);
                var fila = await _meses.GetAsync(id);
                var pedido = fila.PedidoUtc;
                try
                {
                    var resumen = await PonerAlDiaAsync(fila.OrganizacionId, fila.Anio, fila.Mes);
                    fila.Terminar(resumen, _reloj.Now.ToUniversalTime(), pedido);
                    hechos++;
                }
                catch (MotorFiscalNoDisponibleException)
                {
                    fila.Fallar("El motor contable no respondió; se vuelve a intentar en unos minutos.", _reloj.Now.ToUniversalTime());
                }
                catch (Exception ex) when (ex is UserFriendlyException or BusinessException)
                {
                    fila.Detener(ex.Message, pedido);
                }

                await _meses.UpdateAsync(fila, autoSave: true);
                await uow.CompleteAsync();
            }
            catch (AbpDbConcurrencyException)
            {
                // Otra petición la tocó mientras tanto: se vuelve a intentar en la siguiente vuelta.
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "No se pudo poner al día la contabilidad {Id}", id);
                await AnotarFallaAsync(id);
            }
        }

        return hechos;
    }

    /// <summary>Conciliar (si hace falta), amarrar lo seguro y rehacer las pólizas. -> qué se hizo, en una línea.</summary>
    private async Task<string> PonerAlDiaAsync(Guid organizacionId, int anio, int mes)
    {
        if (await _periodos.EstaCerradoAsync(organizacionId, anio, mes))
        {
            return "El mes está cerrado: se queda como se cerró.";
        }

        var hecho = new List<string>();
        if (await _conciliador.HaceFaltaAsync(organizacionId, anio, mes))
        {
            await _conciliador.ConciliarAsync(organizacionId, anio, mes);
            hecho.Add("se concilió el banco");
        }

        var amarre = await _contabilizador.AmarrarAsync(organizacionId, anio, mes, conClaude: false);
        var amarrados = amarre == null ? 0 : amarre.PorUuid + amarre.PorRegla;
        if (amarrados > 0)
        {
            hecho.Add(amarrados == 1 ? "1 movimiento del banco amarrado" : $"{amarrados} movimientos del banco amarrados");
        }

        var polizas = await _contabilizador.ContabilizarAsync(organizacionId, anio, mes, cierre: null, conservarCierre: true);
        var resultado = JsonSerializer.Deserialize<ResultadoContable>(polizas.Resultado) ?? new ResultadoContable();
        hecho.Add(resultado.Polizas.Count == 1 ? "1 póliza" : $"{resultado.Polizas.Count} pólizas");
        var detienen = resultado.Revision.Where(r => r.Nivel == "detiene").Select(r => r.Operacion ?? "").Distinct().Count();
        if (detienen > 0)
        {
            hecho.Add(detienen == 1 ? "1 operación detenida por revisar" : $"{detienen} operaciones detenidas por revisar");
        }

        var texto = string.Join(" · ", hecho);
        return char.ToUpperInvariant(texto[0]) + texto[1..] + ".";
    }

    /// <summary>La fila del mes (la crea si no existe). <paramref name="pedidoUtc"/> solo cuenta al crearla.</summary>
    public async Task<ContabilidadDelMes> FilaAsync(Guid organizacionId, int anio, int mes, DateTime pedidoUtc)
    {
        var fila = await _meses.FirstOrDefaultAsync(m => m.OrganizacionId == organizacionId && m.Anio == anio && m.Mes == mes);
        return fila ?? await _meses.InsertAsync(new ContabilidadDelMes(_guids.Create(), organizacionId, anio, mes, pedidoUtc), autoSave: true);
    }

    /// <summary>«Actualizar ahora»: en la siguiente vuelta, sin esperar.</summary>
    public async Task<ContabilidadDelMes> PedirAhoraAsync(Guid organizacionId, int anio, int mes)
    {
        var ya = _reloj.Now.ToUniversalTime() - Espera;
        var fila = await FilaAsync(organizacionId, anio, mes, ya);
        fila.Pedir(ya);
        await _meses.UpdateAsync(fila, autoSave: true);
        return fila;
    }

    /// <summary>Las pólizas se acaban de generar a mano: lo pedido hasta ahora ya está.</summary>
    public async Task HechoAsync(Guid organizacionId, int anio, int mes)
    {
        var ahora = _reloj.Now.ToUniversalTime();
        var fila = await FilaAsync(organizacionId, anio, mes, ahora);
        fila.Terminar("Pólizas generadas a mano.", ahora, ahora);
        await _meses.UpdateAsync(fila, autoSave: true);
    }

    /// <summary>Que una falla inesperada no la reintente en cada vuelta: espera como cualquier otro pedido.</summary>
    private async Task AnotarFallaAsync(Guid id)
    {
        try
        {
            using var uow = _unidades.Begin(requiresNew: true);
            var fila = await _meses.GetAsync(id);
            fila.Fallar("No se pudo poner al día; se vuelve a intentar en unos minutos.", _reloj.Now.ToUniversalTime());
            await _meses.UpdateAsync(fila, autoSave: true);
            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Tampoco se pudo anotar la falla de la contabilidad {Id}", id);
        }
    }
}
