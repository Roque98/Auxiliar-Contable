using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Documentos;
using AuxiliarContable.IA;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Timing;

namespace AuxiliarContable.PerfilesFiscales;

/// <summary>
/// Lee una CSF y propone sus datos al perfil del cliente. Lo llama el trabajo en segundo
/// plano (<c>LeerCsfJob</c>), dentro de su unidad de trabajo.
///
/// <para>Reparto: el CÓDIGO lee los identificadores de formato fijo (<see cref="LectorDeCsf"/>)
/// y valida; el MODELO lee las tablas (regímenes, obligaciones, actividades) y el nombre, que
/// en el texto extraído del PDF salen en un orden poco fiable. Si el modelo y el código no
/// coinciden en un identificador, gana el código y queda un aviso.</para>
///
/// <para>Nada se da por bueno: lo extraído queda «sin confirmar» hasta que el cliente o el
/// despacho lo confirme, y nunca pisa un dato ya confirmado (<see cref="PerfilFiscal.Proponer"/>).</para>
/// </summary>
public class ProcesadorDeCsf : ITransientDependency
{
    /// <summary>La visión pide señalar una CSF de más de ~3 meses.</summary>
    public const int DiasDeVigenciaRecomendada = 90;

    public const string Proposito = "csf";

    /// <summary>Acentos y «ñ» tal cual en el JSON guardado (legible en la base); se siguen escapando &lt;, &gt; y &amp;.</summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly IModeloDeIA _modelo;
    private readonly IClock _reloj;
    private readonly IGuidGenerator _guids;
    private readonly ILogger<ProcesadorDeCsf> _logger;

    public ProcesadorDeCsf(
        IRepository<Documento, Guid> documentos,
        IRepository<PerfilFiscal, Guid> perfiles,
        AlmacenDeDocumentos almacen,
        IModeloDeIA modelo,
        IClock reloj,
        IGuidGenerator guids,
        ILogger<ProcesadorDeCsf> logger)
    {
        _documentos = documentos;
        _perfiles = perfiles;
        _almacen = almacen;
        _modelo = modelo;
        _reloj = reloj;
        _guids = guids;
        _logger = logger;
    }

    public async Task ProcesarAsync(Guid documentoId)
    {
        var documento = await _documentos.FindAsync(documentoId);
        if (documento == null)
        {
            // Se borró mientras esperaba en la cola: nada que hacer.
            return;
        }

        var perfil = await _perfiles.FirstOrDefaultAsync(p => p.OrganizacionId == documento.OrganizacionId)
                     ?? await _perfiles.InsertAsync(new PerfilFiscal(_guids.Create(), documento.OrganizacionId), autoSave: true);

        var avisos = new List<string>();
        var contenido = await _almacen.LeerAsync(documento);
        var texto = LectorDePdf.Texto(contenido);
        var ids = LectorDeCsf.Leer(texto);

        LecturaDelModelo? delModelo = null;
        if (_modelo.EstaConfigurado)
        {
            try
            {
                delModelo = (await _modelo.ExtraerAsync<LecturaDelModelo>(Solicitud(texto, contenido))).Datos;
            }
            catch (ErrorDeIAException ex) when (!ex.EsReintentable)
            {
                // Lo reintentable (saturación, caída) se deja subir: el trabajo lo reintenta.
                _logger.LogWarning("CSF {DocumentoId}: el modelo no la pudo leer ({Motivo}).", documentoId, ex.Motivo);
                avisos.Add("No pude leer las tablas de la constancia (regímenes, obligaciones); revísalas a mano.");
            }
        }
        else
        {
            avisos.Add("La lectura con IA no está activa: solo se leyeron RFC, CURP, código postal y fecha.");
        }

        if (!ids.PareceCsf && delModelo is not { EsConstancia: true })
        {
            documento.MarcarConError("No parece una Constancia de Situación Fiscal.");
            perfil.TerminarLecturaDeCsf(documento.Id, ["El archivo no parece una Constancia de Situación Fiscal. Descárgala del portal del SAT y súbela de nuevo."]);
            return;
        }

        var ahora = _reloj.Now;
        var propuestos = Combinar(ids, delModelo, ahora, avisos);
        foreach (var (campo, valor) in propuestos)
        {
            if (!perfil.Proponer(campo, valor, OrigenDelDato.ExtraidoDeCsf, documento.Id, ahora))
            {
                var etiqueta = CamposDelPerfil.Buscar(campo)!.Etiqueta;
                avisos.Add($"«{etiqueta}» en la constancia no coincide con el que ya confirmaste; lo dejé como estaba. Revísalo.");
            }
        }

        documento.MarcarProcesado();
        perfil.TerminarLecturaDeCsf(documento.Id, avisos);
    }

