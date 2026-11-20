using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.BackgroundJobs;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Documentos;
using AuxiliarContable.Files;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.BackgroundJobs;
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
    private readonly GestorDePeriodos _periodos;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly IBackgroundJobManager _trabajos;

    public BovedaAppService(
        IRepository<Documento, Guid> documentos,
        GestorDeDocumentos gestor,
        AlmacenDeDocumentos almacen,
        Autorizador autorizador,
        GestorDePeriodos periodos,
        IRepository<PerfilFiscal, Guid> perfiles,
        IBackgroundJobManager trabajos)
    {
        _documentos = documentos;
        _gestor = gestor;
        _almacen = almacen;
        _autorizador = autorizador;
        _periodos = periodos;
        _perfiles = perfiles;
        _trabajos = trabajos;
    }

    public async Task<ResultadoDeSubidaDto> SubirAsync(Guid organizacionId, TipoDeDocumento tipo, IRemoteStreamContent archivo,
        int? anio = null, int? mes = null)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        if ((anio == null) != (mes == null))
        {
            throw new UserFriendlyException("Di el mes y el año del documento, o ninguno de los dos.");
        }

        DateTime? delMes = anio is { } a && mes is { } m ? PrimeroDelMes(a, m) : null;
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

        // El catálogo de cuentas y la balanza de comprobación son XML de la contabilidad electrónica, no facturas.
        var contabilidadElectronica = tipo is TipoDeDocumento.CatalogoDeCuentas or TipoDeDocumento.BalanzaDeComprobacion;
        if (contabilidadElectronica && !esXml)
        {
            throw new UserFriendlyException("Del catálogo de cuentas y de la balanza se sube el XML de la contabilidad electrónica (Anexo 24).");
        }

        // Un XML es una factura aunque la persona haya elegido otro tipo (salvo esos dos); lo demás nunca es XML.
        var tipoReal = esXml && !contabilidadElectronica ? TipoDeDocumento.Cfdi : tipo;

        // Una factura va al mes de su XML, diga lo que diga quien la sube: es lo que vale ante el SAT.
        var diaDeLaFactura = tipoReal == TipoDeDocumento.Cfdi ? FechaDelCfdi.Leer(bytes) : null;
        var fecha = diaDeLaFactura is { } dia ? FechaDelCfdi.ComoFecha(dia) : delMes;
        await _periodos.ExigirAbiertoAsync(organizacionId, fecha ?? Clock.Now);
        var (documento, duplicado) = await _gestor.RecibirAsync(organizacionId, tipoReal, nombre, bytes,
            mime => tipoReal == TipoDeDocumento.Cfdi || contabilidadElectronica ? mime == DetectorDeDocumento.Xml : mime != DetectorDeDocumento.Xml,
            fecha);

        // Un estado de cuenta sin mes: lo pone en su mes el motor, leyendo su periodo.
        var porFechar = !duplicado && documento.Tipo == TipoDeDocumento.EstadoDeCuenta && documento.FechaDelDocumento == null;
        if (porFechar)
        {
            await _trabajos.EnqueueAsync(new FecharEstadoDeCuentaArgs { DocumentoId = documento.Id });
        }

        return new ResultadoDeSubidaDto
        {
            Documento = Describir(documento),
            EsDuplicado = duplicado,
            Aviso = tipoReal == TipoDeDocumento.Cfdi && diaDeLaFactura == null && !duplicado
                ? "No trae la fecha de una factura (¿es un CFDI?): quedó en el mes que elegiste."
                : porFechar
                    ? "Se está leyendo de qué periodo es: en un momento se acomoda solo en su mes."
                    : null,
        };
    }

    public async Task<ResultadoDeSubidaDto> SubirFacturaAsync(string rfc, IRemoteStreamContent archivo)
    {
        var limpio = (rfc ?? string.Empty).Trim().ToUpperInvariant();
        var clientes = await AsyncExecuter.ToListAsync(
            from p in await _perfiles.GetQueryableAsync()
            from d in p.Datos
            where d.Campo == CamposDelPerfil.Rfc && d.Valor.ToUpper() == limpio
            select p.OrganizacionId);
        var quien = await _autorizador.QuienMiraAsync();
        var suyos = clientes.Distinct().Where(o => quien.Puede(o, AccionEnOrganizacion.Subir)).ToList();
        if (suyos.Count != 1)
        {
            // Ajeno e inexistente dicen lo mismo: no se confirma qué RFC tiene el despacho.
            throw new UserFriendlyException(suyos.Count == 0
                ? $"No tienes un cliente con el RFC {limpio}: revisa el RFC en su perfil fiscal."
                : $"Hay más de un cliente con el RFC {limpio}: súbelas desde «Documentos» de cada uno.");
        }

        return await SubirAsync(suyos[0], TipoDeDocumento.Cfdi, archivo);
    }

    public async Task<DocumentoDeBovedaDto> CambiarMesAsync(Guid organizacionId, Guid documentoId, int anio, int mes)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var documento = await DocumentoAsync(organizacionId, documentoId);
        if (documento.Tipo == TipoDeDocumento.Cfdi && FechaDelCfdi.Leer(await _almacen.LeerAsync(documento)) != null)
        {
            throw new UserFriendlyException("Una factura va en el mes de la fecha de su XML: ese no se cambia.");
        }

        var nuevo = PrimeroDelMes(anio, mes);
        await _periodos.ExigirAbiertoAsync(organizacionId, documento.FechaDelDocumento ?? documento.CreationTime);
        await _periodos.ExigirAbiertoAsync(organizacionId, nuevo);
        documento.FecharEn(nuevo);
        // Una persona dijo el mes: si el motor no pudo leer el periodo, ya está resuelto.
        if (documento.Estado == EstadoDeDocumento.ConError && documento.Tipo == TipoDeDocumento.EstadoDeCuenta)
        {
            documento.MarcarProcesado();
        }

        await _documentos.UpdateAsync(documento, autoSave: true);
        return Describir(documento);
    }

    /// <summary>El mes de un documento: se guarda como su día 1 (ver <see cref="FechaDelCfdi.ComoFecha"/>).</summary>
    private DateTime PrimeroDelMes(int anio, int mes)
    {
        var hoy = Clock.Now;
        if (mes is < 1 or > 12 || anio < 2000 || new DateOnly(anio, mes, 1) > new DateOnly(hoy.Year, hoy.Month, 1).AddMonths(1))
        {
            throw new UserFriendlyException("Mes no válido.");
        }

        return FechaDelCfdi.ComoFecha(new DateOnly(anio, mes, 1));
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
        await _periodos.ExigirAbiertoAsync(organizacionId, documento.FechaDelDocumento ?? documento.CreationTime);
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
