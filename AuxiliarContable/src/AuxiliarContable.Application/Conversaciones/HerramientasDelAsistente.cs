using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using System.Threading.Tasks;
using AuxiliarContable.Documentos;
using AuxiliarContable.IA;
using AuxiliarContable.PerfilesFiscales;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace AuxiliarContable.Conversaciones;

/// <summary>
/// Lo que el asistente puede HACER. Cada herramienta llama al mismo servicio que usaría la
/// pantalla, con los permisos de quien escribe: el modelo no tiene más poder que la persona.
/// El cliente activo lo pone el código, nunca el modelo (no hay «organizacionId» en ningún esquema).
///
/// <para>Las validaciones son las de siempre (<see cref="PerfilFiscalAppService.Normalizar"/>):
/// si el modelo manda algo mal, recibe el error como resultado y se lo pregunta a la persona.</para>
/// </summary>
public class HerramientasDelAsistente : ITransientDependency
{
    public const string ConsultarPerfil = "consultar_perfil";
    public const string GuardarDato = "guardar_dato_del_perfil";
    public const string ConfirmarDato = "confirmar_dato_del_perfil";
    public const string ListarDocumentos = "listar_documentos";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private readonly IPerfilFiscalAppService _perfiles;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IAsyncQueryableExecuter _ejecutor;

    public HerramientasDelAsistente(
        IPerfilFiscalAppService perfiles, IRepository<Documento, Guid> documentos, IAsyncQueryableExecuter ejecutor)
    {
        _perfiles = perfiles;
        _documentos = documentos;
        _ejecutor = ejecutor;
    }

    /// <summary>Las que puede usar quien escribe: sin permiso de cambiar, solo las de consulta.</summary>
    public static IReadOnlyList<HerramientaDelModelo> Disponibles(bool puedeEditar)
    {
        var lista = new List<HerramientaDelModelo>
        {
            new(ConsultarPerfil,
                "Devuelve el perfil fiscal del cliente activo: cada dato con su valor, de dónde salió (constancia, cliente, despacho) " +
                "y si ya está confirmado, más los avisos de la última constancia. Úsala antes de preguntar algo para no repetir preguntas.",
                """{"type":"object","properties":{}}"""),
            new(ListarDocumentos,
                "Lista los documentos que el cliente activo ha subido (los más recientes primero): tipo, nombre, fecha y estado.",
                """{"type":"object","properties":{"limite":{"type":"integer","minimum":1,"maximum":50}}}"""),
        };

        if (puedeEditar)
        {
            var claves = string.Join(",", CamposDelPerfil.Todos.Select(c => $"\"{c.Clave}\""));
            lista.Add(new(GuardarDato,
                "Guarda un dato del perfil que la persona te dijo. Queda confirmado y con su origen. Formatos: fechas AAAA-MM-DD; " +
                "sí/no como «si» o «no»; listas (bancos, regímenes…) como arreglo JSON en texto, p. ej. [\"BBVA\",\"Banorte\"]; " +
                "tipo_persona «fisica» o «moral». Si devuelve error, explícaselo a la persona y pregunta de nuevo.",
                $$$"""{"type":"object","properties":{"campo":{"type":"string","enum":[{{{claves}}}]},"valor":{"type":"string"}},"required":["campo","valor"]}"""));
            lista.Add(new(ConfirmarDato,
                "Marca como correcto un dato que ya está en el perfil (extraído de la constancia o inferido) SOLO cuando la persona " +
                "te dijo explícitamente que es correcto.",
                $$$"""{"type":"object","properties":{"campo":{"type":"string","enum":[{{{claves}}}]}},"required":["campo"]}"""));
        }

        return lista;
    }

    /// <summary>Etiqueta corta de lo que hizo una herramienta, para enseñarla en la conversación.</summary>
    public static string? Describir(BloqueDeUsoDeHerramienta uso)
    {
        string Etiqueta() =>
            uso.Entrada.ValueKind == JsonValueKind.Object && uso.Entrada.TryGetProperty("campo", out var c) && c.GetString() is { } clave
                ? CamposDelPerfil.Buscar(clave)?.Etiqueta ?? clave
                : "?";

        return uso.Nombre switch
        {
            GuardarDato => $"Guardó: {Etiqueta()}",
            ConfirmarDato => $"Confirmó: {Etiqueta()}",
            _ => null,
        };
    }

