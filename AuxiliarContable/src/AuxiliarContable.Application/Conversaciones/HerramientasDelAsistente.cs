using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using System.Threading.Tasks;
using AuxiliarContable.Boveda;
using AuxiliarContable.Cartera;
using AuxiliarContable.Documentos;
using AuxiliarContable.IA;
using AuxiliarContable.Identity;
using AuxiliarContable.Invitaciones;
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
/// <para>Aquí solo hay herramientas de la plataforma (perfil, documentos, clientes y personas).
/// Leer CFDI y estados de cuenta, conciliar y todo cálculo fiscal es del motor
/// (<c>motores-fiscales</c>): sus herramientas se agregan cuando el motor esté conectado.</para>
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

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private readonly IPerfilFiscalAppService _perfiles;
    private readonly IBovedaAppService _boveda;
    private readonly ICarteraAppService _cartera;
    private readonly IOrganizacionAppService _organizaciones;
    private readonly IInvitacionAppService _invitaciones;

    public HerramientasDelAsistente(
        IPerfilFiscalAppService perfiles,
        IBovedaAppService boveda,
        ICarteraAppService cartera,
        IOrganizacionAppService organizaciones,
        IInvitacionAppService invitaciones)
    {
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
                  "tipo":{"type":"string","enum":["cfdi","estado_de_cuenta","ticket","constancia","otro"]},
                  "texto":{"type":"string"}}}
                """),
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
    ];

    public async Task<BloqueDeResultadoDeHerramienta> EjecutarAsync(Guid organizacionId, BloqueDeUsoDeHerramienta uso)
    {
        // El candado entre las dos clases de conversación: en la de la cartera (Guid.Empty) solo sus
        // herramientas (resumen y alta); en la de un cliente, nunca esas. Lo que pida el modelo no lo salta.
        if ((organizacionId == Guid.Empty) != (uso.Nombre is ResumenDeCartera or CrearCliente))
        {
            return new BloqueDeResultadoDeHerramienta(uso.Id, $"La herramienta «{uso.Nombre}» no está disponible en esta conversación.", EsError: true);
        }

        try
        {
            var contenido = uso.Nombre switch
            {
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

    private static int? Entero(JsonElement entrada, string propiedad) =>
        entrada.ValueKind == JsonValueKind.Object && entrada.TryGetProperty(propiedad, out var v) && v.TryGetInt32(out var n) ? n : null;
}
