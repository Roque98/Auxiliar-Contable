using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Cfdis;
using AuxiliarContable.Documentos;
using AuxiliarContable.Files;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Boveda;

/// <summary>
/// Ver <see cref="IBovedaAppService"/>. Todo filtra por el cliente que llega en la llamada y
/// lo autoriza <see cref="Autorizador"/> (lo ajeno = 404), igual que el perfil fiscal.
/// </summary>
[Authorize]
public class BovedaAppService : AuxiliarContableAppService, IBovedaAppService
{
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<Cfdi, Guid> _cfdis;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly GestorDeDocumentos _gestor;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly Autorizador _autorizador;

    public BovedaAppService(
        IRepository<Documento, Guid> documentos,
        IRepository<Cfdi, Guid> cfdis,
        IRepository<PerfilFiscal, Guid> perfiles,
        GestorDeDocumentos gestor,
        AlmacenDeDocumentos almacen,
        Autorizador autorizador)
    {
        _documentos = documentos;
        _cfdis = cfdis;
        _perfiles = perfiles;
        _gestor = gestor;
        _almacen = almacen;
        _autorizador = autorizador;
    }

    public async Task<ResultadoDeSubidaDto> SubirAsync(Guid organizacionId, TipoDeDocumento tipo, IRemoteStreamContent archivo)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        if (archivo == null)
        {
            throw new UserFriendlyException("No llegó ningún archivo.");
        }

        if (tipo == TipoDeDocumento.ConstanciaDeSituacionFiscal)
        {
            throw new UserFriendlyException("La constancia de situación fiscal se sube en «Perfil fiscal»: ahí se lee y llena el perfil.");
        }

        const string demasiado = "El archivo pesa demasiado (máximo 20 MB).";
        if (archivo.ContentLength > DocumentoConsts.MaxBytes)
        {
            throw new UserFriendlyException(demasiado);
        }

        var bytes = await LectorConTope.LeerAsync(archivo.GetStream(), DocumentoConsts.MaxBytes, demasiado);
        var nombre = archivo.FileName ?? "documento";
        var rfc = await RfcDelClienteAsync(organizacionId);

        if (DetectorDeDocumento.Detectar(bytes) == DetectorDeDocumento.Xml)
        {
            return await SubirCfdiAsync(organizacionId, nombre, bytes, rfc);
        }

        if (tipo == TipoDeDocumento.Cfdi)
        {
            throw new UserFriendlyException("De una factura se sube el XML: el PDF es solo su representación impresa.");
        }

