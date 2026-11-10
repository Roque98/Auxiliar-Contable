using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.MotorFiscal;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Guarda la conciliación de un mes y deja una partida por cada movimiento que el motor no pudo
/// ligar a una factura. Lo ya decidido de una partida se conserva entre conciliaciones; las que
/// dejaron de estar pendientes (llegó su factura) se quitan.
/// </summary>
public class GestorDeConciliaciones : DomainService
{
    /// <summary>Lo que el motor deja sin factura (ver <c>ConciliacionDeMovimiento.Estado</c>).</summary>
    public static readonly string[] EstadosSinFactura = ["sin_factura", "pendiente"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IRepository<ConciliacionBancaria, Guid> _conciliaciones;
    private readonly IRepository<PartidaPendiente, Guid> _partidas;

    public GestorDeConciliaciones(
        IRepository<ConciliacionBancaria, Guid> conciliaciones,
        IRepository<PartidaPendiente, Guid> partidas)
    {
        _conciliaciones = conciliaciones;
        _partidas = partidas;
    }

    public async Task<ConciliacionBancaria> RegistrarAsync(
        Guid organizacionId, int anio, int mes, string rfc, int estadosDeCuenta, int facturas, string huella,
        ResultadoDeConciliacion resultado)
    {
        var conciliacion = await _conciliaciones.FirstOrDefaultAsync(c =>
            c.OrganizacionId == organizacionId && c.Anio == anio && c.Mes == mes);
        var nueva = conciliacion == null;
        conciliacion ??= new ConciliacionBancaria(GuidGenerator.Create(), organizacionId, anio, mes);
        conciliacion.Registrar(rfc, estadosDeCuenta, facturas, huella, JsonSerializer.Serialize(resultado, Json), Clock.Now);
        if (nueva)
        {
            await _conciliaciones.InsertAsync(conciliacion, autoSave: true);
        }
        else
        {
            await _conciliaciones.UpdateAsync(conciliacion, autoSave: true);
        }

        await SincronizarPartidasAsync(organizacionId, anio, mes, resultado.Movimientos);
        return conciliacion;
    }

    public static ResultadoDeConciliacion Leer(ConciliacionBancaria conciliacion) =>
        JsonSerializer.Deserialize<ResultadoDeConciliacion>(conciliacion.Resultado, Json) ?? new ResultadoDeConciliacion();

    private async Task SincronizarPartidasAsync(Guid organizacionId, int anio, int mes, IReadOnlyList<MovimientoLeido> movimientos)
    {
        var claves = ClaveDeMovimiento.De(movimientos);
        var sinFactura = new Dictionary<string, MovimientoLeido>();
        for (var i = 0; i < movimientos.Count; i++)
        {
            if (EstadosSinFactura.Contains(movimientos[i].Conciliacion?.Estado))
            {
                sinFactura[claves[i]] = movimientos[i];
            }
        }

        var existentes = await _partidas.GetListAsync(p => p.OrganizacionId == organizacionId && p.Anio == anio && p.Mes == mes);
        var sobran = existentes.Where(p => !sinFactura.ContainsKey(p.Clave)).ToList();
        if (sobran.Count > 0)
        {
            await _partidas.DeleteManyAsync(sobran, autoSave: true);
        }

        var nuevas = sinFactura
            .Where(par => existentes.All(p => p.Clave != par.Key))
            .Select(par => new PartidaPendiente(GuidGenerator.Create(), organizacionId, anio, mes, par.Key,
                ClaveDeMovimiento.Describir(par.Value)))
            .ToList();
        if (nuevas.Count > 0)
        {
            await _partidas.InsertManyAsync(nuevas, autoSave: true);
        }
    }
}
