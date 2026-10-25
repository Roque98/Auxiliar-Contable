using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.IA.Claude;

/// <summary>
/// <see cref="IModeloDeIA"/> con Claude, por la Messages API de Anthropic
/// (<c>POST v1/messages</c>, cabecera <c>anthropic-version: 2023-06-01</c>).
///
/// <para>La salida estructurada se fuerza con una herramienta: se declara una sola, con el
/// esquema de la solicitud como <c>input_schema</c>, y <c>tool_choice</c> obliga al modelo a
/// usarla. Lo que se devuelve es el <c>input</c> de ese bloque <c>tool_use</c>.</para>
///
/// <para>HttpClient a mano, como <c>BrevoClient</c>, y no el SDK de Anthropic para .NET: son
/// una llamada y cuatro campos, y así no entra otra dependencia que actualizar.</para>
///
/// <para>⚠️ Nada del contenido (documentos de los clientes) ni de la respuesta va al log: solo
/// propósito, modelo, tokens, duración y el <c>request-id</c> de Anthropic para soporte.</para>
/// </summary>
[ExposeServices(typeof(IModeloDeIA), typeof(IModeloConversacional), typeof(ClaudeModeloDeIA))]
public class ClaudeModeloDeIA : IModeloDeIA, IModeloConversacional, ITransientDependency
{
    public const string NombreHttpClient = "Claude";
    public const string NombreDelMedidor = "AuxiliarContable.IA";
    public const string VersionDeLaApi = "2023-06-01";

    private static readonly Meter Medidor = new(NombreDelMedidor);
    private static readonly Counter<long> Tokens = Medidor.CreateCounter<long>("auxiliar.ia.tokens", "{token}", "Tokens facturados por Anthropic");
    private static readonly Counter<long> Llamadas = Medidor.CreateCounter<long>("auxiliar.ia.llamadas", "{llamada}", "Llamadas al modelo por resultado");
    private static readonly Histogram<double> Duracion = Medidor.CreateHistogram<double>("auxiliar.ia.duracion", "s", "Duración de cada llamada, reintentos incluidos");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _fabrica;
    private readonly ClaudeOptions _opciones;
    private readonly ILogger<ClaudeModeloDeIA> _logger;

    public ClaudeModeloDeIA(IHttpClientFactory fabrica, IOptions<ClaudeOptions> opciones, ILogger<ClaudeModeloDeIA> logger)
    {
        _fabrica = fabrica;
        _opciones = opciones.Value;
        _logger = logger;
    }

    public bool EstaConfigurado => _opciones.TieneCredenciales;

    public async Task<ResultadoDeIA<T>> ExtraerAsync<T>(SolicitudDeIA solicitud, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        if (!EstaConfigurado)
        {
            throw new ErrorDeIAException(MotivoDeErrorDeIA.NoConfigurado, "Claude no está configurado (falta Claude:ApiKey).");
        }

        var cronometro = Stopwatch.StartNew();
        var resultado = "error";
        try
        {
            var respuesta = await LlamarAsync(ConstruirPeticion(solicitud), solicitud.Proposito, cancellationToken);
            var uso = respuesta.Usage?.AUso() ?? new UsoDeIA(0, 0, 0, 0);
            ContarTokens(solicitud.Proposito, respuesta.Model, uso);

            if (respuesta.StopReason == "max_tokens")
            {
                resultado = "truncada";
                throw new ErrorDeIAException(MotivoDeErrorDeIA.Truncada,
                    $"La respuesta se cortó en {uso.TokensDeSalida} tokens de salida: sube MaxTokens o parte el documento.");
            }

            var bloque = respuesta.Content?.FirstOrDefault(c => c.Type == "tool_use" && c.Name == solicitud.Esquema.Nombre);
            if (bloque?.Input is not { } entrada)
            {
                resultado = "invalida";
                throw new ErrorDeIAException(MotivoDeErrorDeIA.RespuestaInvalida,
                    $"El modelo no devolvió datos con el esquema «{solicitud.Esquema.Nombre}» (stop_reason: {respuesta.StopReason}).");
            }

            T? datos;
            try
            {
                datos = entrada.Deserialize<T>(Json);
            }
            catch (JsonException ex)
            {
                resultado = "invalida";
                // Sin el JSON en el mensaje: son los datos del documento.
                throw new ErrorDeIAException(MotivoDeErrorDeIA.RespuestaInvalida,
                    $"Los datos del modelo no encajan en {typeof(T).Name}: {ex.Path}", ex);
            }

            if (datos is null)
            {
                resultado = "invalida";
                throw new ErrorDeIAException(MotivoDeErrorDeIA.RespuestaInvalida, "El modelo devolvió null.");
            }

            resultado = "ok";
            _logger.LogInformation(
                "IA {Proposito}: {Modelo}, {Entrada}+{Salida} tokens (caché: {CacheLeidos} leídos, {CacheEscritos} escritos) en {Ms} ms.",
                solicitud.Proposito, respuesta.Model, uso.TokensDeEntrada, uso.TokensDeSalida,
                uso.TokensLeidosDeCache, uso.TokensEscritosEnCache, cronometro.ElapsedMilliseconds);

            return new ResultadoDeIA<T>(datos, respuesta.Model ?? _opciones.Modelo, uso, cronometro.Elapsed);
        }
        catch (ErrorDeIAException ex) when (ex.Motivo is MotivoDeErrorDeIA.Rechazada or MotivoDeErrorDeIA.NoDisponible)
        {
            resultado = ex.Motivo == MotivoDeErrorDeIA.Rechazada ? "rechazada" : "no-disponible";
            throw;
        }
        finally
        {
            var etiquetas = new TagList { { "proposito", solicitud.Proposito }, { "resultado", resultado } };
            Llamadas.Add(1, etiquetas);
            Duracion.Record(cronometro.Elapsed.TotalSeconds, new TagList { { "proposito", solicitud.Proposito } });
        }
    }

