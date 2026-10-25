using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Cfdis;
using AuxiliarContable.EstadosDeCuenta;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Conciliaciones;

/// <summary>
/// Ver <see cref="IConciliacionAppService"/>. Entran solo movimientos de estados de cuenta que
/// cuadran o ya se confirmaron (los que «requieren revisión» tienen cifras dudosas) y facturas de
/// ingreso en pesos cuyo emisor o receptor es el RFC del cliente.
/// </summary>
[Authorize]
public class ConciliacionAppService : AuxiliarContableAppService, IConciliacionAppService
{
    private readonly IRepository<Conciliacion, Guid> _conciliaciones;
    private readonly IRepository<MovimientoBancario, Guid> _movimientos;
    private readonly IRepository<EstadoDeCuenta, Guid> _estados;
    private readonly IRepository<Cfdi, Guid> _cfdis;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly Autorizador _autorizador;

    public ConciliacionAppService(
        IRepository<Conciliacion, Guid> conciliaciones,
        IRepository<MovimientoBancario, Guid> movimientos,
        IRepository<EstadoDeCuenta, Guid> estados,
        IRepository<Cfdi, Guid> cfdis,
        IRepository<PerfilFiscal, Guid> perfiles,
        Autorizador autorizador)
    {
        _conciliaciones = conciliaciones;
        _movimientos = movimientos;
        _estados = estados;
        _cfdis = cfdis;
        _perfiles = perfiles;
        _autorizador = autorizador;
    }

