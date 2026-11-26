using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.LeyesDelCliente;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Normativa;
using System.Text.Json.Nodes;
using AuxiliarContable.Organizaciones;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace AuxiliarContable.Conversaciones;

/// <summary>
/// Fase 4a del asistente: CONSULTAR y EXPLICAR lo que ya está guardado del cliente (operaciones con sus tres patas,
/// el porqué de la deducibilidad que dejó el motor, pólizas, impuestos y las leyes que le aplican). Solo lee: nada de
/// aquí cambia datos ni pide cálculos nuevos.
///
/// <para>Los permisos son los de las pantallas: ver al cliente (<see cref="AccionEnOrganizacion.Ver"/>, lo ajeno = 404)
/// para operaciones, explicación y leyes; pólizas e impuestos completos solo el equipo del despacho, como en la mesa de
/// trabajo. Al cliente se le explica el porqué de SU factura (motivos con artículo y cómo corregirlo), sin las pólizas.</para>
/// </summary>
public class ConsultasDelAsistente : ITransientDependency
{
    /// <summary>Lo más que se le pasa al modelo de una lista (cada renglón cuesta tokens en cada turno).</summary>
    public const int MaxResultados = 20;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Autorizador _autorizador;
    private readonly IRepository<Operacion, Guid> _operaciones;
    private readonly IRepository<ImpuestosDelMes, Guid> _impuestos;
    private readonly IRepository<PolizasDelMes, Guid> _polizas;
    private readonly ILeyesDelClienteAppService _leyes;
    private readonly IAsyncQueryableExecuter _async;
    private readonly AnalizadorNormativo _normativa;
    private readonly IMotorFiscal _motor;

    public ConsultasDelAsistente(
        Autorizador autorizador,
        IRepository<Operacion, Guid> operaciones,
        IRepository<ImpuestosDelMes, Guid> impuestos,
        IRepository<PolizasDelMes, Guid> polizas,
        ILeyesDelClienteAppService leyes,
        IAsyncQueryableExecuter ejecutor,
        AnalizadorNormativo normativa,
        IMotorFiscal motor)
    {
        _normativa = normativa;
        _motor = motor;
        _autorizador = autorizador;
        _operaciones = operaciones;
        _impuestos = impuestos;
        _polizas = polizas;
        _leyes = leyes;
        _async = ejecutor;
    }

    // ---------------------------------------------------------------- operaciones

    /// <summary>Filtros de <see cref="BuscarOperacionesAsync"/>; todos opcionales.</summary>
    public sealed record FiltroDeOperaciones(
        string? Texto, int? Anio, int? Mes, decimal? ImporteMinimo, decimal? ImporteMaximo, string? Sentido, string? Falta);

