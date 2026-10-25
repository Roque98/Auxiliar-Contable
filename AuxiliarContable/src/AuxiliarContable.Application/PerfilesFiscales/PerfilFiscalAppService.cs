using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.BackgroundJobs;
using AuxiliarContable.Documentos;
using AuxiliarContable.Files;
using AuxiliarContable.Organizaciones;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.PerfilesFiscales;

/// <summary>
/// Ver <see cref="IPerfilFiscalAppService"/>. Quién puede qué lo decide <see cref="Autorizador"/>
/// (ver = <see cref="AccionEnOrganizacion.Ver"/>; cambiar = <see cref="AccionEnOrganizacion.Subir"/>);
/// lo ajeno responde 404.
/// </summary>
[Authorize]
public class PerfilFiscalAppService : AuxiliarContableAppService, IPerfilFiscalAppService
{
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<Organizacion, Guid> _organizaciones;
    private readonly GestorDeDocumentos _gestor;
    private readonly Autorizador _autorizador;
    private readonly IBackgroundJobManager _trabajos;

    public PerfilFiscalAppService(
        IRepository<PerfilFiscal, Guid> perfiles,
        IRepository<Documento, Guid> documentos,
        IRepository<Organizacion, Guid> organizaciones,
        GestorDeDocumentos gestor,
        Autorizador autorizador,
        IBackgroundJobManager trabajos)
    {
        _perfiles = perfiles;
        _documentos = documentos;
        _organizaciones = organizaciones;
        _gestor = gestor;
        _autorizador = autorizador;
        _trabajos = trabajos;
    }

    public async Task<List<ClienteVisibleDto>> GetClientesAsync()
    {
        var quien = await _autorizador.QuienMiraAsync();
        var consulta = (await _organizaciones.GetQueryableAsync()).AsQueryable();
        if (!quien.Equipo)
        {
            var mias = quien.Organizaciones.Keys.ToList();
            consulta = consulta.Where(o => mias.Contains(o.Id));
        }

        var lista = await AsyncExecuter.ToListAsync(consulta.OrderBy(o => o.Nombre).Select(o => new { o.Id, o.Nombre }));
        return lista.Select(o => new ClienteVisibleDto
        {
            Id = o.Id,
            Nombre = o.Nombre,
            PuedeEditar = quien.Puede(o.Id, AccionEnOrganizacion.Subir),
        }).ToList();
    }

    public async Task<PerfilFiscalDto> GetAsync(Guid organizacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        return await DescribirAsync(organizacionId);
    }

    public async Task<PerfilFiscalDto> SubirCsfAsync(Guid organizacionId, IRemoteStreamContent archivo)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        if (archivo == null)
        {
            throw new UserFriendlyException("No llegó ningún archivo.");
        }

        const string demasiado = "La constancia pesa demasiado. Descárgala de nuevo del portal del SAT.";
        if (archivo.ContentLength > DocumentoConsts.MaxBytes)
        {
            throw new UserFriendlyException(demasiado);
        }

        var bytes = await LectorConTope.LeerAsync(archivo.GetStream(), DocumentoConsts.MaxBytes, demasiado);
        var (documento, _) = await _gestor.RecibirAsync(
            organizacionId, TipoDeDocumento.ConstanciaDeSituacionFiscal, archivo.FileName ?? "constancia.pdf", bytes,
            tipo => tipo == DetectorDeDocumento.Pdf);

        var perfil = await PerfilAsync(organizacionId);
        perfil.EmpezarLecturaDeCsf(documento.Id);
        await _perfiles.UpdateAsync(perfil, autoSave: true);

