using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using System.Threading.Tasks;
using AuxiliarContable.Bancos;
using AuxiliarContable.Boveda;
using AuxiliarContable.Cartera;
using AuxiliarContable.Documentos;
using AuxiliarContable.IA;
using AuxiliarContable.Identity;
using AuxiliarContable.Invitaciones;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;

namespace AuxiliarContable.Conversaciones;

/// <summary>
/// Lo que el asistente puede HACER. Cada herramienta llama al mismo servicio que usaría la
/// pantalla, con los permisos de quien escribe: el modelo no tiene más poder que la persona.
/// El cliente activo lo pone el código, nunca el modelo (no hay «organizacionId» en ningún esquema).
///
/// <para>Las de la plataforma (perfil, documentos, clientes y personas) y las del motor fiscal
/// (<c>motores-fiscales</c>, ver <see cref="IMotorFiscal"/>): leer CFDI y estados de cuenta, conciliar
/// y todo cálculo fiscal es del motor; la consola solo le pregunta.</para>
///
/// <para>Las validaciones son las de siempre (<see cref="PerfilFiscalAppService.Normalizar"/>):
/// si el modelo manda algo mal, recibe el error como resultado y se lo pregunta a la persona.</para>
/// </summary>
public class HerramientasDelAsistente : ITransientDependency
{
    public const string ConsultarPerfil = "consultar_perfil";
    public const string GuardarDato = "guardar_dato_del_perfil";
    public const string ConfirmarDato = "confirmar_dato_del_perfil";
    public const string BuscarDocumentos = "buscar_documentos";
    public const string ResumenDeDocumentos = "resumen_de_documentos";
    public const string ResumenDeCartera = "resumen_de_cartera";
    public const string CrearCliente = "crear_cliente";
    public const string InvitarPersona = "invitar_persona";
    public const string PersonasDelCliente = "personas_del_cliente";
    public const string CambiarPapel = "cambiar_papel";
    public const string QuitarPersona = "quitar_persona";
    public const string ConsultarLeyes = "consultar_leyes";
    public const string ConsultarSentencias = "consultar_sentencias";
    public const string ConciliarMes = "conciliar_mes";
    public const string BuscarOperaciones = "buscar_operaciones";
    public const string ExplicarOperacion = "explicar_operacion";
    public const string PolizasDelMes = "polizas_del_mes";
    public const string ImpuestosDelMes = "impuestos_del_mes";
    public const string LeyesQueLeAplican = "leyes_que_le_aplican";
    public const string EncuadrarPregunta = "encuadrar_pregunta";
    public const string AnalizarOperacion = "analizar_operacion";
    public const string Solicitar = "solicitar";
    public const string TarifaIsr = "tarifa_isr";
    public const string LeerLey = "leer_ley";
    public const string VerSolicitudes = "ver_solicitudes";
    public const string ResolverSolicitud = "resolver_solicitud";
    public const string SolicitudesPendientes = "solicitudes_pendientes";

    /// <summary>De la conciliación, el modelo recibe un resumen y hasta este número de pendientes por lista.</summary>
    private const int MaxPendientesPorLista = 15;

    /// <summary>Lo que se le pasa al modelo de cada artículo: los largos se recortan (cuestan tokens en cada turno).</summary>
    private const int MaxCaracteresPorArticulo = 3000;

    /// <summary>Consultar leyes no toca datos de nadie: vale igual en la conversación de un cliente y en la de la cartera.</summary>
    private static readonly HerramientaDelModelo DeLeyes = new(ConsultarLeyes,
        "Busca en las leyes federales vigentes (LISR, LIVA, CFF, LIEPS, LFT, LSS, Ley del INFONAVIT…), sus reglamentos, la " +
        "Resolución Miscelánea Fiscal 2026 con TODOS sus anexos, las RGCE y las NIF. Devuelve los fragmentos más relevantes con " +
        "ley, artículo y última reforma. Úsala SIEMPRE que la respuesta dependa de la ley y cita ley y artículo. Busca con las " +
        "palabras de la ley o la cita («artículo 27 LISR»). «ley» acota a unas siglas. Dónde está cada cosa: RMF (reglas); " +
        "RMF-A8 tarifas de ISR (retenciones a salarios «calculada en días» —la semanal es la diaria por 7—, mensual, anual, " +
        "RESICO); RMF-A2 fichas de trámite; RMF-A3 y RMF-A7 criterios; RMF-A24 contabilidad electrónica; RMF-A29 CFDI; " +
        "RMF-A5 cantidades actualizadas; Reg_LISR reglamento. Si la primera búsqueda no trae lo que necesitas, NO te rindas: " +
        "reformula con otras palabras, acota con «ley» y lee el documento seguido con leer_ley.",
        """{"type":"object","properties":{"pregunta":{"type":"string","minLength":2,"maxLength":300},"ley":{"type":"string","maxLength":20}},"required":["pregunta"]}""");

    /// <summary>Leer una ley seguida tampoco es de nadie.</summary>
    private static readonly HerramientaDelModelo DeLeerLey = new(LeerLey,
        "Lee un ordenamiento SEGUIDO, por partes (3 fragmentos por vez): las tablas completas de un anexo, un capítulo, lo que " +
        "sigue de un artículo. «clave» son las siglas que devuelve consultar_leyes (LISR, RMF, RMF-A8, Reg_LISR_060516…); " +
        "«articulo» empieza en ese artículo con TODAS sus partes (los largos vienen en «parte 1 de 3»: léelo completo antes de " +
        "concluir, las últimas fracciones traen topes, excepciones y multas); «desde» el fragmento por el que empiezas (usa " +
        "«siguiente» para continuar). Úsala cuando consultar_leyes te dé un trozo y necesites el artículo o la tabla completa.",
        """{"type":"object","properties":{"clave":{"type":"string","minLength":2,"maxLength":40},"articulo":{"type":"string","maxLength":30},"desde":{"type":"integer","minimum":0}},"required":["clave"]}""");

