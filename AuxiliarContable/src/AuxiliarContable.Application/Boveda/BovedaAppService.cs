using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Documentos;
using AuxiliarContable.Files;
using AuxiliarContable.Organizaciones;
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
    private readonly GestorDeDocumentos _gestor;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly Autorizador _autorizador;

    public BovedaAppService(
        IRepository<Documento, Guid> documentos,
        GestorDeDocumentos gestor,
        AlmacenDeDocumentos almacen,
        Autorizador autorizador)
    {
        _documentos = documentos;
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
            throw new UserFriendlyException("La constancia de situación fiscal se sube en «Perfil fiscal».");
        }

        const string demasiado = "El archivo pesa demasiado (máximo 20 MB).";
        if (archivo.ContentLength > DocumentoConsts.MaxBytes)
        {
            throw new UserFriendlyException(demasiado);
        }

        var bytes = await LectorConTope.LeerAsync(archivo.GetStream(), DocumentoConsts.MaxBytes, demasiado);
        var nombre = archivo.FileName ?? "documento";
        var esXml = DetectorDeDocumento.Detectar(bytes) == DetectorDeDocumento.Xml;

        if (tipo == TipoDeDocumento.Cfdi && !esXml)
        {
            throw new UserFriendlyException("De una factura se sube el XML: el PDF es solo su representación impresa.");
        }

        // Un XML es una factura aunque la persona haya elegido otro tipo; lo demás nunca es XML.
        var tipoReal = esXml ? TipoDeDocumento.Cfdi : tipo;
        var (documento, duplicado) = await _gestor.RecibirAsync(organizacionId, tipoReal, nombre, bytes,
            mime => tipoReal == TipoDeDocumento.Cfdi ? mime == DetectorDeDocumento.Xml : mime != DetectorDeDocumento.Xml);

        return new ResultadoDeSubidaDto { Documento = Describir(documento), EsDuplicado = duplicado };
    }

    public async Task<PagedResultDto<DocumentoDeBovedaDto>> GetListAsync(BuscarEnBovedaDto input)
    {
        await _autorizador.ExigirAsync(input.OrganizacionId, AccionEnOrganizacion.Ver);

        var consulta =
            from d in await _documentos.GetQueryableAsync()
            where d.OrganizacionId == input.OrganizacionId
            select new { D = d, Fecha = d.FechaDelDocumento ?? d.CreationTime };

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

        if (!string.IsNullOrWhiteSpace(input.Texto))
        {
            var t = input.Texto.Trim().ToLower();
            consulta = consulta.Where(x => x.D.NombreOriginal.ToLower().Contains(t));
        }

        var total = await AsyncExecuter.CountAsync(consulta);
        var pagina = await AsyncExecuter.ToListAsync(consulta
            .OrderByDescending(x => x.Fecha).ThenByDescending(x => x.D.CreationTime)
            .Skip(input.SkipCount)
            .Take(Math.Clamp(input.MaxResultCount, 1, 100)));

        return new PagedResultDto<DocumentoDeBovedaDto>(total, pagina.Select(x => Describir(x.D)).ToList());
    }

    public async Task<List<ResumenDelMesDto>> GetResumenAsync(Guid organizacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);

        var consulta =
            from d in await _documentos.GetQueryableAsync()
            where d.OrganizacionId == organizacionId
            select new { Fecha = d.FechaDelDocumento ?? d.CreationTime, d.Tipo };

        // Por cliente son cientos o pocos miles de filas: se agrupa en memoria.
        var filas = await AsyncExecuter.ToListAsync(consulta);
        return filas
            .GroupBy(f => new { f.Fecha.Year, f.Fecha.Month, f.Tipo })
            .Select(g => new ResumenDelMesDto
            {
                Anio = g.Key.Year,
                Mes = g.Key.Month,
                Tipo = g.Key.Tipo,
                Cantidad = g.Count(),
            })
            .OrderByDescending(r => r.Anio).ThenByDescending(r => r.Mes).ThenBy(r => r.Tipo)
            .ToList();
    }

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

    private static DocumentoDeBovedaDto Describir(Documento d) => new()
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
    };
}
