using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.BackgroundJobs;
using AuxiliarContable.Documentos;
using AuxiliarContable.Organizaciones;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.EstadosDeCuenta;

/// <summary>Ver <see cref="IEstadosDeCuentaAppService"/>. Lo ajeno = 404, como en toda la bóveda.</summary>
[Authorize]
public class EstadosDeCuentaAppService : AuxiliarContableAppService, IEstadosDeCuentaAppService
{
    private readonly IRepository<EstadoDeCuenta, Guid> _estados;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly Autorizador _autorizador;
    private readonly IBackgroundJobManager _trabajos;

    public EstadosDeCuentaAppService(
        IRepository<EstadoDeCuenta, Guid> estados,
        IRepository<Documento, Guid> documentos,
        Autorizador autorizador,
        IBackgroundJobManager trabajos)
    {
        _estados = estados;
        _documentos = documentos;
        _autorizador = autorizador;
        _trabajos = trabajos;
    }

    public async Task<List<EstadoDeCuentaResumenDto>> GetListAsync(Guid organizacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        var lista = await _estados.GetListAsync(e => e.OrganizacionId == organizacionId, includeDetails: true);
        return lista
            .OrderByDescending(e => e.PeriodoFin ?? e.CreationTime)
            .Select(e => Llenar(new EstadoDeCuentaResumenDto(), e))
            .ToList();
    }

    public async Task<EstadoDeCuentaDto> GetDetalleAsync(Guid organizacionId, Guid estadoDeCuentaId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        return await DescribirAsync(await EstadoAsync(organizacionId, estadoDeCuentaId));
    }

    public async Task<EstadoDeCuentaDto> ConfirmarAsync(Guid organizacionId, Guid estadoDeCuentaId, bool aunqueNoCuadre)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var estado = await EstadoAsync(organizacionId, estadoDeCuentaId);
        estado.Confirmar(aunqueNoCuadre);
        await _estados.UpdateAsync(estado, autoSave: true);
        return await DescribirAsync(estado);
    }

    public async Task<EstadoDeCuentaDto> CorregirMovimientoAsync(Guid organizacionId, Guid estadoDeCuentaId, CorregirMovimientoDto input)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var estado = await EstadoAsync(organizacionId, estadoDeCuentaId);
        estado.CorregirMovimiento(input.MovimientoId, input.Fecha, input.Concepto, input.Cargo, input.Abono);
        await _estados.UpdateAsync(estado, autoSave: true);
        return await DescribirAsync(estado);
    }

    public async Task<EstadoDeCuentaDto> CorregirSaldosAsync(Guid organizacionId, Guid estadoDeCuentaId, CorregirSaldosDto input)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var estado = await EstadoAsync(organizacionId, estadoDeCuentaId);
        estado.CorregirSaldos(input.SaldoInicial, input.SaldoFinal);
        await _estados.UpdateAsync(estado, autoSave: true);
        return await DescribirAsync(estado);
    }

    public async Task VolverALeerAsync(Guid organizacionId, Guid documentoId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var documento = await _documentos.FindAsync(documentoId);
        if (documento == null || documento.OrganizacionId != organizacionId || documento.Tipo != TipoDeDocumento.EstadoDeCuenta)
        {
            throw new EntityNotFoundException(typeof(Documento), documentoId);
        }

        await _trabajos.EnqueueAsync(new LeerEstadoDeCuentaArgs { DocumentoId = documento.Id });
    }

    private async Task<EstadoDeCuenta> EstadoAsync(Guid organizacionId, Guid estadoDeCuentaId)
    {
        var estado = await _estados.FindAsync(estadoDeCuentaId, includeDetails: true);
        if (estado == null || estado.OrganizacionId != organizacionId)
        {
            throw new EntityNotFoundException(typeof(EstadoDeCuenta), estadoDeCuentaId);
        }

        return estado;
    }

    private async Task<EstadoDeCuentaDto> DescribirAsync(EstadoDeCuenta e)
    {
        var quien = await _autorizador.QuienMiraAsync();
        var dto = Llenar(new EstadoDeCuentaDto(), e);
        dto.PuedeEditar = quien.Puede(e.OrganizacionId, AccionEnOrganizacion.Subir);

        var anterior = e.SaldoInicial;
        foreach (var m in e.Movimientos.OrderBy(x => x.Orden))
        {
            var salto = m.Saldo is { } s && anterior is { } a && Math.Abs(a + m.Abono - m.Cargo - s) > EstadoDeCuentaConsts.Tolerancia;
            anterior = m.Saldo ?? (anterior is { } b ? b + m.Abono - m.Cargo : null);
            dto.Renglones.Add(new MovimientoBancarioDto
            {
                Id = m.Id,
                Orden = m.Orden,
                Fecha = m.Fecha,
                Concepto = m.Concepto,
                Referencia = m.Referencia,
                Cargo = m.Cargo,
                Abono = m.Abono,
                Saldo = m.Saldo,
                SaltoDeSaldo = salto,
            });
        }

        return dto;
    }

    private static T Llenar<T>(T dto, EstadoDeCuenta e)
        where T : EstadoDeCuentaResumenDto
    {
        dto.Id = e.Id;
        dto.DocumentoId = e.DocumentoId;
        dto.Banco = e.Banco;
        dto.Cuenta = e.Cuenta;
        dto.PeriodoInicio = e.PeriodoInicio;
        dto.PeriodoFin = e.PeriodoFin;
        dto.Moneda = e.Moneda;
        dto.SaldoInicial = e.SaldoInicial;
        dto.SaldoFinal = e.SaldoFinal;
        dto.TotalAbonos = e.TotalAbonos;
        dto.TotalCargos = e.TotalCargos;
        dto.Diferencia = e.Diferencia;
        dto.Cuadra = e.Cuadra;
        dto.SaltosDeSaldo = e.SaltosDeSaldo;
        dto.Movimientos = e.Movimientos.Count;
        dto.Revision = e.Revision;
        return dto;
    }
}