    /// <summary>La tarifa de ISR tampoco es de nadie: vale en el chat de un cliente y en el de la cartera.</summary>
    private static readonly HerramientaDelModelo DeTarifa = new(TarifaIsr,
            "Tarifa de ISR para retener a salarios (art. 96 LISR y Anexo 8 de la RMF 2026) de un periodo de pago: diario, " +
            "semanal, decenal, catorcenal, quincenal o mensual, con sus renglones (límite inferior y superior, cuota fija, " +
            "% sobre el excedente). Con «base» (lo GRAVADO del periodo, sin lo exento) devuelve además el cálculo paso a paso: " +
            "excedente, impuesto marginal, ISR de la tarifa, subsidio para el empleo 2026 e ISR a retener. Úsala SIEMPRE para " +
            "tarifas o retenciones de sueldos: no las calcules de memoria. «mes» (1-12) cambia el subsidio de enero.",
            """{"type":"object","properties":{"periodo":{"type":"string","enum":["diario","semanal","decenal","catorcenal","quincenal","mensual"]},"base":{"type":"number","minimum":0},"mes":{"type":"integer","minimum":1,"maximum":12}},"required":["periodo"]}""");

    /// <summary>Las sentencias tampoco son de nadie: valen en las dos clases de conversación, como las leyes.</summary>
    private static readonly HerramientaDelModelo DeSentencias = new(ConsultarSentencias,
        "Busca en la librería de sentencias del despacho: tesis, precedentes y jurisprudencias fiscales del TFJA y de la SCJN. " +
        "Cada resultado trae la conclusión del tribunal, a quién favoreció (contribuyente, autoridad, mixto), la regla práctica, " +
        "cuándo aplica, qué documentar, los artículos que interpreta y la clave de la tesis. Úsala cuando se hable de una operación " +
        "o situación concreta (deducir un gasto, proveedores, materialidad, CFDI, revisiones del SAT, multas, devoluciones) para " +
        "tomar en cuenta cómo la han resuelto los tribunales. Describe la situación con palabras («deducción de servicios sin " +
        "evidencia de que se prestaron»). «fuente» acota a TFJA o SCJN.",
        """{"type":"object","properties":{"situacion":{"type":"string","minLength":2,"maxLength":300},"fuente":{"type":"string","enum":["TFJA","SCJN"]}},"required":["situacion"]}""");

    private static readonly string EsquemaDeAnalizarOperacion =
        "{\"type\":\"object\",\"properties\":{" +
        "\"descripcion\":{\"type\":\"string\",\"minLength\":10,\"maxLength\":600}," +
        "\"fecha\":{\"type\":\"string\",\"format\":\"date\"}," +
        "\"contraparte_tipo\":{\"type\":\"string\",\"enum\":[\"fisica\",\"moral\"]}," +
        "\"contraparte_residencia\":{\"type\":\"string\",\"maxLength\":3,\"description\":\"MEX o clave del país\"}," +
        "\"disparadores\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"enum\":[" +
        string.Join(",", AuxiliarContable.Normativa.NormativaAppService.ListaDeDisparadores.Select(d => "\"" + d.Clave + "\"")) +
        "]}}},\"required\":[\"descripcion\"]}";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private readonly IPerfilFiscalAppService _perfiles;
    private readonly IBovedaAppService _boveda;
    private readonly ICarteraAppService _cartera;
    private readonly IOrganizacionAppService _organizaciones;
    private readonly IInvitacionAppService _invitaciones;
    private readonly IMotorFiscal _motor;
    private readonly IBancosAppService _bancos;
    private readonly ConsultasDelAsistente _consultas;
    private readonly ISolicitudesAppService _solicitudes;

    public HerramientasDelAsistente(
        IPerfilFiscalAppService perfiles,
        IBovedaAppService boveda,
        ICarteraAppService cartera,
        IOrganizacionAppService organizaciones,
        IInvitacionAppService invitaciones,
        IMotorFiscal motor,
        IBancosAppService bancos,
        ConsultasDelAsistente consultas,
        ISolicitudesAppService solicitudes)
    {
        _solicitudes = solicitudes;
        _consultas = consultas;
        _motor = motor;
        _bancos = bancos;
        _invitaciones = invitaciones;
        _organizaciones = organizaciones;
        _perfiles = perfiles;
        _boveda = boveda;
        _cartera = cartera;
    }

