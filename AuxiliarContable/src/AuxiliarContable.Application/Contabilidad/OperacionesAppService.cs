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
    public const int MinReferenciaParaJuntar = RegistroDeOperaciones.MinReferenciaParaJuntar;

    private readonly IRepository<Operacion, Guid> _operaciones;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<MovimientoFueraDelBanco, Guid> _movimientos;
    private readonly IRepository<SolicitudDeFactura, Guid> _solicitudes;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly RegistroDeOperaciones _registro;
    private readonly GestorDePeriodos _periodos;
    private readonly Autorizador _autorizador;

    public OperacionesAppService(
        IRepository<Operacion, Guid> operaciones,
        IRepository<Documento, Guid> documentos,
        IRepository<MovimientoFueraDelBanco, Guid> movimientos,
        IRepository<SolicitudDeFactura, Guid> solicitudes,
        AlmacenDeDocumentos almacen,
        RegistroDeOperaciones registro,
        GestorDePeriodos periodos,
        Autorizador autorizador)
    {
        _operaciones = operaciones;
        _documentos = documentos;
        _movimientos = movimientos;
        _solicitudes = solicitudes;
        _almacen = almacen;
        _registro = registro;
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
        await _registro.AgregarLoQueFaltaAsync(organizacionId, desde, hasta);

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

    public async Task<FacturaDeLaOperacionDto?> GetFacturaAsync(Guid organizacionId, Guid operacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        var o = await _operaciones.FindAsync(operacionId);
        if (o == null || o.OrganizacionId != organizacionId)
        {
            throw new EntityNotFoundException(typeof(Operacion), operacionId);
        }

        if (o.FacturaDocumentoId is not { } docId || await _documentos.FindAsync(docId) is not { } d || d.OrganizacionId != organizacionId)
        {
            return null;
        }

        var c = DatosDelCfdi.Leer(await _almacen.LeerAsync(d));
        return c == null
            ? null
            : new FacturaDeLaOperacionDto
            {
                Tipo = c.Tipo, Metodo = c.MetodoPago, Forma = c.FormaPago, Total = c.Total, Moneda = c.Moneda,
                TipoRelacion = c.TipoRelacion, UuidRelacionado = c.UuidRelacionado,
            };
    }

    public async Task<List<PorSubirDto>> GetPorSubirAsync(Guid organizacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        var lista = await AsyncExecuter.ToListAsync((await _operaciones.GetQueryableAsync())
            .Where(o => o.OrganizacionId == organizacionId
                        && ((o.FacturaEstado == EstadoDePata.Pendiente && o.FacturaMotivo != null)
                            || (o.TransaccionEstado == EstadoDePata.Pendiente && o.TransaccionMotivo != null)
                            || (o.ControlEstado == EstadoDePata.Pendiente && o.ControlMotivo != null)))
            .OrderBy(o => o.Fecha)
            .Take(200));
        var hoy = Clock.Now.Date;
        var r = new List<PorSubirDto>();
        foreach (var o in lista)
        {
            void Agregar(PataDeOperacion pata, EstadoDePata estado, string? motivo)
            {
                // Lo que se amarra solo con el estado de cuenta no es algo que el cliente tenga que subir.
                if (estado == EstadoDePata.Pendiente && !string.IsNullOrWhiteSpace(motivo) && !motivo.StartsWith(MotivoDelEstadoDeCuenta, StringComparison.Ordinal))
                {
                    r.Add(new PorSubirDto
                    {
                        OperacionId = o.Id, Fecha = o.Fecha, Descripcion = o.Descripcion, Importe = o.Importe, Pata = pata,
                        Motivo = motivo, Dias = Math.Max(0, (int)(hoy - (o.LastModificationTime ?? o.CreationTime).Date).TotalDays),
                    });
                }
            }

            Agregar(PataDeOperacion.Factura, o.FacturaEstado, o.FacturaMotivo);
            Agregar(PataDeOperacion.Transaccion, o.TransaccionEstado, o.TransaccionMotivo);
            Agregar(PataDeOperacion.ControlInterno, o.ControlEstado, o.ControlMotivo);
        }

        return r;
    }

    /// <summary>El motivo con el que la guía deja una transacción para el amarre con el estado de cuenta.</summary>
    public const string MotivoDelEstadoDeCuenta = "Se amarra con el estado de cuenta";

    public async Task<OperacionDelClienteDto> DeDocumentoAsync(Guid organizacionId, Guid documentoId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var documento = await DocumentoDelClienteAsync(organizacionId, documentoId);
        var operacion = await _registro.DeDocumentoAsync(organizacionId, documento, await _registro.RfcAsync(organizacionId))
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
        await _registro.JuntarEnAsync(o, otra);
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
        var gemela = await _registro.GemelaAsync(o);
        if (gemela != null)
        {
            await _periodos.ExigirAbiertoAsync(o.OrganizacionId, gemela.Fecha);
            // Se queda la más antigua (la que el cliente ya conocía).
            var (queda, sale) = gemela.CreationTime <= o.CreationTime ? (gemela, o) : (o, gemela);
            await _registro.JuntarEnAsync(queda, sale);
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
        else if (!string.IsNullOrWhiteSpace(p.Motivo))
        {
            // Pendiente con motivo: «lo subiré después».
            o.Posponer(pata, p.Motivo);
        }
        else
        {
            o.Vaciar(pata);
        }
    }

    private async Task<List<OperacionDelClienteDto>> DescribirAsync(List<Operacion> lista)
    {
        var docIds = lista.SelectMany(o => new[] { o.FacturaDocumentoId, o.TransaccionDocumentoId, o.ControlDocumentoId })
            .OfType<Guid>().Distinct().ToList();
        var docs = docIds.Count == 0
            ? new Dictionary<Guid, Documento>()
            : (await _documentos.GetListAsync(d => docIds.Contains(d.Id))).ToDictionary(d => d.Id);
        var nombres = docs.ToDictionary(d => d.Key, d => d.Value.NombreOriginal);
        AnalisisCortoDto? Analisis(Guid? id) => id is { } x && docs.TryGetValue(x, out var d) ? AnalisisCortoDto.De(d.Analisis) : null;
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
                    Analisis = Analisis(o.TransaccionDocumentoId), DelBanco = o.ClaveDeMovimiento != null,
                    MovimientoFueraDelBancoId = o.MovimientoFueraDelBancoId, Vence = m?.Vence, Saldado = m?.Saldado,
                },
                ControlInterno = new PataDto
                {
                    Estado = o.ControlEstado, DocumentoId = o.ControlDocumentoId, NombreDelDocumento = Nombre(o.ControlDocumentoId), Motivo = o.ControlMotivo,
                    Analisis = Analisis(o.ControlDocumentoId),
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