        // Se vuelve a leer aunque sea un duplicado: los avisos dependen de la fecha (antigüedad)
        // y de lo que se haya confirmado desde la vez anterior.
        await _trabajos.EnqueueAsync(new LeerCsfArgs { DocumentoId = documento.Id });
        return await DescribirAsync(organizacionId);
    }

    public async Task<PerfilFiscalDto> ConfirmarAsync(Guid organizacionId, string campo)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var perfil = await PerfilAsync(organizacionId);
        perfil.Confirmar(campo, Clock.Now);
        await _perfiles.UpdateAsync(perfil, autoSave: true);
        return await DescribirAsync(organizacionId);
    }

    public async Task<PerfilFiscalDto> ConfirmarConstanciaAsync(Guid organizacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var perfil = await PerfilAsync(organizacionId);
        perfil.ConfirmarLoDeLaConstancia(Clock.Now);
        await _perfiles.UpdateAsync(perfil, autoSave: true);
        return await DescribirAsync(organizacionId);
    }

    public async Task<PerfilFiscalDto> ResponderAsync(Guid organizacionId, ResponderDatoDelPerfilDto input)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var valor = Normalizar(input.Campo, input.Valor);

        var quien = await _autorizador.QuienMiraAsync();
        var perfil = await PerfilAsync(organizacionId);
        perfil.Responder(input.Campo, valor,
            quien.Equipo ? OrigenDelDato.CapturadoPorElDespacho : OrigenDelDato.RespondidoPorElCliente, Clock.Now);
        await _perfiles.UpdateAsync(perfil, autoSave: true);
        return await DescribirAsync(organizacionId);
    }

    /// <summary>
    /// Valida y deja el valor en su forma canónica. Lo que una persona escribe pasa por aquí;
    /// lo que extrae la CSF, por <see cref="ProcesadorDeCsf.Combinar"/>.
    /// </summary>
    public static string Normalizar(string clave, string valor)
    {
        var campo = CamposDelPerfil.Buscar(clave)
                    ?? throw new BusinessException("AuxiliarContable:CampoDelPerfilDesconocido").WithData("campo", clave);
        var v = (valor ?? string.Empty).Trim();
        if (v.Length == 0)
        {
            throw Invalido(campo, "no puede ir vacío");
        }

        switch (clave)
        {
            case CamposDelPerfil.Rfc:
                v = v.ToUpperInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);
                return Sat.EsRfc(v) ? v : throw Invalido(campo, "no tiene formato de RFC (12 o 13 caracteres)");
            case CamposDelPerfil.Curp:
                v = v.ToUpperInvariant();
                return Sat.EsCurp(v) ? v : throw Invalido(campo, "no tiene formato de CURP (18 caracteres)");
            case CamposDelPerfil.CodigoPostal:
                return v.Length == 5 && v.All(char.IsDigit) ? v : throw Invalido(campo, "son 5 dígitos");
            case CamposDelPerfil.TipoDePersona:
                v = v.ToLowerInvariant().Replace("í", "i");
                return v is "fisica" or "moral" ? v : throw Invalido(campo, "es «fisica» o «moral»");
        }

        switch (campo.Tipo)
        {
            case TipoDeCampo.Fecha:
                return DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    ? v
                    : throw Invalido(campo, "va como AAAA-MM-DD");
            case TipoDeCampo.SiNo:
                v = v.ToLowerInvariant().Replace("í", "i");
                return v is "si" or "no" ? v : throw Invalido(campo, "es «si» o «no»");
            case TipoDeCampo.Lista:
                try
                {
                    using var json = JsonDocument.Parse(v);
                    return json.RootElement.ValueKind == JsonValueKind.Array ? v : throw Invalido(campo, "es una lista");
                }
                catch (JsonException)
                {
                    throw Invalido(campo, "es una lista");
                }

            default:
                return v;
        }
    }

    private static BusinessException Invalido(CampoDelPerfil campo, string regla) =>
        new BusinessException("AuxiliarContable:DatoDelPerfilInvalido").WithData("campo", campo.Etiqueta).WithData("regla", regla);

    private async Task<PerfilFiscal> PerfilAsync(Guid organizacionId) =>
        await _perfiles.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId)
        ?? await _perfiles.InsertAsync(new PerfilFiscal(GuidGenerator.Create(), organizacionId), autoSave: true);

    private async Task<PerfilFiscalDto> DescribirAsync(Guid organizacionId)
    {
        var perfil = await _perfiles.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId);
        var quien = await _autorizador.QuienMiraAsync();

        var dto = new PerfilFiscalDto
        {
            OrganizacionId = organizacionId,
            PuedeEditar = quien.Puede(organizacionId, AccionEnOrganizacion.Subir),
            Campos = CamposDelPerfil.Todos.Select(c =>
            {
                var dato = perfil?.Dato(c.Clave);
                return new DatoDelPerfilDto
                {
                    Clave = c.Clave,
                    Etiqueta = c.Etiqueta,
                    Grupo = c.Grupo,
                    Tipo = c.Tipo,
                    VieneEnLaCsf = c.VieneEnLaCsf,
                    Valor = dato?.Valor,
                    Origen = dato?.Origen,
                    Confirmado = dato?.Confirmado ?? false,
                    DocumentoId = dato?.DocumentoId,
                    ActualizadoUtc = dato?.ActualizadoUtc,
                };
            }).ToList(),
        };

        if (perfil?.UltimaCsfId is { } csfId && await _documentos.FindAsync(csfId) is { } csf
            && csf.OrganizacionId == organizacionId)
        {
            dto.Csf = new CsfDelPerfilDto
            {
                DocumentoId = csf.Id,
                Nombre = csf.NombreOriginal,
                SubidaUtc = csf.CreationTime,
                Estado = csf.Estado,
                Motivo = csf.Motivo,
                Avisos = perfil.AvisosDeLaCsf?.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList() ?? [],
            };
        }

        return dto;
    }
}