    public async Task<ConciliacionDelMesDto> GetDelMesAsync(Guid organizacionId, int anio, int mes)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        return await DescribirAsync(await CargarAsync(organizacionId, anio, mes));
    }

    public async Task<ConciliacionDelMesDto> ProponerAsync(Guid organizacionId, int anio, int mes)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var datos = await CargarAsync(organizacionId, anio, mes);

        var ocupadosM = datos.Uniones.Where(u => u.Estado != EstadoDeConciliacion.Rechazada).Select(u => u.MovimientoId).ToHashSet();
        var ocupadasF = datos.Uniones.Where(u => u.Estado != EstadoDeConciliacion.Rechazada).Select(u => u.CfdiId).ToHashSet();
        var rechazadas = datos.Uniones.Where(u => u.Estado == EstadoDeConciliacion.Rechazada).Select(u => (u.MovimientoId, u.CfdiId)).ToHashSet();

        var parejas = Emparejador.Emparejar(
            datos.Movimientos.Where(m => !ocupadosM.Contains(m.Id) && m.SinFacturaPorque == null)
                .Select(m => new Emparejador.Movimiento(m.Id, m.Fecha, m.Cargo, m.Abono, m.Concepto, m.Referencia)),
            datos.Facturas.Where(f => !ocupadasF.Contains(f.Cfdi.Id))
                .Select(f => new Emparejador.Factura(f.Cfdi.Id, f.Cfdi.Fecha, f.Cfdi.Total, f.Cfdi.Moneda, f.EsVenta,
                    f.EsVenta ? f.Cfdi.ReceptorRfc : f.Cfdi.EmisorRfc, f.Cfdi.Uuid)),
            rechazadas);

        foreach (var p in parejas)
        {
            await _conciliaciones.InsertAsync(new Conciliacion(GuidGenerator.Create(), organizacionId, p.MovimientoId, p.FacturaId, p.Puntaje, p.Motivo));
        }

        await CurrentUnitOfWork!.SaveChangesAsync();
        return await DescribirAsync(await CargarAsync(organizacionId, anio, mes));
    }

    public async Task ConfirmarAsync(Guid organizacionId, Guid conciliacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var c = await UnionAsync(organizacionId, conciliacionId);
        c.Confirmar();
        await _conciliaciones.UpdateAsync(c, autoSave: true);
    }

    public async Task RechazarAsync(Guid organizacionId, Guid conciliacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var c = await UnionAsync(organizacionId, conciliacionId);
        c.Rechazar();
        await _conciliaciones.UpdateAsync(c, autoSave: true);
    }

    public async Task ConciliarAManoAsync(Guid organizacionId, ConciliarAManoDto input)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var movimiento = await _movimientos.FindAsync(input.MovimientoId);
        var cfdi = await _cfdis.FindAsync(input.CfdiId);
        if (movimiento == null || movimiento.OrganizacionId != organizacionId)
        {
            throw new EntityNotFoundException(typeof(MovimientoBancario), input.MovimientoId);
        }

        if (cfdi == null || cfdi.OrganizacionId != organizacionId)
        {
            throw new EntityNotFoundException(typeof(Cfdi), input.CfdiId);
        }

        var vivas = await _conciliaciones.GetListAsync(c => c.OrganizacionId == organizacionId
                                                           && c.Estado != EstadoDeConciliacion.Rechazada
                                                           && (c.MovimientoId == movimiento.Id || c.CfdiId == cfdi.Id));
        if (vivas.Any(c => c.Estado == EstadoDeConciliacion.Confirmada))
        {
            throw new UserFriendlyException("Ese movimiento o esa factura ya están conciliados con otro. Deshaz esa unión primero.");
        }

        // Las propuestas que chocan con lo que decide la persona se descartan.
        foreach (var propuesta in vivas)
        {
            propuesta.Rechazar();
            await _conciliaciones.UpdateAsync(propuesta);
        }

        await _conciliaciones.InsertAsync(new Conciliacion(GuidGenerator.Create(), organizacionId, movimiento.Id, cfdi.Id, 100, string.Empty, aMano: true), autoSave: true);
    }

    public async Task MarcarSinFacturaAsync(Guid organizacionId, MarcarSinFacturaDto input)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var movimiento = await _movimientos.FindAsync(input.MovimientoId);
        if (movimiento == null || movimiento.OrganizacionId != organizacionId)
        {
            throw new EntityNotFoundException(typeof(MovimientoBancario), input.MovimientoId);
        }

        movimiento.MarcarSinFactura(input.Porque);
        await _movimientos.UpdateAsync(movimiento, autoSave: true);
    }

    private async Task<Conciliacion> UnionAsync(Guid organizacionId, Guid id)
    {
        var c = await _conciliaciones.FindAsync(id);
        return c != null && c.OrganizacionId == organizacionId ? c : throw new EntityNotFoundException(typeof(Conciliacion), id);
    }

    private sealed record FacturaDelCliente(Cfdi Cfdi, bool EsVenta);

    private sealed record MovimientoConBanco(MovimientoBancario Movimiento, string Banco, RevisionDelEstadoDeCuenta Revision);

    private sealed class DatosDelMes
    {
        public Guid OrganizacionId { get; init; }
        public int Anio { get; init; }
        public int Mes { get; init; }
        public List<MovimientoBancario> Movimientos { get; init; } = [];
        public Dictionary<Guid, string> Bancos { get; init; } = [];
        public int EstadosSinRevisar { get; init; }
        public bool HayEstados { get; init; }
        public List<FacturaDelCliente> Facturas { get; init; } = [];
        public List<Conciliacion> Uniones { get; init; } = [];
    }

    private async Task<DatosDelMes> CargarAsync(Guid organizacionId, int anio, int mes)
    {
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes inválido.");
        }

        var inicio = new DateTime(anio, mes, 1);
        var fin = inicio.AddMonths(1);

        var consulta =
            from m in await _movimientos.GetQueryableAsync()
            join e in await _estados.GetQueryableAsync() on m.EstadoDeCuentaId equals e.Id
            where m.OrganizacionId == organizacionId && m.Fecha >= inicio && m.Fecha < fin
            select new MovimientoConBanco(m, e.Banco, e.Revision);
        var todos = await AsyncExecuter.ToListAsync(consulta);
        var validos = todos.Where(x => x.Revision != RevisionDelEstadoDeCuenta.RequiereRevision).ToList();

        // Facturas del mes con margen: una venta del 30 se cobra el 2 del mes siguiente.
        var rfc = (await _perfiles.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId))?.Dato(CamposDelPerfil.Rfc)?.Valor;
        var desde = inicio.AddDays(-ConciliacionConsts.DiasDeTolerancia);
        var hasta = fin.AddDays(ConciliacionConsts.DiasDeTolerancia);
        var cfdis = await _cfdis.GetListAsync(c => c.OrganizacionId == organizacionId && c.TipoDeComprobante == "I"
                                                   && c.Fecha >= desde && c.Fecha < hasta);
        var facturas = cfdis
            .Select(c => (c, d: c.Direccion(rfc)))
            .Where(x => x.d != DireccionDelCfdi.Desconocida)
            .Select(x => new FacturaDelCliente(x.c, x.d == DireccionDelCfdi.Emitido))
            .ToList();

        var idsM = validos.Select(x => x.Movimiento.Id).ToList();
        var idsF = facturas.Select(f => f.Cfdi.Id).ToList();
        var uniones = await _conciliaciones.GetListAsync(c => c.OrganizacionId == organizacionId
                                                             && (idsM.Contains(c.MovimientoId) || idsF.Contains(c.CfdiId)));

        return new DatosDelMes
        {
            OrganizacionId = organizacionId,
            Anio = anio,
            Mes = mes,
            Movimientos = validos.Select(x => x.Movimiento).ToList(),
            Bancos = todos.ToDictionary(x => x.Movimiento.Id, x => x.Banco),
            EstadosSinRevisar = todos.Where(x => x.Revision == RevisionDelEstadoDeCuenta.RequiereRevision)
                .Select(x => x.Movimiento.EstadoDeCuentaId).Distinct().Count(),
            HayEstados = todos.Count > 0,
            Facturas = facturas,
            Uniones = uniones,
        };
    }

    private async Task<ConciliacionDelMesDto> DescribirAsync(DatosDelMes d)
    {
        var quien = await _autorizador.QuienMiraAsync();
        var movimientos = d.Movimientos.ToDictionary(m => m.Id);
        var facturas = d.Facturas.ToDictionary(f => f.Cfdi.Id);

        MovimientoDelBancoDto Mov(MovimientoBancario m) => new()
        {
            Id = m.Id,
            Fecha = m.Fecha,
            Banco = d.Bancos.GetValueOrDefault(m.Id, string.Empty),
            Concepto = m.Concepto,
            Cargo = m.Cargo,
            Abono = m.Abono,
            SinFacturaPorque = m.SinFacturaPorque,
        };

        FacturaDelMesDto Fac(FacturaDelCliente f) => new()
        {
            Id = f.Cfdi.Id,
            Fecha = f.Cfdi.Fecha,
            EsVenta = f.EsVenta,
            Contraparte = (f.EsVenta ? f.Cfdi.ReceptorNombre : f.Cfdi.EmisorNombre) ?? string.Empty,
            RfcContraparte = f.EsVenta ? f.Cfdi.ReceptorRfc : f.Cfdi.EmisorRfc,
            Total = f.Cfdi.Total,
            MetodoDePago = f.Cfdi.MetodoDePago,
            Uuid = f.Cfdi.Uuid,
        };

        var dto = new ConciliacionDelMesDto
        {
            OrganizacionId = d.OrganizacionId,
            Anio = d.Anio,
            Mes = d.Mes,
            PuedeEditar = quien.Puede(d.OrganizacionId, AccionEnOrganizacion.Subir),
            EstadosSinRevisar = d.EstadosSinRevisar,
            HayEstadosDeCuenta = d.HayEstados,
        };

        var unidosM = new HashSet<Guid>();
        var unidasF = new HashSet<Guid>();
        foreach (var u in d.Uniones.Where(u => u.Estado != EstadoDeConciliacion.Rechazada))
        {
            if (!movimientos.TryGetValue(u.MovimientoId, out var m) || !facturas.TryGetValue(u.CfdiId, out var f))
            {
                continue;
            }

            unidosM.Add(m.Id);
            unidasF.Add(f.Cfdi.Id);
            var pareja = new ParejaDto { Id = u.Id, Puntaje = u.Puntaje, Motivo = u.Motivo, Movimiento = Mov(m), Factura = Fac(f) };
            (u.Estado == EstadoDeConciliacion.Confirmada ? dto.Confirmadas : dto.Propuestas).Add(pareja);
        }

        foreach (var m in d.Movimientos.Where(m => !unidosM.Contains(m.Id)).OrderBy(m => m.Fecha))
        {
            if (m.SinFacturaPorque != null)
            {
                dto.SinFacturaJustificados.Add(Mov(m));
            }
            else if (m.Abono > 0)
            {
                dto.DepositosSinFactura.Add(Mov(m));
            }
            else
            {
                dto.CargosSinFactura.Add(Mov(m));
            }
        }

        // Solo las de contado y del mes mismo: las PPD se cobran después, con su complemento de pago.
        var inicio = new DateTime(d.Anio, d.Mes, 1);
        foreach (var f in d.Facturas.Where(f => !unidasF.Contains(f.Cfdi.Id) && f.Cfdi.MetodoDePago != "PPD"
                                                && f.Cfdi.Fecha >= inicio && f.Cfdi.Fecha < inicio.AddMonths(1))
                     .OrderBy(f => f.Cfdi.Fecha))
        {
            (f.EsVenta ? dto.VentasSinDeposito : dto.GastosSinCargo).Add(Fac(f));
        }

        dto.Propuestas = dto.Propuestas.OrderBy(p => p.Movimiento.Fecha).ToList();
        dto.Confirmadas = dto.Confirmadas.OrderBy(p => p.Movimiento.Fecha).ToList();
        return dto;
    }
}
