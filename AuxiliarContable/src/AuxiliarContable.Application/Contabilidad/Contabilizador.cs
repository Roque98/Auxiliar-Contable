using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Linq;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// La contabilidad de un mes sin pasar por un usuario: amarrar los movimientos del banco con sus operaciones y generar
/// las pólizas. Lo usan la mesa de trabajo (con sus permisos) y la contabilidad al día (trabajo en segundo plano). Lanza
/// <see cref="UserFriendlyException"/> si falta algo y <see cref="MotorFiscalNoDisponibleException"/> si el motor no
/// responde.
/// </summary>
public class Contabilizador : ITransientDependency
{
    private readonly IRepository<Operacion, Guid> _operaciones;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<ConciliacionBancaria, Guid> _conciliaciones;
    private readonly IRepository<MovimientoFueraDelBanco, Guid> _movimientos;
    private readonly IRepository<PolizasDelMes, Guid> _polizas;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly IMotorFiscal _motor;
    private readonly RegistroDeOperaciones _registro;
    private readonly GestorDePeriodos _periodos;
    private readonly IGuidGenerator _guids;
    private readonly IAsyncQueryableExecuter _consultas;

    public ILogger<Contabilizador> Logger { get; set; } = NullLogger<Contabilizador>.Instance;

    public Contabilizador(
        IRepository<Operacion, Guid> operaciones,
        IRepository<Documento, Guid> documentos,
        IRepository<ConciliacionBancaria, Guid> conciliaciones,
        IRepository<MovimientoFueraDelBanco, Guid> movimientos,
        IRepository<PolizasDelMes, Guid> polizas,
        IRepository<PerfilFiscal, Guid> perfiles,
        AlmacenDeDocumentos almacen,
        IMotorFiscal motor,
        RegistroDeOperaciones registro,
        GestorDePeriodos periodos,
        IGuidGenerator guids,
        IAsyncQueryableExecuter consultas)
    {
        _operaciones = operaciones;
        _documentos = documentos;
        _conciliaciones = conciliaciones;
        _movimientos = movimientos;
        _polizas = polizas;
        _perfiles = perfiles;
        _almacen = almacen;
        _motor = motor;
        _registro = registro;
        _periodos = periodos;
        _guids = guids;
        _consultas = consultas;
    }

    /// <summary>
    /// Cada movimiento del banco conciliado se vuelve operación y se junta con la suya: por UUID, por regla (mismo
    /// sentido e importe, fecha cercana, una sola candidata) y, si <paramref name="conClaude"/>, lo que quede con Claude
    /// (lo seguro se amarra, lo dudoso se sugiere). Null si el mes no tiene conciliación.
    /// </summary>
    public async Task<ResultadoDeAmarreDto?> AmarrarAsync(Guid organizacionId, int anio, int mes, bool conClaude)
    {
        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1);
        var conciliacion = await _conciliaciones.FirstOrDefaultAsync(c => c.OrganizacionId == organizacionId && c.Anio == anio && c.Mes == mes);
        if (conciliacion == null)
        {
            return null;
        }

        await _periodos.ExigirAbiertoAsync(organizacionId, desde);

        var movs = GestorDeConciliaciones.Leer(conciliacion).Movimientos;
        var claves = ClaveDeMovimiento.De(movs);
        var existentes = (await _operaciones.GetListAsync(o => o.OrganizacionId == organizacionId && o.ClaveDeMovimiento != null
                                                               && claves.Contains(o.ClaveDeMovimiento)))
            .ToDictionary(o => o.ClaveDeMovimiento!);
        var r = new ResultadoDeAmarreDto();