        var (documento, duplicado) = await _gestor.RecibirAsync(organizacionId, tipo, nombre, bytes,
            mime => mime != DetectorDeDocumento.Xml);
        return new ResultadoDeSubidaDto { Documento = Describir(documento, null, rfc), EsDuplicado = duplicado };
    }

    private async Task<ResultadoDeSubidaDto> SubirCfdiAsync(Guid organizacionId, string nombre, byte[] bytes, string? rfc)
    {
        DatosDeCfdi datos;
        try
        {
            datos = LectorDeCfdi.Leer(bytes);
        }
        catch (CfdiInvalidoException ex)
        {
            throw new UserFriendlyException(ex.Message);
        }

        // El mismo UUID con otros bytes (otra descarga del mismo comprobante) es el mismo CFDI.
        var previo = await _cfdis.FirstOrDefaultAsync(c => c.OrganizacionId == organizacionId && c.Uuid == datos.Uuid);
        if (previo != null && await _documentos.FindAsync(previo.DocumentoId) is { } suyo)
        {
            return new ResultadoDeSubidaDto { Documento = Describir(suyo, previo, rfc), EsDuplicado = true };
        }

        var (documento, duplicado) = await _gestor.RecibirAsync(organizacionId, TipoDeDocumento.Cfdi, nombre, bytes,
            mime => mime == DetectorDeDocumento.Xml);
        if (duplicado && previo != null)
        {
            return new ResultadoDeSubidaDto { Documento = Describir(documento, previo, rfc), EsDuplicado = true };
        }

        var cfdi = await _cfdis.InsertAsync(new Cfdi(GuidGenerator.Create(), organizacionId, documento.Id, datos), autoSave: true);
        documento.FecharEn(datos.Fecha);
        documento.MarcarProcesado();
        await _documentos.UpdateAsync(documento, autoSave: true);

        string? aviso = null;
        var direccion = cfdi.Direccion(rfc);
        if (rfc == null)
        {
            aviso = "Aún no tenemos el RFC del cliente: no sabemos si esta factura es un ingreso o un gasto. Completa su perfil fiscal.";
        }
        else if (direccion == DireccionDelCfdi.Desconocida)
        {
            aviso = $"Esta factura no la emitió ni la recibió el RFC del cliente ({rfc}). Revisa que sea de este cliente.";
        }

        return new ResultadoDeSubidaDto { Documento = Describir(documento, cfdi, rfc), Aviso = aviso };
    }

    public async Task<PagedResultDto<DocumentoDeBovedaDto>> GetListAsync(BuscarEnBovedaDto input)
    {
        await _autorizador.ExigirAsync(input.OrganizacionId, AccionEnOrganizacion.Ver);
        var rfc = await RfcDelClienteAsync(input.OrganizacionId) ?? string.Empty;

        var consulta =
            from d in await _documentos.GetQueryableAsync()
            where d.OrganizacionId == input.OrganizacionId
            join c in await _cfdis.GetQueryableAsync() on d.Id equals c.DocumentoId into cs
            from c in cs.DefaultIfEmpty()
            select new { D = d, C = c, Fecha = d.FechaDelDocumento ?? d.CreationTime };

        if (input.Anio is { } anio)
        {
            consulta = consulta.Where(x => x.Fecha.Year == anio);
        }

        if (input.Mes is { } mes)
        {
            consulta = consulta.Where(x => x.Fecha.Month == mes);
        }

        if (input.Tipo is { } tipo)
        {
            consulta = consulta.Where(x => x.D.Tipo == tipo);
        }

        switch (input.Direccion)
        {
            case DireccionDelCfdi.Emitido:
                consulta = consulta.Where(x => x.C != null && x.C.EmisorRfc == rfc);
                break;
            case DireccionDelCfdi.Recibido:
                consulta = consulta.Where(x => x.C != null && x.C.ReceptorRfc == rfc && x.C.EmisorRfc != rfc);
                break;
            case DireccionDelCfdi.Desconocida:
                consulta = consulta.Where(x => x.C != null && x.C.EmisorRfc != rfc && x.C.ReceptorRfc != rfc);
                break;
        }

        if (!string.IsNullOrWhiteSpace(input.Texto))
        {
            var t = input.Texto.Trim().ToLower();
            consulta = consulta.Where(x =>
                x.D.NombreOriginal.ToLower().Contains(t)
                || (x.C != null && (x.C.Uuid.ToLower().Contains(t)
                                    || x.C.EmisorRfc.ToLower().Contains(t)
                                    || x.C.ReceptorRfc.ToLower().Contains(t)
                                    || (x.C.EmisorNombre != null && x.C.EmisorNombre.ToLower().Contains(t))
                                    || (x.C.ReceptorNombre != null && x.C.ReceptorNombre.ToLower().Contains(t))
                                    || (x.C.Concepto != null && x.C.Concepto.ToLower().Contains(t)))));
        }

        var total = await AsyncExecuter.CountAsync(consulta);
        var pagina = await AsyncExecuter.ToListAsync(consulta
            .OrderByDescending(x => x.Fecha).ThenByDescending(x => x.D.CreationTime)
            .Skip(input.SkipCount)
            .Take(Math.Clamp(input.MaxResultCount, 1, 100)));

        return new PagedResultDto<DocumentoDeBovedaDto>(total, pagina.Select(x => Describir(x.D, x.C, rfc)).ToList());
    }

    public async Task<List<ResumenDelMesDto>> GetResumenAsync(Guid organizacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        var rfc = await RfcDelClienteAsync(organizacionId);

        var consulta =
            from d in await _documentos.GetQueryableAsync()
            where d.OrganizacionId == organizacionId
            join c in await _cfdis.GetQueryableAsync() on d.Id equals c.DocumentoId into cs
            from c in cs.DefaultIfEmpty()
            select new
            {
                Fecha = d.FechaDelDocumento ?? d.CreationTime,
                d.Tipo,
                EmisorRfc = c == null ? null : c.EmisorRfc,
                ReceptorRfc = c == null ? null : c.ReceptorRfc,
                TipoDeComprobante = c == null ? null : c.TipoDeComprobante,
                Moneda = c == null ? null : c.Moneda,
                Total = c == null ? 0m : c.Total,
            };

        // Por cliente son cientos o pocos miles de filas: se agrupa en memoria, con las reglas a la vista.
        var filas = await AsyncExecuter.ToListAsync(consulta);
        return filas
            .GroupBy(f => new { f.Fecha.Year, f.Fecha.Month, f.Tipo })
            .Select(g => new ResumenDelMesDto
            {
                Anio = g.Key.Year,
                Mes = g.Key.Month,
                Tipo = g.Key.Tipo,
                Cantidad = g.Count(),
                TotalEmitido = g.Where(f => f.Moneda == "MXN" && f.EmisorRfc != null
                                            && Cfdi.DireccionPara(f.EmisorRfc, f.ReceptorRfc!, rfc) == DireccionDelCfdi.Emitido)
                    .Sum(f => Signo(f.TipoDeComprobante) * f.Total),
                TotalRecibido = g.Where(f => f.Moneda == "MXN" && f.EmisorRfc != null
                                             && Cfdi.DireccionPara(f.EmisorRfc, f.ReceptorRfc!, rfc) == DireccionDelCfdi.Recibido)
                    .Sum(f => Signo(f.TipoDeComprobante) * f.Total),
            })
            .OrderByDescending(r => r.Anio).ThenByDescending(r => r.Mes).ThenBy(r => r.Tipo)
            .ToList();
    }

    /// <summary>Ingreso suma, egreso (nota de crédito) resta; traslado, nómina y pago no son montos de venta/gasto.</summary>
    private static decimal Signo(string? tipoDeComprobante) => tipoDeComprobante switch
    {
        "I" => 1m,
        "E" => -1m,
        _ => 0m,
    };

    public async Task<IRemoteStreamContent> GetArchivoAsync(Guid organizacionId, Guid documentoId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        var documento = await DocumentoAsync(organizacionId, documentoId);
        var bytes = await _almacen.LeerAsync(documento);
        return new RemoteStreamContent(new MemoryStream(bytes), documento.NombreOriginal, documento.TipoMime);
    }

    public async Task EliminarAsync(Guid organizacionId, Guid documentoId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var documento = await DocumentoAsync(organizacionId, documentoId);
        // Borrado lógico (queda en la auditoría y el archivo cifrado sigue en disco).
        await _cfdis.DeleteAsync(c => c.DocumentoId == documento.Id, autoSave: true);
        await _documentos.DeleteAsync(documento, autoSave: true);
    }

    private async Task<Documento> DocumentoAsync(Guid organizacionId, Guid documentoId)
    {
        var documento = await _documentos.FindAsync(documentoId);
        // De otro cliente = no existe (no se confirma que exista).
        if (documento == null || documento.OrganizacionId != organizacionId)
        {
            throw new EntityNotFoundException(typeof(Documento), documentoId);
        }

        return documento;
    }

    private async Task<string?> RfcDelClienteAsync(Guid organizacionId) =>
        (await _perfiles.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId))?.Dato(CamposDelPerfil.Rfc)?.Valor;

    private static DocumentoDeBovedaDto Describir(Documento d, Cfdi? c, string? rfc) => new()
    {
        Id = d.Id,
        Tipo = d.Tipo,
        Nombre = d.NombreOriginal,
        TipoMime = d.TipoMime,
        Tamano = d.Tamano,
        Estado = d.Estado,
        Motivo = d.Motivo,
        SubidoUtc = d.CreationTime,
        Fecha = d.FechaDelDocumento ?? d.CreationTime,
        Cfdi = c == null
            ? null
            : new CfdiResumenDto
            {
                Uuid = c.Uuid,
                TipoDeComprobante = c.TipoDeComprobante,
                Direccion = c.Direccion(rfc),
                Serie = c.Serie,
                Folio = c.Folio,
                EmisorRfc = c.EmisorRfc,
                EmisorNombre = c.EmisorNombre,
                ReceptorRfc = c.ReceptorRfc,
                ReceptorNombre = c.ReceptorNombre,
                Total = c.Total,
                Moneda = c.Moneda,
                MetodoDePago = c.MetodoDePago,
                Concepto = c.Concepto,
            },
    };
}
