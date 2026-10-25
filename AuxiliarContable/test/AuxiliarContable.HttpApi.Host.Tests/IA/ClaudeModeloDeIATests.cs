using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.IA;
using AuxiliarContable.IA.Claude;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// El cliente de Claude contra una API falsa: nunca se llama a Anthropic de verdad (costaría
/// dinero y necesitaría la clave). Lo que se comprueba es la forma de la petición, la lectura de
/// la respuesta y qué se reintenta y qué no.
/// </summary>
public class ClaudeModeloDeIATests
{
    private const string Clave = "sk-ant-clave-de-prueba";

    private const string Esquema = """
        { "type": "object",
          "properties": { "banco": { "type": "string" }, "saldoFinal": { "type": "number" } },
          "required": ["banco", "saldoFinal"] }
        """;

    private sealed class Lectura
    {
        public string Banco { get; set; } = string.Empty;
        public decimal SaldoFinal { get; set; }
    }

    private static SolicitudDeIA Solicitud(params ParteDeContenido[] contenido) => new(
        "estado-de-cuenta",
        "Lee el estado de cuenta.",
        new EsquemaDeSalida("registrar_estado_de_cuenta", "Datos del estado de cuenta", Esquema),
        contenido.Length > 0 ? contenido : [ParteDeContenido.Texto("¿Qué banco es?")]);

    private static string RespuestaOk(string stopReason = "tool_use", string? input = null) => $$"""
        { "id": "msg_1", "type": "message", "role": "assistant", "model": "claude-haiku-5-5-20261001",
          "stop_reason": "{{stopReason}}",
          "content": [
            { "type": "text", "text": "Aquí van los datos." },
            { "type": "tool_use", "id": "toolu_1", "name": "registrar_estado_de_cuenta",
              "input": {{input ?? """{ "banco": "BBVA", "saldoFinal": 1234.56 }"""}} } ],
          "usage": { "input_tokens": 1500, "output_tokens": 80, "cache_read_input_tokens": 1000, "cache_creation_input_tokens": 0 } }
        """;

    [Fact]
    public async Task Fuerza_la_herramienta_del_esquema_y_manda_imagenes_y_pdf_en_base64()
    {
        var api = new ApiFalsa(Respuesta(HttpStatusCode.OK, RespuestaOk()));
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7 falso");
        var foto = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };

        await Modelo(api).ExtraerAsync<Lectura>(Solicitud(
            ParteDeContenido.Pdf(pdf),
            ParteDeContenido.Imagen(foto, "IMAGE/JPEG"),
            ParteDeContenido.Texto("Extrae los datos.")));

        var peticion = api.Peticiones.ShouldHaveSingleItem();
        peticion.Url.ShouldBe("https://api.anthropic.com/v1/messages");
        peticion.Cabeceras["x-api-key"].ShouldBe(Clave);
        peticion.Cabeceras["anthropic-version"].ShouldBe("2023-06-01");

        var cuerpo = peticion.Cuerpo.RootElement;
        cuerpo.GetProperty("model").GetString().ShouldBe("claude-haiku-5-5");
        cuerpo.GetProperty("max_tokens").GetInt32().ShouldBe(8192);
        cuerpo.GetProperty("tool_choice").GetProperty("type").GetString().ShouldBe("tool");
        cuerpo.GetProperty("tool_choice").GetProperty("name").GetString().ShouldBe("registrar_estado_de_cuenta");