    public async Task<RespuestaDeConversacion> ConversarAsync(SolicitudDeConversacion solicitud, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        if (!EstaConfigurado)
        {
            throw new ErrorDeIAException(MotivoDeErrorDeIA.NoConfigurado, "Claude no está configurado (falta Claude:ApiKey).");
        }

        var cronometro = Stopwatch.StartNew();
        var resultado = "error";
        try
        {
            var respuesta = await LlamarAsync(ConstruirConversacion(solicitud), solicitud.Proposito, cancellationToken);
            var uso = respuesta.Usage?.AUso() ?? new UsoDeIA(0, 0, 0, 0);
            ContarTokens(solicitud.Proposito, respuesta.Model, uso);

            var bloques = new List<BloqueDeTurno>();
            foreach (var bloque in respuesta.Content ?? new List<BloqueDeContenido>())
            {
                if (bloque.Type == "text" && !string.IsNullOrEmpty(bloque.Text))
                {
                    bloques.Add(new BloqueDeTexto(bloque.Text));
                }
                else if (bloque.Type == "tool_use" && bloque.Id is { } id && bloque.Name is { } nombre)
                {
                    bloques.Add(new BloqueDeUsoDeHerramienta(id, nombre, bloque.Input ?? JsonDocument.Parse("{}").RootElement));
                }
            }

            resultado = respuesta.StopReason == "max_tokens" ? "truncada" : "ok";
            _logger.LogInformation(
                "IA {Proposito}: {Modelo}, {Entrada}+{Salida} tokens (caché: {CacheLeidos} leídos, {CacheEscritos} escritos) en {Ms} ms, parada {Parada}.",
                solicitud.Proposito, respuesta.Model, uso.TokensDeEntrada, uso.TokensDeSalida,
                uso.TokensLeidosDeCache, uso.TokensEscritosEnCache, cronometro.ElapsedMilliseconds, respuesta.StopReason);

            return new RespuestaDeConversacion(bloques, respuesta.StopReason, uso, respuesta.Model ?? ModeloDelChat);
        }
        catch (ErrorDeIAException ex) when (ex.Motivo is MotivoDeErrorDeIA.Rechazada or MotivoDeErrorDeIA.NoDisponible)
        {
            resultado = ex.Motivo == MotivoDeErrorDeIA.Rechazada ? "rechazada" : "no-disponible";
            throw;
        }
        finally
        {
            Llamadas.Add(1, new TagList { { "proposito", solicitud.Proposito }, { "resultado", resultado } });
            Duracion.Record(cronometro.Elapsed.TotalSeconds, new TagList { { "proposito", solicitud.Proposito } });
        }
    }

    private string ModeloDelChat => string.IsNullOrWhiteSpace(_opciones.ModeloDelChat) ? _opciones.Modelo : _opciones.ModeloDelChat;

