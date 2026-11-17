using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Contabilidad;

/// <summary>Ver <see cref="IMesaDeTrabajoAppService"/>.</summary>
[Authorize]
public class MesaDeTrabajoAppService : AuxiliarContableAppService, IMesaDeTrabajoAppService
{
    /// <summary>Lo que Claude puede leer: tickets, notas, comprobantes y fotos (las facturas las lee el motor por su XML).</summary>
    private static readonly TipoDeDocumento[] Analizables =
        [TipoDeDocumento.ControlInterno, TipoDeDocumento.ComprobanteDePago, TipoDeDocumento.Ticket, TipoDeDocumento.Otro];

    private readonly IRepository<Operacion, Guid> _operaciones;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<ConciliacionBancaria, Guid> _conciliaciones;
    private readonly IRepository<MovimientoFueraDelBanco, Guid> _movimientos;
    private readonly IRepository<PolizasDelMes, Guid> _polizas;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly IPerfilFiscalAppService _perfiles;
    private readonly IMotorFiscal _motor;
    private readonly IOperacionesAppService _operacionesApp;
    private readonly GestorDePeriodos _periodos;
    private readonly Autorizador _autorizador;

    public MesaDeTrabajoAppService(
        IRepository<Operacion, Guid> operaciones,
        IRepository<Documento, Guid> documentos,
        IRepository<ConciliacionBancaria, Guid> conciliaciones,
        IRepository<MovimientoFueraDelBanco, Guid> movimientos,
        IRepository<PolizasDelMes, Guid> polizas,
        AlmacenDeDocumentos almacen,
        IPerfilFiscalAppService perfiles,
        IMotorFiscal motor,
        IOperacionesAppService operacionesApp,
        GestorDePeriodos periodos,
        Autorizador autorizador)
    {
        _operaciones = operaciones;
        _documentos = documentos;
        _conciliaciones = conciliaciones;
        _movimientos = movimientos;
        _polizas = polizas;
        _almacen = almacen;
        _perfiles = perfiles;
        _motor = motor;
        _operacionesApp = operacionesApp;
        _periodos = periodos;
        _autorizador = autorizador;
    }

    public async Task<MesaDelMesDto> GetAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        var (desde, hasta) = Mes(anio, mes);
        var operaciones = await _operacionesApp.GetListAsync(organizacionId, anio, mes);
        var analizables = await AnalizablesDelMesAsync(organizacionId, desde, hasta);
        var conciliacion = await _conciliaciones.FirstOrDefaultAsync(c => c.OrganizacionId == organizacionId && c.Anio == anio && c.Mes == mes);
        var movimientos = 0;
        var comoOperacion = 0;
        if (conciliacion != null)
        {
            var movs = GestorDeConciliaciones.Leer(conciliacion).Movimientos;
            var claves = ClaveDeMovimiento.De(movs);
            movimientos = movs.Count(m => m.Conciliacion?.Estado != "traspaso");
            comoOperacion = await _operaciones.CountAsync(o => o.OrganizacionId == organizacionId && o.ClaveDeMovimiento != null
                                                               && claves.Contains(o.ClaveDeMovimiento));
        }

