using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Documentos;
using AuxiliarContable.PerfilesFiscales;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Linq;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Lo que arma las operaciones del cliente sin pasar por un usuario: de cada documento su operación, juntar dos que son
/// la misma. Lo usan «Todo» (con sus permisos) y la contabilidad al día (trabajo en segundo plano).
/// </summary>
public class RegistroDeOperaciones : ITransientDependency
{
    /// <summary>Una referencia más corta que esto no se usa para juntar operaciones solas: «1» o «12» se repiten.</summary>
    public const int MinReferenciaParaJuntar = 4;

    private readonly IRepository<Operacion, Guid> _operaciones;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<MovimientoFueraDelBanco, Guid> _movimientos;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly GestorDePeriodos _periodos;
    private readonly IGuidGenerator _guids;
    private readonly IAsyncQueryableExecuter _consultas;

    public RegistroDeOperaciones(
        IRepository<Operacion, Guid> operaciones,
        IRepository<Documento, Guid> documentos,
        IRepository<MovimientoFueraDelBanco, Guid> movimientos,
        IRepository<PerfilFiscal, Guid> perfiles,
        AlmacenDeDocumentos almacen,
        GestorDePeriodos periodos,
        IGuidGenerator guids,
        IAsyncQueryableExecuter consultas)
    {
        _operaciones = operaciones;
        _documentos = documentos;
        _movimientos = movimientos;
        _perfiles = perfiles;
        _almacen = almacen;
        _periodos = periodos;
        _guids = guids;
        _consultas = consultas;
    }

    /// <summary>Junta <paramref name="saleId"/> en <paramref name="quedaId"/> (las dos del cliente, de meses abiertos).</summary>
    public async Task JuntarAsync(Guid organizacionId, Guid quedaId, Guid saleId)
    {
        if (quedaId == saleId)
        {
            throw new UserFriendlyException("Es la misma operación.");
        }

        var queda = await _operaciones.FirstOrDefaultAsync(o => o.Id == quedaId && o.OrganizacionId == organizacionId)
                    ?? throw new UserFriendlyException("Esa operación ya no existe.");
        var sale = await _operaciones.FirstOrDefaultAsync(o => o.Id == saleId && o.OrganizacionId == organizacionId)
                   ?? throw new UserFriendlyException("Esa operación ya no existe.");
        await _periodos.ExigirAbiertoAsync(organizacionId, queda.Fecha);
        await _periodos.ExigirAbiertoAsync(organizacionId, sale.Fecha);
        await JuntarEnAsync(queda, sale);
    }

    public async Task<Operacion?> GemelaAsync(Operacion o)
    {
        var q = (await _operaciones.GetQueryableAsync()).Where(x => x.OrganizacionId == o.OrganizacionId && x.Id != o.Id);
        if (o.Uuid is { } uuid)
        {
            var conUuid = await _consultas.FirstOrDefaultAsync(q.Where(x => x.Uuid == uuid));
            if (conUuid != null)
            {
                return conUuid;
            }
        }

        if (o.TransaccionForma == FormaDeTransaccion.Banco && o.Referencia is { Length: >= MinReferenciaParaJuntar } referencia)
        {
            var r = referencia.ToUpper();
            return await _consultas.FirstOrDefaultAsync(q.Where(x => x.TransaccionForma == FormaDeTransaccion.Banco
                                                                         && x.Referencia != null && x.Referencia.ToUpper() == r));
        }

        return null;
    }

    public async Task JuntarEnAsync(Operacion queda, Operacion sale)
    {
        // Si las dos traen la misma pata amarrada a cosas distintas, no se adivina cuál vale.
        foreach (var pata in Enum.GetValues<PataDeOperacion>())
        {
            if (queda.Estado(pata) == EstadoDePata.Ligada && sale.Estado(pata) == EstadoDePata.Ligada && !MismaPata(queda, sale, pata))
            {
                throw new UserFriendlyException(pata switch
                {
                    PataDeOperacion.Factura => "Las dos operaciones ya tienen factura y son distintas: deja la que no va «para después» antes de juntarlas.",
                    PataDeOperacion.Transaccion => "Las dos operaciones ya tienen transacción y son distintas: deja la que no va «para después» antes de juntarlas.",
                    _ => "Las dos operaciones ya tienen control interno y son distintos: deja el que no va «para después» antes de juntarlas.",
                });
            }
        }

        queda.Absorber(sale);
        await _operaciones.DeleteAsync(sale, autoSave: true);
        await _operaciones.UpdateAsync(queda, autoSave: true);
    }