    public async Task<BloqueDeResultadoDeHerramienta> EjecutarAsync(Guid organizacionId, BloqueDeUsoDeHerramienta uso)
    {
        try
        {
            var contenido = uso.Nombre switch
            {
                ConsultarPerfil => Perfil(await _perfiles.GetAsync(organizacionId)),
                ListarDocumentos => await DocumentosAsync(organizacionId, Entero(uso.Entrada, "limite") ?? 20),
                GuardarDato => await GuardarAsync(organizacionId, uso.Entrada),
                ConfirmarDato => await ConfirmarAsync(organizacionId, uso.Entrada),
                _ => throw new UserFriendlyException($"No existe la herramienta «{uso.Nombre}»."),
            };
            return new BloqueDeResultadoDeHerramienta(uso.Id, contenido);
        }
        catch (UserFriendlyException ex)
        {
            return new BloqueDeResultadoDeHerramienta(uso.Id, ex.Message, EsError: true);
        }
        catch (BusinessException ex)
        {
            // El código y los datos bastan para que el modelo lo explique; no hay datos de otros clientes.
            var datos = string.Join(", ", ex.Data.Keys.Cast<object>().Select(k => $"{k}: {ex.Data[k]}"));
            return new BloqueDeResultadoDeHerramienta(uso.Id, $"Error {ex.Code}. {datos}".Trim(), EsError: true);
        }
    }

    private async Task<string> GuardarAsync(Guid organizacionId, JsonElement entrada)
    {
        var campo = Texto(entrada, "campo");
        var valor = Texto(entrada, "valor");
        var perfil = await _perfiles.ResponderAsync(organizacionId, new ResponderDatoDelPerfilDto { Campo = campo, Valor = valor });
        var guardado = perfil.Campos.First(c => c.Clave == campo);
        return $"Guardado. {guardado.Etiqueta} = {guardado.Valor}";
    }

    private async Task<string> ConfirmarAsync(Guid organizacionId, JsonElement entrada)
    {
        var campo = Texto(entrada, "campo");
        var perfil = await _perfiles.ConfirmarAsync(organizacionId, campo);
        var dato = perfil.Campos.First(c => c.Clave == campo);
        return $"Confirmado. {dato.Etiqueta} = {dato.Valor}";
    }

    private static string Perfil(PerfilFiscalDto perfil)
    {
        var json = new JsonObject
        {
            ["datos"] = new JsonArray(perfil.Campos.Select(c => (JsonNode)new JsonObject
            {
                ["campo"] = c.Clave,
                ["etiqueta"] = c.Etiqueta,
                ["valor"] = c.Valor,
                ["origen"] = c.Origen?.ToString(),
                ["confirmado"] = c.Confirmado,
            }).ToArray()),
            ["faltan"] = new JsonArray(perfil.Campos.Where(c => c.Valor == null).Select(c => (JsonNode)c.Clave).ToArray()),
            ["constancia"] = perfil.Csf == null
                ? "No ha subido su Constancia de Situación Fiscal."
                : new JsonObject
                {
                    ["estado"] = perfil.Csf.Estado.ToString(),
                    ["subida"] = perfil.Csf.SubidaUtc.ToString("yyyy-MM-dd"),
                    ["motivo"] = perfil.Csf.Motivo,
                    ["avisos"] = new JsonArray(perfil.Csf.Avisos.Select(a => (JsonNode)a).ToArray()),
                },
        };
        return json.ToJsonString(Json);
    }

    private async Task<string> DocumentosAsync(Guid organizacionId, int limite)
    {
        var consulta = (await _documentos.GetQueryableAsync())
            .Where(d => d.OrganizacionId == organizacionId)
            .OrderByDescending(d => d.CreationTime)
            .Take(Math.Clamp(limite, 1, 50))
            .Select(d => new { d.Tipo, d.NombreOriginal, d.CreationTime, d.Estado });
        var lista = await _ejecutor.ToListAsync(consulta);
        if (lista.Count == 0)
        {
            return "No ha subido ningún documento.";
        }

        return JsonSerializer.Serialize(lista.Select(d => new
        {
            tipo = d.Tipo.ToString(),
            nombre = d.NombreOriginal,
            subido = d.CreationTime.ToString("yyyy-MM-dd"),
            estado = d.Estado.ToString(),
        }), Json);
    }

    private static string Texto(JsonElement entrada, string propiedad) =>
        entrada.ValueKind == JsonValueKind.Object && entrada.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : throw new UserFriendlyException($"Falta «{propiedad}».");

    private static int? Entero(JsonElement entrada, string propiedad) =>
        entrada.ValueKind == JsonValueKind.Object && entrada.TryGetProperty(propiedad, out var v) && v.TryGetInt32(out var n) ? n : null;
}
