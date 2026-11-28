using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Concentrado;
using AuxiliarContable.Documentos;
using AuxiliarContable.Files;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.LeyesDelCliente;

/// <summary>Ver <see cref="ILeyesDelClienteAppService"/>. Todo filtra por el cliente de la llamada (lo ajeno = 404).</summary>
[Authorize]
public class LeyesDelClienteAppService : AuxiliarContableAppService, ILeyesDelClienteAppService
{
    /// <summary>Claves de estado que acepta: las mismas de las tablas del ISN de la plataforma y del motor.</summary>
    public static readonly string[] Estados =
    [
        "AGS", "BC", "BCS", "CAMP", "CDMX", "CHIH", "CHIS", "COAH", "COL", "DGO", "GRO", "GTO", "HGO", "JAL", "MEX", "MICH", "MOR", "NAY",
        "NL", "OAX", "PUE", "QRO", "QROO", "SIN", "SLP", "SON", "TAB", "TAMPS", "TLAX", "VER", "YUC", "ZAC",
    ];

    private readonly IRepository<LeyDelCliente, Guid> _leyes;
    private readonly IRepository<HallazgoDeLey, Guid> _hallazgos;
    private readonly IRepository<PreguntaSobreLeyes, Guid> _preguntas;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly GestorDeDocumentos _gestor;
    private readonly Autorizador _autorizador;
    private readonly IMotorFiscal _motor;
    private readonly ProcesadorDeLeyesDelCliente _procesador;

    public LeyesDelClienteAppService(IRepository<LeyDelCliente, Guid> leyes, IRepository<HallazgoDeLey, Guid> hallazgos,
        IRepository<PreguntaSobreLeyes, Guid> preguntas, IRepository<PerfilFiscal, Guid> perfiles, GestorDeDocumentos gestor,
        Autorizador autorizador, IMotorFiscal motor, ProcesadorDeLeyesDelCliente procesador)
    {
        _leyes = leyes;
        _hallazgos = hallazgos;
        _preguntas = preguntas;
        _perfiles = perfiles;
        _gestor = gestor;
        _autorizador = autorizador;
        _motor = motor;
        _procesador = procesador;
    }

