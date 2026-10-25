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
using AuxiliarContable.EstadosDeCuenta;
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
    public const string ProponerParejas = "proponer_parejas";
    public const string ConfirmarPareja = "confirmar_pareja";
    public const string RechazarPareja = "rechazar_pareja";
    public const string UnirAMano = "unir_a_mano";
    public const string MarcarSinFactura = "marcar_sin_factura";
    public const string EstadosDeCuenta = "estados_de_cuenta";
    public const string RevisarEstadoDeCuenta = "revisar_estado_de_cuenta";
    public const string ConfirmarEstadoDeCuenta = "confirmar_estado_de_cuenta";
    public const string CorregirSaldos = "corregir_saldos";
    public const string CorregirMovimiento = "corregir_movimiento";
    public const string PersonasDelCliente = "personas_del_cliente";
    public const string CambiarPapel = "cambiar_papel";
    public const string QuitarPersona = "quitar_persona";

    private const string EsquemaDeId = """{"type":"object","properties":{"id":{"type":"string","format":"uuid"}},"required":["id"]}""";

    private const string EsquemaDelMes =
        """{"type":"object","properties":{"anio":{"type":"integer","minimum":2000,"maximum":2100},"mes":{"type":"integer","minimum":1,"maximum":12}},"required":["anio","mes"]}""";

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
    private readonly IEstadosDeCuentaAppService _estados;

    public HerramientasDelAsistente(
        IPerfilFiscalAppService perfiles,
        IBovedaAppService boveda,
        IConciliacionAppService conciliacion,
        ICarteraAppService cartera,
        IOrganizacionAppService organizaciones,
        IInvitacionAppService invitaciones,
        IEstadosDeCuentaAppService estados)
    {
        _estados = estados;
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
    public static IReadOnlyList<HerramientaDelModelo> Disponibles(bool puedeEditar, bool puedeInvitar = false, bool esEquipo = false)
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
                "lo que FALTA: depósitos sin factura, cargos sin factura, ventas de contado sin depósito y gastos sin cargo, y las " +
                "parejas propuestas por revisar. Úsala para «¿qué me falta?» o «¿qué facturas me faltan de octubre?». No cambia nada. " +
                "Los «id» son para las herramientas de conciliación: NUNCA se los enseñes a la persona.",
                EsquemaDelMes),
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
            new(EstadosDeCuenta,
                "Los estados de cuenta del cliente activo leídos por la IA: banco, periodo, saldos, si CUADRA (saldo inicial + " +
                "abonos − cargos = saldo final), la diferencia y su revisión (por confirmar, requiere revisión, confirmado). " +
                "Uno que no cuadra no entra a la conciliación hasta que se corrija o se confirme. Los «id» no se enseñan.",
                """{"type":"object","properties":{}}"""),
            new(RevisarEstadoDeCuenta,
                "Detalle de un estado de cuenta para encontrar por qué no cuadra: los renglones donde el saldo corrido salta " +
                "(ahí la IA leyó mal un monto) con sus vecinos. id = el de estados_de_cuenta.",
                EsquemaDeId),
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

            // La conciliación desde el chat. Las uniones las decide una PERSONA: el modelo propone y
            // explica, y solo confirma, rechaza o une lo que la persona aprobó en la conversación.
            lista.Add(new(ProponerParejas,
                "Busca automáticamente qué movimiento del banco va con qué factura en un mes y deja las parejas como PROPUESTAS " +
                "(no confirma nada). Después llama a conciliacion_del_mes para verlas y explícaselas a la persona.",
                EsquemaDelMes));
            lista.Add(new(ConfirmarPareja,
                "Confirma una pareja propuesta (movimiento del banco = factura) SOLO cuando la persona la aprobó de forma explícita " +
                "(«sí, esa es», «confirma todas las de 100 puntos»). id = el de la propuesta en conciliacion_del_mes.",
                """{"type":"object","properties":{"id":{"type":"string","format":"uuid"}},"required":["id"]}"""));
            lista.Add(new(RechazarPareja,
                "Descarta una pareja propuesta cuando la persona dice que NO van juntos. No se vuelve a proponer.",
                """{"type":"object","properties":{"id":{"type":"string","format":"uuid"}},"required":["id"]}"""));
            lista.Add(new(UnirAMano,
                "Une un movimiento del banco con una factura que la persona identificó (cuando no hubo propuesta). Ids de " +
                "conciliacion_del_mes: movimientoId de depósitos/cargos sin factura, facturaId de ventas/gastos sin movimiento.",
                """{"type":"object","properties":{"movimientoId":{"type":"string","format":"uuid"},"facturaId":{"type":"string","format":"uuid"}},"required":["movimientoId","facturaId"]}"""));
            lista.Add(new(MarcarSinFactura,
                "Marca un movimiento del banco como «no lleva factura» con la razón que dio la persona (traspaso entre cuentas, " +
                "préstamo, comisión del banco…). Solo con la razón que ELLA dio; no la inventes.",
                """{"type":"object","properties":{"movimientoId":{"type":"string","format":"uuid"},"porque":{"type":"string","minLength":3,"maxLength":200}},"required":["movimientoId","porque"]}"""));

            // Estados de cuenta: las correcciones son las que la PERSONA lee en su PDF; el modelo no las deduce.
            lista.Add(new(ConfirmarEstadoDeCuenta,
                "Da por buena la lectura de un estado de cuenta SOLO cuando la persona lo aprobó. Si no cuadra, aunqueNoCuadre=true " +
                "solo si ella lo pide explícitamente sabiendo la diferencia.",
                """{"type":"object","properties":{"id":{"type":"string","format":"uuid"},"aunqueNoCuadre":{"type":"boolean"}},"required":["id"]}"""));
            lista.Add(new(CorregirSaldos,
                "Corrige el saldo inicial o final de un estado de cuenta con el valor que la persona LEYÓ en su PDF. Devuelve si ya cuadra.",
                """{"type":"object","properties":{"id":{"type":"string","format":"uuid"},"saldoInicial":{"type":"number"},"saldoFinal":{"type":"number"}},"required":["id"]}"""));
            lista.Add(new(CorregirMovimiento,
                "Corrige un renglón mal leído (fecha AAAA-MM-DD, concepto, cargo o abono; uno de los dos en 0) con lo que la persona " +
                "LEYÓ en su PDF. id = el del estado; movimientoId = el del renglón en revisar_estado_de_cuenta. Devuelve si ya cuadra.",
                """{"type":"object","properties":{"id":{"type":"string","format":"uuid"},"movimientoId":{"type":"string","format":"uuid"},"fecha":{"type":"string","format":"date"},"concepto":{"type":"string","maxLength":500},"cargo":{"type":"number","minimum":0},"abono":{"type":"number","minimum":0}},"required":["id","movimientoId","fecha","concepto","cargo","abono"]}"""));
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
            ProponerParejas => "Buscó parejas para conciliar",
            ConfirmarPareja => "Confirmó una pareja de la conciliación",
            RechazarPareja => "Descartó una pareja propuesta",
            UnirAMano => "Unió un movimiento con su factura",
            MarcarSinFactura => "Marcó un movimiento sin factura",
            ConfirmarEstadoDeCuenta => "Confirmó un estado de cuenta",
            CorregirSaldos => "Corrigió los saldos de un estado de cuenta",
            CorregirMovimiento => "Corrigió un renglón del estado de cuenta",
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
                ProponerParejas => await ProponerAsync(organizacionId, uso.Entrada),
                ConfirmarPareja => await HechoAsync(() => _conciliacion.ConfirmarAsync(organizacionId, Id(uso.Entrada, "id")), "Pareja confirmada."),
                RechazarPareja => await HechoAsync(() => _conciliacion.RechazarAsync(organizacionId, Id(uso.Entrada, "id")), "Pareja descartada."),
                UnirAMano => await HechoAsync(() => _conciliacion.ConciliarAManoAsync(organizacionId, new ConciliarAManoDto
                {
                    MovimientoId = Id(uso.Entrada, "movimientoId"),
                    CfdiId = Id(uso.Entrada, "facturaId"),
                }), "Unidos."),
                MarcarSinFactura => await HechoAsync(() => _conciliacion.MarcarSinFacturaAsync(organizacionId, new MarcarSinFacturaDto
                {
                    MovimientoId = Id(uso.Entrada, "movimientoId"),
                    Porque = Texto(uso.Entrada, "porque").Trim(),
                }), "Marcado sin factura."),
                PersonasDelCliente => Personas(await _organizaciones.GetAsync(organizacionId)),
                CambiarPapel => Personas(await _organizaciones.CambiarRolDeMiembroAsync(organizacionId, Id(uso.Entrada, "userId"),
                    new CambiarRolEnOrganizacionDto { Rol = Papel(Texto(uso.Entrada, "papel")) })),
                QuitarPersona => Personas(await _organizaciones.QuitarMiembroAsync(organizacionId, Id(uso.Entrada, "userId"))),
                EstadosDeCuenta => ListaDeEstados(await _estados.GetListAsync(organizacionId)),
                RevisarEstadoDeCuenta => DetalleDeEstado(await _estados.GetDetalleAsync(organizacionId, Id(uso.Entrada, "id"))),
                ConfirmarEstadoDeCuenta => Cuadre(await _estados.ConfirmarAsync(organizacionId, Id(uso.Entrada, "id"),
                    uso.Entrada.TryGetProperty("aunqueNoCuadre", out var aunque) && aunque.ValueKind == JsonValueKind.True)),
                CorregirSaldos => Cuadre(await CorregirSaldosAsync(organizacionId, uso.Entrada)),
                CorregirMovimiento => Cuadre(await _estados.CorregirMovimientoAsync(organizacionId, Id(uso.Entrada, "id"), new CorregirMovimientoDto
                {
                    MovimientoId = Id(uso.Entrada, "movimientoId"),
                    Fecha = DateTime.TryParse(Texto(uso.Entrada, "fecha"), System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var fecha) ? fecha : throw new UserFriendlyException("«fecha» va como AAAA-MM-DD."),
                    Concepto = Texto(uso.Entrada, "concepto"),
                    Cargo = Decimal(uso.Entrada, "cargo") ?? 0m,
                    Abono = Decimal(uso.Entrada, "abono") ?? 0m,
                })),
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
            return new BloqueDeResultadoDeHerramienta(uso.Id, "No existe en este cliente. Vuelve a consultar con conciliacion_del_mes.", EsError: true);
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

    private async Task<string> ConciliacionAsync(Guid organizacionId, JsonElement entrada)
    {
        var anio = Entero(entrada, "anio") ?? throw new UserFriendlyException("Falta «anio».");
        var mes = Entero(entrada, "mes") ?? throw new UserFriendlyException("Falta «mes».");
        var c = await _conciliacion.GetDelMesAsync(organizacionId, anio, mes);

        object Mov(MovimientoDelBancoDto m) => new
        {
            id = m.Id,
            fecha = m.Fecha.ToString("yyyy-MM-dd"),
            banco = m.Banco,
            concepto = m.Concepto,
            monto = m.Abono > 0 ? m.Abono : m.Cargo,
        };

        object Fac(FacturaDelMesDto f) => new
        {
            id = f.Id,
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
            // Puntaje 0-100 del emparejador; «motivo» dice por qué (y «revisa» si hay otra candidata).
            propuestasPorRevisar = c.Propuestas.Select(p => new { id = p.Id, puntaje = p.Puntaje, motivo = p.Motivo, movimiento = Mov(p.Movimiento), factura = Fac(p.Factura) }),
            depositosSinFactura = c.DepositosSinFactura.Select(Mov),
            cargosSinFactura = c.CargosSinFactura.Select(Mov),
            ventasSinDeposito = c.VentasSinDeposito.Select(Fac),
            gastosSinCargoEnElBanco = c.GastosSinCargo.Select(Fac),
            sinFacturaJustificados = c.SinFacturaJustificados.Count,
        }, Json);
    }

    private static object Resumen(EstadoDeCuentaResumenDto e) => new
    {
        id = e.Id,
        banco = e.Banco,
        cuenta = e.Cuenta is null ? null : $"…{e.Cuenta}",
        periodo = $"{e.PeriodoInicio:yyyy-MM-dd} a {e.PeriodoFin:yyyy-MM-dd}",
        moneda = e.Moneda,
        saldoInicial = e.SaldoInicial,
        saldoFinal = e.SaldoFinal,
        abonos = e.TotalAbonos,
        cargos = e.TotalCargos,
        cuadra = e.Cuadra,
        diferencia = e.Diferencia,
        saltosDeSaldo = e.SaltosDeSaldo,
        movimientos = e.Movimientos,
        revision = e.Revision switch
        {
            RevisionDelEstadoDeCuenta.Confirmado => "confirmado",
            RevisionDelEstadoDeCuenta.RequiereRevision => "requiere revisión",
            _ => "por confirmar",
        },
    };

    private static string ListaDeEstados(List<EstadoDeCuentaResumenDto> estados) =>
        estados.Count == 0
            ? "Este cliente no tiene estados de cuenta leídos. Pídele que mande el PDF de su banco con el clip."
            : JsonSerializer.Serialize(estados.Select(Resumen), Json);

    /// <summary>Solo los renglones sospechosos y sus vecinos: un estado puede tener cientos.</summary>
    private static string DetalleDeEstado(EstadoDeCuentaDto e)
    {
        var sospechosos = e.Renglones.Select((r, i) => (r, i)).Where(x => x.r.SaltoDeSaldo).Select(x => x.i).ToHashSet();
        var aMostrar = e.Renglones.Where((_, i) => sospechosos.Contains(i) || sospechosos.Contains(i + 1) || sospechosos.Contains(i - 1));
        return JsonSerializer.Serialize(new
        {
            estado = Resumen(e),
            renglonesConSaltoDeSaldo = sospechosos.Count,
            renglones = aMostrar.Take(40).Select(r => new
            {
                movimientoId = r.Id,
                orden = r.Orden,
                fecha = r.Fecha.ToString("yyyy-MM-dd"),
                concepto = r.Concepto,
                cargo = r.Cargo,
                abono = r.Abono,
                saldo = r.Saldo,
                saltaAqui = r.SaltoDeSaldo,
            }),
        }, Json);
    }

    /// <summary>
    /// El servicio reemplaza LOS DOS saldos (la pantalla siempre manda ambos); en el chat la persona
    /// suele dictar uno, así que el que no dijo se conserva tal cual estaba.
    /// </summary>
    private async Task<EstadoDeCuentaDto> CorregirSaldosAsync(Guid organizacionId, JsonElement entrada)
    {
        var id = Id(entrada, "id");
        var actual = await _estados.GetDetalleAsync(organizacionId, id);
        return await _estados.CorregirSaldosAsync(organizacionId, id, new CorregirSaldosDto
        {
            SaldoInicial = Decimal(entrada, "saldoInicial") ?? actual.SaldoInicial,
            SaldoFinal = Decimal(entrada, "saldoFinal") ?? actual.SaldoFinal,
        });
    }

    private static string Cuadre(EstadoDeCuentaDto e) => JsonSerializer.Serialize(Resumen(e), Json);

    private static decimal? Decimal(JsonElement entrada, string propiedad) =>
        entrada.ValueKind == JsonValueKind.Object && entrada.TryGetProperty(propiedad, out var v) && v.TryGetDecimal(out var d) ? d : null;

    private async Task<string> ProponerAsync(Guid organizacionId, JsonElement entrada)
    {
        var anio = Entero(entrada, "anio") ?? throw new UserFriendlyException("Falta «anio».");
        var mes = Entero(entrada, "mes") ?? throw new UserFriendlyException("Falta «mes».");
        var c = await _conciliacion.ProponerAsync(organizacionId, anio, mes);
        return JsonSerializer.Serialize(new { periodo = $"{anio}-{mes:00}", propuestas = c.Propuestas.Count, yaConciliados = c.Confirmadas.Count }, Json);
    }

    private static async Task<string> HechoAsync(Func<Task> accion, string mensaje)
    {
        await accion();
        return mensaje;
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
