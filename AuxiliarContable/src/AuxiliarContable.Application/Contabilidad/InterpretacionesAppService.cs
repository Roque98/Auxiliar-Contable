using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Contabilidad;

/// <summary>Ver <see cref="IInterpretacionesAppService"/>.</summary>
[Authorize]
public class InterpretacionesAppService : AuxiliarContableAppService, IInterpretacionesAppService
{
    private readonly IRepository<Operacion, Guid> _operaciones;
    private readonly InterpretadorDeOperaciones _interpretador;
    private readonly GestorDePeriodos _periodos;
    private readonly Autorizador _autorizador;

    public InterpretacionesAppService(
        IRepository<Operacion, Guid> operaciones,
        InterpretadorDeOperaciones interpretador,
        GestorDePeriodos periodos,
        Autorizador autorizador)
    {
        _operaciones = operaciones;
        _interpretador = interpretador;
        _periodos = periodos;
        _autorizador = autorizador;
    }

    public async Task<InterpretacionesDelMesDto> GetDelMesAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        Validar(anio, mes);
        return await DescribirAsync(organizacionId, anio, mes);
    }

    public async Task<InterpretacionesDelMesDto> PedirAsync(Guid organizacionId, int anio, int mes, Guid? operacionId)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        Validar(anio, mes);
        await _periodos.ExigirAbiertoAsync(organizacionId, new DateTime(anio, mes, 1));
        await _interpretador.PedirAsync(organizacionId, anio, mes, operacionId);
        return await DescribirAsync(organizacionId, anio, mes);
    }

    private async Task<InterpretacionesDelMesDto> DescribirAsync(Guid organizacionId, int anio, int mes)
    {
        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1);
        var ops = await _operaciones.GetListAsync(o => o.OrganizacionId == organizacionId && o.Fecha >= desde && o.Fecha < hasta);
        var entradas = (await _interpretador.EntradasAsync(organizacionId, ops)).ToDictionary(e => e.Operacion.Id);
        var filas = await _interpretador.FilasAsync(organizacionId, ops.Select(o => o.Id).ToList());
        var dto = new InterpretacionesDelMesDto { Anio = anio, Mes = mes };
        foreach (var f in filas)
        {
            var r = f.Resultado == null ? null : JsonSerializer.Deserialize<InterpretacionDelMotor>(f.Resultado);
            dto.Operaciones.Add(new InterpretacionDeOperacionDto
            {
                OperacionId = f.OperacionId,
                EnLaFila = f.Pedida,
                AlDia = r != null && entradas.TryGetValue(f.OperacionId, out var e) && e.Huella == f.Huella,
                InterpretadaUtc = f.InterpretadaUtc,
                Error = f.Error,
                Fuentes = r?.Fuentes ?? [],
                Interpretacion = r?.Interpretacion,
            });
        }

        var conFila = dto.Operaciones.ToDictionary(x => x.OperacionId);
        dto.EnLaFila = dto.Operaciones.Count(x => x.EnLaFila);
        dto.Pendientes = entradas.Values.Count(e => e.TieneFuentes
            && (!conFila.TryGetValue(e.Operacion.Id, out var x) || (!x.EnLaFila && !x.AlDia)));
        return dto;
    }

    private static void Validar(int anio, int mes)
    {
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }
    }

    private async Task ExigirEquipoAsync(Guid organizacionId, AccionEnOrganizacion accion)
    {
        await _autorizador.ExigirAsync(organizacionId, accion);
        if (!(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new AbpAuthorizationException("La interpretación de las operaciones es del despacho.");
        }
    }
}