    /// <summary>Junta código y modelo, valida y decide qué se propone. Público para probarlo sin base de datos.</summary>
    public static List<(string Campo, string Valor)> Combinar(IdentificadoresDeCsf ids, LecturaDelModelo? m, DateTime ahoraUtc, List<string> avisos)
    {
        var datos = new List<(string, string)>();

        // --- Identificadores: manda el código ---------------------------------------------
        var rfc = ids.Rfc ?? (Sat.EsRfc(m?.Rfc?.Trim().ToUpperInvariant()) ? m!.Rfc!.Trim().ToUpperInvariant() : null);
        if (ids.Rfc != null && m?.Rfc != null && !string.Equals(m.Rfc.Trim(), ids.Rfc, StringComparison.OrdinalIgnoreCase))
        {
            avisos.Add("El RFC se leyó de dos formas distintas; me quedé con el que aparece junto a la etiqueta «RFC». Confírmalo.");
        }

        if (rfc != null)
        {
            datos.Add((CamposDelPerfil.Rfc, rfc));
            datos.Add((CamposDelPerfil.TipoDePersona, rfc.Length == 12 ? "moral" : "fisica"));
            if (!Sat.DigitoVerificadorCuadra(rfc))
            {
                avisos.Add("El dígito verificador del RFC no cuadra; revisa que esté bien escrito.");
            }
        }
        else
        {
            avisos.Add("No encontré el RFC en la constancia.");
        }

        var curp = ids.Curp ?? (Sat.EsCurp(m?.Curp?.Trim().ToUpperInvariant()) ? m!.Curp!.Trim().ToUpperInvariant() : null);
        if (curp != null && rfc?.Length == 13)
        {
            datos.Add((CamposDelPerfil.Curp, curp));
        }

        var cp = ids.CodigoPostal ?? (m?.CodigoPostal is { Length: 5 } c && c.All(char.IsDigit) ? c : null);
        if (cp != null)
        {
            datos.Add((CamposDelPerfil.CodigoPostal, cp));
        }

        var emision = ids.FechaDeEmision ?? Fecha(m?.FechaDeEmision);
        if (emision is { } fecha)
        {
            datos.Add((CamposDelPerfil.FechaDeEmisionDeLaCsf, fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            var dias = DateOnly.FromDateTime(ahoraUtc).DayNumber - fecha.DayNumber;
            if (dias > DiasDeVigenciaRecomendada)
            {
                avisos.Add($"Tu constancia es del {fecha:dd/MM/yyyy}: tiene más de 3 meses. Conviene descargar una reciente del portal del SAT.");
            }
        }

        if (m == null)
        {
            return datos;
        }

        // --- Lo que lee el modelo: se valida forma, no se inventa ---------------------------
        Agregar(datos, CamposDelPerfil.Nombre, m.Nombre);
        Agregar(datos, CamposDelPerfil.DomicilioFiscal, m.Domicilio);
        Agregar(datos, CamposDelPerfil.EstatusEnElPadron, m.EstatusEnElPadron);
        if (Fecha(m.FechaInicioDeOperaciones) is { } inicio)
        {
            datos.Add((CamposDelPerfil.FechaInicioDeOperaciones, inicio.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        var regimenes = new List<object>();
        foreach (var r in m.Regimenes ?? [])
        {
            var clave = r.Clave?.Trim();
            if (clave == null || !Sat.Regimenes.TryGetValue(clave, out var descripcion))
            {
                avisos.Add($"No reconocí el régimen «{r.Descripcion}» en el catálogo del SAT; revísalo.");
                continue;
            }

            regimenes.Add(new { clave, descripcion, fechaInicio = Fecha(r.FechaInicio)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
        }

        if (regimenes.Count > 0)
        {
            datos.Add((CamposDelPerfil.Regimenes, JsonSerializer.Serialize(regimenes, Json)));
        }

        var obligaciones = (m.Obligaciones ?? [])
            .Where(o => !string.IsNullOrWhiteSpace(o.Descripcion))
            .Select(o => new { descripcion = o.Descripcion!.Trim(), fechaInicio = Fecha(o.FechaInicio)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) })
            .ToList();
        if (obligaciones.Count > 0)
        {
            datos.Add((CamposDelPerfil.Obligaciones, JsonSerializer.Serialize(obligaciones, Json)));
        }

        var actividades = (m.Actividades ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a.Descripcion))
            .Select(a => new { descripcion = a.Descripcion!.Trim(), porcentaje = a.Porcentaje is >= 0 and <= 100 ? a.Porcentaje : null })
            .ToList();
        if (actividades.Count > 0)
        {
            datos.Add((CamposDelPerfil.ActividadesEconomicas, JsonSerializer.Serialize(actividades, Json)));
            var suma = actividades.Sum(a => a.porcentaje ?? 0);
            if (actividades.All(a => a.porcentaje != null) && suma != 100)
            {
                avisos.Add($"Los porcentajes de las actividades económicas suman {suma}, no 100; revísalos.");
            }
        }

        return datos;
    }

    private static void Agregar(List<(string, string)> datos, string campo, string? valor)
    {
        if (!string.IsNullOrWhiteSpace(valor))
        {
            var limpio = valor.Trim();
            datos.Add((campo, limpio.Length <= CamposDelPerfil.MaxValorLength ? limpio : limpio[..CamposDelPerfil.MaxValorLength]));
        }
    }

    private static DateOnly? Fecha(string? texto) =>
        DateOnly.TryParseExact(texto?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;

    private static SolicitudDeIA Solicitud(string texto, byte[] pdf)
    {
        // El TEXTO si lo hay (mucho más barato que el PDF); el PDF solo si es un escaneo.
        ParteDeContenido contenido = LectorDePdf.TieneTexto(texto)
            ? ParteDeContenido.Texto("Texto extraído de la constancia:\n\n" + texto)
            : ParteDeContenido.Pdf(pdf);

        return new SolicitudDeIA(Proposito, Instrucciones, Esquema, [contenido, ParteDeContenido.Texto("Registra los datos de esta constancia.")])
        {
            MaxTokens = 4096,
        };
    }

    private static readonly string Instrucciones =
        "Lees Constancias de Situación Fiscal (CSF) del SAT de México y registras sus datos con la herramienta. " +
        "Reglas: copia SOLO lo que aparece en el documento, tal cual; si un dato no aparece, déjalo vacío. NUNCA inventes ni completes. " +
        "Fechas en formato AAAA-MM-DD. Para cada régimen, la clave de este catálogo del SAT (c_RegimenFiscal): " +
        string.Join("; ", Sat.Regimenes.Select(r => $"{r.Key} = {r.Value}")) + ". " +
        "Si el documento no es una CSF, marca esConstancia como false y no llenes nada más.";

    private static readonly EsquemaDeSalida Esquema = new(
        "registrar_csf",
        "Registra los datos leídos de una Constancia de Situación Fiscal.",
        """
        {
          "type": "object",
          "properties": {
            "esConstancia": { "type": "boolean", "description": "true si el documento es una Constancia de Situación Fiscal del SAT" },
            "rfc": { "type": "string" },
            "curp": { "type": "string", "description": "Solo personas físicas" },
            "nombre": { "type": "string", "description": "Nombre completo (física) o denominación/razón social (moral)" },
            "estatusEnElPadron": { "type": "string" },
            "fechaInicioDeOperaciones": { "type": "string", "description": "AAAA-MM-DD" },
            "codigoPostal": { "type": "string" },
            "domicilio": { "type": "string", "description": "Domicilio fiscal en una línea" },
            "fechaDeEmision": { "type": "string", "description": "Fecha de emisión de la constancia, AAAA-MM-DD" },
            "regimenes": { "type": "array", "items": { "type": "object", "properties": {
              "clave": { "type": "string" }, "descripcion": { "type": "string" }, "fechaInicio": { "type": "string" } },
              "required": ["descripcion"] } },
            "obligaciones": { "type": "array", "items": { "type": "object", "properties": {
              "descripcion": { "type": "string" }, "fechaInicio": { "type": "string" } }, "required": ["descripcion"] } },
            "actividades": { "type": "array", "items": { "type": "object", "properties": {
              "descripcion": { "type": "string" }, "porcentaje": { "type": "integer" } }, "required": ["descripcion"] } }
          },
          "required": ["esConstancia"]
        }
        """);
}

/// <summary>Lo que devuelve el modelo (camelCase = el esquema de arriba).</summary>
public class LecturaDelModelo
{
    public bool EsConstancia { get; set; }
    public string? Rfc { get; set; }
    public string? Curp { get; set; }
    public string? Nombre { get; set; }
    public string? EstatusEnElPadron { get; set; }
    public string? FechaInicioDeOperaciones { get; set; }
    public string? CodigoPostal { get; set; }
    public string? Domicilio { get; set; }
    public string? FechaDeEmision { get; set; }
    public List<RegimenLeido>? Regimenes { get; set; }
    public List<ObligacionLeida>? Obligaciones { get; set; }
    public List<ActividadLeida>? Actividades { get; set; }
}

public class RegimenLeido
{
    public string? Clave { get; set; }
    public string? Descripcion { get; set; }
    public string? FechaInicio { get; set; }
}

public class ObligacionLeida
{
    public string? Descripcion { get; set; }
    public string? FechaInicio { get; set; }
}

public class ActividadLeida
{
    public string? Descripcion { get; set; }
    public int? Porcentaje { get; set; }
}
