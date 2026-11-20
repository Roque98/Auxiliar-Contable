using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.MotorFiscal;

/// <summary>Sección <c>MotorFiscal</c>. Sin <see cref="Url"/> el motor queda «sin configurar».</summary>
public class MotorFiscalOptions
{
    public const string Seccion = "MotorFiscal";

    /// <summary>En el VPS el servicio <c>motores-fiscales</c> escucha en 127.0.0.1:5090 (nunca expuesto).</summary>
    public string? Url { get; set; }

    /// <summary>La búsqueda en leyes tarda menos de un segundo; más que esto es que el motor está atorado.</summary>
    public int TimeoutSegundos { get; set; } = 20;

    /// <summary>
    /// Leer y conciliar un mes tarda más: un escaneo lo lee claude -p y eso son minutos la primera vez (después el motor
    /// lo tiene guardado). nginx da lo mismo solo a /api/app/bancos (deploy/nginx.conf).
    /// </summary>
    public int TimeoutDeBancosSegundos { get; set; } = 900;
}

/// <summary>
/// Cliente HTTP del servicio <c>motores-fiscales</c> (FastAPI, ver <c>servicio/app.py</c> en ese repo).
/// Cualquier fallo (caído, lento, 5xx) se vuelve <see cref="MotorFiscalNoDisponibleException"/>: el
/// chat lo recibe como error de herramienta y le dice a la persona que no pudo consultar.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IMotorFiscal))]
public class MotorFiscalHttp : IMotorFiscal, ITransientDependency
{
    public const string NombreHttpClient = "MotorFiscal";

    private readonly IHttpClientFactory _fabrica;
    private readonly MotorFiscalOptions _opciones;
    private readonly ILogger<MotorFiscalHttp> _log;

    /// <summary>El servicio habla snake_case (Python); los tipos de la consola, PascalCase.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public MotorFiscalHttp(IHttpClientFactory fabrica, IOptions<MotorFiscalOptions> opciones, ILogger<MotorFiscalHttp> log)
    {
        _fabrica = fabrica;
        _opciones = opciones.Value;
        _log = log;
    }

    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(_opciones.Url);

    public async Task<IReadOnlyList<ResultadoDeLey>> BuscarEnLeyesAsync(
        string consulta, string? ley = null, int cuantos = 5, int desde = 0, CancellationToken cancellationToken = default)
    {
        var ruta = $"rag/buscar?q={Uri.EscapeDataString(consulta)}&k={Math.Clamp(cuantos, 1, 50)}&desde={Math.Clamp(desde, 0, 450)}";
        if (!string.IsNullOrWhiteSpace(ley))
        {
            ruta += $"&ley={Uri.EscapeDataString(ley.Trim().ToUpperInvariant())}";
        }

        using var tope = CancelationConTope(_opciones.TimeoutSegundos, cancellationToken);
        var respuesta = await EnviarAsync<RespuestaDeBusqueda>(new HttpRequestMessage(HttpMethod.Get, ruta), tope.Token, cancellationToken);
        return respuesta.Resultados
            .Select(r => new ResultadoDeLey(r.Clave ?? "", r.Ordenamiento ?? "", r.Articulo, r.Ubicacion,
                r.UltimaReformaDof, r.NotaVigencia, r.Url, r.Texto ?? ""))
            .ToList();
    }