        // 0) Cada movimiento es una operación (los traspasos entre cuentas propias no).
        var delBanco = new List<(Operacion Op, MovimientoLeido Mov)>();
        for (var i = 0; i < movs.Count; i++)
        {
            var m = movs[i];
            if (m.Conciliacion?.Estado == "traspaso" || m.Importe <= 0)
            {
                continue;
            }

            if (!existentes.TryGetValue(claves[i], out var o))
            {
                o = new Operacion(_guids.Create(), organizacionId);
                o.Describir(Fecha(m.Fecha) ?? desde, Sentido(m), m.Importe,
                    Corto(string.IsNullOrWhiteSpace(m.Concepto) ? "Movimiento del banco" : m.Concepto, OperacionConsts.MaxDescripcionLength),
                    string.IsNullOrWhiteSpace(m.Contraparte) ? null : Corto(m.Contraparte, OperacionConsts.MaxContraparteLength));
                o.LigarMovimientoDelBanco(claves[i], new[] { m.Referencia, m.Rastreo }.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
                await _operaciones.InsertAsync(o, autoSave: true);
                existentes[claves[i]] = o;
                r.OperacionesNuevas++;
            }

            delBanco.Add((o, m));
        }

        var juntadas = new HashSet<Guid>();

        // 1) Por UUID: lo que el motor concilió con su factura.
        foreach (var (o, m) in delBanco.Where(x => x.Op.FacturaEstado == EstadoDePata.Pendiente))
        {
            var uuids = (m.Conciliacion?.Facturas ?? []).Where(u => Guid.TryParse(u, out _)).Select(u => u.Trim().ToUpperInvariant()).ToList();
            if (uuids.Count == 0)
            {
                continue;
            }

            var conFactura = await _operaciones.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId && x.Id != o.Id
                && x.Uuid != null && uuids.Contains(x.Uuid) && x.TransaccionEstado == EstadoDePata.Pendiente);
            if (conFactura != null && await JuntarAsync(organizacionId, conFactura.Id, o.Id, r.Avisos))
            {
                juntadas.Add(o.Id);
            }
            else
            {
                // La factura no está en la plataforma (o ya tiene pago): el movimiento se queda con su UUID.
                o.LigarFactura(uuids[0], null, null);
                await _operaciones.UpdateAsync(o, autoSave: true);
            }

            r.PorUuid++;
        }

        // 2) Por regla: mismo sentido e importe, fecha cercana, y una sola candidata de cada lado.
        var soloBanco = delBanco.Where(x => !juntadas.Contains(x.Op.Id) && x.Op.FacturaEstado == EstadoDePata.Pendiente
                                            && x.Op.ControlEstado == EstadoDePata.Pendiente).Select(x => x.Op).ToList();
        var candidatas = await _consultas.ToListAsync((await _operaciones.GetQueryableAsync()).Where(x =>
            x.OrganizacionId == organizacionId && x.TransaccionEstado == EstadoDePata.Pendiente && x.ClaveDeMovimiento == null
            && x.Fecha >= desde.AddDays(-MesaDeTrabajoConsts.DiasHaciaAtras) && x.Fecha < hasta.AddDays(MesaDeTrabajoConsts.DiasHaciaAdelante)));

        bool Encaja(Operacion mov, Operacion c) =>
            c.Sentido == mov.Sentido && c.Importe is { } i && Math.Abs(i - mov.Importe!.Value) <= 0.01m
            && c.Fecha <= mov.Fecha.AddDays(MesaDeTrabajoConsts.DiasHaciaAdelante) && c.Fecha >= mov.Fecha.AddDays(-MesaDeTrabajoConsts.DiasHaciaAtras);

        foreach (var mov in soloBanco.ToList())
        {
            var suyas = candidatas.Where(c => Encaja(mov, c)).ToList();
            if (suyas.Count == 1 && soloBanco.Count(x => Encaja(x, suyas[0])) == 1
                && await JuntarAsync(organizacionId, suyas[0].Id, mov.Id, r.Avisos))
            {
                candidatas.Remove(suyas[0]);
                soloBanco.Remove(mov);
                r.PorRegla++;
            }
        }

