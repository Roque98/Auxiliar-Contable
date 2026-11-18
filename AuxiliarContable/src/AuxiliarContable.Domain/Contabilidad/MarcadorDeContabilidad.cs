using System;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.Documentos;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;
using Volo.Abp.Guids;
using Volo.Abp.Timing;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Entra, cambia o se borra algo de un mes que toca su contabilidad (una factura, un estado de cuenta, un ticket, un
/// registro de efectivo o crédito, una operación, la conciliación) → ese mes queda «por actualizar» en
/// <see cref="ContabilidadDelMes"/>. Lo que cambia la propia actualización no cuenta (<see cref="Callar"/>): si no, se
/// pediría otra vez a sí misma.
/// </summary>
public class MarcadorDeContabilidad :
    ILocalEventHandler<EntityChangedEventData<Documento>>,
    ILocalEventHandler<EntityChangedEventData<Operacion>>,
    ILocalEventHandler<EntityChangedEventData<MovimientoFueraDelBanco>>,
    ILocalEventHandler<EntityChangedEventData<ConciliacionBancaria>>,
    ITransientDependency
{
    private static readonly AsyncLocal<bool> Callado = new();

    private readonly IRepository<ContabilidadDelMes, Guid> _meses;
    private readonly IGuidGenerator _guids;
    private readonly IClock _reloj;

    public MarcadorDeContabilidad(IRepository<ContabilidadDelMes, Guid> meses, IGuidGenerator guids, IClock reloj)
    {
        _meses = meses;
        _guids = guids;
        _reloj = reloj;
    }

    /// <summary>Mientras dure, los cambios no marcan nada (los hace la actualización misma).</summary>
    public static IDisposable Callar()
    {
        var antes = Callado.Value;
        Callado.Value = true;
        return new Restaurar(antes);
    }

    public Task HandleEventAsync(EntityChangedEventData<Documento> evento)
    {
        var d = evento.Entity;
        if (d.Tipo == TipoDeDocumento.ConstanciaDeSituacionFiscal)
        {
            return Task.CompletedTask;
        }

        var fecha = d.FechaDelDocumento ?? d.CreationTime;
        return MarcarAsync(d.OrganizacionId, fecha.Year, fecha.Month);
    }

    public Task HandleEventAsync(EntityChangedEventData<Operacion> evento) =>
        MarcarAsync(evento.Entity.OrganizacionId, evento.Entity.Fecha.Year, evento.Entity.Fecha.Month);

    public Task HandleEventAsync(EntityChangedEventData<MovimientoFueraDelBanco> evento) =>
        MarcarAsync(evento.Entity.OrganizacionId, evento.Entity.Fecha.Year, evento.Entity.Fecha.Month);

    public Task HandleEventAsync(EntityChangedEventData<ConciliacionBancaria> evento) =>
        MarcarAsync(evento.Entity.OrganizacionId, evento.Entity.Anio, evento.Entity.Mes);

    public async Task MarcarAsync(Guid organizacionId, int anio, int mes)
    {
        if (Callado.Value || anio is < 2000 or > 2100)
        {
            return;
        }

        var ahora = _reloj.Now.ToUniversalTime();
        var fila = await _meses.FirstOrDefaultAsync(m => m.OrganizacionId == organizacionId && m.Anio == anio && m.Mes == mes);
        if (fila == null)
        {
            // Guardada al momento: varias facturas del mismo mes en la misma unidad de trabajo no deben crearla dos veces.
            await _meses.InsertAsync(new ContabilidadDelMes(_guids.Create(), organizacionId, anio, mes, ahora), autoSave: true);
            return;
        }

        fila.Pedir(ahora);
        await _meses.UpdateAsync(fila);
    }

    private sealed class Restaurar(bool antes) : IDisposable
    {
        public void Dispose() => Callado.Value = antes;
    }
}
