using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Documentos;
using AuxiliarContable.Facturacion;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Contabilidad;

/// <summary>Ver <see cref="IOperacionesAppService"/>.</summary>
[Authorize]
public class OperacionesAppService : AuxiliarContableAppService, IOperacionesAppService
{
    /// <summary>Una referencia más corta que esto no se usa para juntar operaciones solas: «1» o «12» se repiten.</summary>
    public const int MinReferenciaParaJuntar = 4;

    private readonly IRepository<Operacion, Guid> _operaciones;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<MovimientoFueraDelBanco, Guid> _movimientos;
    private readonly IRepository<SolicitudDeFactura, Guid> _solicitudes;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly IPerfilFiscalAppService _perfiles;
    private readonly GestorDePeriodos _periodos;
    private readonly Autorizador _autorizador;

    public OperacionesAppService(
        IRepository<Operacion, Guid> operaciones,
        IRepository<Documento, Guid> documentos,
        IRepository<MovimientoFueraDelBanco, Guid> movimientos,
        IRepository<SolicitudDeFactura, Guid> solicitudes,
        AlmacenDeDocumentos almacen,
        IPerfilFiscalAppService perfiles,
        GestorDePeriodos periodos,
        Autorizador autorizador)
    {
        _operaciones = operaciones;
        _documentos = documentos;
        _movimientos = movimientos;
        _solicitudes = solicitudes;
        _almacen = almacen;
        _perfiles = perfiles;
        _periodos = periodos;
        _autorizador = autorizador;
    }