    private static bool MismaPata(Operacion a, Operacion b, PataDeOperacion pata) => pata switch
    {
        PataDeOperacion.Factura => (a.Uuid != null && a.Uuid == b.Uuid) || (a.FacturaDocumentoId != null && a.FacturaDocumentoId == b.FacturaDocumentoId),
        PataDeOperacion.Transaccion => (a.Referencia != null && string.Equals(a.Referencia, b.Referencia, StringComparison.OrdinalIgnoreCase))
                                       || (a.MovimientoFueraDelBancoId != null && a.MovimientoFueraDelBancoId == b.MovimientoFueraDelBancoId)
                                       || (a.ClaveDeMovimiento != null && a.ClaveDeMovimiento == b.ClaveDeMovimiento)
                                       || (a.TransaccionDocumentoId != null && a.TransaccionDocumentoId == b.TransaccionDocumentoId),
        _ => a.ControlDocumentoId != null && a.ControlDocumentoId == b.ControlDocumentoId,
    };

    /// <summary>
    /// Lo del mes que aún no tiene operación: las facturas, los comprobantes de pago, el control interno y los
    /// registros de efectivo y crédito. Así «Todo» enseña también lo que se subió antes de que existieran las operaciones.
    /// </summary>
    public async Task AgregarLoQueFaltaAsync(Guid organizacionId, DateTime desde, DateTime hasta)
    {
        var tipos = new[] { TipoDeDocumento.Cfdi, TipoDeDocumento.ComprobanteDePago, TipoDeDocumento.ControlInterno, TipoDeDocumento.Ticket };
        var ops = await _operaciones.GetQueryableAsync();
        var documentos = await _consultas.ToListAsync(
            from d in await _documentos.GetQueryableAsync()
            where d.OrganizacionId == organizacionId && tipos.Contains(d.Tipo)
                  && (d.FechaDelDocumento ?? d.CreationTime) >= desde && (d.FechaDelDocumento ?? d.CreationTime) < hasta
                  && !ops.Any(o => o.FacturaDocumentoId == d.Id || o.TransaccionDocumentoId == d.Id || o.ControlDocumentoId == d.Id)
            select d);
        var movimientos = await _consultas.ToListAsync(
            from m in await _movimientos.GetQueryableAsync()
            where m.OrganizacionId == organizacionId && m.Fecha >= desde && m.Fecha < hasta
                  && !ops.Any(o => o.MovimientoFueraDelBancoId == m.Id)
            select m);
        if (documentos.Count == 0 && movimientos.Count == 0)
        {
            return;
        }

        var rfc = await RfcAsync(organizacionId);
        foreach (var d in documentos.OrderBy(d => d.Tipo == TipoDeDocumento.Cfdi ? 0 : 1))
        {
            await DeDocumentoAsync(organizacionId, d, rfc);
        }

        foreach (var m in movimientos)
        {
            var o = new Operacion(_guids.Create(), organizacionId);
            var entrada = m.Tipo is TipoDeMovimientoFueraDelBanco.EntradaEnEfectivo or TipoDeMovimientoFueraDelBanco.VentaACredito;
            o.Describir(m.Fecha, entrada ? SentidoDeOperacion.Entrada : SentidoDeOperacion.Salida, m.Importe, m.Concepto, m.Contraparte);
            o.LigarTransaccion(m.EsACredito ? FormaDeTransaccion.Credito : FormaDeTransaccion.Efectivo, null, null, m.Id);
            await _operaciones.InsertAsync(o, autoSave: true);
        }
    }