        // 3) Lo que queda, con Claude: lo seguro se amarra, lo dudoso se sugiere.
        if (conClaude && soloBanco.Count > 0 && candidatas.Count > 0)
        {
            var (_, giro) = await RfcYGiroAsync(organizacionId);
            var porId = soloBanco.Take(300).ToDictionary(o => o.Id.ToString("N"));
            var candidatasPorId = candidatas.Take(600).ToDictionary(o => o.Id.ToString("N"));
            try
            {
                var sugerencias = await _motor.SugerirAmarresAsync(
                    porId.Select(x => new MovimientoParaAmarrar
                    {
                        Id = x.Key, Fecha = x.Value.Fecha.ToString("yyyy-MM-dd"),
                        Naturaleza = x.Value.Sentido == SentidoDeOperacion.Entrada ? "abono" : "cargo",
                        Importe = x.Value.Importe ?? 0, Concepto = x.Value.Descripcion, Referencia = x.Value.Referencia ?? "",
                        Contraparte = x.Value.Contraparte ?? "",
                    }).ToList(),
                    candidatasPorId.Select(x => new CandidataParaAmarrar
                    {
                        Id = x.Key, Fecha = x.Value.Fecha.ToString("yyyy-MM-dd"),
                        Sentido = x.Value.Sentido == SentidoDeOperacion.Entrada ? "entrada" : "salida",
                        Importe = x.Value.Importe, Descripcion = x.Value.Descripcion, Contraparte = x.Value.Contraparte, Uuid = x.Value.Uuid,
                    }).ToList(),
                    giro);
                foreach (var par in sugerencias.Pares)
                {
                    if (!porId.TryGetValue(par.Movimiento, out var mov) || !candidatasPorId.TryGetValue(par.Operacion, out var c))
                    {
                        continue;
                    }

                    if (par.Confianza == "alta" && await JuntarAsync(organizacionId, c.Id, mov.Id, r.Avisos))
                    {
                        r.PorClaude++;
                        continue;
                    }

                    r.Sugerencias.Add(new SugerenciaDeAmarreDto
                    {
                        MovimientoId = mov.Id,
                        Movimiento = $"{mov.Fecha:dd/MM} · {mov.Descripcion} · {mov.Importe:C2}",
                        OperacionId = c.Id,
                        Operacion = $"{c.Fecha:dd/MM} · {c.Contraparte ?? c.Descripcion} · {c.Importe:C2}",
                        Confianza = par.Confianza,
                        Porque = par.Porque,
                    });
                }
            }
            catch (MotorFiscalNoDisponibleException ex)
            {
                Logger.LogWarning(ex, "No se pudieron sugerir amarres con el motor");
                r.Avisos.Add("Claude no está disponible: se amarró solo lo seguro (UUID y regla). Intenta de nuevo en unos minutos.");
            }
        }

