using System;
using System.Linq;
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
/// Entra, cambia o se borra una factura → su mes y los siguientes del mismo ejercicio quedan «por actualizar» (el pago
/// provisional acumula desde enero), y si aún no existían se crean el de la factura y el mes en curso, para que el
/// cálculo de este mes esté al día aunque nadie lo haya abierto.
/// </summary>
public class MarcadorDeImpuestos : ILocalEventHandler<EntityChangedEventData<Documento>>, ITransientDependency
{
    private readonly IRepository<ImpuestosDelMes, Guid> _impuestos;
    private readonly IGuidGenerator _guids;
    private readonly IClock _reloj;

    public MarcadorDeImpuestos(IRepository<ImpuestosDelMes, Guid> impuestos, IGuidGenerator guids, IClock reloj)
    {
        _impuestos = impuestos;
        _guids = guids;
        _reloj = reloj;
    }

    public async Task HandleEventAsync(EntityChangedEventData<Documento> evento)
    {
        var d = evento.Entity;
        if (d.Tipo != TipoDeDocumento.Cfdi)
        {
            return;
        }

        var fecha = d.FechaDelDocumento ?? d.CreationTime;
        await MarcarAsync(d.OrganizacionId, fecha.Year, fecha.Month);
    }

    public async Task MarcarAsync(Guid organizacionId, int anio, int mes)
    {
        var ahora = _reloj.Now.ToUniversalTime();
        var filas = await _impuestos.GetListAsync(i => i.OrganizacionId == organizacionId && i.Anio == anio && i.Mes >= mes);
        foreach (var f in filas)
        {
            f.Pedir(ahora);
            await _impuestos.UpdateAsync(f);
        }

        var hoy = _reloj.Now;
        var crear = new[] { mes, anio == hoy.Year && hoy.Month > mes ? hoy.Month : mes }.Distinct();
        foreach (var m in crear.Where(m => filas.All(f => f.Mes != m)))
        {
            // Guardada al momento: varias facturas del mismo mes en la misma unidad de trabajo no deben crearla dos veces.
            await _impuestos.InsertAsync(new ImpuestosDelMes(_guids.Create(), organizacionId, anio, m, ahora), autoSave: true);
        }
    }
}