    /// <summary>La operación del documento (la que ya lo tiene, la de su mismo UUID, o una nueva). Null si es un estado de cuenta o una constancia.</summary>
    public async Task<Operacion?> DeDocumentoAsync(Guid organizacionId, Documento d, string? rfcDelCliente)
    {
        var ya = await _operaciones.FirstOrDefaultAsync(o => o.OrganizacionId == organizacionId
            && (o.FacturaDocumentoId == d.Id || o.TransaccionDocumentoId == d.Id || o.ControlDocumentoId == d.Id));
        if (ya != null)
        {
            return ya;
        }

        var fecha = d.FechaDelDocumento ?? d.CreationTime;
        switch (d.Tipo)
        {
            case TipoDeDocumento.Cfdi:
            {
                var cfdi = DatosDelCfdi.Leer(await _almacen.LeerAsync(d));
                // El mismo UUID ya tiene operación (p. ej. la abrió su ticket): el XML se le amarra a esa.
                if (cfdi?.Uuid is { } uuid && await _operaciones.FirstOrDefaultAsync(o => o.OrganizacionId == organizacionId && o.Uuid == uuid) is { } conUuid)
                {
                    if (conUuid.FacturaDocumentoId is null)
                    {
                        conUuid.LigarFactura(uuid, d.Id, conUuid.SolicitudDeFacturaId);
                        await _operaciones.UpdateAsync(conUuid, autoSave: true);
                    }

                    return conUuid;
                }

                var emitida = rfcDelCliente != null && cfdi?.EmisorRfc == rfcDelCliente;
                var o = new Operacion(_guids.Create(), organizacionId);
                o.Describir(cfdi?.Fecha is { } f ? FechaDelCfdi.ComoFecha(f) : fecha,
                    emitida ? SentidoDeOperacion.Entrada : SentidoDeOperacion.Salida,
                    cfdi?.Total is > 0 ? cfdi.Total : null,
                    Corto(cfdi?.PrimerConcepto ?? d.NombreOriginal, OperacionConsts.MaxDescripcionLength)!,
                    Corto(emitida ? cfdi?.ReceptorNombre ?? cfdi?.ReceptorRfc : cfdi?.EmisorNombre ?? cfdi?.EmisorRfc, OperacionConsts.MaxContraparteLength));
                o.LigarFactura(cfdi?.Uuid, d.Id, null);
                await _operaciones.InsertAsync(o, autoSave: true);
                return o;
            }
            case TipoDeDocumento.ComprobanteDePago:
            {
                var o = Nueva(organizacionId, d, fecha);
                o.LigarTransaccion(FormaDeTransaccion.Banco, null, d.Id, null);
                await _operaciones.InsertAsync(o, autoSave: true);
                return o;
            }
            case TipoDeDocumento.ControlInterno or TipoDeDocumento.Ticket or TipoDeDocumento.Otro:
            {
                var o = Nueva(organizacionId, d, fecha);
                o.LigarControl(d.Id);
                await _operaciones.InsertAsync(o, autoSave: true);
                return o;
            }
            default:
                return null;
        }
    }

    private Operacion Nueva(Guid organizacionId, Documento d, DateTime fecha)
    {
        var o = new Operacion(_guids.Create(), organizacionId);
        o.Describir(fecha, SentidoDeOperacion.Salida, null, Corto(d.NombreOriginal, OperacionConsts.MaxDescripcionLength)!, null);
        return o;
    }

    /// <summary>Para saber si una factura es emitida o recibida; sin RFC en el perfil se toma como recibida.</summary>
    public async Task<string?> RfcAsync(Guid organizacionId)
    {
        var perfil = await _perfiles.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId);
        var rfc = perfil?.Dato(CamposDelPerfil.Rfc)?.Valor?.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(rfc) ? null : rfc;
    }

    private static string? Corto(string? texto, int max) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim().Length <= max ? texto.Trim() : texto.Trim()[..max];
}
