using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Documentos;
using AuxiliarContable.Organizaciones;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Contabilidad;

/// <summary>Ver <see cref="IDineroFueraDelBancoAppService"/>.</summary>
[Authorize]
public class DineroFueraDelBancoAppService : AuxiliarContableAppService, IDineroFueraDelBancoAppService
{
    private readonly IRepository<MovimientoFueraDelBanco, Guid> _movimientos;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly GestorDePeriodos _periodos;
    private readonly Autorizador _autorizador;

    public DineroFueraDelBancoAppService(
        IRepository<MovimientoFueraDelBanco, Guid> movimientos,
        IRepository<Documento, Guid> documentos,
        GestorDePeriodos periodos,
        Autorizador autorizador)
    {
        _movimientos = movimientos;
        _documentos = documentos;
        _periodos = periodos;
        _autorizador = autorizador;
    }

    public async Task<DineroFueraDelBancoDelMesDto> GetAsync(Guid organizacionId, int anio, int mes)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }

        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1);
        // Lo del mes, y lo a crédito de antes que sigue pendiente: una deuda no desaparece al cambiar de mes.
        var lista = await AsyncExecuter.ToListAsync(
            (await _movimientos.GetQueryableAsync())
            .Where(m => m.OrganizacionId == organizacionId
                        && ((m.Fecha >= desde && m.Fecha < hasta)
                            || (m.Fecha < desde && m.Saldado == null
                                && (m.Tipo == TipoDeMovimientoFueraDelBanco.CompraACredito || m.Tipo == TipoDeMovimientoFueraDelBanco.VentaACredito))))
            .OrderBy(m => m.Fecha).ThenBy(m => m.CreationTime));

        var nombres = await NombresDeDocumentosAsync(lista);
        var cerrado = await _periodos.EstaCerradoAsync(organizacionId, anio, mes);
        var hoy = Clock.Now.Date;
        var delMes = lista.Where(m => m.Fecha >= desde).ToList();
        return new DineroFueraDelBancoDelMesDto
        {
            Anio = anio,
            Mes = mes,
            Movimientos = lista.Select(m => Describir(m, nombres, hoy, m.Fecha < desde)).ToList(),
            EntradasEnEfectivo = delMes.Where(m => m.Tipo == TipoDeMovimientoFueraDelBanco.EntradaEnEfectivo).Sum(m => m.Importe),
            SalidasEnEfectivo = delMes.Where(m => m.Tipo == TipoDeMovimientoFueraDelBanco.SalidaEnEfectivo).Sum(m => m.Importe),
            PorPagar = lista.Where(m => m.Pendiente && m.Tipo == TipoDeMovimientoFueraDelBanco.CompraACredito).Sum(m => m.Importe),
            PorCobrar = lista.Where(m => m.Pendiente && m.Tipo == TipoDeMovimientoFueraDelBanco.VentaACredito).Sum(m => m.Importe),
            PuedeEditar = !cerrado && (await _autorizador.QuienMiraAsync()).Puede(organizacionId, AccionEnOrganizacion.Subir),
            Cerrado = cerrado,
        };
    }

    public async Task<MovimientoFueraDelBancoDto> CrearAsync(Guid organizacionId, GuardarMovimientoFueraDelBancoDto input)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        await _periodos.ExigirAbiertoAsync(organizacionId, input.Fecha);
        await ExigirDocumentoDelClienteAsync(organizacionId, input.DocumentoId);

        var m = new MovimientoFueraDelBanco(GuidGenerator.Create(), organizacionId);
        Poner(m, input);
        await _movimientos.InsertAsync(m, autoSave: true);
        return Describir(m, await NombresDeDocumentosAsync([m]), Clock.Now.Date, false);
    }

    public async Task<MovimientoFueraDelBancoDto> ActualizarAsync(Guid organizacionId, Guid movimientoId, GuardarMovimientoFueraDelBancoDto input)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var m = await DelClienteAsync(organizacionId, movimientoId);
        // Ni sacarlo de un mes cerrado ni meterlo en uno.
        await _periodos.ExigirAbiertoAsync(organizacionId, m.Fecha);
        await _periodos.ExigirAbiertoAsync(organizacionId, input.Fecha);
        await ExigirDocumentoDelClienteAsync(organizacionId, input.DocumentoId);

        Poner(m, input);
        await _movimientos.UpdateAsync(m, autoSave: true);
        return Describir(m, await NombresDeDocumentosAsync([m]), Clock.Now.Date, false);
    }

    public async Task BorrarAsync(Guid organizacionId, Guid movimientoId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var m = await DelClienteAsync(organizacionId, movimientoId);
        await _periodos.ExigirAbiertoAsync(organizacionId, m.Fecha);
        await _movimientos.DeleteAsync(m, autoSave: true);
    }

    public async Task<MovimientoFueraDelBancoDto> SaldarAsync(Guid organizacionId, Guid movimientoId, DateTime? fecha)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var m = await DelClienteAsync(organizacionId, movimientoId);
        // El pago cae en el mes en que se paga: ese mes tiene que estar abierto (el de la compra puede estar cerrado).
        if (fecha is { } f)
        {
            if (f.Date < m.Fecha)
            {
                throw new UserFriendlyException("No se puede pagar antes de la fecha de la compra o venta.");
            }

            await _periodos.ExigirAbiertoAsync(organizacionId, f);
        }
        else if (m.Saldado is { } antes)
        {
            await _periodos.ExigirAbiertoAsync(organizacionId, antes);
        }

        m.MarcarSaldado(fecha);
        await _movimientos.UpdateAsync(m, autoSave: true);
        return Describir(m, await NombresDeDocumentosAsync([m]), Clock.Now.Date, false);
    }

    private static void Poner(MovimientoFueraDelBanco m, GuardarMovimientoFueraDelBancoDto input)
    {
        if (!Enum.IsDefined(input.Tipo))
        {
            throw new UserFriendlyException("Tipo de movimiento no válido.");
        }

        if (input.Fecha.Year is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Fecha no válida.");
        }

        m.Poner(input.Tipo, input.Fecha, input.Importe, input.Concepto, input.Contraparte, input.DocumentoId, input.Vence, input.Nota);
    }

    /// <summary>El de otro cliente (o uno inventado) no existe: 404, sin decir de quién es.</summary>
    private async Task<MovimientoFueraDelBanco> DelClienteAsync(Guid organizacionId, Guid movimientoId) =>
        await _movimientos.FirstOrDefaultAsync(m => m.Id == movimientoId && m.OrganizacionId == organizacionId)
        ?? throw new EntityNotFoundException(typeof(MovimientoFueraDelBanco), movimientoId);

    private async Task ExigirDocumentoDelClienteAsync(Guid organizacionId, Guid? documentoId)
    {
        if (documentoId is { } id && !await _documentos.AnyAsync(d => d.Id == id && d.OrganizacionId == organizacionId))
        {
            throw new EntityNotFoundException(typeof(Documento), id);
        }
    }

    private async Task<Dictionary<Guid, string>> NombresDeDocumentosAsync(IReadOnlyCollection<MovimientoFueraDelBanco> lista)
    {
        var ids = lista.Where(m => m.DocumentoId.HasValue).Select(m => m.DocumentoId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return (await _documentos.GetListAsync(d => ids.Contains(d.Id))).ToDictionary(d => d.Id, d => d.NombreOriginal);
    }

    private static MovimientoFueraDelBancoDto Describir(MovimientoFueraDelBanco m, Dictionary<Guid, string> nombres, DateTime hoy, bool deOtroMes) => new()
    {
        Id = m.Id,
        Tipo = m.Tipo,
        Fecha = m.Fecha,
        Importe = m.Importe,
        Concepto = m.Concepto,
        Contraparte = m.Contraparte,
        DocumentoId = m.DocumentoId,
        NombreDelDocumento = m.DocumentoId is { } id ? nombres.GetValueOrDefault(id) : null,
        Vence = m.Vence,
        Saldado = m.Saldado,
        Nota = m.Nota,
        Pendiente = m.Pendiente,
        Vencido = m.Pendiente && m.Vence is { } v && v < hoy,
        DeOtroMes = deOtroMes,
    };
}