    public async Task<LecturaDeEstados> LeerEstadosDeCuentaAsync(
        IReadOnlyList<ArchivoParaElMotor> archivos, CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "bancos/leer")
        {
            Content = JsonContent.Create(new { archivos = archivos.Select(Base64) }, options: Json),
        };
        return await EnviarAsync<LecturaDeEstados>(peticion, tope.Token, cancellationToken);
    }

    public async Task<ResultadoDeConciliacion> ConciliarAsync(
        string rfcDelCliente, IReadOnlyList<ArchivoParaElMotor> estadosDeCuenta, IReadOnlyList<ArchivoParaElMotor> facturas,
        CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "bancos/conciliar")
        {
            Content = JsonContent.Create(new
            {
                rfc_cliente = rfcDelCliente,
                estados = estadosDeCuenta.Select(Base64),
                cfdis = facturas.Select(Base64),
            }, options: Json),
        };
        return await EnviarAsync<ResultadoDeConciliacion>(peticion, tope.Token, cancellationToken);
    }

    public async Task<ConstanciaLeida> LeerConstanciaAsync(ArchivoParaElMotor constancia, CancellationToken cancellationToken = default)
    {
        // Es texto: lo lee el método de siempre en un segundo, sin Claude.
        using var tope = CancelationConTope(_opciones.TimeoutSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "csf/leer")
        {
            Content = JsonContent.Create(new { archivo = Base64(constancia) }, options: Json),
        };
        return await EnviarAsync<ConstanciaLeida>(peticion, tope.Token, cancellationToken);
    }

    public async Task<DiagnosticoDeFacturas> DiagnosticarFacturasAsync(
        string rfcDelCliente, IReadOnlyList<ArchivoParaElMotor> facturas, int archivosDelPeriodo,
        CancellationToken cancellationToken = default)
    {
        // Es lectura de XML, sin Claude, pero pueden ser miles de archivos: el tope de bancos.
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "cfdi/diagnosticar")
        {
            Content = JsonContent.Create(new
            {
                rfc_cliente = rfcDelCliente,
                cfdis = facturas.Select(Base64),
                archivos_del_periodo = archivosDelPeriodo,
            }, options: Json),
        };
        return await EnviarAsync<DiagnosticoDeFacturas>(peticion, tope.Token, cancellationToken);
    }

    public async Task<FacturasClasificadas> ClasificarFacturasAsync(
        string rfcDelCliente, IReadOnlyList<ArchivoParaElMotor> facturas, string? giro, int archivosDelPeriodo,
        CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "cfdi/clasificar")
        {
            Content = JsonContent.Create(new
            {
                rfc_cliente = rfcDelCliente,
                cfdis = facturas.Select(Base64),
                giro,
                archivos_del_periodo = archivosDelPeriodo,
            }, options: Json),
        };
        return await EnviarAsync<FacturasClasificadas>(peticion, tope.Token, cancellationToken);
    }

    public async Task<AnalisisDeDocumento> AnalizarDocumentoAsync(
        ArchivoParaElMotor archivo, string? rfcDelCliente, string? giro, CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "documentos/analizar")
        {
            Content = JsonContent.Create(new
            {
                nombre = archivo.Nombre,
                contenido = Convert.ToBase64String(archivo.Contenido),
                rfc_cliente = rfcDelCliente,
                giro,
            }, options: Json),
        };
        return await EnviarAsync<AnalisisDeDocumento>(peticion, tope.Token, cancellationToken);
    }

    public async Task<ResultadoContable> ContabilizarAsync(
        PerfilParaContabilizar perfil, IReadOnlyList<OperacionParaContabilizar> operaciones, IReadOnlyList<ArchivoParaElMotor> facturas,
        string periodo, DatosDeCierre? cierre, CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "contabilidad/polizas")
        {
            Content = JsonContent.Create(new { perfil, operaciones, cfdis = facturas.Select(Base64), periodo, cierre }, options: Json),
        };
        return await EnviarAsync<ResultadoContable>(peticion, tope.Token, cancellationToken);
    }

    public async Task<ContabilidadElectronica> ContabilidadElectronicaAsync(
        string rfc, int anio, int mes, AperturaParaElMotor? apertura, IReadOnlyList<MesParaElMotor> meses,
        ArchivoParaElMotor? catalogo = null, CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "contabilidad/electronica")
        {
            Content = JsonContent.Create(new { rfc, anio, mes, apertura, meses, con_polizas = true, catalogo = catalogo == null ? null : Base64(catalogo) }, options: Json),
        };
        return await EnviarAsync<ContabilidadElectronica>(peticion, tope.Token, cancellationToken);
    }

    public async Task<List<GastoDeClaude>> GastoDeClaudeAsync(DateTime desde, DateTime hasta, CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Get, $"gasto/claude?desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}");
        return (await EnviarAsync<RespuestaDeGasto>(peticion, tope.Token, cancellationToken)).Renglones;
    }

    private sealed class RespuestaDeGasto
    {
        public List<GastoDeClaude> Renglones { get; set; } = [];
    }

    public async Task<AnualDelMotor> AnualAsync(
        string rfc, IReadOnlyList<string> regimenes, int anio, IReadOnlyList<ArchivoParaElMotor> facturas,
        IReadOnlyList<string> cancelados, decimal? coeficienteDeUtilidad, string? plataformas, string? actividadDePlataformas,
        IReadOnlyDictionary<string, decimal> captura, CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "impuestos/anual")
        {
            Content = JsonContent.Create(
                new
                {
                    rfc, regimenes, anio, cfdis = facturas.Select(Base64), cancelados, coeficiente = coeficienteDeUtilidad,
                    plataformas, actividad_plataformas = actividadDePlataformas, captura,
                }, options: Json),
        };
        return await EnviarAsync<AnualDelMotor>(peticion, tope.Token, cancellationToken);
    }

    public async Task<InterpretacionDelMotor> InterpretarOperacionAsync(
        PerfilParaInterpretar perfil, OperacionParaInterpretar operacion, ArchivoParaElMotor? factura, AnalisisDeDocumento? control,
        BancoParaInterpretar? banco, CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "operaciones/interpretar")
        {
            Content = JsonContent.Create(new
            {
                perfil = new
                {
                    rfc = perfil.Rfc, regimenes = perfil.Regimenes, giro = perfil.Giro, tipo_empresa = perfil.TipoEmpresa,
                    sistema_costos = perfil.SistemaCostos, sistema_inventarios = perfil.SistemaInventarios,
                },
                operacion,
                factura = factura == null ? null : Base64(factura),
                control,
                banco,
            }, options: Json),
        };
        return await EnviarAsync<InterpretacionDelMotor>(peticion, tope.Token, cancellationToken);
    }

    public async Task<string> ExpedienteNormativoAsync(PerfilParaNormativa perfil, OperacionParaNormativa operacion,
        CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "normativa/expediente")
        {
            Content = JsonContent.Create(new { perfil, operacion }, options: Json),
        };
        var r = await EnviarAsync<System.Text.Json.JsonElement>(peticion, tope.Token, cancellationToken);
        return r.GetRawText();
    }

    public async Task<string> AnalizarNormativaAsync(PerfilParaNormativa perfil, OperacionParaNormativa operacion,
        CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "normativa/analizar")
        {
            Content = JsonContent.Create(new { perfil, operacion }, options: Json),
        };
        var r = await EnviarAsync<System.Text.Json.JsonElement>(peticion, tope.Token, cancellationToken);
        return r.GetRawText();
    }

    public async Task<NominaDelMotor> CalcularNominaAsync(
        PatronParaNomina patron, PeriodoParaNomina periodo, IReadOnlyList<TrabajadorParaNomina> trabajadores,
        CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "nomina/calcular")
        {
            Content = JsonContent.Create(new
            {
                patron = new { rfc = patron.Rfc, estado = patron.Estado, cp = patron.CodigoPostal, zona = patron.Zona, prima_riesgo = patron.PrimaRiesgo },
                periodo,
                trabajadores = trabajadores.Select(t => new
                {
                    id = t.Id, nombre = t.Nombre, rfc = t.Rfc, nss = t.Nss, fecha_ingreso = t.FechaIngreso,
                    salario_diario = t.SalarioDiario, sdi = t.Sdi, dias_aguinaldo = t.DiasAguinaldo,
                    prima_vacacional_pct = t.PrimaVacacionalPct, incidencias = t.Incidencias,
                    otras_percepciones = t.OtrasPercepciones, otras_deducciones = t.OtrasDeducciones,
                    credito_infonavit = t.CreditoInfonavit, fonacot = t.Fonacot,
                }),
            }, options: Json),
        };
        return await EnviarAsync<NominaDelMotor>(peticion, tope.Token, cancellationToken);
    }

    public async Task<ArchivosDeIdse> ArchivosIdseAsync(
        string registroPatronal, string? guia, string? zona, IReadOnlyList<MovimientoParaIdse> movimientos,
        CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "nomina/idse")
        {
            Content = JsonContent.Create(new { patron = new { registro_patronal = registroPatronal, guia, zona }, movimientos }, options: Json),
        };
        return await EnviarAsync<ArchivosDeIdse>(peticion, tope.Token, cancellationToken);
    }

    public async Task<RecibosDeNomina> RecibosDeNominaAsync(
        PatronParaRecibos patron, PeriodoParaNomina periodo, string periodicidad, NominaDelMotor calculo,
        IReadOnlyList<TrabajadorParaRecibo> trabajadores, CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "nomina/recibos")
        {
            Content = JsonContent.Create(new
            {
                patron,
                periodo = new { inicio = periodo.Inicio, fin = periodo.Fin, fecha_pago = periodo.Fin, periodicidad },
                calculo,
                trabajadores,
            }, options: Json),
        };
        return await EnviarAsync<RecibosDeNomina>(peticion, tope.Token, cancellationToken);
    }

    public async Task<CuotasDeNomina> CuotasDeNominaAsync(IReadOnlyList<NominaDelMotor> calculos, CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "nomina/cuotas")
        {
            Content = JsonContent.Create(new { calculos }, options: Json),
        };
        return await EnviarAsync<CuotasDeNomina>(peticion, tope.Token, cancellationToken);
    }

    public async Task<ArchivosDeSua> ArchivosSuaAsync(
        string registroPatronal, string? zona, int anio, int mes, IReadOnlyList<TrabajadorParaSua> trabajadores,
        IReadOnlyList<MovimientoParaSua> movimientos, IReadOnlyList<IncidenciaParaSua> incidencias,
        CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "nomina/sua")
        {
            Content = JsonContent.Create(new
            {
                patron = new { registro_patronal = registroPatronal, zona }, anio, mes, trabajadores, movimientos, incidencias,
            }, options: Json),
        };
        return await EnviarAsync<ArchivosDeSua>(peticion, tope.Token, cancellationToken);
    }

    public async Task<DiotDelMotor> DiotAsync(
        string rfc, string periodo, IReadOnlyList<ArchivoParaElMotor> facturas, IReadOnlyList<string> cancelados,
        IReadOnlyList<string> regimenes, CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "diot")
        {
            Content = JsonContent.Create(new { rfc, periodo, cfdis = facturas.Select(Base64), cancelados, regimenes }, options: Json),
        };
        return await EnviarAsync<DiotDelMotor>(peticion, tope.Token, cancellationToken);
    }

    public async Task<PagosProvisionales> PagosProvisionalesAsync(
        string rfc, IReadOnlyList<string> regimenes, string periodo, string? nombre, IReadOnlyList<ArchivoParaElMotor> facturas,
        IReadOnlyList<string> cancelados, decimal? coeficienteDeUtilidad, bool conExcel, string? plataformas = null,
        string? actividadDePlataformas = null, IReadOnlyList<CobroDelBanco>? cobrosDelBanco = null,
        CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "impuestos/provisionales")
        {
            Content = JsonContent.Create(
                new
                {
                    rfc, regimenes, periodo, nombre = nombre ?? "", cfdis = facturas.Select(Base64), cancelados,
                    coeficiente = coeficienteDeUtilidad, con_excel = conExcel, plataformas,
                    actividad_plataformas = actividadDePlataformas,
                    cobros_del_banco = cobrosDelBanco ?? [],
                }, options: Json),
        };
        return await EnviarAsync<PagosProvisionales>(peticion, tope.Token, cancellationToken);
    }

    public async Task<SugerenciasDeAmarre> SugerirAmarresAsync(
        IReadOnlyList<MovimientoParaAmarrar> movimientos, IReadOnlyList<CandidataParaAmarrar> candidatas, string? giro,
        CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutDeBancosSegundos, cancellationToken);
        var peticion = new HttpRequestMessage(HttpMethod.Post, "conciliacion/sugerir")
        {
            Content = JsonContent.Create(new { movimientos, candidatas, giro }, options: Json),
        };
        return await EnviarAsync<SugerenciasDeAmarre>(peticion, tope.Token, cancellationToken);
    }

    public async Task<IReadOnlyList<SentenciaDelMotor>> BuscarSentenciasAsync(
        string consulta, string? fuente = null, int cuantos = 5, int desde = 0, CancellationToken cancellationToken = default)
    {
        var ruta = $"sentencias/buscar?q={Uri.EscapeDataString(consulta)}&k={Math.Clamp(cuantos, 1, 50)}&desde={Math.Clamp(desde, 0, 5000)}";
        if (!string.IsNullOrWhiteSpace(fuente))
        {
            ruta += $"&fuente={Uri.EscapeDataString(fuente.Trim().ToUpperInvariant())}";
        }

        using var tope = CancelationConTope(_opciones.TimeoutSegundos, cancellationToken);
        var respuesta = await EnviarAsync<RespuestaDeSentencias>(new HttpRequestMessage(HttpMethod.Get, ruta), tope.Token, cancellationToken);
        return respuesta.Resultados;
    }

    public async Task<EstadoDeSentencias> EstadoDeSentenciasAsync(CancellationToken cancellationToken = default)
    {
        using var tope = CancelationConTope(_opciones.TimeoutSegundos, cancellationToken);
        return await EnviarAsync<EstadoDeSentencias>(new HttpRequestMessage(HttpMethod.Get, "sentencias/estado"), tope.Token, cancellationToken);
    }

    public async Task<bool> ActualizarSentenciasAsync(CancellationToken cancellationToken = default)
    {
        // El motor responde al instante (el trabajo corre en su propio hilo); 409 = ya hay una en curso.
        using var tope = CancelationConTope(_opciones.TimeoutSegundos, cancellationToken);
        try
        {
            await EnviarAsync<EstadoDeSentencias>(new HttpRequestMessage(HttpMethod.Post, "sentencias/actualizar"), tope.Token, cancellationToken);
            return true;
        }
        catch (MotorFiscalNoDisponibleException ex) when (ex.Data["Codigo"] is 409)
        {
            return false;
        }
    }

    private static object Base64(ArchivoParaElMotor a) => new { nombre = a.Nombre, contenido_b64 = Convert.ToBase64String(a.Contenido) };

    /// <summary>Cada operación con su tope; el del HttpClient es solo el máximo de todos.</summary>
    private static CancellationTokenSource CancelationConTope(int segundos, CancellationToken externo)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(externo);
        cts.CancelAfter(TimeSpan.FromSeconds(segundos));
        return cts;
    }

    private async Task<T> EnviarAsync<T>(HttpRequestMessage peticion, CancellationToken cancellationToken, CancellationToken externo)
    {
        using var _ = peticion;
        var ruta = peticion.RequestUri?.OriginalString ?? "";
        if (!EstaConfigurado)
        {
            throw new MotorFiscalNoDisponibleException("Falta MotorFiscal:Url.");
        }

        var cliente = _fabrica.CreateClient(NombreHttpClient);
        cliente.BaseAddress ??= new Uri(_opciones.Url!.TrimEnd('/') + "/");
        try
        {
            using var respuesta = await cliente.SendAsync(peticion, cancellationToken);
            if (!respuesta.IsSuccessStatusCode)
            {
                var cuerpo = await respuesta.Content.ReadAsStringAsync(cancellationToken);
                _log.LogWarning("Motor fiscal {Ruta} respondió {Codigo}: {Cuerpo}", ruta.Split('?')[0], (int)respuesta.StatusCode,
                    cuerpo.Length > 300 ? cuerpo[..300] : cuerpo);
                throw new MotorFiscalNoDisponibleException($"El motor fiscal respondió {(int)respuesta.StatusCode}.")
                {
                    Data = { ["Codigo"] = (int)respuesta.StatusCode },
                };
            }

            return await respuesta.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
                   ?? throw new MotorFiscalNoDisponibleException("El motor fiscal respondió vacío.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !externo.IsCancellationRequested)
        {
            _log.LogWarning(ex, "No se pudo llamar al motor fiscal ({Ruta})", ruta.Split('?')[0]);
            throw new MotorFiscalNoDisponibleException("El motor fiscal no responde.", ex);
        }
    }

    private sealed class RespuestaDeBusqueda
    {
        [JsonPropertyName("resultados")]
        public List<ResultadoCrudo> Resultados { get; set; } = [];
    }

    private sealed class RespuestaDeSentencias
    {
        [JsonPropertyName("resultados")]
        public List<SentenciaDelMotor> Resultados { get; set; } = [];
    }

    private sealed class ResultadoCrudo
    {
        [JsonPropertyName("clave")] public string? Clave { get; set; }
        [JsonPropertyName("ordenamiento")] public string? Ordenamiento { get; set; }
        [JsonPropertyName("articulo")] public string? Articulo { get; set; }
        [JsonPropertyName("ubicacion")] public string? Ubicacion { get; set; }
        [JsonPropertyName("ultima_reforma_dof")] public string? UltimaReformaDof { get; set; }
        [JsonPropertyName("nota_vigencia")] public string? NotaVigencia { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("texto")] public string? Texto { get; set; }
    }
}