        return r;
    }

    /// <summary>
    /// Las pólizas del mes con lo que hay hoy. <paramref name="cierre"/>: lo capturado para el cierre (inventarios); si es
    /// <c>null</c> y <paramref name="conservarCierre"/>, se usa el que ya estaba guardado.
    /// </summary>
    public async Task<PolizasDelMes> ContabilizarAsync(Guid organizacionId, int anio, int mes, DatosDeCierre? cierre, bool conservarCierre)
    {
        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1);
        await _periodos.ExigirAbiertoAsync(organizacionId, desde);
        var guardadas = await _polizas.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId && x.Anio == anio && x.Mes == mes);
        if (cierre == null && conservarCierre && guardadas?.DatosDeCierre != null)
        {
            cierre = JsonSerializer.Deserialize<DatosDeCierre>(guardadas.DatosDeCierre);
        }

        // Lo del mes que aún no es operación (facturas, tickets, efectivo y crédito).
        await _registro.AgregarLoQueFaltaAsync(organizacionId, desde, hasta);

        var ops = await _operaciones.GetListAsync(o => o.OrganizacionId == organizacionId && o.Fecha >= desde && o.Fecha < hasta);
        var perfil = await _perfiles.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId);
        string? Campo(string clave) => perfil?.Dato(clave)?.Valor?.Trim();
        var (rfc, giro) = await RfcYGiroAsync(organizacionId);
        if (rfc == null)
        {
            throw new UserFriendlyException("Falta el RFC del cliente en su perfil fiscal: sin él no se sabe qué facturas emitió y cuáles recibió.");
        }

        // Los movimientos del banco (importe y fecha reales del pago) y el efectivo y crédito (si ya se saldó).
        var conciliacion = await _conciliaciones.FirstOrDefaultAsync(c => c.OrganizacionId == organizacionId && c.Anio == anio && c.Mes == mes);
        var delBanco = new Dictionary<string, MovimientoLeido>();
        if (conciliacion != null)
        {
            var movs = GestorDeConciliaciones.Leer(conciliacion).Movimientos;
            var claves = ClaveDeMovimiento.De(movs);
            for (var i = 0; i < movs.Count; i++)
            {
                delBanco[claves[i]] = movs[i];
            }
        }

        // Facturas de meses anteriores que se pagan (o cobran) en este: entran solo por su pago.
        var clavesDelMes = delBanco.Keys.ToList();
        var saldadosEnElMes = (await _movimientos.GetListAsync(m => m.OrganizacionId == organizacionId && m.Saldado >= desde && m.Saldado < hasta))
            .Select(m => m.Id).ToList();
        var pagadasAhora = await _operaciones.GetListAsync(o => o.OrganizacionId == organizacionId && o.Fecha < desde
            && ((o.ClaveDeMovimiento != null && clavesDelMes.Contains(o.ClaveDeMovimiento))
                || (o.MovimientoFueraDelBancoId != null && saldadosEnElMes.Contains(o.MovimientoFueraDelBancoId.Value))));
        ops.AddRange(pagadasAhora);

        var movIds = ops.Select(o => o.MovimientoFueraDelBancoId).OfType<Guid>().ToList();
        var fueraDelBanco = movIds.Count == 0 ? [] : (await _movimientos.GetListAsync(m => movIds.Contains(m.Id))).ToDictionary(m => m.Id);
        var docIds = ops.SelectMany(o => new[] { o.FacturaDocumentoId, o.TransaccionDocumentoId, o.ControlDocumentoId }).OfType<Guid>().Distinct().ToList();
        var docs = docIds.Count == 0 ? [] : (await _documentos.GetListAsync(d => docIds.Contains(d.Id))).ToDictionary(d => d.Id);

        AnalisisDeDocumento? Analisis(Guid? id) =>
            id is { } x && docs.TryGetValue(x, out var d) && d.Analisis != null ? JsonSerializer.Deserialize<AnalisisDeDocumento>(d.Analisis) : null;
        static string Estado(EstadoDePata e) => e switch { EstadoDePata.Ligada => "ligada", EstadoDePata.NoHay => "no_hay", _ => "pendiente" };

        var paraMotor = ops.Select(o =>
        {
            var banco = o.ClaveDeMovimiento != null && delBanco.TryGetValue(o.ClaveDeMovimiento, out var m) ? m : null;
            var fuera = o.MovimientoFueraDelBancoId is { } mid && fueraDelBanco.TryGetValue(mid, out var fm) ? fm : null;
            return new OperacionParaContabilizar
            {
                Id = o.Id.ToString(),
                Fecha = o.Fecha.ToString("yyyy-MM-dd"),
                Sentido = o.Sentido == SentidoDeOperacion.Entrada ? "entrada" : "salida",
                Importe = o.Importe,
                Descripcion = o.Descripcion,
                Contraparte = o.Contraparte,
                Factura = new PataParaContabilizar { Estado = Estado(o.FacturaEstado), Uuid = o.Uuid },
                Transaccion = new PataParaContabilizar
                {
                    Estado = Estado(o.TransaccionEstado),
                    Forma = o.TransaccionForma switch
                    {
                        FormaDeTransaccion.Efectivo => "efectivo",
                        FormaDeTransaccion.Credito => "credito",
                        FormaDeTransaccion.Banco => "banco",
                        _ => null,
                    },
                    Referencia = o.Referencia,
                    Importe = banco?.Importe,
                    Fecha = banco != null ? Fecha(banco.Fecha)?.ToString("yyyy-MM-dd")
                        : fuera?.Saldado is { } saldado ? saldado.ToString("yyyy-MM-dd") : fuera?.Fecha.ToString("yyyy-MM-dd"),
                    Saldado = fuera?.EsACredito == true ? fuera.Saldado != null : null,
                    Analisis = Analisis(o.TransaccionDocumentoId),
                },
                Control = new PataParaContabilizar { Estado = Estado(o.ControlEstado), Analisis = Analisis(o.ControlDocumentoId) },
                Aceptados = o.CodigosAceptados.ToList(),
            };
        }).ToList();

        var facturas = new List<ArchivoParaElMotor>();
        foreach (var d in ops.Select(o => o.FacturaDocumentoId).OfType<Guid>().Distinct().Select(id => docs.GetValueOrDefault(id)).OfType<Documento>()
                     .Where(d => d.Tipo == TipoDeDocumento.Cfdi))
        {
            facturas.Add(new ArchivoParaElMotor(d.NombreOriginal, await _almacen.LeerAsync(d)));
        }

        var resultado = await _motor.ContabilizarAsync(new PerfilParaContabilizar
        {
            Rfc = rfc,
            TipoEmpresa = Campo(CamposDelPerfil.TipoDeEmpresa),
            SistemaCostos = Campo(CamposDelPerfil.SistemaDeCostos),
            SistemaInventarios = Campo(CamposDelPerfil.SistemaDeInventarios),
            Giro = giro,
        }, paraMotor, facturas, $"{anio:0000}-{mes:00}", cierre);

        var nueva = guardadas == null;
        guardadas ??= new PolizasDelMes(_guids.Create(), organizacionId, anio, mes);
        guardadas.Guardar(JsonSerializer.Serialize(resultado), DateTime.UtcNow, cierre == null ? null : JsonSerializer.Serialize(cierre));
        if (nueva)
        {
            await _polizas.InsertAsync(guardadas, autoSave: true);
        }
        else
        {
            await _polizas.UpdateAsync(guardadas, autoSave: true);
        }

        return guardadas;
    }

    public async Task<(string? Rfc, string? Giro)> RfcYGiroAsync(Guid organizacionId)
    {
        var perfil = await _perfiles.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId);
        string? Campo(string clave) => perfil?.Dato(clave)?.Valor?.Trim();
        var giro = Campo(CamposDelPerfil.Giro);
        if (string.IsNullOrWhiteSpace(giro))
        {
            giro = Campo(CamposDelPerfil.ActividadesEconomicas)?
                .Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        }

        var rfc = Campo(CamposDelPerfil.Rfc)?.ToUpperInvariant();
        return (string.IsNullOrWhiteSpace(rfc) ? null : rfc, string.IsNullOrWhiteSpace(giro) ? null : giro);
    }

    /// <summary>Junta la operación del movimiento en la otra (la que queda es la de la factura o el ticket).</summary>
    private async Task<bool> JuntarAsync(Guid organizacionId, Guid queda, Guid sale, List<string> avisos)
    {
        try
        {
            await _registro.JuntarAsync(organizacionId, queda, sale);
            return true;
        }
        catch (UserFriendlyException ex)
        {
            avisos.Add(ex.Message);
            return false;
        }
    }

    private static SentidoDeOperacion Sentido(MovimientoLeido m) =>
        m.Naturaleza.Trim().Equals("abono", StringComparison.OrdinalIgnoreCase) ? SentidoDeOperacion.Entrada : SentidoDeOperacion.Salida;

    private static DateTime? Fecha(string? texto) =>
        DateTime.TryParseExact(texto?.Trim(), ["dd/MM/yyyy", "yyyy-MM-dd", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)
            ? f
            : null;

    private static string Corto(string texto, int max) => texto.Trim().Length <= max ? texto.Trim() : texto.Trim()[..max];
}