        var herramienta = cuerpo.GetProperty("tools").EnumerateArray().ShouldHaveSingleItem();
        herramienta.GetProperty("input_schema").GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()).ShouldBe(["banco", "saldoFinal"]);

        var sistema = cuerpo.GetProperty("system").EnumerateArray().ShouldHaveSingleItem();
        sistema.GetProperty("text").GetString().ShouldBe("Lee el estado de cuenta.");
        sistema.GetProperty("cache_control").GetProperty("type").GetString().ShouldBe("ephemeral");

        var partes = cuerpo.GetProperty("messages")[0].GetProperty("content").EnumerateArray().ToList();
        partes.Select(p => p.GetProperty("type").GetString()).ShouldBe(["document", "image", "text"]);
        partes[0].GetProperty("source").GetProperty("media_type").GetString().ShouldBe("application/pdf");
        partes[0].GetProperty("source").GetProperty("data").GetString().ShouldBe(Convert.ToBase64String(pdf));
        partes[1].GetProperty("source").GetProperty("media_type").GetString().ShouldBe("image/jpeg");
        partes[1].GetProperty("source").GetProperty("data").GetString().ShouldBe(Convert.ToBase64String(foto));
    }

    [Fact]
    public async Task Devuelve_los_datos_de_la_herramienta_y_los_tokens()
    {
        var api = new ApiFalsa(Respuesta(HttpStatusCode.OK, RespuestaOk()));

        var resultado = await Modelo(api).ExtraerAsync<Lectura>(Solicitud());

        resultado.Datos.Banco.ShouldBe("BBVA");
        resultado.Datos.SaldoFinal.ShouldBe(1234.56m);
        resultado.Modelo.ShouldBe("claude-haiku-5-5-20261001");
        resultado.Uso.ShouldBe(new UsoDeIA(1500, 80, 1000, 0));
    }

    [Fact]
    public async Task Sin_clave_no_llama_a_nadie()
    {
        var api = new ApiFalsa();
        var modelo = Modelo(api, o => o.ApiKey = " ");

        modelo.EstaConfigurado.ShouldBeFalse();
        var error = await Should.ThrowAsync<ErrorDeIAException>(() => modelo.ExtraerAsync<Lectura>(Solicitud()));

        error.Motivo.ShouldBe(MotivoDeErrorDeIA.NoConfigurado);
        api.Peticiones.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData((HttpStatusCode)529)]
    public async Task Reintenta_la_saturacion_y_respeta_el_retry_after(HttpStatusCode codigo)
    {
        var saturado = Respuesta(codigo, """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""");
        saturado.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
        var api = new ApiFalsa(saturado, Respuesta(HttpStatusCode.OK, RespuestaOk()));
        var modelo = Modelo(api);

        var resultado = await modelo.ExtraerAsync<Lectura>(Solicitud());

        resultado.Datos.Banco.ShouldBe("BBVA");
        api.Peticiones.Count.ShouldBe(2);
        modelo.Esperas.ShouldBe([TimeSpan.FromSeconds(7)]);
    }

    [Fact]
    public async Task Tras_agotar_los_reintentos_avisa_que_se_puede_reintentar_mas_tarde()
    {
        var api = new ApiFalsa(Enumerable.Range(0, 3).Select(_ => Respuesta((HttpStatusCode)529, "{}")).ToArray());
        var modelo = Modelo(api, o => o.Reintentos = 2);

        var error = await Should.ThrowAsync<ErrorDeIAException>(() => modelo.ExtraerAsync<Lectura>(Solicitud()));

        error.Motivo.ShouldBe(MotivoDeErrorDeIA.NoDisponible);
        error.EsReintentable.ShouldBeTrue();
        api.Peticiones.Count.ShouldBe(3);
        modelo.Esperas.ShouldBe([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)]);
    }

    [Fact]
    public async Task Un_rechazo_no_se_reintenta_y_el_error_no_lleva_la_clave()
    {
        var api = new ApiFalsa(Respuesta(HttpStatusCode.BadRequest,
            """{"type":"error","error":{"type":"invalid_request_error","message":"image too large"}}"""));

        var error = await Should.ThrowAsync<ErrorDeIAException>(() => Modelo(api).ExtraerAsync<Lectura>(Solicitud()));

        error.Motivo.ShouldBe(MotivoDeErrorDeIA.Rechazada);
        error.EsReintentable.ShouldBeFalse();
        error.Message.ShouldContain("400");
        error.Message.ShouldContain("image too large");
        error.Message.ShouldNotContain(Clave);
        api.Peticiones.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Una_respuesta_cortada_por_el_tope_de_tokens_no_se_da_por_buena()
    {
        var api = new ApiFalsa(Respuesta(HttpStatusCode.OK, RespuestaOk("max_tokens")));

        var error = await Should.ThrowAsync<ErrorDeIAException>(() => Modelo(api).ExtraerAsync<Lectura>(Solicitud()));

        error.Motivo.ShouldBe(MotivoDeErrorDeIA.Truncada);
    }

    [Fact]
    public async Task Datos_que_no_encajan_en_el_tipo_son_respuesta_invalida_sin_filtrar_el_contenido()
    {
        var api = new ApiFalsa(Respuesta(HttpStatusCode.OK,
            RespuestaOk(input: """{ "banco": "BBVA", "saldoFinal": "RFC XAXX010101000 no es un número" }""")));

        var error = await Should.ThrowAsync<ErrorDeIAException>(() => Modelo(api).ExtraerAsync<Lectura>(Solicitud()));

        error.Motivo.ShouldBe(MotivoDeErrorDeIA.RespuestaInvalida);
        error.Message.ShouldNotContain("XAXX010101000");
    }

    [Fact]
    public async Task Sin_bloque_de_la_herramienta_es_respuesta_invalida()
    {
        var api = new ApiFalsa(Respuesta(HttpStatusCode.OK, """
            { "model": "claude-haiku-5-5", "stop_reason": "end_turn",
              "content": [ { "type": "text", "text": "No puedo leer esto." } ],
              "usage": { "input_tokens": 10, "output_tokens": 5 } }
            """));

        var error = await Should.ThrowAsync<ErrorDeIAException>(() => Modelo(api).ExtraerAsync<Lectura>(Solicitud()));

        error.Motivo.ShouldBe(MotivoDeErrorDeIA.RespuestaInvalida);
    }

    [Fact]
    public async Task El_tope_de_tokens_de_la_solicitud_manda_sobre_la_configuracion()
    {
        var api = new ApiFalsa(Respuesta(HttpStatusCode.OK, RespuestaOk()));
        var solicitud = new SolicitudDeIA("conciliacion", "Decide.",
            new EsquemaDeSalida("registrar_estado_de_cuenta", "Decisión", Esquema),
            [ParteDeContenido.Texto("caso")]) { MaxTokens = 500 };

        await Modelo(api).ExtraerAsync<Lectura>(solicitud);

        api.Peticiones.ShouldHaveSingleItem().Cuerpo.RootElement.GetProperty("max_tokens").GetInt32().ShouldBe(500);
    }

    [Fact]
    public async Task El_chat_manda_el_historial_con_herramientas_y_lee_texto_y_usos()
    {
        var api = new ApiFalsa(Respuesta(HttpStatusCode.OK, """
            { "model": "claude-haiku-5-5", "stop_reason": "tool_use",
              "content": [
                { "type": "text", "text": "Déjame revisar." },
                { "type": "tool_use", "id": "toolu_9", "name": "consultar_perfil", "input": {} } ],
              "usage": { "input_tokens": 300, "output_tokens": 20 } }
            """));
        var entrada = JsonDocument.Parse("""{"campo":"giro"}""").RootElement;
        var solicitud = new SolicitudDeConversacion("chat", "Eres el asistente.",
        [
            TurnoDeConversacion.DelUsuario("Hola"),
            TurnoDeConversacion.DelAsistente([new BloqueDeUsoDeHerramienta("toolu_1", "consultar_perfil", entrada)]),
            TurnoDeConversacion.DelUsuario(new BloqueDeResultadoDeHerramienta("toolu_1", "sin datos", EsError: true)),
        ],
        [new HerramientaDelModelo("consultar_perfil", "Lee el perfil", """{"type":"object","properties":{}}""")]);

        var respuesta = await Modelo(api, o => o.ModeloDelChat = "claude-sonnet-5-5").ConversarAsync(solicitud);

        var cuerpo = api.Peticiones.ShouldHaveSingleItem().Cuerpo.RootElement;
        cuerpo.GetProperty("model").GetString().ShouldBe("claude-sonnet-5-5");
        cuerpo.GetProperty("max_tokens").GetInt32().ShouldBe(2048);
        cuerpo.TryGetProperty("tool_choice", out _).ShouldBeFalse("en el chat el modelo decide si usa herramientas");
        cuerpo.GetProperty("tools")[0].GetProperty("cache_control").GetProperty("type").GetString().ShouldBe("ephemeral");

        var mensajes = cuerpo.GetProperty("messages").EnumerateArray().ToList();
        mensajes.Select(m => m.GetProperty("role").GetString()).ShouldBe(["user", "assistant", "user"]);
        var uso = mensajes[1].GetProperty("content")[0];
        uso.GetProperty("type").GetString().ShouldBe("tool_use");
        uso.GetProperty("input").GetProperty("campo").GetString().ShouldBe("giro");
        var resultado = mensajes[2].GetProperty("content")[0];
        resultado.GetProperty("type").GetString().ShouldBe("tool_result");
        resultado.GetProperty("tool_use_id").GetString().ShouldBe("toolu_1");
        resultado.GetProperty("is_error").GetBoolean().ShouldBeTrue();

        respuesta.Texto.ShouldBe("Déjame revisar.");
        var pedido = respuesta.UsosDeHerramientas.ShouldHaveSingleItem();
        pedido.Id.ShouldBe("toolu_9");
        pedido.Nombre.ShouldBe("consultar_perfil");
        respuesta.MotivoDeParada.ShouldBe("tool_use");
    }

    [Fact]
    public void Los_bloques_se_guardan_y_se_leen_igual()
    {
        var bloques = new BloqueDeTurno[]
        {
            new BloqueDeTexto("hola"),
            new BloqueDeUsoDeHerramienta("u1", "guardar", JsonDocument.Parse("""{"valor":"Panadería"}""").RootElement),
            new BloqueDeResultadoDeHerramienta("u1", "ok"),
        };

        var leidos = BloqueDeTurno.Deserializar(BloqueDeTurno.Serializar(bloques));

        leidos[0].ShouldBe(new BloqueDeTexto("hola"));
        leidos[1].ShouldBeOfType<BloqueDeUsoDeHerramienta>().Entrada.GetProperty("valor").GetString().ShouldBe("Panadería");
        leidos[2].ShouldBe(new BloqueDeResultadoDeHerramienta("u1", "ok"));
    }

    // --- Andamiaje -------------------------------------------------------------------------

    private static ModeloSinEsperas Modelo(ApiFalsa api, Action<ClaudeOptions>? ajustar = null)
    {
        var opciones = new ClaudeOptions { ApiKey = Clave };
        ajustar?.Invoke(opciones);
        return new ModeloSinEsperas(new Fabrica(api), Options.Create(opciones));
    }

    private static HttpResponseMessage Respuesta(HttpStatusCode codigo, string json) =>
        new(codigo) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Apunta las esperas en vez de dormir.</summary>
    private sealed class ModeloSinEsperas(IHttpClientFactory fabrica, IOptions<ClaudeOptions> opciones)
        : ClaudeModeloDeIA(fabrica, opciones, NullLogger<ClaudeModeloDeIA>.Instance)
    {
        public List<TimeSpan> Esperas { get; } = [];

        protected override Task EsperarAsync(TimeSpan espera, CancellationToken cancellationToken)
        {
            Esperas.Add(espera);
            return Task.CompletedTask;
        }
    }

    private sealed record PeticionVista(string Url, Dictionary<string, string> Cabeceras, JsonDocument Cuerpo);

    private sealed class ApiFalsa(params HttpResponseMessage[] respuestas) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _respuestas = new(respuestas);

        public List<PeticionVista> Peticiones { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cuerpo = await request.Content!.ReadAsStringAsync(cancellationToken);
            Peticiones.Add(new PeticionVista(
                request.RequestUri!.ToString(),
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                JsonDocument.Parse(cuerpo)));
            return _respuestas.Count > 0 ? _respuestas.Dequeue() : throw new InvalidOperationException("La API falsa se quedó sin respuestas.");
        }
    }

    private sealed class Fabrica(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
