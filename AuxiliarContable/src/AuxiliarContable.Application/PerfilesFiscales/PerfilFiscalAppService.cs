using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Documentos;
using AuxiliarContable.Files;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
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
    private readonly IMotorFiscal _motor;

    /// <summary>Las listas del perfil se guardan como JSON legible (acentos tal cual).</summary>
    private static readonly JsonSerializerOptions Listas = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    public PerfilFiscalAppService(
        IRepository<PerfilFiscal, Guid> perfiles,
        IRepository<Documento, Guid> documentos,
        IRepository<Organizacion, Guid> organizaciones,
        GestorDeDocumentos gestor,
        Autorizador autorizador,
        IMotorFiscal motor)
    {
        _motor = motor;
        _perfiles = perfiles;
        _documentos = documentos;
        _organizaciones = organizaciones;
        _gestor = gestor;
        _autorizador = autorizador;
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
        // La lee el motor fiscal y sus datos se PROPONEN: quedan «de la constancia» y sin confirmar, y nunca
        // pisan lo que una persona ya confirmó (eso sale como aviso).
        var avisos = await ProponerLoDeLaConstanciaAsync(perfil, documento.Id, archivo.FileName ?? "constancia.pdf", bytes);
        perfil.TerminarLecturaDeCsf(documento.Id, avisos);
        await _perfiles.UpdateAsync(perfil, autoSave: true);
        return await DescribirAsync(organizacionId);
    }

    private async Task<List<string>> ProponerLoDeLaConstanciaAsync(PerfilFiscal perfil, Guid documentoId, string nombre, byte[] bytes)
    {
        ConstanciaLeida leida;
        try
        {
            leida = await _motor.LeerConstanciaAsync(new ArchivoParaElMotor(nombre, bytes));
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor fiscal no leyó una constancia");
            return ["No se pudo leer la constancia en este momento. Quedó en el expediente: captura los datos abajo o vuelve a subirla más tarde."];
        }

        if (!leida.EsConstancia)
        {
            return ["No parece la Constancia de Situación Fiscal descargada del SAT (una foto o un escaneo no se pueden leer). Quedó en el expediente: captura los datos abajo."];
        }

        var avisos = new List<string>();
        void Proponer(string clave, string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
            {
                return;
            }

            string limpio;
            try
            {
                limpio = Normalizar(clave, valor);
            }
            catch (BusinessException)
            {
                return; // lo que no pasa las mismas reglas que una captura no se propone
            }

            if (!perfil.Proponer(clave, limpio, OrigenDelDato.ExtraidoDeCsf, documentoId, Clock.Now))
            {
                avisos.Add($"{CamposDelPerfil.Buscar(clave)!.Etiqueta}: la constancia dice «{limpio}», distinto de lo que ya estaba confirmado. Revísalo.");
            }
        }

        string? Lista(List<string> valores) => valores.Count == 0 ? null : JsonSerializer.Serialize(valores, Listas);

        Proponer(CamposDelPerfil.TipoDePersona, leida.TipoPersona);
        Proponer(CamposDelPerfil.Rfc, leida.Rfc);
        Proponer(CamposDelPerfil.Nombre, leida.Nombre);
        Proponer(CamposDelPerfil.CodigoPostal, leida.CodigoPostal);
        Proponer(CamposDelPerfil.Regimenes, Lista(leida.Regimenes));
        Proponer(CamposDelPerfil.Obligaciones, Lista(leida.Obligaciones));
        Proponer(CamposDelPerfil.ActividadesEconomicas, Lista(leida.Actividades));
        return avisos;
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
    /// lo que extrae el motor fiscal de la CSF también.
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
            case TipoDeCampo.Opcion:
                v = v.ToLowerInvariant();
                return campo.Opciones?.Any(o => o.Valor == v) == true
                    ? v
                    : throw Invalido(campo, "es uno de: " + string.Join(", ", campo.Opciones?.Select(o => o.Valor) ?? []));
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
                    Opciones = c.Opciones?.Select(o => new OpcionDelCampoDto { Valor = o.Valor, Etiqueta = o.Etiqueta }).ToList(),
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