    public async Task<string> BuscarOperacionesAsync(Guid organizacionId, FiltroDeOperaciones f)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);

        var q = (await _operaciones.GetQueryableAsync()).Where(o => o.OrganizacionId == organizacionId);
        if (f.Anio is { } anio)
        {
            if (f.Mes is { } mes)
            {
                if (mes is < 1 or > 12)
                {
                    throw new UserFriendlyException("Mes no válido.");
                }

                var desde = new DateTime(anio, mes, 1);
                var hasta = desde.AddMonths(1);
                q = q.Where(o => o.Fecha >= desde && o.Fecha < hasta);
            }
            else
            {
                var desde = new DateTime(anio, 1, 1);
                var hasta = desde.AddYears(1);
                q = q.Where(o => o.Fecha >= desde && o.Fecha < hasta);
            }
        }

        if (!string.IsNullOrWhiteSpace(f.Texto))
        {
            var t = f.Texto.Trim().ToLower();
            q = q.Where(o => o.Descripcion.ToLower().Contains(t)
                             || (o.Contraparte != null && o.Contraparte.ToLower().Contains(t))
                             || (o.Uuid != null && o.Uuid.ToLower().Contains(t))
                             || (o.Referencia != null && o.Referencia.ToLower().Contains(t)));
        }

        if (f.ImporteMinimo is { } min)
        {
            q = q.Where(o => o.Importe >= min);
        }

        if (f.ImporteMaximo is { } max)
        {
            q = q.Where(o => o.Importe <= max);
        }

        q = f.Sentido switch
        {
            null => q,
            "entrada" => q.Where(o => o.Sentido == SentidoDeOperacion.Entrada),
            "salida" => q.Where(o => o.Sentido == SentidoDeOperacion.Salida),
            var otro => throw new UserFriendlyException($"Sentido desconocido: «{otro}». Usa entrada o salida."),
        };

        q = f.Falta switch
        {
            null => q,
            "factura" => q.Where(o => o.FacturaEstado != EstadoDePata.Ligada),
            "pago" => q.Where(o => o.TransaccionEstado != EstadoDePata.Ligada),
            "control" => q.Where(o => o.ControlEstado != EstadoDePata.Ligada),
            var otro => throw new UserFriendlyException($"«falta» desconocido: «{otro}». Usa factura, pago o control."),
        };

        var total = await _async.CountAsync(q);
        var lista = await _async.ToListAsync(q.OrderByDescending(o => o.Fecha).ThenByDescending(o => o.Importe).Take(MaxResultados));
        if (total == 0)
        {
            return "No hay operaciones con esos filtros. Prueba con menos filtros u otras palabras (nombre del proveedor o cliente, concepto o UUID).";
        }

        return JsonSerializer.Serialize(new
        {
            total,
            mostradas = lista.Count,
            operaciones = lista.Select(Resumen),
        }, Json);
    }

    /// <summary>
    /// Una operación (por su id o por el UUID de su factura) con sus tres patas y el PORQUÉ: lo que el motor concluyó de
    /// su deducibilidad en el cálculo de impuestos (motivo, hipótesis, artículo, cómo corregirlo) y lo que la revisión de
    /// las pólizas encontró. El equipo ve además sus pólizas.
    /// </summary>
    public async Task<string> ExplicarOperacionAsync(Guid organizacionId, Guid? operacionId, string? uuid)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        var equipo = (await _autorizador.QuienMiraAsync()).Equipo;

        Operacion? o;
        if (operacionId is { } id)
        {
            o = await _operaciones.FindAsync(id);
            if (o == null || o.OrganizacionId != organizacionId)
            {
                throw new EntityNotFoundException(typeof(Operacion), id);
            }
        }
        else if (!string.IsNullOrWhiteSpace(uuid))
        {
            var u = uuid.Trim().ToLower();
            o = await _async.FirstOrDefaultAsync((await _operaciones.GetQueryableAsync())
                .Where(x => x.OrganizacionId == organizacionId && x.Uuid != null && x.Uuid.ToLower() == u));
        }
        else
        {
            throw new UserFriendlyException("Falta «operacionId» o «uuid». Búscala primero con buscar_operaciones.");
        }

        var uuidDeLaFactura = o?.Uuid ?? uuid?.Trim();
        var calculo = uuidDeLaFactura == null ? null : await DelCalculoAsync(organizacionId, uuidDeLaFactura, o?.Fecha);
        if (o == null && calculo == null)
        {
            return "No hay ninguna operación ni factura con ese UUID en este cliente.";
        }

        var (revision, polizas) = o == null ? (new List<object>(), new List<object>()) : await DeLasPolizasAsync(organizacionId, o);

        return JsonSerializer.Serialize(new
        {
            operacion = o == null ? null : Detalle(o),
            deducibilidad = calculo == null
                ? (object)"Esta factura todavía no aparece en un cálculo de impuestos guardado (puede que no se haya calculado el mes o que no sea un CFDI de gasto o ingreso)."
                : calculo,
            revision = revision.Count == 0 ? null : revision,
            polizas = equipo && polizas.Count > 0 ? polizas : null,
            nota = equipo ? null : "Explica con estos motivos y su artículo; si hay «solucion», dile cómo corregirlo. Las pólizas solo las ve el despacho.",
        }, Json);
    }

    private static object Resumen(Operacion o) => new
    {
        id = o.Id,
        fecha = o.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        sentido = o.Sentido == SentidoDeOperacion.Entrada ? "entrada" : "salida",
        importe = o.Importe,
        descripcion = Corto(o.Descripcion, 120),
        contraparte = o.Contraparte,
        factura = Estado(o.FacturaEstado) + (o.Uuid != null ? $" (UUID {o.Uuid})" : ""),
        pago = Estado(o.TransaccionEstado) + (o.TransaccionForma is { } forma ? $" ({Forma(forma)})" : ""),
        control = Estado(o.ControlEstado),
    };

    private static object Detalle(Operacion o) => new
    {
        id = o.Id,
        fecha = o.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        sentido = o.Sentido == SentidoDeOperacion.Entrada ? "entrada (cobro o ingreso)" : "salida (pago o gasto)",
        importe = o.Importe,
        descripcion = o.Descripcion,
        contraparte = o.Contraparte,
        factura = new { estado = Estado(o.FacturaEstado), uuid = o.Uuid, motivo = o.FacturaMotivo },
        pago = new
        {
            estado = Estado(o.TransaccionEstado),
            forma = o.TransaccionForma is { } forma ? Forma(forma) : null,
            referencia = o.Referencia,
            delBanco = o.ClaveDeMovimiento != null ? true : (bool?)null,
            motivo = o.TransaccionMotivo,
        },
        control = new { estado = Estado(o.ControlEstado), motivo = o.ControlMotivo },
        revisionAceptada = string.IsNullOrWhiteSpace(o.Aceptados) ? null : o.Aceptados,
    };

    private static string Estado(EstadoDePata e) => e switch
    {
        EstadoDePata.Ligada => "ligada",
        EstadoDePata.NoHay => "no hay",
        _ => "pendiente",
    };

    private static string Forma(FormaDeTransaccion f) => f switch
    {
        FormaDeTransaccion.Efectivo => "efectivo",
        FormaDeTransaccion.Credito => "a crédito",
        _ => "banco",
    };

    /// <summary>
    /// La factura en el cálculo de impuestos guardado más reciente del año que la incluya (el detalle va de enero al mes
    /// calculado). Sin pedir cálculos: lo que ya está.
    /// </summary>
    private async Task<object?> DelCalculoAsync(Guid organizacionId, string uuid, DateTime? fecha)
    {
        var q = (await _impuestos.GetQueryableAsync()).Where(i => i.OrganizacionId == organizacionId && i.Resultado != null);
        if (fecha is { } f)
        {
            var anio = f.Year;
            var mes = f.Month;
            q = q.Where(i => i.Anio == anio && i.Mes >= mes);
        }

        var filas = await _async.ToListAsync(q.OrderByDescending(i => i.Anio).ThenByDescending(i => i.Mes).Take(12));

        foreach (var fila in filas)
        {
            PagosProvisionales? r;
            try
            {
                r = JsonSerializer.Deserialize<PagosProvisionales>(fila.Resultado!);
            }
            catch (JsonException)
            {
                continue;
            }

            if (r?.Detalle == null)
            {
                continue;
            }

            var ingreso = r.Detalle.Ingresos.FirstOrDefault(x => string.Equals(x.Uuid, uuid, StringComparison.OrdinalIgnoreCase));
            var deduccion = r.Detalle.Deducciones.FirstOrDefault(x => string.Equals(x.Uuid, uuid, StringComparison.OrdinalIgnoreCase));
            if (ingreso == null && deduccion == null)
            {
                continue;
            }

            var x = deduccion ?? ingreso!;
            return new
            {
                calculo = $"{fila.Anio:0000}-{fila.Mes:00}",
                lado = deduccion != null ? "gasto (deducción)" : "ingreso",
                emisorOReceptor = string.IsNullOrWhiteSpace(x.Nombre) ? x.Rfc : x.Nombre,
                fecha = x.Fecha,
                tipo = x.Tipo,
                metodo = x.Metodo,
                forma = x.Forma,
                uso = x.Uso,
                concepto = Corto(x.Concepto, 160),
                total = x.Total,
                baseSinIva = x.Base,
                iva = x.Iva,
                estado = x.Estado,
                deducible = deduccion != null ? x.Deducible : (decimal?)null,
                ivaAcreditable = deduccion != null ? x.IvaAcreditable : (decimal?)null,
                acumulable = ingreso != null && deduccion == null ? x.Acumulable : (decimal?)null,
                proporcionNoDeducible = x.ProporcionNoDeducible == 0 ? (decimal?)null : x.ProporcionNoDeducible,
                meses = x.Meses,
                motivos = x.Motivos.Select(m => new
                {
                    regla = m.Regla,
                    clase = m.Clase,
                    hipotesis = m.Hipotesis,
                    articulo = m.Articulo,
                    resultado = m.Resultado,
                    concepto = string.IsNullOrWhiteSpace(m.Concepto) ? null : m.Concepto,
                    importe = m.Importe,
                    noDeducible = m.NoDeducible,
                    solucion = string.IsNullOrWhiteSpace(m.Solucion) ? null : m.Solucion,
                    pregunta = string.IsNullOrWhiteSpace(m.Pregunta) ? null : m.Pregunta,
                    explicacion = m.Explicacion,
                }),
            };
        }

        return null;
    }

    /// <summary>Lo que la revisión de las pólizas del mes dijo de la operación y, para el equipo, sus pólizas.</summary>
    private async Task<(List<object> Revision, List<object> Polizas)> DeLasPolizasAsync(Guid organizacionId, Operacion o)
    {
        var fila = await _async.FirstOrDefaultAsync((await _polizas.GetQueryableAsync())
            .Where(p => p.OrganizacionId == organizacionId && p.Anio == o.Fecha.Year && p.Mes == o.Fecha.Month));
        if (fila == null)
        {
            return ([], []);
        }

        ResultadoContable? r;
        try
        {
            r = JsonSerializer.Deserialize<ResultadoContable>(fila.Resultado);
        }
        catch (JsonException)
        {
            return ([], []);
        }

        if (r == null)
        {
            return ([], []);
        }

        var id = o.Id.ToString();
        var revision = r.Revision.Where(x => x.Operacion == id)
            .Select(x => (object)new { codigo = x.Codigo, nivel = x.Nivel, mensaje = x.Mensaje }).ToList();
        var polizas = r.Polizas.Where(p => p.Operacion == id).Select(PolizaCorta).ToList();
        return (revision, polizas);
    }

    private static object PolizaCorta(PolizaContable p) => new
    {
        tipo = p.Tipo,
        numero = p.Numero,
        fecha = p.Fecha,
        concepto = Corto(p.Concepto, 120),
        uuid = p.Uuid,
        categoria = p.Categoria,
        movimientos = p.Movimientos.Select(m => new
        {
            cuenta = $"{m.Cuenta} {m.Nombre}".Trim(),
            debe = m.Debe == 0 ? (decimal?)null : m.Debe,
            haber = m.Haber == 0 ? (decimal?)null : m.Haber,
        }),
    };

    // ---------------------------------------------------------------- pólizas e impuestos (solo equipo)

    public async Task<string> PolizasDelMesAsync(Guid organizacionId, int anio, int mes, string? texto)
    {
        await ExigirEquipoAsync(organizacionId);
        Mes(anio, mes);
        var fila = await _async.FirstOrDefaultAsync((await _polizas.GetQueryableAsync())
            .Where(p => p.OrganizacionId == organizacionId && p.Anio == anio && p.Mes == mes));
        var r = fila == null ? null : JsonSerializer.Deserialize<ResultadoContable>(fila.Resultado);
        if (fila == null || r == null)
        {
            return $"No hay pólizas generadas de {anio}-{mes:00}. Se generan en la mesa de trabajo (o solas con la contabilidad al día).";
        }

        var polizas = r.Polizas.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(texto))
        {
            var t = texto.Trim();
            polizas = polizas.Where(p => p.Concepto.Contains(t, StringComparison.OrdinalIgnoreCase)
                                         || (p.Uuid?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)
                                         || p.Movimientos.Any(m => m.Nombre.Contains(t, StringComparison.OrdinalIgnoreCase)
                                                                   || m.Cuenta.StartsWith(t, StringComparison.OrdinalIgnoreCase)));
        }

        var lista = polizas.ToList();
        return JsonSerializer.Serialize(new
        {
            periodo = $"{anio:0000}-{mes:00}",
            generadas = fila.GeneradoUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC",
            total = r.Polizas.Count,
            coinciden = lista.Count,
            polizas = lista.Take(MaxResultados).Select(PolizaCorta),
            detenidas = r.Revision.Where(x => x.Nivel == "detiene").Select(x => x.Operacion).Distinct().Count(),
            avisosDelMes = r.Revision.Where(x => x.Operacion == null).Select(x => x.Mensaje).Take(10),
            pendientesDeCierre = r.PendientesDeCierre,
        }, Json);
    }

    public async Task<string> ImpuestosDelMesAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId);
        Mes(anio, mes);
        var fila = await _async.FirstOrDefaultAsync((await _impuestos.GetQueryableAsync())
            .Where(i => i.OrganizacionId == organizacionId && i.Anio == anio && i.Mes == mes));
        var r = fila?.Resultado == null ? null : JsonSerializer.Deserialize<PagosProvisionales>(fila.Resultado);
        var periodo = $"{anio:0000}-{mes:00}";
        var delMes = r?.Meses.FirstOrDefault(m => m.Periodo == periodo);
        if (fila == null || r == null || delMes == null)
        {
            return $"No hay cálculo de impuestos guardado de {periodo}{(fila?.Error is { } e ? $" (último intento: {e})" : "")}. " +
                   "Se calcula solo al entrar facturas; en la pantalla de impuestos se puede pedir «Actualizar ahora».";
        }

        static IEnumerable<object> Renglones(List<RenglonDelPapel> l) => l.Select(x => new
        {
            concepto = x.Concepto,
            importe = x.Importe,
            fundamento = string.IsNullOrWhiteSpace(x.Fundamento) ? null : x.Fundamento,
        });

        return JsonSerializer.Serialize(new
        {
            periodo,
            calculado = fila.CalculadoUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            porActualizar = fila.PorActualizar ? "entró algo nuevo: se va a recalcular en unos minutos" : null,
            regimen = r.Regimen.Nombre ?? r.Regimen.Principal,
            metodo = delMes.Metodo,
            facturas = new { fila.Facturas, fila.Canceladas, danadas = fila.Danadas, sinEstatus = fila.SinEstatus },
            isr = new { causado = delMes.IsrCausado, retenido = delMes.IsrRetenido, pagosAnteriores = delMes.IsrPagosAnteriores, aPagar = delMes.IsrAPagar, aFavor = delMes.IsrAFavor },
            iva = new { cobrado = delMes.IvaCobrado, acreditable = delMes.IvaAcreditable, aPagar = delMes.IvaAPagar, saldoAFavor = delMes.IvaSaldoAFavor },
            renglonesIsr = Renglones(delMes.RenglonesIsr),
            renglonesIva = Renglones(delMes.RenglonesIva),
            noConsideradas = r.Detalle?.Deducciones.Count(d => d.Estado is "no_considerada" or "parcial" or "por_definir"),
            avisos = r.Avisos.Take(10),
            supuestos = r.Supuestos.Take(10),
        }, Json);
    }

    // ---------------------------------------------------------------- leyes que le aplican

    public async Task<string> LeyesQueLeAplicanAsync(Guid organizacionId, string? tema)
    {
        // El servicio de la pantalla exige ver al cliente.
        var r = await _leyes.GetQueTeAplicanAsync(organizacionId);
        if (r.Leyes.Count == 0)
        {
            return r.Aviso ?? "Todavía no se puede decir qué leyes locales le aplican (falta el domicilio fiscal o la actividad en el perfil).";
        }

        var leyes = r.Leyes.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(tema))
        {
            var t = tema.Trim();
            leyes = leyes.Where(l => l.Nombre.Contains(t, StringComparison.OrdinalIgnoreCase)
                                     || (l.NombreOficial?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)
                                     || (l.Pide?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var lista = leyes.ToList();
        return JsonSerializer.Serialize(new
        {
            estado = r.NombreDelEstado ?? r.Estado,
            municipio = r.Municipio,
            avisoMunicipal = r.AvisoMunicipal,
            seTomoEnCuenta = r.Disparadores,
            total = r.Leyes.Count,
            leyes = lista.Take(30).Select(l => new
            {
                nombre = l.NombreOficial ?? l.Nombre,
                ambito = l.Ambito,
                porQue = l.Motivos.Count == 0 ? null : string.Join("; ", l.Motivos),
                quePide = l.Pide,
                estatus = l.Estatus switch
                {
                    "local" => "la tenemos",
                    "pendiente" => "no la encontramos en línea",
                    "no_existe" => "el municipio no tiene reglamento propio",
                    "estatal" => "lo rige la ley estatal",
                    "federal" => "lo rige la ley federal",
                    var e => e,
                },
                ultimaReforma = l.UltimaReforma,
                url = l.Url,
                nota = string.IsNullOrWhiteSpace(l.Nota) ? null : Corto(l.Nota, 300),
                loTratan = l.LoTratan.Count == 0 ? null : l.LoTratan.Select(x => x.Donde == null ? x.Nombre : $"{x.Nombre} ({x.Donde})"),
            }),
        }, Json);
    }

    // ---------------------------------------------------------------- antes de operar

    /// <summary>
    /// Lo que hay que revisar de una operación que el cliente PIENSA hacer (o ya hizo): el expediente de hipótesis normativa
    /// del motor, sin Claude: núcleo fiscal, leyes que activa su perfil y las descartadas con su motivo, preguntas
    /// obligatorias («chispazos») con sus fuentes, artículos del mapa y fragmentos de ley. Al motor va el perfil sin RFC
    /// ni nombre (<see cref="AnalizadorNormativo.PerfilAsync"/>); el modelo del chat lo razona.
    /// </summary>
    public async Task<string> AnalizarOperacionAsync(Guid organizacionId, string descripcion, DateTime? fecha,
        string? contraparteTipo, string? contraparteResidencia, IReadOnlyList<string> disparadores)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        descripcion = descripcion.Trim();
        if (descripcion.Length < 10)
        {
            throw new UserFriendlyException("Describe la operación con un poco más de detalle (qué, con quién, cómo se paga).");
        }

        var tipo = contraparteTipo?.Trim().ToLowerInvariant();
        if (tipo is not (null or "" or "fisica" or "moral"))
        {
            throw new UserFriendlyException("La contraparte es persona «fisica» o «moral».");
        }

        var conocidos = NormativaAppService.ListaDeDisparadores.Select(d => d.Clave).ToHashSet();
        var perfil = await _normativa.PerfilAsync(organizacionId, fecha?.Year);
        var operacion = new OperacionParaNormativa
        {
            Descripcion = descripcion,
            Fecha = fecha?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Contraparte = string.IsNullOrEmpty(tipo) && string.IsNullOrWhiteSpace(contraparteResidencia)
                ? null
                : new ContraparteParaNormativa { TipoPersona = string.IsNullOrEmpty(tipo) ? null : tipo, Residencia = contraparteResidencia?.Trim().ToUpperInvariant() },
            Disparadores = [.. disparadores.Where(conocidos.Contains).Distinct()],
        };

        return Compactar(await _motor.ExpedienteNormativoAsync(perfil, operacion));
    }

    /// <summary>
    /// El expediente cabe en el turno: listas con tope y fragmentos recortados (cada carácter cuesta tokens en cada vuelta).
    /// Lo que no se entiende se devuelve tal cual, recortado.
    /// </summary>
    public static string Compactar(string json)
    {
        JsonNode? raiz;
        try
        {
            raiz = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return Corto(json, 12_000);
        }

        if (raiz is not JsonObject o)
        {
            return Corto(json, 12_000);
        }

        void Tope(string clave, int max)
        {
            if (o[clave] is JsonArray a)
            {
                while (a.Count > max)
                {
                    a.RemoveAt(a.Count - 1);
                }
            }
        }

        Tope("leyes_descartadas", 15);
        Tope("mapa", 20);
        Tope("cadena", 10);
        Tope("fragmentos", 8);
        o.Remove("perfil");
        if (o["fragmentos"] is JsonArray fragmentos)
        {
            foreach (var f in fragmentos.OfType<JsonObject>())
            {
                if (f["texto"] is JsonValue v && v.TryGetValue<string>(out var t) && t.Length > 900)
                {
                    f["texto"] = t[..900] + " […]";
                }
            }
        }

        var texto = o.ToJsonString(Json);
        return Corto(texto, 20_000);
    }

    // ---------------------------------------------------------------- comunes

    private async Task ExigirEquipoAsync(Guid organizacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        if (!(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new AbpAuthorizationException("Las pólizas y los impuestos son del despacho.");
        }
    }

    private static void Mes(int anio, int mes)
    {
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }
    }

    private static string Corto(string texto, int max) => texto.Length > max ? texto[..max] + "…" : texto;
}