    public async Task<OperacionesDelMesDto> GetListAsync(Guid organizacionId, int anio, int mes)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }

        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1);
        await AgregarLoQueFaltaAsync(organizacionId, desde, hasta);

        var lista = await AsyncExecuter.ToListAsync((await _operaciones.GetQueryableAsync())
            .Where(o => o.OrganizacionId == organizacionId && o.Fecha >= desde && o.Fecha < hasta));
        // Las más completas primero: lo amarrado, luego lo respondido; y dentro, lo más reciente.
        lista = lista.OrderByDescending(o => o.Ligadas).ThenByDescending(o => o.Respondidas)
            .ThenByDescending(o => o.Fecha).ThenByDescending(o => o.CreationTime).ToList();

        var cerrado = await _periodos.EstaCerradoAsync(organizacionId, anio, mes);
        return new OperacionesDelMesDto
        {
            Anio = anio,
            Mes = mes,
            Operaciones = await DescribirAsync(lista),
            PuedeEditar = !cerrado && (await _autorizador.QuienMiraAsync()).Puede(organizacionId, AccionEnOrganizacion.Subir),
            Cerrado = cerrado,
        };
    }

    public async Task<OperacionDelClienteDto> DeDocumentoAsync(Guid organizacionId, Guid documentoId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var documento = await DocumentoDelClienteAsync(organizacionId, documentoId);
        var operacion = await DeDocumentoInternoAsync(organizacionId, documento, await RfcDelClienteAsync(organizacionId))
                        ?? throw new UserFriendlyException(
                            "Un estado de cuenta trae muchos movimientos: cada uno se amarra en «Mi información → Todo» después de conciliar el mes.");
        return (await DescribirAsync([operacion]))[0];
    }

    public async Task<OperacionDelClienteDto> DeSolicitudAsync(Guid organizacionId, Guid solicitudId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var s = await _solicitudes.FindAsync(solicitudId);
        if (s == null || s.OrganizacionId != organizacionId)
        {
            throw new EntityNotFoundException(typeof(SolicitudDeFactura), solicitudId);
        }

        var o = await _operaciones.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId && x.SolicitudDeFacturaId == solicitudId);
        if (o == null)
        {
            o = new Operacion(GuidGenerator.Create(), organizacionId);
            o.Describir(s.CreationTime, SentidoDeOperacion.Entrada, s.Total > 0 ? s.Total : null, $"Factura a {s.ReceptorNombre}", s.ReceptorNombre);
            o.LigarFactura(null, s.XmlDocumentoId, s.Id);
            await _operaciones.InsertAsync(o, autoSave: true);
        }

        return (await DescribirAsync([o]))[0];
    }

    public async Task<OperacionDelClienteDto> CrearAsync(Guid organizacionId, GuardarOperacionDto input)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        ValidarFecha(input.Fecha);
        await _periodos.ExigirAbiertoAsync(organizacionId, input.Fecha);

        var o = new Operacion(GuidGenerator.Create(), organizacionId);
        o.Describir(input.Fecha, input.Sentido, input.Importe, input.Descripcion, input.Contraparte);
        await _operaciones.InsertAsync(o, autoSave: true);
        o = await PonerPatasAsync(o, input);
        return (await DescribirAsync([o]))[0];
    }

    public async Task<OperacionDelClienteDto> ActualizarAsync(Guid organizacionId, Guid operacionId, GuardarOperacionDto input)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        ValidarFecha(input.Fecha);
        var o = await DelClienteAsync(organizacionId, operacionId);
        // Ni sacarla de un mes cerrado ni meterla en uno.
        await _periodos.ExigirAbiertoAsync(organizacionId, o.Fecha);
        await _periodos.ExigirAbiertoAsync(organizacionId, input.Fecha);

        o.Describir(input.Fecha, input.Sentido, input.Importe, input.Descripcion, input.Contraparte);
        await _operaciones.UpdateAsync(o, autoSave: true);
        o = await PonerPatasAsync(o, input);
        return (await DescribirAsync([o]))[0];
    }

    public async Task<OperacionDelClienteDto> JuntarAsync(Guid organizacionId, Guid operacionId, Guid conOperacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        if (operacionId == conOperacionId)
        {
            throw new UserFriendlyException("Es la misma operación.");
        }

        var o = await DelClienteAsync(organizacionId, operacionId);
        var otra = await DelClienteAsync(organizacionId, conOperacionId);
        await _periodos.ExigirAbiertoAsync(organizacionId, o.Fecha);
        await _periodos.ExigirAbiertoAsync(organizacionId, otra.Fecha);
        await JuntarEnAsync(o, otra);
        return (await DescribirAsync([o]))[0];
    }

    public async Task BorrarAsync(Guid organizacionId, Guid operacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var o = await DelClienteAsync(organizacionId, operacionId);
        await _periodos.ExigirAbiertoAsync(organizacionId, o.Fecha);
        await _operaciones.DeleteAsync(o, autoSave: true);
    }

    /// <summary>Aplica las patas que vengan. Puede terminar en otra operación (si el UUID o la referencia ya estaban en una).</summary>
    private async Task<Operacion> PonerPatasAsync(Operacion o, GuardarOperacionDto input)
    {
        if (input.Factura is { } f)
        {
            await PonerFacturaAsync(o, f);
        }

        if (input.Transaccion is { } t)
        {
            await PonerTransaccionAsync(o, t);
        }

        if (input.ControlInterno is { } c)
        {
            if (c.Estado == EstadoDePata.Ligada)
            {
                var doc = await DocumentoDelClienteAsync(o.OrganizacionId, c.DocumentoId
                    ?? throw new UserFriendlyException("Sube el ticket, la nota o el comprobante interno."));
                o.LigarControl(doc.Id);
            }
            else
            {
                Responder(o, PataDeOperacion.ControlInterno, c);
            }
        }

        await _operaciones.UpdateAsync(o, autoSave: true);

        // ¿El UUID o la referencia ya estaban en otra operación del cliente? Son la misma: se juntan.
        var gemela = await GemelaAsync(o);
        if (gemela != null)
        {
            await _periodos.ExigirAbiertoAsync(o.OrganizacionId, gemela.Fecha);
            // Se queda la más antigua (la que el cliente ya conocía).
            var (queda, sale) = gemela.CreationTime <= o.CreationTime ? (gemela, o) : (o, gemela);
            await JuntarEnAsync(queda, sale);
            return queda;
        }

        return o;
    }

    private async Task PonerFacturaAsync(Operacion o, GuardarPataDto f)
    {
        if (f.Estado != EstadoDePata.Ligada)
        {
            Responder(o, PataDeOperacion.Factura, f);
            return;
        }

        var uuid = f.Referencia;
        if (f.DocumentoId is { } id)
        {
            var doc = await DocumentoDelClienteAsync(o.OrganizacionId, id);
            if (doc.Tipo == TipoDeDocumento.Cfdi && string.IsNullOrWhiteSpace(uuid))
            {
                uuid = DatosDelCfdi.Leer(await _almacen.LeerAsync(doc))?.Uuid;
            }
        }

        try
        {
            o.LigarFactura(uuid, f.DocumentoId, o.SolicitudDeFacturaId);
        }
        catch (BusinessException e) when (e.Code == "AuxiliarContable:PataSinDato")
        {
            throw new UserFriendlyException("Escribe el UUID (folio fiscal) de la factura o sube su XML.");
        }
    }

    private async Task PonerTransaccionAsync(Operacion o, GuardarPataDto t)
    {
        var antes = o.MovimientoFueraDelBancoId;
        if (t.Estado != EstadoDePata.Ligada)
        {
            Responder(o, PataDeOperacion.Transaccion, t);
            await SoltarMovimientoAsync(antes);
            return;
        }

        var forma = t.Forma ?? FormaDeTransaccion.Banco;
        if (!Enum.IsDefined(forma))
        {
            throw new UserFriendlyException("Forma de pago no válida.");
        }

        if (t.DocumentoId is { } id)
        {
            await DocumentoDelClienteAsync(o.OrganizacionId, id);
        }

        if (forma == FormaDeTransaccion.Banco)
        {
            if (string.IsNullOrWhiteSpace(t.Referencia) && t.DocumentoId is null)
            {
                throw new UserFriendlyException("Escribe la referencia o folio de la transferencia (la del comprobante o la del estado de cuenta), o sube el comprobante.");
            }

            o.LigarTransaccion(forma, t.Referencia, t.DocumentoId, null);
            await SoltarMovimientoAsync(antes);
            return;
        }

        // Efectivo o crédito: queda también en «Efectivo y crédito», que es donde se lleva la caja y lo por pagar.
        if (o.Importe is not { } importe)
        {
            throw new UserFriendlyException("Pon el importe de la operación para registrarla en efectivo o a crédito.");
        }

        var tipo = (forma, o.Sentido) switch
        {
            (FormaDeTransaccion.Efectivo, SentidoDeOperacion.Entrada) => TipoDeMovimientoFueraDelBanco.EntradaEnEfectivo,
            (FormaDeTransaccion.Efectivo, _) => TipoDeMovimientoFueraDelBanco.SalidaEnEfectivo,
            (_, SentidoDeOperacion.Entrada) => TipoDeMovimientoFueraDelBanco.VentaACredito,
            _ => TipoDeMovimientoFueraDelBanco.CompraACredito,
        };
        var m = antes is { } mid ? await _movimientos.FindAsync(mid) : null;
        var nuevo = m == null;
        m ??= new MovimientoFueraDelBanco(GuidGenerator.Create(), o.OrganizacionId);
        var vence = forma == FormaDeTransaccion.Credito ? t.Vence ?? m.Vence : null;
        m.Poner(tipo, o.Fecha, importe, o.Descripcion, o.Contraparte, t.DocumentoId ?? o.ControlDocumentoId ?? o.FacturaDocumentoId, vence, null);
        if (nuevo)
        {
            await _movimientos.InsertAsync(m, autoSave: true);
        }
        else
        {
            await _movimientos.UpdateAsync(m, autoSave: true);
        }

        o.LigarTransaccion(forma, t.Referencia, t.DocumentoId, m.Id);
    }

    /// <summary>El registro de efectivo o crédito que ya no es la transacción de la operación se quita (si su mes sigue abierto).</summary>
    private async Task SoltarMovimientoAsync(Guid? movimientoId)
    {
        if (movimientoId is { } id && await _movimientos.FindAsync(id) is { } m)
        {
            await _periodos.ExigirAbiertoAsync(m.OrganizacionId, m.Fecha);
            await _movimientos.DeleteAsync(m, autoSave: true);
        }
    }

    private static void Responder(Operacion o, PataDeOperacion pata, GuardarPataDto p)
    {
        if (p.Estado == EstadoDePata.NoHay)
        {
            if (string.IsNullOrWhiteSpace(p.Motivo))
            {
                throw new UserFriendlyException("Di por qué no hay: por ejemplo «no se facturó» o «no tengo la referencia».");
            }

            o.DeclararQueNoHay(pata, p.Motivo);
        }
        else
        {
            o.Vaciar(pata);
        }
    }

    private async Task<Operacion?> GemelaAsync(Operacion o)
    {
        var q = (await _operaciones.GetQueryableAsync()).Where(x => x.OrganizacionId == o.OrganizacionId && x.Id != o.Id);
        if (o.Uuid is { } uuid)
        {
            var conUuid = await AsyncExecuter.FirstOrDefaultAsync(q.Where(x => x.Uuid == uuid));
            if (conUuid != null)
            {
                return conUuid;
            }
        }

        if (o.TransaccionForma == FormaDeTransaccion.Banco && o.Referencia is { Length: >= MinReferenciaParaJuntar } referencia)
        {
            var r = referencia.ToUpper();
            return await AsyncExecuter.FirstOrDefaultAsync(q.Where(x => x.TransaccionForma == FormaDeTransaccion.Banco
                                                                         && x.Referencia != null && x.Referencia.ToUpper() == r));
        }

        return null;
    }

    private async Task JuntarEnAsync(Operacion queda, Operacion sale)
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
                                       || (a.TransaccionDocumentoId != null && a.TransaccionDocumentoId == b.TransaccionDocumentoId),
        _ => a.ControlDocumentoId != null && a.ControlDocumentoId == b.ControlDocumentoId,
    };

    /// <summary>
    /// Lo del mes que aún no tiene operación: las facturas, los comprobantes de pago, el control interno y los
    /// registros de efectivo y crédito. Así «Todo» enseña también lo que se subió antes de que existieran las operaciones.
    /// </summary>
    private async Task AgregarLoQueFaltaAsync(Guid organizacionId, DateTime desde, DateTime hasta)
    {
        var tipos = new[] { TipoDeDocumento.Cfdi, TipoDeDocumento.ComprobanteDePago, TipoDeDocumento.ControlInterno, TipoDeDocumento.Ticket };
        var ops = await _operaciones.GetQueryableAsync();
        var documentos = await AsyncExecuter.ToListAsync(
            from d in await _documentos.GetQueryableAsync()
            where d.OrganizacionId == organizacionId && tipos.Contains(d.Tipo)
                  && (d.FechaDelDocumento ?? d.CreationTime) >= desde && (d.FechaDelDocumento ?? d.CreationTime) < hasta
                  && !ops.Any(o => o.FacturaDocumentoId == d.Id || o.TransaccionDocumentoId == d.Id || o.ControlDocumentoId == d.Id)
            select d);
        var movimientos = await AsyncExecuter.ToListAsync(
            from m in await _movimientos.GetQueryableAsync()
            where m.OrganizacionId == organizacionId && m.Fecha >= desde && m.Fecha < hasta
                  && !ops.Any(o => o.MovimientoFueraDelBancoId == m.Id)
            select m);
        if (documentos.Count == 0 && movimientos.Count == 0)
        {
            return;
        }

        var rfc = await RfcDelClienteAsync(organizacionId);
        foreach (var d in documentos.OrderBy(d => d.Tipo == TipoDeDocumento.Cfdi ? 0 : 1))
        {
            await DeDocumentoInternoAsync(organizacionId, d, rfc);
        }

        foreach (var m in movimientos)
        {
            var o = new Operacion(GuidGenerator.Create(), organizacionId);
            var entrada = m.Tipo is TipoDeMovimientoFueraDelBanco.EntradaEnEfectivo or TipoDeMovimientoFueraDelBanco.VentaACredito;
            o.Describir(m.Fecha, entrada ? SentidoDeOperacion.Entrada : SentidoDeOperacion.Salida, m.Importe, m.Concepto, m.Contraparte);
            o.LigarTransaccion(m.EsACredito ? FormaDeTransaccion.Credito : FormaDeTransaccion.Efectivo, null, null, m.Id);
            await _operaciones.InsertAsync(o, autoSave: true);
        }
    }

    /// <summary>La operación del documento (la que ya lo tiene, la de su mismo UUID, o una nueva). Null si es un estado de cuenta o una constancia.</summary>
    private async Task<Operacion?> DeDocumentoInternoAsync(Guid organizacionId, Documento d, string? rfcDelCliente)
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
                var o = new Operacion(GuidGenerator.Create(), organizacionId);
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
        var o = new Operacion(GuidGenerator.Create(), organizacionId);
        o.Describir(fecha, SentidoDeOperacion.Salida, null, Corto(d.NombreOriginal, OperacionConsts.MaxDescripcionLength)!, null);
        return o;
    }

    private async Task<List<OperacionDelClienteDto>> DescribirAsync(List<Operacion> lista)
    {
        var docIds = lista.SelectMany(o => new[] { o.FacturaDocumentoId, o.TransaccionDocumentoId, o.ControlDocumentoId })
            .OfType<Guid>().Distinct().ToList();
        var nombres = docIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _documentos.GetListAsync(d => docIds.Contains(d.Id))).ToDictionary(d => d.Id, d => d.NombreOriginal);
        var movIds = lista.Select(o => o.MovimientoFueraDelBancoId).OfType<Guid>().ToList();
        var movimientos = movIds.Count == 0
            ? new Dictionary<Guid, MovimientoFueraDelBanco>()
            : (await _movimientos.GetListAsync(m => movIds.Contains(m.Id))).ToDictionary(m => m.Id);

        string? Nombre(Guid? id) => id is { } x && nombres.TryGetValue(x, out var n) ? n : null;

        return lista.Select(o =>
        {
            var m = o.MovimientoFueraDelBancoId is { } mid && movimientos.TryGetValue(mid, out var mm) ? mm : null;
            return new OperacionDelClienteDto
            {
                Id = o.Id,
                Fecha = o.Fecha,
                Sentido = o.Sentido,
                Importe = o.Importe,
                Descripcion = o.Descripcion,
                Contraparte = o.Contraparte,
                Factura = new PataDto
                {
                    Estado = o.FacturaEstado, Referencia = o.Uuid, DocumentoId = o.FacturaDocumentoId,
                    NombreDelDocumento = Nombre(o.FacturaDocumentoId), Motivo = o.FacturaMotivo, SolicitudDeFacturaId = o.SolicitudDeFacturaId,
                },
                Transaccion = new PataDto
                {
                    Estado = o.TransaccionEstado, Referencia = o.Referencia, DocumentoId = o.TransaccionDocumentoId,
                    NombreDelDocumento = Nombre(o.TransaccionDocumentoId), Motivo = o.TransaccionMotivo, Forma = o.TransaccionForma,
                    MovimientoFueraDelBancoId = o.MovimientoFueraDelBancoId, Vence = m?.Vence, Saldado = m?.Saldado,
                },
                ControlInterno = new PataDto
                {
                    Estado = o.ControlEstado, DocumentoId = o.ControlDocumentoId, NombreDelDocumento = Nombre(o.ControlDocumentoId), Motivo = o.ControlMotivo,
                },
                Ligadas = o.Ligadas,
                Respondidas = o.Respondidas,
            };
        }).ToList();
    }

    private async Task<Operacion> DelClienteAsync(Guid organizacionId, Guid operacionId)
    {
        var o = await _operaciones.FindAsync(operacionId);
        if (o == null || o.OrganizacionId != organizacionId)
        {
            throw new EntityNotFoundException(typeof(Operacion), operacionId);
        }

        return o;
    }

    private async Task<Documento> DocumentoDelClienteAsync(Guid organizacionId, Guid documentoId)
    {
        var d = await _documentos.FindAsync(documentoId);
        if (d == null || d.OrganizacionId != organizacionId)
        {
            throw new EntityNotFoundException(typeof(Documento), documentoId);
        }

        return d;
    }

    /// <summary>Para saber si una factura es emitida o recibida; sin RFC en el perfil se toma como recibida.</summary>
    private async Task<string?> RfcDelClienteAsync(Guid organizacionId)
    {
        var perfil = await _perfiles.GetAsync(organizacionId);
        var rfc = perfil.Campos.FirstOrDefault(c => c.Clave == CamposDelPerfil.Rfc)?.Valor?.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(rfc) ? null : rfc;
    }

    private static void ValidarFecha(DateTime fecha)
    {
        if (fecha.Year is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Fecha no válida.");
        }
    }

    private static string? Corto(string? texto, int max) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim().Length <= max ? texto.Trim() : texto.Trim()[..max];
}
