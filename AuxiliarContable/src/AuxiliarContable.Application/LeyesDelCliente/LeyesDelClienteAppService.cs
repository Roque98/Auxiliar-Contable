using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Concentrado;
using AuxiliarContable.Documentos;
using AuxiliarContable.Files;
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

    public LeyesDelClienteAppService(IRepository<LeyDelCliente, Guid> leyes, IRepository<HallazgoDeLey, Guid> hallazgos,
        IRepository<PreguntaSobreLeyes, Guid> preguntas, IRepository<PerfilFiscal, Guid> perfiles, GestorDeDocumentos gestor,
        Autorizador autorizador)
    {
        _leyes = leyes;
        _hallazgos = hallazgos;
        _preguntas = preguntas;
        _perfiles = perfiles;
        _gestor = gestor;
        _autorizador = autorizador;
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
        var ya = await _leyes.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId && x.DocumentoId == documento.Id);
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
            CreationTime = l.CreationTime,
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
