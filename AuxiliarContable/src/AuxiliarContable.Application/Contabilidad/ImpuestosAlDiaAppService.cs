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

/// <summary>Ver <see cref="IImpuestosAlDiaAppService"/>.</summary>
[Authorize]
public class ImpuestosAlDiaAppService : AuxiliarContableAppService, IImpuestosAlDiaAppService
{
    private readonly IRepository<ImpuestosDelMes, Guid> _impuestos;
    private readonly IRepository<MesSinEntrega, Guid> _sinEntrega;
    private readonly CalculadoraDeImpuestos _calculadora;
    private readonly Autorizador _autorizador;

    public ImpuestosAlDiaAppService(
        IRepository<ImpuestosDelMes, Guid> impuestos,
        IRepository<MesSinEntrega, Guid> sinEntrega,
        CalculadoraDeImpuestos calculadora,
        Autorizador autorizador)
    {
        _impuestos = impuestos;
        _sinEntrega = sinEntrega;
        _calculadora = calculadora;
        _autorizador = autorizador;
    }

    public Task<ImpuestosAlDiaDto> GetAsync(Guid organizacionId, int anio, int mes) => VerAsync(organizacionId, anio, mes, ahora: false);

    public Task<ImpuestosAlDiaDto> ActualizarAhoraAsync(Guid organizacionId, int anio, int mes) => VerAsync(organizacionId, anio, mes, ahora: true);

    private async Task<ImpuestosAlDiaDto> VerAsync(Guid organizacionId, int anio, int mes, bool ahora)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        if (!(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new AbpAuthorizationException("Los impuestos al día son del despacho.");
        }

        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }

        // Un mes que nadie había pedido, o calculado con otro régimen o coeficiente: se pide sin esperar.
        var ya = Clock.Now.ToUniversalTime() - ActualizadorDeImpuestos.Espera;
        var fila = await _calculadora.FilaAsync(organizacionId, anio, mes, ya);
        if (ahora || (!fila.PorActualizar && fila.HuellaDelPerfil != (await _calculadora.PerfilAsync(organizacionId)).Huella))
        {
            fila.Pedir(ya);
            await _impuestos.UpdateAsync(fila, autoSave: true);
        }

        var resultado = fila.Resultado == null ? null : JsonSerializer.Deserialize<PagosProvisionales>(fila.Resultado);
        var periodo = $"{anio:0000}-{mes:00}";
        return new ImpuestosAlDiaDto
        {
            Anio = anio,
            Mes = mes,
            Calculado = resultado != null,
            CalculadoUtc = fila.CalculadoUtc,
            Actualizando = fila.PorActualizar,
            SeActualizaUtc = fila.PorActualizar ? fila.PedidoUtc + ActualizadorDeImpuestos.Espera : null,
            Error = fila.Error,
            Facturas = fila.Facturas,
            Canceladas = fila.Canceladas,
            Danadas = fila.Danadas,
            SinEstatus = fila.SinEstatus,
            SinEntregaDelCliente = await _sinEntrega.AnyAsync(s => s.OrganizacionId == organizacionId && s.Anio == anio && s.Mes == mes),
            Resultado = resultado,
            DelMes = resultado?.Meses.FirstOrDefault(m => m.Periodo == periodo),
        };
    }
}