        return new MesaDelMesDto
        {
            Operaciones = operaciones,
            PorAnalizar = analizables.Count(d => d.AnalizadoUtc == null),
            Analizados = analizables.Count(d => d.AnalizadoUtc != null),
            ConciliadaUtc = conciliacion?.ConciliadaUtc,
            MovimientosDelBanco = movimientos,
            MovimientosComoOperacion = comoOperacion,
        };
    }

    public async Task<ResultadoDeAnalisisDto> AnalizarAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        var (desde, hasta) = Mes(anio, mes);
        var pendientes = (await AnalizablesDelMesAsync(organizacionId, desde, hasta)).Where(d => d.AnalizadoUtc == null).ToList();
        var (rfc, giro) = await RfcYGiroAsync(organizacionId);
        var r = new ResultadoDeAnalisisDto();

        foreach (var d in pendientes.Take(MesaDeTrabajoConsts.AnalisisPorVuelta))
        {
            AnalisisDeDocumento a;
            try
            {
                a = await _motor.AnalizarDocumentoAsync(new ArchivoParaElMotor(d.NombreOriginal, await _almacen.LeerAsync(d)), rfc, giro);
            }
            catch (MotorFiscalNoDisponibleException ex)
            {
                Logger.LogWarning(ex, "No se pudo analizar un documento con el motor");
                r.Avisos.Add("Claude no está disponible en este momento: intenta de nuevo en unos minutos.");
                break;
            }

            d.GuardarAnalisis(JsonSerializer.Serialize(a), Clock.Now.ToUniversalTime());
            await _documentos.UpdateAsync(d, autoSave: true);
            r.Analizados++;
            if (!a.Legible)
            {
                r.Avisos.Add($"«{d.NombreOriginal}» no se pudo leer: pide una foto más clara.");
                continue;
            }

            try
            {
                if (await CompletarOperacionAsync(organizacionId, d, a))
                {
                    r.Completadas++;
                }
            }
            catch (UserFriendlyException ex)
            {
                r.Avisos.Add($"«{d.NombreOriginal}»: {ex.Message}");
            }
        }

        r.Faltan = pendientes.Count - r.Analizados;
        return r;
    }

    /// <summary>Lo leído completa la operación del documento: solo lo que falta, nunca encima de lo que ya dijo alguien.</summary>
    private async Task<bool> CompletarOperacionAsync(Guid organizacionId, Documento d, AnalisisDeDocumento a)
    {
        var op = await _operacionesApp.DeDocumentoAsync(organizacionId, d.Id);
        var datos = new GuardarOperacionDto
        {
            Fecha = op.Fecha, Sentido = op.Sentido, Importe = op.Importe, Descripcion = op.Descripcion, Contraparte = op.Contraparte,
        };
        var cambio = false;
        var deFactura = op.Factura.Estado == EstadoDePata.Ligada && op.Factura.DocumentoId != null;
        if (op.Importe is null && a.Importe is > 0)
        {
            datos.Importe = a.Importe;
            cambio = true;
        }

        if (op.Descripcion == d.NombreOriginal || op.Descripcion == d.NombreOriginal[..Math.Min(d.NombreOriginal.Length, OperacionConsts.MaxDescripcionLength)])
        {
            if (!string.IsNullOrWhiteSpace(a.QueEs))
            {
                datos.Descripcion = Corto(a.QueEs, OperacionConsts.MaxDescripcionLength);
                cambio = true;
            }
        }

        if (string.IsNullOrWhiteSpace(op.Contraparte) && !string.IsNullOrWhiteSpace(a.EmisorNombre ?? a.EmisorRfc))
        {
            datos.Contraparte = Corto((a.EmisorNombre ?? a.EmisorRfc)!, OperacionConsts.MaxContraparteLength);
            cambio = true;
        }

        if (!deFactura)
        {
            // La fecha y el sentido de la factura mandan; si no hay factura, los del documento.
            if (DateTime.TryParseExact(a.Fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)
                && f.Year >= 2000 && f.Date != op.Fecha.Date)
            {
                datos.Fecha = f;
                cambio = true;
            }

            var sentido = a.Sentido switch
            {
                "entrada" => SentidoDeOperacion.Entrada,
                "salida" => SentidoDeOperacion.Salida,
                _ => op.Sentido,
            };
            cambio |= sentido != op.Sentido;
            datos.Sentido = sentido;
        }

        if (op.Factura.Estado == EstadoDePata.Pendiente && Guid.TryParse(a.Uuid, out _))
        {
            datos.Factura = new GuardarPataDto { Estado = EstadoDePata.Ligada, Referencia = a.Uuid };
        }

        var referencia = string.IsNullOrWhiteSpace(a.Referencia) ? null : Corto(a.Referencia, OperacionConsts.MaxReferenciaLength);
        if (referencia != null && a.FormaDePago is "transferencia" or "tarjeta" or "cheque")
        {
            if (op.Transaccion.Estado == EstadoDePata.Pendiente)
            {
                datos.Transaccion = new GuardarPataDto { Estado = EstadoDePata.Ligada, Forma = FormaDeTransaccion.Banco, Referencia = referencia };
            }
            else if (op.Transaccion.DocumentoId == d.Id && op.Transaccion.Referencia == null)
            {
                datos.Transaccion = new GuardarPataDto
                {
                    Estado = EstadoDePata.Ligada, Forma = FormaDeTransaccion.Banco, Referencia = referencia, DocumentoId = d.Id,
                };
            }
        }

        if (!cambio && datos.Factura == null && datos.Transaccion == null)
        {
            return false;
        }

        await _operacionesApp.ActualizarAsync(organizacionId, op.Id, datos);
        return true;
    }

    public async Task<ResultadoDeAmarreDto> AmarrarBancoAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        var (desde, hasta) = Mes(anio, mes);
        var conciliacion = await _conciliaciones.FirstOrDefaultAsync(c => c.OrganizacionId == organizacionId && c.Anio == anio && c.Mes == mes)
                           ?? throw new UserFriendlyException($"Primero concilia {desde:MM/yyyy} en «Cierre del mes»: de ahí salen los movimientos del banco.");
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
                o = new Operacion(GuidGenerator.Create(), organizacionId);
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
        var candidatas = await AsyncExecuter.ToListAsync((await _operaciones.GetQueryableAsync()).Where(x =>
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
        if (soloBanco.Count > 0 && candidatas.Count > 0)
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

    public async Task<PolizasDelMesDto?> GetPolizasAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        Mes(anio, mes);
        var p = await _polizas.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId && x.Anio == anio && x.Mes == mes);
        return p == null ? null : Describir(p);
    }

    public async Task<PolizasDelMesDto> GenerarPolizasAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        var (desde, hasta) = Mes(anio, mes);
        await _periodos.ExigirAbiertoAsync(organizacionId, desde);
        await _operacionesApp.GetListAsync(organizacionId, anio, mes);   // agrega lo del mes que aún no es operación

        var ops = await _operaciones.GetListAsync(o => o.OrganizacionId == organizacionId && o.Fecha >= desde && o.Fecha < hasta);
        var perfilDto = await _perfiles.GetAsync(organizacionId);
        string? Campo(string clave) => perfilDto.Campos.FirstOrDefault(c => c.Clave == clave)?.Valor?.Trim();
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
                    Fecha = banco != null ? Fecha(banco.Fecha)?.ToString("yyyy-MM-dd") : fuera?.Fecha.ToString("yyyy-MM-dd"),
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

        ResultadoContable resultado;
        try
        {
            resultado = await _motor.ContabilizarAsync(new PerfilParaContabilizar
            {
                Rfc = rfc,
                TipoEmpresa = Campo(CamposDelPerfil.TipoDeEmpresa),
                SistemaCostos = Campo(CamposDelPerfil.SistemaDeCostos),
                SistemaInventarios = Campo(CamposDelPerfil.SistemaDeInventarios),
                Giro = giro,
            }, paraMotor, facturas);
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor no pudo contabilizar");
            throw new UserFriendlyException("El motor contable no está disponible en este momento. Intenta de nuevo en unos minutos.");
        }

        var guardadas = await _polizas.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId && x.Anio == anio && x.Mes == mes);
        var nueva = guardadas == null;
        guardadas ??= new PolizasDelMes(GuidGenerator.Create(), organizacionId, anio, mes);
        guardadas.Guardar(JsonSerializer.Serialize(resultado), DateTime.UtcNow);
        if (nueva)
        {
            await _polizas.InsertAsync(guardadas, autoSave: true);
        }
        else
        {
            await _polizas.UpdateAsync(guardadas, autoSave: true);
        }

        return Describir(guardadas);
    }

    public async Task AceptarRevisionAsync(Guid organizacionId, Guid operacionId, string codigo)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        var o = await _operaciones.FirstOrDefaultAsync(x => x.Id == operacionId && x.OrganizacionId == organizacionId)
                ?? throw new Volo.Abp.Domain.Entities.EntityNotFoundException(typeof(Operacion), operacionId);
        await _periodos.ExigirAbiertoAsync(organizacionId, o.Fecha);
        o.Aceptar(codigo);
        await _operaciones.UpdateAsync(o, autoSave: true);
    }

    private static PolizasDelMesDto Describir(PolizasDelMes p)
    {
        var r = JsonSerializer.Deserialize<ResultadoContable>(p.Resultado) ?? new ResultadoContable();
        return new PolizasDelMesDto
        {
            Anio = p.Anio,
            Mes = p.Mes,
            GeneradoUtc = p.GeneradoUtc,
            Resultado = r,
            Detenidas = r.Revision.Where(x => x.Nivel == "detiene").Select(x => x.Operacion ?? "").Distinct().Count(),
        };
    }

    /// <summary>Junta la operación del movimiento en la otra (la que queda es la de la factura o el ticket).</summary>
    private async Task<bool> JuntarAsync(Guid organizacionId, Guid queda, Guid sale, List<string> avisos)
    {
        try
        {
            await _operacionesApp.JuntarAsync(organizacionId, queda, sale);
            return true;
        }
        catch (UserFriendlyException ex)
        {
            avisos.Add(ex.Message);
            return false;
        }
    }

    private async Task<List<Documento>> AnalizablesDelMesAsync(Guid organizacionId, DateTime desde, DateTime hasta) =>
        await AsyncExecuter.ToListAsync((await _documentos.GetQueryableAsync()).Where(d =>
            d.OrganizacionId == organizacionId && Analizables.Contains(d.Tipo)
            && (d.TipoMime.StartsWith("image/") || d.TipoMime == "application/pdf")
            && (d.FechaDelDocumento ?? d.CreationTime) >= desde && (d.FechaDelDocumento ?? d.CreationTime) < hasta)
            .OrderBy(d => d.CreationTime));

    private async Task<(string? Rfc, string? Giro)> RfcYGiroAsync(Guid organizacionId)
    {
        var perfil = await _perfiles.GetAsync(organizacionId);
        string? Campo(string clave) => perfil.Campos.FirstOrDefault(c => c.Clave == clave)?.Valor?.Trim();
        var giro = Campo(CamposDelPerfil.Giro);
        if (string.IsNullOrWhiteSpace(giro))
        {
            giro = Campo(CamposDelPerfil.ActividadesEconomicas)?
                .Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        }

        var rfc = Campo(CamposDelPerfil.Rfc)?.ToUpperInvariant();
        return (string.IsNullOrWhiteSpace(rfc) ? null : rfc, string.IsNullOrWhiteSpace(giro) ? null : giro);
    }

    private async Task ExigirEquipoAsync(Guid organizacionId, AccionEnOrganizacion accion)
    {
        await _autorizador.ExigirAsync(organizacionId, accion);
        if (!(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new AbpAuthorizationException("La mesa de trabajo es del despacho.");
        }
    }

    private static (DateTime Desde, DateTime Hasta) Mes(int anio, int mes)
    {
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }

        var desde = new DateTime(anio, mes, 1);
        return (desde, desde.AddMonths(1));
    }

    private static SentidoDeOperacion Sentido(MovimientoLeido m) =>
        m.Naturaleza.Trim().Equals("abono", StringComparison.OrdinalIgnoreCase) ? SentidoDeOperacion.Entrada : SentidoDeOperacion.Salida;

    private static DateTime? Fecha(string? texto) =>
        DateTime.TryParseExact(texto?.Trim(), ["dd/MM/yyyy", "yyyy-MM-dd", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)
            ? f
            : null;

    private static string Corto(string texto, int max) => texto.Trim().Length <= max ? texto.Trim() : texto.Trim()[..max];
}