    /// <summary>
    /// Las que puede usar quien escribe: sin permiso de cambiar, solo las de consulta. Invitar, solo
    /// el equipo con permiso de dar de alta personas (el servicio lo vuelve a exigir).
    /// </summary>
    public static IReadOnlyList<HerramientaDelModelo> Disponibles(bool puedeEditar, bool puedeInvitar = false, bool esEquipo = false)
    {
        var lista = new List<HerramientaDelModelo>
        {
            new(ConsultarPerfil,
                "Devuelve el perfil fiscal del cliente activo: cada dato con su valor, de dónde salió (constancia, cliente, despacho) " +
                "y si ya está confirmado. Úsala antes de preguntar algo para no repetir preguntas.",
                """{"type":"object","properties":{}}"""),
            new(ResumenDeDocumentos,
                "Cuántos documentos tiene el cliente activo por año, mes y tipo. Úsala para «¿qué me falta?» o para detectar meses " +
                "sin documentos. No da montos: los documentos aún no se leen.",
                """{"type":"object","properties":{}}"""),
            new(BuscarDocumentos,
                "Busca en la bóveda del cliente activo. Filtros opcionales: año, mes (1-12), tipo y texto (parte del nombre del archivo). " +
                "Devuelve hasta 20 documentos, los más recientes primero, y cuántos hay en total.",
                """
                {"type":"object","properties":{
                  "anio":{"type":"integer","minimum":2000,"maximum":2100},
                  "mes":{"type":"integer","minimum":1,"maximum":12},
                  "tipo":{"type":"string","enum":["cfdi","estado_de_cuenta","ticket","comprobante_de_pago","control_interno","constancia","otro"]},
                  "texto":{"type":"string"}}}
                """),
            new(EncuadrarPregunta,
                "ÚSALA PRIMERO con cualquier pregunta de dinero, deducciones, ingresos o permisos del cliente («¿cuánto gasté de " +
                "dulces?», «¿por qué la gasolina no fue deducible?», «compré un refrigerador, ¿es deducible?», «quiero comprar un " +
                "carro», «¿por qué salieron altos mis ingresos?»). Con su perfil (giro, régimen, tipo de persona) dice qué quiere " +
                "saber, de qué objeto habla y QUÉ ES ESO PARA ÉL (mercancía que revende, insumo, activo fijo, gasto, gasto personal, " +
                "deducción personal), cómo se deduce en su régimen, requisitos, artículos a leer, cómo buscarlo en sus operaciones " +
                "(términos y periodo para buscar_operaciones) y SOLO los datos que faltan y cambian la respuesta (preguntar_antes). " +
                "Si preguntar_antes viene vacío, NO digas que faltan datos: trabaja la respuesta. Pásale la pregunta tal cual.",
                """{"type":"object","properties":{"pregunta":{"type":"string","minLength":2,"maxLength":600}},"required":["pregunta"]}"""),
            new(BuscarOperaciones,
                "Busca en las operaciones del cliente activo (cada una junta su factura, su pago y su control interno). Filtros " +
                "opcionales: texto (proveedor o cliente, concepto, UUID o referencia del pago), terminos (varias palabras: trae las " +
                "que tengan CUALQUIERA, p. ej. [\"gasolina\",\"magna\",\"pemex\"]), año, mes, importe mínimo y máximo, " +
                "sentido (entrada = cobros e ingresos, salida = pagos y gastos) y falta (factura, pago o control: las que no tienen " +
                "esa parte). Devuelve hasta 20, las más recientes, con su id y si la factura y el pago están ligados, y la SUMA de " +
                "TODAS las que coinciden (importeTotal y cuánto de eso no tiene factura): con eso contestas «¿cuánto gasté de…?». " +
                "Úsala para «¿ya se pagó la factura de Telmex?», «¿qué gastos de octubre no tienen factura?» o «dame los datos de …».",
                """
                {"type":"object","properties":{
                  "texto":{"type":"string","maxLength":100},
                  "terminos":{"type":"array","maxItems":10,"items":{"type":"string","minLength":2,"maxLength":40}},
                  "anio":{"type":"integer","minimum":2000,"maximum":2100},
                  "mes":{"type":"integer","minimum":1,"maximum":12},
                  "importe_minimo":{"type":"number","minimum":0},
                  "importe_maximo":{"type":"number","minimum":0},
                  "sentido":{"type":"string","enum":["entrada","salida"]},
                  "falta":{"type":"string","enum":["factura","pago","control"]}}}
                """),
            new(ExplicarOperacion,
                "Explica UNA operación o factura: sus tres partes (factura, pago, control interno) con su estado y motivo, y el PORQUÉ " +
                "que dejó el sistema: si el gasto se dedujo o no y cuánto, con cada motivo, la hipótesis, el ARTÍCULO y cómo " +
                "corregirlo; y lo que encontró la revisión contable. Pásale el id que dio buscar_operaciones o el UUID de la factura. " +
                "Úsala para «¿por qué esta factura es no deducible?» o «¿por qué no se tomó este gasto?». Cita el artículo que trae; " +
                "si quieres el texto de la ley, consúltalo después con consultar_leyes.",
                """{"type":"object","properties":{"operacionId":{"type":"string","format":"uuid"},"uuid":{"type":"string","maxLength":40}}}"""),
            new(LeyesQueLeAplican,
                "Las leyes estatales y reglamentos municipales que le aplican al cliente activo por su domicilio y su actividad: por " +
                "qué, qué le piden, si la tenemos, si no la encontramos en línea, si la rige la ley estatal, y qué otros reglamentos " +
                "tratan el tema cuando no hay uno propio. «tema» filtra (licencia, protección civil, anuncios, predial…).",
                """{"type":"object","properties":{"tema":{"type":"string","maxLength":60}}}"""),
            new(AnalizarOperacion,
                "Arma lo que hay que revisar de una operación que el cliente PIENSA hacer o ya hizo («voy a comprar una camioneta», " +
                "«me van a pagar desde Estados Unidos», «quiero rentar un local»): con su perfil fiscal, el índice de leyes saca el " +
                "núcleo fiscal, las leyes que activa su perfil (y las descartadas con su motivo), las PREGUNTAS OBLIGATORIAS que hay " +
                "que resolver (chispazos) con sus fuentes, los artículos que aplican y fragmentos de la ley. Úsala para orientar " +
                "ANTES de operar: requisitos para deducir, retenciones, IVA, a nombre de quién facturar. Razona con lo que trae, " +
                "cita ley y artículo, y pregunta lo que los chispazos dejan abierto. descripcion: qué, con quién, monto aproximado " +
                "y cómo se pagaría. disparadores: solo los que la persona dijo o son obvios.",
                EsquemaDeAnalizarOperacion),
            DeTarifa,
            DeLeerLey,
            new(Solicitar,
                "Deja una SOLICITUD registrada. Si escribe el cliente, va al despacho: un cambio o corrección que él no puede " +
                "hacer (su contabilidad la cambia el despacho), o una aclaración. Si escribe el despacho, va al cliente: un " +
                "documento, un dato o una aclaración. tipo: cambio, documento o aclaracion. texto: qué se pide y por qué, claro y " +
                "completo para quien lo lea sin la plática (montos, fechas, UUID si los hay). operacionId si es sobre una " +
                "operación. Úsala SOLO después de proponerle el texto a la persona y que diga que sí.",
                """{"type":"object","properties":{"tipo":{"type":"string","enum":["cambio","documento","aclaracion"]},"texto":{"type":"string","minLength":5,"maxLength":1000},"operacionId":{"type":"string","format":"uuid"}},"required":["tipo","texto"]}"""),
            new(VerSolicitudes,
                "Las solicitudes del cliente activo (al despacho y del despacho), las pendientes primero, con su respuesta si ya " +
                "se atendió. Úsala para «¿qué me pidió el despacho?», «¿ya vieron lo que pedí?».",
                """{"type":"object","properties":{"solo_pendientes":{"type":"boolean"}}}"""),
            new(ResolverSolicitud,
                "Marca una solicitud como atendida o rechazada, con una respuesta (rechazar exige decir por qué). Lo que el " +
                "cliente le pidió al despacho solo lo resuelve el despacho; lo que el despacho le pidió al cliente, el cliente " +
                "(p. ej. «ya subí el estado de cuenta»). Úsala SOLO cuando la persona lo diga.",
                """{"type":"object","properties":{"solicitudId":{"type":"string","format":"uuid"},"estado":{"type":"string","enum":["atendida","rechazada"]},"respuesta":{"type":"string","maxLength":1000}},"required":["solicitudId","estado"]}"""),
            DeLeyes,
            DeSentencias,
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
                "Marca como correcto un dato que ya está en el perfil SOLO cuando la persona te dijo explícitamente que es correcto.",
                $$$"""{"type":"object","properties":{"campo":{"type":"string","enum":[{{{claves}}}]}},"required":["campo"]}"""));
        }

        if (puedeInvitar)
        {
            lista.Add(new(InvitarPersona,
                "Manda por correo una invitación para que una persona entre a la app como parte del cliente activo. papel: " +
                "«dueño» (además invita a otros de su negocio), «miembro» (sube documentos y platica) o «lector» (solo consulta). " +
                "Úsala SOLO después de que la persona confirme el correo exacto y el papel que le propusiste.",
                """{"type":"object","properties":{"email":{"type":"string","format":"email","maxLength":256},"papel":{"type":"string","enum":["dueño","miembro","lector"]}},"required":["email","papel"]}"""));
        }

        // Quién entra a este cliente: lo administra el despacho (el servicio de Organizaciones lo exige).
        if (esEquipo)
        {
            lista.Add(new(ConciliarMes,
                "Concilia los estados de cuenta del cliente activo contra sus facturas (XML) de un mes, con el motor fiscal del " +
                "despacho: si cada estado de cuenta cuadra con lo que imprime el banco, cuántos movimientos tienen factura, cuáles " +
                "no (depósitos sin factura emitida, pagos sin factura recibida), facturas sin movimiento y alertas (forma de pago " +
                "mal, PUE pagada en otro mes). Tarda unos segundos. Úsala para «¿qué falta conciliar de octubre?».",
                """{"type":"object","properties":{"anio":{"type":"integer","minimum":2000,"maximum":2100},"mes":{"type":"integer","minimum":1,"maximum":12}},"required":["anio","mes"]}"""));
            lista.Add(new(PolizasDelMes,
                "Solo despacho. Las pólizas guardadas del cliente activo de un mes (cuentas, debe y haber), cuántas operaciones " +
                "detuvo la revisión y los pendientes de cierre. «texto» filtra por concepto, UUID, nombre de cuenta o número de cuenta. " +
                "No las genera: lee las que hay.",
                """{"type":"object","properties":{"anio":{"type":"integer","minimum":2000,"maximum":2100},"mes":{"type":"integer","minimum":1,"maximum":12},"texto":{"type":"string","maxLength":60}},"required":["anio","mes"]}"""));
            lista.Add(new(ImpuestosDelMes,
                "Solo despacho. El pago provisional de ISR e IVA guardado del cliente activo de un mes: régimen, artículo con que se " +
                "calculó, ISR e IVA a pagar o a favor, cada renglón del papel de trabajo con su fundamento, cuántos gastos no se " +
                "consideraron, avisos y supuestos. No recalcula: dice si está por actualizarse.",
                """{"type":"object","properties":{"anio":{"type":"integer","minimum":2000,"maximum":2100},"mes":{"type":"integer","minimum":1,"maximum":12}},"required":["anio","mes"]}"""));
            lista.Add(new(PersonasDelCliente,
                "Las personas con acceso al cliente activo: nombre, correo y papel (dueño, miembro, lector). Los «userId» no se enseñan.",
                """{"type":"object","properties":{}}"""));
            lista.Add(new(CambiarPapel,
                "Cambia el papel de una persona del cliente activo (dueño, miembro o lector). SOLO tras confirmar con quien te escribe.",
                """{"type":"object","properties":{"userId":{"type":"string","format":"uuid"},"papel":{"type":"string","enum":["dueño","miembro","lector"]}},"required":["userId","papel"]}"""));
            lista.Add(new(QuitarPersona,
                "Quita el acceso de una persona al cliente activo (su cuenta sigue existiendo). SOLO tras confirmar nombre y correo con quien te escribe.",
                """{"type":"object","properties":{"userId":{"type":"string","format":"uuid"}},"required":["userId"]}"""));
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
            CrearCliente when uso.Entrada.ValueKind == JsonValueKind.Object && uso.Entrada.TryGetProperty("nombre", out var n) =>
                $"Dio de alta al cliente: {n.GetString()}",
            InvitarPersona when uso.Entrada.ValueKind == JsonValueKind.Object && uso.Entrada.TryGetProperty("email", out var e) =>
                $"Invitó a {e.GetString()}",
            CambiarPapel => "Cambió el papel de una persona",
            QuitarPersona => "Quitó el acceso a una persona",
            ConsultarLeyes => "Consultó la ley",
            ConsultarSentencias => "Consultó sentencias",
            ConciliarMes => "Concilió el banco",
            BuscarOperaciones => "Buscó en las operaciones",
            ExplicarOperacion => "Revisó el porqué de una operación",
            PolizasDelMes => "Consultó las pólizas",
            ImpuestosDelMes => "Consultó los impuestos",
            LeyesQueLeAplican => "Revisó las leyes que te aplican",
            EncuadrarPregunta => "Encuadró la pregunta con tu perfil",
            AnalizarOperacion => "Analizó la operación con la ley",
            TarifaIsr => "Consultó la tarifa de ISR",
            LeerLey => "Leyó la ley",
            Solicitar => "Dejó una solicitud",
            VerSolicitudes or SolicitudesPendientes => "Revisó las solicitudes",
            ResolverSolicitud when uso.Entrada.ValueKind == JsonValueKind.Object && uso.Entrada.TryGetProperty("estado", out var es) =>
                es.GetString() == "rechazada" ? "Rechazó una solicitud" : "Marcó una solicitud como atendida",
            _ => null,
        };
    }

    /// <summary>Las de la conversación de toda la cartera (solo equipo): el resumen, nada del detalle de un cliente.</summary>
    public static IReadOnlyList<HerramientaDelModelo> DeCartera() =>
    [
        new(ResumenDeCartera,
            "Resumen de TODOS los clientes del despacho para un mes: semáforo (al día, atención, urgente), RFC, perfil completo en %, " +
            "documentos, facturas y estados de cuenta recibidos en el mes y la lista de pendientes de cada uno.",
            """{"type":"object","properties":{"anio":{"type":"integer","minimum":2000,"maximum":2100},"mes":{"type":"integer","minimum":1,"maximum":12}},"required":["anio","mes"]}"""),
        new(CrearCliente,
            "Da de alta un cliente nuevo del despacho (una organización: 1 cliente = 1 RFC). Úsala SOLO después de que la persona " +
            "confirme el nombre exacto que le propusiste. Después, el cliente aparece en la lista de arriba para elegirlo, llenar su " +
            "perfil e invitarlo. Si ya existe uno con ese nombre, devuelve error.",
            """{"type":"object","properties":{"nombre":{"type":"string","minLength":2,"maxLength":128},"descripcion":{"type":"string","maxLength":256}},"required":["nombre"]}"""),
        new(SolicitudesPendientes,
            "La bandeja del despacho: lo que los clientes han pedido (cambios, correcciones, aclaraciones) y sigue pendiente, " +
            "de toda la cartera, lo más viejo primero, con el nombre del cliente. Para atender una, di que elijan a ese cliente.",
            """{"type":"object","properties":{}}"""),
        DeLeyes,
        DeSentencias,
        DeTarifa,
        DeLeerLey,
    ];

    /// <summary>Las que solo consultan al motor (sin base ni cliente): sirven en las dos conversaciones y pueden correr a la vez.</summary>
    public static bool SoloConsultaAlMotor(string nombre) => nombre is ConsultarLeyes or ConsultarSentencias or TarifaIsr or LeerLey;

    public async Task<BloqueDeResultadoDeHerramienta> EjecutarAsync(Guid organizacionId, BloqueDeUsoDeHerramienta uso)
    {
        // El candado entre las dos clases de conversación: en la de la cartera (Guid.Empty) solo sus
        // herramientas (resumen y alta); en la de un cliente, nunca esas. Lo que pida el modelo no lo salta.
        if (!SoloConsultaAlMotor(uso.Nombre) && (organizacionId == Guid.Empty) != (uso.Nombre is ResumenDeCartera or CrearCliente or SolicitudesPendientes))
        {
            return new BloqueDeResultadoDeHerramienta(uso.Id, $"La herramienta «{uso.Nombre}» no está disponible en esta conversación.", EsError: true);
        }

        try
        {
            var contenido = uso.Nombre switch
            {
                ConsultarLeyes => await LeyesAsync(uso.Entrada),
                ConsultarSentencias => await SentenciasAsync(uso.Entrada),
                ConciliarMes => await ConciliarAsync(organizacionId, uso.Entrada),
                BuscarOperaciones => await _consultas.BuscarOperacionesAsync(organizacionId, new ConsultasDelAsistente.FiltroDeOperaciones(
                    Opcional(uso.Entrada, "texto"), Entero(uso.Entrada, "anio"), Entero(uso.Entrada, "mes"),
                    Decimal(uso.Entrada, "importe_minimo"), Decimal(uso.Entrada, "importe_maximo"),
                    Opcional(uso.Entrada, "sentido"), Opcional(uso.Entrada, "falta"), Lista(uso.Entrada, "terminos"))),
                EncuadrarPregunta => await _consultas.EncuadrarPreguntaAsync(organizacionId, Texto(uso.Entrada, "pregunta")),
                ExplicarOperacion => await _consultas.ExplicarOperacionAsync(organizacionId,
                    Opcional(uso.Entrada, "operacionId") != null ? Id(uso.Entrada, "operacionId") : (Guid?)null,
                    Opcional(uso.Entrada, "uuid")),
                PolizasDelMes => await _consultas.PolizasDelMesAsync(organizacionId,
                    Entero(uso.Entrada, "anio") ?? throw new UserFriendlyException("Falta «anio»."),
                    Entero(uso.Entrada, "mes") ?? throw new UserFriendlyException("Falta «mes»."),
                    Opcional(uso.Entrada, "texto")),
                ImpuestosDelMes => await _consultas.ImpuestosDelMesAsync(organizacionId,
                    Entero(uso.Entrada, "anio") ?? throw new UserFriendlyException("Falta «anio»."),
                    Entero(uso.Entrada, "mes") ?? throw new UserFriendlyException("Falta «mes».")),
                LeyesQueLeAplican => await _consultas.LeyesQueLeAplicanAsync(organizacionId, Opcional(uso.Entrada, "tema")),
                Solicitar => SolicitudesJson([await _solicitudes.CrearAsync(organizacionId, new CrearSolicitudDto
                {
                    Tipo = Texto(uso.Entrada, "tipo") switch
                    {
                        "cambio" => TipoDeSolicitud.Cambio,
                        "documento" => TipoDeSolicitud.Documento,
                        "aclaracion" => TipoDeSolicitud.Aclaracion,
                        var t => throw new UserFriendlyException($"Tipo desconocido: «{t}». Usa cambio, documento o aclaracion."),
                    },
                    Texto = Texto(uso.Entrada, "texto"),
                    OperacionId = Opcional(uso.Entrada, "operacionId") != null ? Id(uso.Entrada, "operacionId") : (Guid?)null,
                })]),
                VerSolicitudes => SolicitudesJson(await _solicitudes.GetListAsync(organizacionId,
                    uso.Entrada.ValueKind == JsonValueKind.Object && uso.Entrada.TryGetProperty("solo_pendientes", out var sp)
                    && sp.ValueKind == JsonValueKind.True)),
                ResolverSolicitud => SolicitudesJson([await _solicitudes.ResolverAsync(organizacionId, Id(uso.Entrada, "solicitudId"),
                    new ResolverSolicitudDto
                    {
                        Estado = Texto(uso.Entrada, "estado") == "rechazada" ? EstadoDeSolicitud.Rechazada : EstadoDeSolicitud.Atendida,
                        Respuesta = Opcional(uso.Entrada, "respuesta"),
                    })]),
                SolicitudesPendientes => SolicitudesJson(await _solicitudes.GetPendientesDelDespachoAsync()),
                LeerLey => await _motor.LeerLeyAsync(Texto(uso.Entrada, "clave"), Entero(uso.Entrada, "desde") ?? 0, 3,
                    Opcional(uso.Entrada, "articulo")),
                TarifaIsr => await _motor.TarifaIsrAsync(
                    Texto(uso.Entrada, "periodo") switch
                    {
                        "diario" => 1,
                        "semanal" => 7,
                        "decenal" => 10,
                        "catorcenal" => 14,
                        "quincenal" => 15,
                        "mensual" => 30,
                        var p => throw new UserFriendlyException($"Periodo desconocido: «{p}»."),
                    },
                    Decimal(uso.Entrada, "base"), Entero(uso.Entrada, "mes")),
                AnalizarOperacion => await _consultas.AnalizarOperacionAsync(organizacionId, Texto(uso.Entrada, "descripcion"),
                    Opcional(uso.Entrada, "fecha") is { } f
                        ? DateTime.TryParse(f, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
                            ? d.Date
                            : throw new UserFriendlyException("«fecha» va como AAAA-MM-DD.")
                        : null,
                    Opcional(uso.Entrada, "contraparte_tipo"), Opcional(uso.Entrada, "contraparte_residencia"),
                    Lista(uso.Entrada, "disparadores")),
                ResumenDeCartera => await CarteraAsync(uso.Entrada),
                CrearCliente => await CrearClienteAsync(uso.Entrada),
                InvitarPersona => await InvitarAsync(organizacionId, uso.Entrada),
                ConsultarPerfil => Perfil(await _perfiles.GetAsync(organizacionId)),
                ResumenDeDocumentos => await ResumenAsync(organizacionId),
                BuscarDocumentos => await BuscarAsync(organizacionId, uso.Entrada),
                GuardarDato => await GuardarAsync(organizacionId, uso.Entrada),
                ConfirmarDato => await ConfirmarAsync(organizacionId, uso.Entrada),
                PersonasDelCliente => Personas(await _organizaciones.GetAsync(organizacionId)),
                CambiarPapel => Personas(await _organizaciones.CambiarRolDeMiembroAsync(organizacionId, Id(uso.Entrada, "userId"),
                    new CambiarRolEnOrganizacionDto { Rol = Papel(Texto(uso.Entrada, "papel")) })),
                QuitarPersona => Personas(await _organizaciones.QuitarMiembroAsync(organizacionId, Id(uso.Entrada, "userId"))),
                _ => throw new UserFriendlyException($"No existe la herramienta «{uso.Nombre}»."),
            };
            return new BloqueDeResultadoDeHerramienta(uso.Id, contenido);
        }
        catch (UserFriendlyException ex)
        {
            return new BloqueDeResultadoDeHerramienta(uso.Id, ex.Message, EsError: true);
        }
        catch (MotorFiscalNoDisponibleException)
        {
            // Sin detalles técnicos: el modelo solo tiene que decir que ahora no pudo consultar.
            return new BloqueDeResultadoDeHerramienta(uso.Id,
                uso.Nombre switch
                {
                    ConsultarSentencias => "La librería de sentencias no está disponible en este momento. Dilo así y no cites tesis de memoria.",
                    ConciliarMes => "El motor fiscal no responde en este momento: no se pudo conciliar. Dilo así.",
                    TarifaIsr => "La tarifa de ISR no está disponible en este momento. Dilo así y no la escribas de memoria.",
                    _ => "La consulta de leyes no está disponible en este momento. Dilo así y no respondas de memoria sobre lo que dice la ley.",
                },
                EsError: true);
        }
        catch (Volo.Abp.Authorization.AbpAuthorizationException)
        {
            // Lo que solo ve el despacho (pólizas, impuestos): sin detalles, y la conversación sigue.
            return new BloqueDeResultadoDeHerramienta(uso.Id, "Eso solo lo puede consultar el despacho.", EsError: true);
        }
        catch (EntityNotFoundException)
        {
            // Un id que no es de ESTE cliente (o que el modelo se inventó): sin decir de quién es.
            return new BloqueDeResultadoDeHerramienta(uso.Id, "No existe en este cliente.", EsError: true);
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

    private static readonly Dictionary<string, TipoDeDocumento> Tipos = new()
    {
        ["cfdi"] = TipoDeDocumento.Cfdi,
        ["estado_de_cuenta"] = TipoDeDocumento.EstadoDeCuenta,
        ["ticket"] = TipoDeDocumento.Ticket,
        ["comprobante_de_pago"] = TipoDeDocumento.ComprobanteDePago,
        ["control_interno"] = TipoDeDocumento.ControlInterno,
        ["constancia"] = TipoDeDocumento.ConstanciaDeSituacionFiscal,
        ["otro"] = TipoDeDocumento.Otro,
    };

    private static string NombreDeTipo(TipoDeDocumento tipo) => Tipos.FirstOrDefault(t => t.Value == tipo).Key ?? "otro";

    private async Task<string> ResumenAsync(Guid organizacionId)
    {
        var resumen = await _boveda.GetResumenAsync(organizacionId);
        if (resumen.Count == 0)
        {
            return "No ha subido ningún documento.";
        }

        return JsonSerializer.Serialize(resumen.Select(r => new
        {
            periodo = $"{r.Anio}-{r.Mes:00}",
            tipo = NombreDeTipo(r.Tipo),
            cantidad = r.Cantidad,
        }), Json);
    }

    private async Task<string> BuscarAsync(Guid organizacionId, JsonElement entrada)
    {
        var filtro = new BuscarEnBovedaDto
        {
            OrganizacionId = organizacionId,
            Anio = Entero(entrada, "anio"),
            Mes = Entero(entrada, "mes"),
            Texto = Opcional(entrada, "texto"),
            MaxResultCount = 20,
        };
        if (Opcional(entrada, "tipo") is { } tipo)
        {
            filtro.Tipo = Tipos.TryGetValue(tipo, out var t) ? t : throw new UserFriendlyException($"Tipo desconocido: {tipo}.");
        }

        var resultado = await _boveda.GetListAsync(filtro);
        return JsonSerializer.Serialize(new
        {
            total = resultado.TotalCount,
            documentos = resultado.Items.Select(d => new
            {
                tipo = NombreDeTipo(d.Tipo),
                nombre = d.Nombre,
                fecha = d.Fecha.ToString("yyyy-MM-dd"),
                estado = d.Estado.ToString(),
            }),
        }, Json);
    }

    private static RolEnOrganizacion Papel(string papel) => papel switch
    {
        "dueño" => RolEnOrganizacion.Administrador,
        "miembro" => RolEnOrganizacion.Miembro,
        "lector" => RolEnOrganizacion.Lector,
        var otro => throw new UserFriendlyException($"Papel desconocido: «{otro}». Usa dueño, miembro o lector."),
    };

    private static string Personas(OrganizacionDetalleDto o) => o.ListaDeMiembros.Count == 0
        ? "Nadie del cliente tiene acceso todavía. Puedes invitar a alguien."
        : JsonSerializer.Serialize(o.ListaDeMiembros.Select(m => new
        {
            userId = m.UserId,
            nombre = m.NombreCompleto ?? m.UserName,
            email = m.Email,
            papel = m.Rol switch { RolEnOrganizacion.Administrador => "dueño", RolEnOrganizacion.Lector => "lector", _ => "miembro" },
        }), Json);

    private async Task<string> InvitarAsync(Guid organizacionId, JsonElement entrada)
    {
        var papel = Papel(Texto(entrada, "papel"));
        // El mismo servicio que la pantalla de Invitaciones: permiso, jerarquía, correo ya con cuenta
        // y el envío (en segundo plano). El cliente es el activo: el modelo no lo elige.
        var invitacion = await _invitaciones.CreateAsync(new CrearInvitacionDto
        {
            Email = Texto(entrada, "email").Trim(),
            Rol = AuxiliarRoles.Usuario,
            OrganizacionId = organizacionId,
            RolEnOrganizacion = papel,
        });
        return JsonSerializer.Serialize(new { invitado = invitacion.Email, papel = Texto(entrada, "papel"), correo = "en camino" }, Json);
    }

    private async Task<string> CrearClienteAsync(JsonElement entrada)
    {
        var nombre = Texto(entrada, "nombre").Trim();
        var descripcion = entrada.TryGetProperty("descripcion", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
        // El mismo servicio que la pantalla de Organizaciones: mismas validaciones (nombre libre) y permiso.
        var creada = await _organizaciones.CreateAsync(new GuardarOrganizacionDto { Nombre = nombre, Descripcion = descripcion });
        return JsonSerializer.Serialize(new { creado = true, nombre = creada.Nombre }, Json);
    }

    private async Task<string> LeyesAsync(JsonElement entrada)
    {
        var resultados = await _motor.BuscarEnLeyesAsync(Texto(entrada, "pregunta").Trim(), Opcional(entrada, "ley"), cuantos: 6);
        if (resultados.Count == 0)
        {
            return "No se encontró nada con esa búsqueda. Prueba con otras palabras o sin acotar la ley.";
        }

        return JsonSerializer.Serialize(resultados.Select(r => new
        {
            ley = r.Clave,
            ordenamiento = r.Ordenamiento,
            articulo = r.Articulo,
            ubicacion = r.Ubicacion,
            ultimaReformaDof = r.UltimaReformaDof,
            url = r.Url,
            // Avisos del motor: «parte 1 de 3» (leer el resto) y decretos que aún no entran en vigor.
            aviso = r.NotaDeVigencia,
            texto = r.Texto.Length > MaxCaracteresPorArticulo ? r.Texto[..MaxCaracteresPorArticulo] + " […]" : r.Texto,
        }), Json);
    }

    private async Task<string> SentenciasAsync(JsonElement entrada)
    {
        var resultados = await _motor.BuscarSentenciasAsync(Texto(entrada, "situacion").Trim(), Opcional(entrada, "fuente"), cuantos: 5);
        if (resultados.Count == 0)
        {
            return "No hay tesis sobre eso en la librería. Prueba con otras palabras; si no aparece, dilo y no cites tesis de memoria.";
        }

        // Lo estudiado basta para razonar; el texto completo solo si la tesis aún no se estudia (recortado: cuesta tokens).
        return JsonSerializer.Serialize(resultados.Select(t => new
        {
            tesis = $"{t.Fuente} {t.Clave}".Trim(),
            tipo = t.Tipo,
            instancia = t.Instancia,
            fecha = t.Fecha,
            rubro = t.Rubro,
            conclusion = t.Estudio?.Conclusion,
            aFavor = t.Estudio?.AFavor,
            reglaPractica = t.Estudio?.ReglaPractica,
            aplicaCuando = t.Estudio?.AplicaCuando,
            queDocumentar = t.Estudio?.QueDocumentar,
            fundamentos = t.Estudio?.Fundamentos,
            vigencia = t.Estudio?.Vigencia,
            texto = t.Estudio is not null ? null
                : t.Texto.Length > MaxCaracteresPorArticulo ? t.Texto[..MaxCaracteresPorArticulo] + " […]" : t.Texto,
            url = t.Url,
        }), Json);
    }

    private async Task<string> ConciliarAsync(Guid organizacionId, JsonElement entrada)
    {
        var anio = Entero(entrada, "anio") ?? throw new UserFriendlyException("Falta «anio».");
        var mes = Entero(entrada, "mes") ?? throw new UserFriendlyException("Falta «mes».");
        var c = await _bancos.ConciliarAsync(organizacionId, anio, mes);
        var r = c.Resultado;
        var porEstado = r.Movimientos.GroupBy(m => m.Conciliacion?.Estado ?? "pendiente").ToDictionary(g => g.Key, g => g.Count());
        var porUuid = r.Facturas.Where(f => f.Uuid != null).GroupBy(f => f.Uuid!).ToDictionary(g => g.Key, g => g.First());
        return JsonSerializer.Serialize(new
        {
            periodo = $"{anio}-{mes:00}",
            archivos = new { estadosDeCuenta = c.EstadosDeCuenta, facturas = c.Facturas },
            cuentas = r.Cuentas.Select(x => new
            {
                banco = x.Banco,
                // Solo la terminación: el número completo no hace falta para platicar.
                cuenta = x.Cuenta.Length > 4 ? "…" + x.Cuenta[^4..] : x.Cuenta,
                periodo = $"{x.PeriodoInicial} a {x.PeriodoFinal}",
                cuadra = x.Cuadra,
                diferencias = x.Cuadre.Where(q => q.Ok == false).Select(q => $"{q.Concepto}: banco {q.Declarado}, leído {q.Calculado}"),
            }),
            movimientos = new
            {
                total = r.Movimientos.Count,
                conFactura = porEstado.GetValueOrDefault("conciliado") + porEstado.GetValueOrDefault("combinacion"),
                traspasos = porEstado.GetValueOrDefault("traspaso"),
                sinFactura = porEstado.GetValueOrDefault("sin_factura"),
                pendientes = porEstado.GetValueOrDefault("pendiente"),
            },
            sinFactura = r.Movimientos
                .Where(m => m.Conciliacion?.Estado is "sin_factura" or "pendiente")
                .OrderByDescending(m => m.Importe)
                .Take(MaxPendientesPorLista)
                .Select(m => new { fecha = m.Fecha, concepto = Corto(m.Concepto, 80), tipo = m.Naturaleza, importe = m.Importe, motivo = m.Conciliacion?.Motivo }),
            facturasSinMovimiento = r.FacturasSinMovimiento
                .OrderByDescending(f => f.Importe)
                .Take(MaxPendientesPorLista)
                .Select(f => new { fecha = f.Fecha, tercero = f.Tercero, importe = f.Importe, tipo = f.Naturaleza, motivo = f.Motivo }),
            alertas = r.Alertas.Take(MaxPendientesPorLista).Select(a => new
            {
                factura = a.Uuid != null && porUuid.TryGetValue(a.Uuid, out var f) ? $"{f.Tercero} {f.SerieFolio} ({f.Importe})" : a.Uuid,
                mensaje = a.Mensaje,
            }),
            noSeLeyeron = r.Errores.Select(e => e.Archivo),
        }, Json);
    }

    private static string Corto(string texto, int max) => texto.Length > max ? texto[..max] + "…" : texto;

    private async Task<string> CarteraAsync(JsonElement entrada)
    {
        var anio = Entero(entrada, "anio") ?? throw new UserFriendlyException("Falta «anio».");
        var mes = Entero(entrada, "mes") ?? throw new UserFriendlyException("Falta «mes».");
        var clientes = await _cartera.GetClientesAsync(anio, mes);
        return JsonSerializer.Serialize(new
        {
            periodo = $"{anio}-{mes:00}",
            clientes = clientes.Select(c => new
            {
                nombre = c.Nombre,
                rfc = c.Rfc,
                semaforo = c.Semaforo switch { SemaforoDeCliente.Urgente => "urgente", SemaforoDeCliente.Atencion => "atención", _ => "al día" },
                perfil = $"{c.PerfilCompleto} %",
                documentosDelMes = c.DocumentosDelMes,
                pendientes = c.Pendientes,
            }),
        }, Json);
    }

    private static Guid Id(JsonElement entrada, string propiedad) =>
        Guid.TryParse(Texto(entrada, propiedad), out var id) ? id : throw new UserFriendlyException($"«{propiedad}» no es un id válido.");

    private static string? Opcional(JsonElement entrada, string propiedad) =>
        entrada.ValueKind == JsonValueKind.Object && entrada.TryGetProperty(propiedad, out var v)
        && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static string Texto(JsonElement entrada, string propiedad) =>
        entrada.ValueKind == JsonValueKind.Object && entrada.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : throw new UserFriendlyException($"Falta «{propiedad}».");

    private static string SolicitudesJson(IReadOnlyList<SolicitudDto> lista) => lista.Count == 0
        ? "No hay solicitudes."
        : JsonSerializer.Serialize(lista.Take(SolicitudConsts.MaxEnLista).Select(s => new
        {
            id = s.Id,
            cliente = s.NombreDelCliente,
            de = s.Sentido == SentidoDeSolicitud.AlDespacho ? "del cliente al despacho" : "del despacho al cliente",
            tipo = s.Tipo switch { TipoDeSolicitud.Cambio => "cambio", TipoDeSolicitud.Documento => "documento", _ => "aclaración" },
            texto = s.Texto,
            operacionId = s.OperacionId,
            estado = s.Estado switch { EstadoDeSolicitud.Atendida => "atendida", EstadoDeSolicitud.Rechazada => "rechazada", _ => "pendiente" },
            respuesta = s.Respuesta,
            fecha = s.CreationTime.ToString("yyyy-MM-dd"),
            puedesResolverla = s.PuedeResolver,
        }), Json);

    private static List<string> Lista(JsonElement entrada, string propiedad) =>
        entrada.ValueKind == JsonValueKind.Object && entrada.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.Array
            ? [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim())]
            : [];

    private static decimal? Decimal(JsonElement entrada, string propiedad) =>
        entrada.ValueKind == JsonValueKind.Object && entrada.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.Number
        && v.TryGetDecimal(out var n) ? n : null;

    private static int? Entero(JsonElement entrada, string propiedad) =>
        entrada.ValueKind == JsonValueKind.Object && entrada.TryGetProperty(propiedad, out var v) && v.TryGetInt32(out var n) ? n : null;
}
