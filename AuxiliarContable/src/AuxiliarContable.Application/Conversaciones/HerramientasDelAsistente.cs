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
using AuxiliarContable.Cfdis;
using AuxiliarContable.Conciliaciones;
using AuxiliarContable.Documentos;
using AuxiliarContable.IA;
using AuxiliarContable.Identity;
using AuxiliarContable.Invitaciones;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

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
    public const string BuscarDocumentos = "buscar_documentos";
    public const string ResumenDeDocumentos = "resumen_de_documentos";
    public const string ConciliacionDelMes = "conciliacion_del_mes";
    public const string ResumenDeCartera = "resumen_de_cartera";
    public const string CrearCliente = "crear_cliente";
    public const string InvitarPersona = "invitar_persona";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private readonly IPerfilFiscalAppService _perfiles;
    private readonly IBovedaAppService _boveda;
    private readonly IConciliacionAppService _conciliacion;
    private readonly ICarteraAppService _cartera;
    private readonly IOrganizacionAppService _organizaciones;
    private readonly IInvitacionAppService _invitaciones;

    public HerramientasDelAsistente(
        IPerfilFiscalAppService perfiles,
        IBovedaAppService boveda,
        IConciliacionAppService conciliacion,
        ICarteraAppService cartera,
        IOrganizacionAppService organizaciones,
        IInvitacionAppService invitaciones)
    {
        _invitaciones = invitaciones;
        _organizaciones = organizaciones;
        _perfiles = perfiles;
        _boveda = boveda;
        _conciliacion = conciliacion;
        _cartera = cartera;
    }

    /// <summary>
    /// Las que puede usar quien escribe: sin permiso de cambiar, solo las de consulta. Invitar, solo
    /// el equipo con permiso de dar de alta personas (el servicio lo vuelve a exigir).
    /// </summary>
    public static IReadOnlyList<HerramientaDelModelo> Disponibles(bool puedeEditar, bool puedeInvitar = false)
    {
        var lista = new List<HerramientaDelModelo>
        {
            new(ConsultarPerfil,
                "Devuelve el perfil fiscal del cliente activo: cada dato con su valor, de dónde salió (constancia, cliente, despacho) " +
                "y si ya está confirmado, más los avisos de la última constancia. Úsala antes de preguntar algo para no repetir preguntas.",
                """{"type":"object","properties":{}}"""),
            new(ResumenDeDocumentos,
                "Cuántos documentos tiene el cliente activo por año, mes y tipo, y la suma de sus facturas (CFDI en MXN) emitidas " +
                "(ventas) y recibidas (gastos) por mes. Úsala para «¿qué me falta?», «¿cuánto facturé en…?» o detectar meses sin documentos. " +
                "Las sumas son informativas (ingresos menos notas de crédito), no un cálculo de impuestos.",
                """{"type":"object","properties":{}}"""),
            new(ConciliacionDelMes,
                "La conciliación de un mes del cliente activo: cuántos movimientos del banco ya están unidos a su factura y, sobre todo, " +
                "lo que FALTA: depósitos sin factura, cargos sin factura, ventas de contado sin depósito y gastos sin cargo. " +
                "Úsala para «¿qué me falta?» o «¿qué facturas me faltan de octubre?». No cambia nada.",
                """{"type":"object","properties":{"anio":{"type":"integer","minimum":2000,"maximum":2100},"mes":{"type":"integer","minimum":1,"maximum":12}},"required":["anio","mes"]}"""),
            new(BuscarDocumentos,
                "Busca en la bóveda del cliente activo. Filtros opcionales: año, mes (1-12), tipo, dirección de la factura " +
                "(emitida = venta, recibida = gasto) y texto (RFC, nombre del emisor o receptor, UUID, concepto o nombre del archivo). " +
                "Devuelve hasta 20 documentos, los más recientes primero, y cuántos hay en total.",
                """
                {"type":"object","properties":{
                  "anio":{"type":"integer","minimum":2000,"maximum":2100},
                  "mes":{"type":"integer","minimum":1,"maximum":12},
                  "tipo":{"type":"string","enum":["cfdi","estado_de_cuenta","ticket","constancia","otro"]},
                  "direccion":{"type":"string","enum":["emitida","recibida"]},
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
                "Marca como correcto un dato que ya está en el perfil (extraído de la constancia o inferido) SOLO cuando la persona " +
                "te dijo explícitamente que es correcto.",
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
            _ => null,
        };
    }

    /// <summary>Las de la conversación de toda la cartera (solo equipo): el resumen, nada del detalle de un cliente.</summary>
    public static IReadOnlyList<HerramientaDelModelo> DeCartera() =>
    [
        new(ResumenDeCartera,
            "Resumen de TODOS los clientes del despacho para un mes: semáforo (al día, atención, urgente), RFC, perfil completo en %, " +
            "documentos, facturas y estados de cuenta del mes, estados que no cuadran, parejas por confirmar, movimientos sin " +
            "factura y la lista de pendientes de cada uno.",
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
                ConciliacionDelMes => await ConciliacionAsync(organizacionId, uso.Entrada),
                BuscarDocumentos => await BuscarAsync(organizacionId, uso.Entrada),
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
            emitido = r.Tipo == TipoDeDocumento.Cfdi ? r.TotalEmitido : (decimal?)null,
            recibido = r.Tipo == TipoDeDocumento.Cfdi ? r.TotalRecibido : (decimal?)null,
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

        filtro.Direccion = Opcional(entrada, "direccion") switch
        {
            "emitida" => DireccionDelCfdi.Emitido,
            "recibida" => DireccionDelCfdi.Recibido,
            _ => null,
        };

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
                factura = d.Cfdi == null
                    ? null
                    : new
                    {
                        direccion = d.Cfdi.Direccion switch
                        {
                            DireccionDelCfdi.Emitido => "emitida",
                            DireccionDelCfdi.Recibido => "recibida",
                            _ => "no es del RFC del cliente",
                        },
                        tipo = d.Cfdi.TipoDeComprobante,
                        emisor = $"{d.Cfdi.EmisorNombre} ({d.Cfdi.EmisorRfc})",
                        receptor = $"{d.Cfdi.ReceptorNombre} ({d.Cfdi.ReceptorRfc})",
                        total = d.Cfdi.Total,
                        moneda = d.Cfdi.Moneda,
                        concepto = d.Cfdi.Concepto,
                        uuid = d.Cfdi.Uuid,
                    },
            }),
        }, Json);
    }

    private async Task<string> InvitarAsync(Guid organizacionId, JsonElement entrada)
    {
        var papel = Texto(entrada, "papel") switch
        {
            "dueño" => RolEnOrganizacion.Administrador,
            "miembro" => RolEnOrganizacion.Miembro,
            "lector" => RolEnOrganizacion.Lector,
            var otro => throw new UserFriendlyException($"Papel desconocido: «{otro}». Usa dueño, miembro o lector."),
        };
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

    private async Task<string> ConciliacionAsync(Guid organizacionId, JsonElement entrada)
    {
        var anio = Entero(entrada, "anio") ?? throw new UserFriendlyException("Falta «anio».");
        var mes = Entero(entrada, "mes") ?? throw new UserFriendlyException("Falta «mes».");
        var c = await _conciliacion.GetDelMesAsync(organizacionId, anio, mes);

        object Mov(MovimientoDelBancoDto m) => new
        {
            fecha = m.Fecha.ToString("yyyy-MM-dd"),
            concepto = m.Concepto,
            monto = m.Abono > 0 ? m.Abono : m.Cargo,
        };

        object Fac(FacturaDelMesDto f) => new
        {
            fecha = f.Fecha.ToString("yyyy-MM-dd"),
            contraparte = $"{f.Contraparte} ({f.RfcContraparte})",
            total = f.Total,
        };

        return JsonSerializer.Serialize(new
        {
            periodo = $"{anio}-{mes:00}",
            hayEstadosDeCuenta = c.HayEstadosDeCuenta,
            estadosQueNoCuadran = c.EstadosSinRevisar,
            conciliados = c.Confirmadas.Count,
            propuestasPorRevisar = c.Propuestas.Count,
            depositosSinFactura = c.DepositosSinFactura.Select(Mov),
            cargosSinFactura = c.CargosSinFactura.Select(Mov),
            ventasSinDeposito = c.VentasSinDeposito.Select(Fac),
            gastosSinCargoEnElBanco = c.GastosSinCargo.Select(Fac),
            sinFacturaJustificados = c.SinFacturaJustificados.Count,
        }, Json);
    }

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