    public async Task<MisLeyesDto> GetMisLeyesAsync(Guid organizacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        var leyes = await AsyncExecuter.ToListAsync((await _leyes.GetQueryableAsync())
            .Where(x => x.OrganizacionId == organizacionId).OrderByDescending(x => x.CreationTime));
        var hallazgos = await AsyncExecuter.ToListAsync((await _hallazgos.GetQueryableAsync())
            .Where(x => x.OrganizacionId == organizacionId).OrderBy(x => x.CreationTime));
        var preguntas = await AsyncExecuter.ToListAsync((await _preguntas.GetQueryableAsync())
            .Where(x => x.OrganizacionId == organizacionId).OrderByDescending(x => x.CreationTime).Take(30));
        var perfil = await _perfiles.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId);
        var nombres = leyes.ToDictionary(l => l.Id, l => l.Titulo ?? l.Materia ?? l.NombreDelArchivo);
        return new MisLeyesDto
        {
            EsDelDespacho = (await _autorizador.QuienMiraAsync()).Equipo,
            EstadoDelCliente = IsnPorEstado.Buscar(perfil?.Dato(CamposDelPerfil.DomicilioFiscal)?.Valor)?.Clave,
            Leyes = [.. leyes.Select(Dto)],
            Hallazgos = [.. hallazgos.Where(h => nombres.ContainsKey(h.LeyId)).Select(h => Dto(h, nombres[h.LeyId]))],
            Preguntas = [.. preguntas.Select(Dto)],
            EnCurso = leyes.Any(l => l.Lectura is LecturaDeLey.Pendiente or LecturaDeLey.Leyendo
                                     || (l.Lectura == LecturaDeLey.Lista && l.HuellaAnalizada == null && l.Intentos < LeyDelCliente.MaxIntentos))
                      || preguntas.Any(p => p.Estado == EstadoDePregunta.Pendiente),
        };
    }

    public async Task<LeyDelClienteDto> SubirAsync(Guid organizacionId, IRemoteStreamContent archivo, string ambito, string? estado = null,
        string? municipio = null, string? materia = null)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        if (archivo == null)
        {
            throw new UserFriendlyException("No llegó ningún archivo.");
        }

        var amb = (ambito ?? "").Trim().ToLowerInvariant() switch
        {
            "estatal" => AmbitoDeLey.Estatal,
            "municipal" => AmbitoDeLey.Municipal,
            _ => throw new UserFriendlyException("Di si la ley es estatal o municipal."),
        };
        var edo = string.IsNullOrWhiteSpace(estado) ? null : estado.Trim().ToUpperInvariant();
        if (edo != null && !Estados.Contains(edo))
        {
            throw new UserFriendlyException("Elige el estado de la lista.");
        }

        if (amb == AmbitoDeLey.Municipal && string.IsNullOrWhiteSpace(municipio))
        {
            throw new UserFriendlyException("Di de qué municipio es la ley.");
        }

        const string demasiado = "El archivo pesa demasiado (máximo 20 MB). Si es muy grande, sube solo la parte que te aplica.";
        if (archivo.ContentLength > DocumentoConsts.MaxBytes)
        {
            throw new UserFriendlyException(demasiado);
        }

        var bytes = await LectorConTope.LeerAsync(archivo.GetStream(), DocumentoConsts.MaxBytes, demasiado);
        if (DetectorDeDocumento.Detectar(bytes) != DetectorDeDocumento.Pdf)
        {
            throw new UserFriendlyException("Sube la ley en PDF (el que publica el periódico oficial o el congreso).");
        }

        var (documento, _) = await _gestor.RecibirAsync(organizacionId, TipoDeDocumento.LeyLocal, archivo.FileName ?? "ley.pdf", bytes,
            mime => mime == DetectorDeDocumento.Pdf);
        var ya = await _leyes.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId && x.Sha256 == documento.Sha256);
        if (ya != null)
        {
            throw new UserFriendlyException("Esa ley ya está subida.");
        }

        var ley = new LeyDelCliente(GuidGenerator.Create(), organizacionId, documento.Id, documento.Sha256, documento.NombreOriginal, amb,
            edo, municipio, materia, Clock.Now.ToUniversalTime());
        await _leyes.InsertAsync(ley, autoSave: true);
        return Dto(ley);
    }

    public async Task EliminarAsync(Guid organizacionId, Guid id)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var ley = await _leyes.FirstOrDefaultAsync(x => x.Id == id && x.OrganizacionId == organizacionId)
                  ?? throw new EntityNotFoundException(typeof(LeyDelCliente), id);
        // Si el despacho ya aceptó algo de esta ley, solo el despacho la quita: deja de contar en los cálculos.
        var hallazgos = await _hallazgos.GetListAsync(x => x.LeyId == id && x.OrganizacionId == organizacionId);
        if (hallazgos.Any(h => h.Decision == DecisionDeHallazgo.Aceptado) && !(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new UserFriendlyException("El despacho ya usa valores de esta ley en tus cálculos: pídele que la quite.");
        }

        await _hallazgos.DeleteManyAsync(hallazgos, autoSave: true);
        await _leyes.DeleteAsync(ley, autoSave: true);
    }

    public async Task ReanalizarAsync(Guid organizacionId, Guid id)
    {
        await ExigirDespachoAsync(organizacionId);
        var ley = await _leyes.FirstOrDefaultAsync(x => x.Id == id && x.OrganizacionId == organizacionId)
                  ?? throw new EntityNotFoundException(typeof(LeyDelCliente), id);
        ley.Reanalizar(Clock.Now.ToUniversalTime());
        await _leyes.UpdateAsync(ley, autoSave: true);
    }

    public async Task<HallazgoDeLeyDto> DecidirAsync(Guid organizacionId, Guid id, DecidirHallazgoDto input)
    {
        await ExigirDespachoAsync(organizacionId);
        var h = await _hallazgos.FirstOrDefaultAsync(x => x.Id == id && x.OrganizacionId == organizacionId)
                ?? throw new EntityNotFoundException(typeof(HallazgoDeLey), id);
        var ahora = Clock.Now.ToUniversalTime();
        if (input.Aceptar && h.Clave != null)
        {
            if (h.ValorNumerico is not { } v || !AjustesDeLeyes.Razonable(h.Clave, v))
            {
                throw new UserFriendlyException("Ese valor no se puede usar en el cálculo: revísalo en la ley.");
            }

            // Un solo valor por clave: el que estaba aceptado queda reemplazado.
            foreach (var otro in await _hallazgos.GetListAsync(x => x.OrganizacionId == organizacionId && x.Clave == h.Clave
                                                                    && x.Decision == DecisionDeHallazgo.Aceptado && x.Id != h.Id))
            {
                otro.Reemplazar(ahora);
                await _hallazgos.UpdateAsync(otro);
            }
        }

        h.Decidir(input.Aceptar, CurrentUser.Id, ahora);
        await _hallazgos.UpdateAsync(h, autoSave: true);
        var ley = await _leyes.GetAsync(h.LeyId);
        return Dto(h, ley.Titulo ?? ley.Materia ?? ley.NombreDelArchivo);
    }

    public async Task<PreguntaSobreLeyesDto> PreguntarAsync(Guid organizacionId, PreguntarSobreLeyesDto input)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var texto = (input.Pregunta ?? "").Trim();
        if (texto.Length < 8)
        {
            throw new UserFriendlyException("Escribe la pregunta con un poco más de detalle.");
        }

        if (!await _leyes.AnyAsync(x => x.OrganizacionId == organizacionId && x.Lectura != LecturaDeLey.Error))
        {
            throw new UserFriendlyException("Sube primero la ley sobre la que quieres preguntar.");
        }

        var p = new PreguntaSobreLeyes(GuidGenerator.Create(), organizacionId, texto, Clock.Now.ToUniversalTime());
        await _preguntas.InsertAsync(p, autoSave: true);
        return Dto(p);
    }

    public async Task<LeyesQueTeAplicanDto> GetQueTeAplicanAsync(Guid organizacionId, string? estado = null, string? municipio = null)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        Dictionary<string, object?> perfil;
        try
        {
            (perfil, estado) = await _procesador.PerfilParaElMotorAsync(organizacionId, ClaveDeEstado(estado), Limpio(municipio));
        }
        catch (UserFriendlyException ex)
        {
            return new LeyesQueTeAplicanDto { Aviso = ex.Message };
        }

        if (estado == null)
        {
            return new LeyesQueTeAplicanDto { Aviso = "Falta el domicilio fiscal: con él sabemos qué leyes estatales te tocan." };
        }

        string json;
        try
        {
            json = await _motor.LeyesEstatalesDelClienteAsync(perfil, estado);
        }
        catch (MotorFiscalNoDisponibleException)
        {
            return new LeyesQueTeAplicanDto { Estado = estado, Aviso = "No pudimos consultar el catálogo de leyes ahora; intenta en unos minutos." };
        }

        var mias = (await _leyes.GetListAsync(x => x.OrganizacionId == organizacionId && x.Biblioteca != null))
            .ToDictionary(x => x.Biblioteca!, x => x.Id);
        return LeerQueTeAplican(json, estado, mias);
    }

    public async Task<LeyDelClienteDto> UsarDeLaBibliotecaAsync(Guid organizacionId, string ley)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        ley = (ley ?? "").Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(ley, "^[a-z_]{2,40}$"))
        {
            throw new UserFriendlyException("Esa ley no está en el catálogo.");
        }

        var (perfil, estado) = await _procesador.PerfilParaElMotorAsync(organizacionId, null, null);
        if (estado == null)
        {
            throw new UserFriendlyException("Falta el domicilio fiscal: con él sabemos de qué estado es la ley.");
        }

        // Las municipales (m_…) necesitan el municipio del domicilio: lo resuelve el motor (clave INEGI).
        string? municipio = null, nombreMunicipio = null;
        if (ley.StartsWith("m_", StringComparison.Ordinal))
        {
            try
            {
                using var d = JsonDocument.Parse(await _motor.LeyesEstatalesDelClienteAsync(perfil, estado));
                municipio = d.RootElement.TryGetProperty("municipio_inegi", out var mi) && mi.ValueKind == JsonValueKind.String ? mi.GetString() : null;
                nombreMunicipio = d.RootElement.TryGetProperty("municipio", out var mn) && mn.ValueKind == JsonValueKind.String ? mn.GetString() : null;
            }
            catch (Exception ex) when (ex is MotorFiscalNoDisponibleException or JsonException)
            {
                throw new UserFriendlyException("No pudimos abrir la ley ahora; intenta en unos minutos.");
            }

            if (municipio == null)
            {
                throw new UserFriendlyException("No sabemos tu municipio: revisa que tu domicilio fiscal lo diga.");
            }
        }

        var clave = municipio == null ? estado + "/" + ley : estado + "/" + municipio + "/" + ley;
        if (await _leyes.AnyAsync(x => x.OrganizacionId == organizacionId && x.Biblioteca == clave))
        {
            throw new UserFriendlyException("Esa ley ya está en tus leyes.");
        }

        LecturaDeLeyDelMotor r;
        try
        {
            r = await _motor.IndexarLeyEstatalAsync(estado, ley, municipio);
        }
        catch (MotorFiscalNoDisponibleException ex) when (ex.Data["Codigo"] is 404)
        {
            throw new UserFriendlyException("Esa ley no la tenemos todavía: súbela tú en PDF.");
        }
        catch (MotorFiscalNoDisponibleException)
        {
            throw new UserFriendlyException("No pudimos abrir la ley ahora; intenta en unos minutos.");
        }

        if (await _leyes.AnyAsync(x => x.OrganizacionId == organizacionId && x.Sha256 == r.Documento))
        {
            throw new UserFriendlyException("Esa ley ya está en tus leyes.");
        }

        var ahora = Clock.Now.ToUniversalTime();
        var nueva = LeyDelCliente.DeBiblioteca(GuidGenerator.Create(), organizacionId, estado, ley, r.Documento,
            r.NombreOficial ?? ley.Replace('_', ' '), ahora, municipio, nombreMunicipio);
        switch (r.Estado)
        {
            case "lista":
                nueva.Leida(r.Paginas, r.PaginasOcr, r.ArticulosTotal, r.NombreOficial ?? r.Titulo, ahora);
                break;
            case "error":
                nueva.NoSePudoLeer(r.Error ?? "El motor no pudo leer la ley.");
                break;
            default:
                nueva.Leyendo(r.Paginas, r.PaginasOcr, r.PaginasLeidas, ahora);
                break;
        }

        await _leyes.InsertAsync(nueva, autoSave: true);
        return Dto(nueva);
    }

    /// <summary>La respuesta del motor (<c>/leyes-estatales/del-cliente</c>) a DTO; <paramref name="mias"/>: «EDO/ley» -> id en Mis leyes.</summary>
    /// <summary>«Morelos», «Ciudad de México», «MOR» -> la clave del estado (MOR, CDMX…); null si no se reconoce.</summary>
    public static string? ClaveDeEstado(string? estado) =>
        string.IsNullOrWhiteSpace(estado) ? null : IsnPorEstado.Buscar(estado.Trim())?.Clave;

    /// <summary>El nombre del municipio que dijo la persona, sin espacios de más y con tope; null si viene vacío.</summary>
    public static string? Limpio(string? municipio)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(municipio ?? "", @"\s+", " ").Trim();
        return t.Length < 2 ? null : t.Length > 80 ? t[..80] : t;
    }

    /// <summary>«lo_tratan» del motor: [{nombre, donde, id}].</summary>
    public static List<ReglamentoQueLoTrataDto> LoTratan(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty("lo_tratan", out var v) && v.ValueKind == JsonValueKind.Array
            ? [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("nombre", out var n) && n.ValueKind == JsonValueKind.String)
                .Select(x => new ReglamentoQueLoTrataDto
                {
                    Nombre = x.GetProperty("nombre").GetString()!,
                    Donde = x.TryGetProperty("donde", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null,
                    Id = x.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null,
                })]
            : [];

    public static LeyesQueTeAplicanDto LeerQueTeAplican(string json, string estado, IReadOnlyDictionary<string, Guid> mias)
    {
        var dto = new LeyesQueTeAplicanDto { Estado = estado };
        try
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            static string? Txt(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v)
                                                           && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            static List<string> Lista(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v)
                                                                  && v.ValueKind == JsonValueKind.Array
                ? [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)]
                : [];
            dto.NombreDelEstado = Txt(r, "nombre_estado");
            dto.Municipio = Txt(r, "municipio");
            dto.CoberturaMunicipio = Txt(r, "cobertura_municipio");
            dto.AvisoMunicipal = AvisoMunicipal(dto.Municipio, dto.CoberturaMunicipio);
            var inegi = Txt(r, "municipio_inegi");
            if (r.TryGetProperty("ficha_municipio", out var fmu) && fmu.ValueKind == JsonValueKind.Object)
            {
                dto.HistoricoMunicipal = Txt(fmu, "historico");
                if (fmu.TryGetProperty("fuentes", out var fms) && fms.ValueKind == JsonValueKind.Array)
                {
                    dto.Fuentes.AddRange(fms.EnumerateArray().Select(x => new FuenteOficialDto { Url = Txt(x, "url") ?? "", Que = Txt(x, "que") ?? "" })
                        .Where(x => x.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)));
                }
            }
            dto.Disparadores = Lista(r, "disparadores");
            if (r.TryGetProperty("ficha", out var f) && f.ValueKind == JsonValueKind.Object)
            {
                dto.Historico = Txt(f, "historico");
                if (f.TryGetProperty("fuentes", out var fs) && fs.ValueKind == JsonValueKind.Array)
                {
                    dto.Fuentes = [.. fs.EnumerateArray().Select(x => new FuenteOficialDto { Url = Txt(x, "url") ?? "", Que = Txt(x, "que") ?? "" })
                        .Where(x => x.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))];
                }
            }
            else
            {
                dto.Aviso = "Todavía no tenemos el catálogo de leyes de tu estado: sube tú las que te apliquen.";
            }

            if (r.TryGetProperty("bloques", out var b) && b.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in b.EnumerateObject())
                {
                    dto.Bloques[p.Name] = p.Value.GetString() ?? p.Name;
                }
            }

            if (r.TryGetProperty("aplican", out var ap) && ap.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in ap.EnumerateArray())
                {
                    var fi = x.TryGetProperty("ficha", out var fx) ? fx : default;
                    var id = Txt(x, "id") ?? "";
                    var ambito = Txt(x, "ambito") ?? (id.StartsWith("m_", StringComparison.Ordinal) ? "municipal" : "estatal");
                    dto.Leyes.Add(new LeyQueTeAplicaDto
                    {
                        Id = id, Nombre = Txt(x, "nombre") ?? id, Bloque = Txt(x, "bloque") ?? "", Motivos = Lista(x, "motivos"),
                        Pide = Txt(x, "pide"), Estatus = Txt(fi, "estatus") ?? "sin_ficha", NombreOficial = Txt(fi, "nombre"),
                        UltimaReforma = Txt(fi, "ultima_reforma"), Url = Txt(fi, "url") is { } u && u.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? u : null,
                        Nota = Txt(fi, "nota"),
                        Disponible = fi.ValueKind == JsonValueKind.Object && fi.TryGetProperty("disponible", out var dv) && dv.ValueKind == JsonValueKind.True,
                        Anual = x.TryGetProperty("anual", out var an) && an.ValueKind == JsonValueKind.True,
                        Ambito = ambito, Clasificacion = Txt(x, "clasificacion"),
                        EnMisLeyes = mias.TryGetValue(ambito == "municipal" ? $"{estado}/{inegi}/{id}" : estado + "/" + id, out var g) ? g : null,
                        LoTratan = LoTratan(fi),
                    });
                }
            }
        }
        catch (JsonException)
        {
            dto.Aviso = "El catálogo de leyes respondió algo que no se pudo leer.";
        }

        return dto;
    }

    /// <summary>El aviso para el cliente cuyo municipio no tenemos todavía (o que no supimos).</summary>
    public static string? AvisoMunicipal(string? municipio, string? cobertura) => cobertura switch
    {
        "cubierto" => null,
        _ when string.IsNullOrWhiteSpace(municipio) =>
            "No pudimos saber tu municipio por tu domicilio fiscal: por ahora tu control abarca lo federal y lo estatal. " +
            "Si tienes los reglamentos municipales que te aplican, súbelos abajo.",
        "seleccionado" =>
            $"Estamos integrando los reglamentos de {municipio}: mientras tanto tu control abarca lo federal y lo estatal. " +
            "Si ya tienes los reglamentos municipales que te aplican, súbelos abajo.",
        "parcial" =>
            $"De {municipio} solo tenemos algunos reglamentos (por ejemplo su ley de ingresos o su bando): de lo demás tu control " +
            "abarca lo federal y lo estatal. Si tienes los reglamentos que te faltan por tu actividad, súbelos abajo.",
        _ => $"La plataforma todavía no contempla los reglamentos de {municipio}: solo tenemos conocimiento de lo federal y lo " +
             "estatal. Si tienes los reglamentos municipales que te aplican por tu actividad, súbelos abajo y se trabajará a partir de ellos.",
    };

    private async Task ExigirDespachoAsync(Guid organizacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        if (!(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new AbpAuthorizationException("Esto lo decide el despacho.");
        }
    }

    private static LeyDelClienteDto Dto(LeyDelCliente l)
    {
        List<string> noEncontrado = [], temas = [];
        if (l.Resultado != null)
        {
            try
            {
                using var d = JsonDocument.Parse(l.Resultado);
                static List<string> Lista(JsonElement r, string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array
                    ? [.. v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)]
                    : [];
                noEncontrado = Lista(d.RootElement, "no_encontrado");
                temas = Lista(d.RootElement, "temas");
            }
            catch (JsonException)
            {
                // se muestra sin eso
            }
        }

        return new LeyDelClienteDto
        {
            Id = l.Id, DocumentoId = l.DocumentoId, NombreDelArchivo = l.NombreDelArchivo, Ambito = l.Ambito == AmbitoDeLey.Estatal ? "estatal" : "municipal",
            Estado = l.Estado, Municipio = l.Municipio, Materia = l.Materia, Titulo = l.Titulo, Lectura = l.Lectura.ToString(),
            Paginas = l.Paginas, PaginasConOcr = l.PaginasConOcr, PaginasLeidas = l.PaginasLeidas, Articulos = l.Articulos,
            Analisis = l.Lectura != LecturaDeLey.Lista ? (l.Lectura == LecturaDeLey.Error ? "error" : "sin_leer")
                : l.HuellaAnalizada != null ? "analizada"
                : l.Intentos >= LeyDelCliente.MaxIntentos ? "error" : "por_analizar",
            Resumen = l.Resumen, NoEncontrado = noEncontrado, Temas = temas, AnalizadoUtc = l.AnalizadoUtc, Error = l.Error,
            CreationTime = l.CreationTime, Biblioteca = l.Biblioteca,
        };
    }

    private static HallazgoDeLeyDto Dto(HallazgoDeLey h, string ley) => new()
    {
        Id = h.Id, LeyId = h.LeyId, Tipo = h.Tipo, Tema = h.Tema, Titulo = h.Titulo, Clave = h.Clave, Valor = h.Valor, ValorNumerico = h.ValorNumerico,
        ValorDelSistema = h.ValorDelSistema, Condicion = h.Condicion, Periodicidad = h.Periodicidad, FechaLimite = h.FechaLimite,
        Articulo = h.Articulo, Etiqueta = h.Etiqueta, Pagina = h.Pagina, Cita = h.Cita, Explicacion = h.Explicacion,
        Decision = h.Decision.ToString(), DecididoUtc = h.DecididoUtc, Ley = ley,
    };

    private static PreguntaSobreLeyesDto Dto(PreguntaSobreLeyes p) => new()
    {
        Id = p.Id, Pregunta = p.Pregunta, Estado = p.Estado.ToString(), Respuesta = p.Respuesta, Error = p.Error, CreationTime = p.CreationTime,
    };
}