    private JsonObject ConstruirConversacion(SolicitudDeConversacion solicitud)
    {
        var mensajes = new JsonArray();
        foreach (var turno in solicitud.Turnos)
        {
            var contenido = new JsonArray();
            foreach (var bloque in turno.Bloques)
            {
                contenido.Add(bloque switch
                {
                    BloqueDeTexto texto => new JsonObject { ["type"] = "text", ["text"] = texto.Texto },
                    BloqueDeUsoDeHerramienta uso => new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = uso.Id,
                        ["name"] = uso.Nombre,
                        ["input"] = JsonNode.Parse(uso.Entrada.GetRawText()),
                    },
                    BloqueDeResultadoDeHerramienta r => new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = r.IdDelUso,
                        ["content"] = r.Contenido,
                        ["is_error"] = r.EsError,
                    },
                    _ => throw new NotSupportedException(bloque.GetType().Name),
                });
            }

            mensajes.Add(new JsonObject
            {
                ["role"] = turno.Rol == RolDelTurno.Usuario ? "user" : "assistant",
                ["content"] = contenido,
            });
        }

        var cuerpo = new JsonObject
        {
            ["model"] = ModeloDelChat,
            ["max_tokens"] = solicitud.MaxTokens ?? _opciones.MaxTokensDelChat,
            ["system"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = solicitud.Instrucciones,
                ["cache_control"] = new JsonObject { ["type"] = "ephemeral" },
            }),
            ["messages"] = mensajes,
        };

        if (solicitud.Herramientas.Count > 0)
        {
            var herramientas = new JsonArray();
            foreach (var h in solicitud.Herramientas)
            {
                herramientas.Add(new JsonObject
                {
                    ["name"] = h.Nombre,
                    ["description"] = h.Descripcion,
                    ["input_schema"] = JsonNode.Parse(h.JsonSchema),
                });
            }

            // La caché cubre system + tools hasta la marca: se pone en la última herramienta.
            ((JsonObject)herramientas[^1]!)["cache_control"] = new JsonObject { ["type"] = "ephemeral" };
            cuerpo["tools"] = herramientas;
        }

        return cuerpo;
    }

    /// <summary>Espera entre reintentos. Virtual para que los tests no esperen de verdad.</summary>
    protected virtual Task EsperarAsync(TimeSpan espera, CancellationToken cancellationToken) =>
        Task.Delay(espera, cancellationToken);

    private async Task<RespuestaDeMensajes> LlamarAsync(JsonObject cuerpo, string proposito, CancellationToken cancellationToken)
    {
        var cliente = _fabrica.CreateClient(NombreHttpClient);
        var url = new Uri(new Uri(_opciones.BaseUrl), "v1/messages");
        var intentos = Math.Max(0, _opciones.Reintentos) + 1;

        for (var intento = 1; ; intento++)
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(cuerpo),
            };
            peticion.Headers.Add("x-api-key", _opciones.ApiKey);
            peticion.Headers.Add("anthropic-version", VersionDeLaApi);

            HttpResponseMessage respuesta;
            try
            {
                respuesta = await cliente.SendAsync(peticion, cancellationToken);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // El Timeout del HttpClient, no una cancelación de quien llama. No se reintenta
                // aquí: con un documento grande volvería a pasar, y se pagaría dos veces.
                throw new ErrorDeIAException(MotivoDeErrorDeIA.NoDisponible,
                    $"Claude no respondió en {_opciones.TimeoutSegundos} s.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new ErrorDeIAException(MotivoDeErrorDeIA.NoDisponible, $"No se pudo hablar con Claude: {ex.Message}", ex);
            }

            using (respuesta)
            {
                if (respuesta.IsSuccessStatusCode)
                {
                    return await respuesta.Content.ReadFromJsonAsync<RespuestaDeMensajes>(Json, cancellationToken)
                        ?? throw new ErrorDeIAException(MotivoDeErrorDeIA.RespuestaInvalida, "Claude devolvió un cuerpo vacío.");
                }

                var codigo = (int)respuesta.StatusCode;
                var detalle = Recortar(await respuesta.Content.ReadAsStringAsync(cancellationToken));
                var idDePeticion = respuesta.Headers.TryGetValues("request-id", out var ids) ? ids.FirstOrDefault() : null;

                if (!EsReintentable(respuesta.StatusCode))
                {
                    throw new ErrorDeIAException(MotivoDeErrorDeIA.Rechazada,
                        $"Claude respondió {codigo} (request-id {idDePeticion}): {detalle}");
                }

                if (intento >= intentos)
                {
                    throw new ErrorDeIAException(MotivoDeErrorDeIA.NoDisponible,
                        $"Claude respondió {codigo} tras {intento} intentos (request-id {idDePeticion}): {detalle}");
                }

                var espera = Espera(respuesta, intento);
                _logger.LogWarning(
                    "IA {Proposito}: Claude respondió {Codigo} (request-id {IdDePeticion}); intento {Intento} de {Intentos}, reintento en {Segundos} s.",
                    proposito, codigo, idDePeticion, intento, intentos, espera.TotalSeconds);
                await EsperarAsync(espera, cancellationToken);
            }
        }
    }

    private JsonObject ConstruirPeticion(SolicitudDeIA solicitud)
    {
        var contenido = new JsonArray();
        foreach (var parte in solicitud.Contenido)
        {
            contenido.Add(parte switch
            {
                // En el orden de quien llama. Anthropic recomienda los documentos ANTES de la pregunta.
                PdfDeContenido pdf => Fuente("document", IAConsts.TipoPdf, pdf.Datos),
                ImagenDeContenido imagen => Fuente("image", imagen.TipoMime, imagen.Datos),
                TextoDeContenido texto => new JsonObject { ["type"] = "text", ["text"] = texto.Valor },
                _ => throw new NotSupportedException(parte.GetType().Name),
            });
        }

        return new JsonObject
        {
            ["model"] = _opciones.Modelo,
            ["max_tokens"] = solicitud.MaxTokens ?? _opciones.MaxTokens,
            ["system"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = solicitud.Instrucciones,
                ["cache_control"] = new JsonObject { ["type"] = "ephemeral" },
            }),
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = contenido }),
            ["tools"] = new JsonArray(new JsonObject
            {
                ["name"] = solicitud.Esquema.Nombre,
                ["description"] = solicitud.Esquema.Descripcion,
                ["input_schema"] = JsonNode.Parse(solicitud.Esquema.JsonSchema),
            }),
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = solicitud.Esquema.Nombre },
        };
    }

    private static JsonObject Fuente(string tipo, string mime, byte[] datos) => new()
    {
        ["type"] = tipo,
        ["source"] = new JsonObject
        {
            ["type"] = "base64",
            ["media_type"] = mime,
            ["data"] = Convert.ToBase64String(datos),
        },
    };

    /// <summary>429 = límite de uso, 529 = Anthropic saturado, 5xx = caída. 408/409 también los marca la doc como reintentables.</summary>
    private static bool EsReintentable(HttpStatusCode codigo) =>
        codigo is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout or HttpStatusCode.Conflict
        || (int)codigo >= 500;

    /// <summary><c>retry-after</c> si viene (con techo de un minuto); si no, 2, 4, 8… segundos.</summary>
    private static TimeSpan Espera(HttpResponseMessage respuesta, int intento)
    {
        var pedido = respuesta.Headers.RetryAfter?.Delta;
        if (pedido is { } delta && delta > TimeSpan.Zero)
        {
            return delta < TimeSpan.FromMinutes(1) ? delta : TimeSpan.FromMinutes(1);
        }

        return TimeSpan.FromSeconds(Math.Pow(2, intento));
    }

    private void ContarTokens(string proposito, string? modelo, UsoDeIA uso)
    {
        void Sumar(string tipo, int cantidad)
        {
            if (cantidad > 0)
            {
                Tokens.Add(cantidad, new TagList { { "proposito", proposito }, { "modelo", modelo ?? _opciones.Modelo }, { "tipo", tipo } });
            }
        }

        Sumar("entrada", uso.TokensDeEntrada);
        Sumar("salida", uso.TokensDeSalida);
        Sumar("cache-leida", uso.TokensLeidosDeCache);
        Sumar("cache-escrita", uso.TokensEscritosEnCache);
    }

    private static string Recortar(string texto) => texto.Length <= 500 ? texto : texto[..500] + "…";

    private sealed class RespuestaDeMensajes
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("stop_reason")] public string? StopReason { get; set; }
        [JsonPropertyName("content")] public List<BloqueDeContenido>? Content { get; set; }
        [JsonPropertyName("usage")] public UsoDeAnthropic? Usage { get; set; }
    }

    private sealed class BloqueDeContenido
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("input")] public JsonElement? Input { get; set; }
    }

    private sealed class UsoDeAnthropic
    {
        [JsonPropertyName("input_tokens")] public int InputTokens { get; set; }
        [JsonPropertyName("output_tokens")] public int OutputTokens { get; set; }
        [JsonPropertyName("cache_read_input_tokens")] public int? CacheReadInputTokens { get; set; }
        [JsonPropertyName("cache_creation_input_tokens")] public int? CacheCreationInputTokens { get; set; }

        public UsoDeIA AUso() => new(InputTokens, OutputTokens, CacheReadInputTokens ?? 0, CacheCreationInputTokens ?? 0);
    }
}
