using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Bancos;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Pide a Claude (motor /operaciones/interpretar) qué es de verdad cada operación y qué es para el cliente, con todas sus
/// fuentes juntas: la factura, el ticket o nota (lo ya leído de él) y el movimiento del banco. Sin usuario: lo usan el
/// trabajo en segundo plano, la mesa (para pedirlo y verlo) y el contabilizador (para usar lo que siga al día).
/// </summary>
public class InterpretadorDeOperaciones : ITransientDependency
{
    /// <summary>Cambia si cambia lo que se le manda a Claude: todo vuelve a quedar por interpretar.</summary>
    public const string Version = "2026-11-18";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IRepository<Operacion, Guid> _operaciones;
    private readonly IRepository<OperacionInterpretada, Guid> _interpretadas;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly IRepository<ConciliacionBancaria, Guid> _conciliaciones;
    private readonly CalculadoraDeImpuestos _calculadora;
    private readonly RegistroDeOperaciones _registro;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly IMotorFiscal _motor;
    private readonly IUnitOfWorkManager _unidades;
    private readonly IGuidGenerator _guids;
    private readonly IClock _reloj;

    public ILogger<InterpretadorDeOperaciones> Logger { get; set; } = NullLogger<InterpretadorDeOperaciones>.Instance;

    public InterpretadorDeOperaciones(
        IRepository<Operacion, Guid> operaciones,
        IRepository<OperacionInterpretada, Guid> interpretadas,
        IRepository<Documento, Guid> documentos,
        IRepository<PerfilFiscal, Guid> perfiles,
        IRepository<ConciliacionBancaria, Guid> conciliaciones,
        CalculadoraDeImpuestos calculadora,
        RegistroDeOperaciones registro,
        AlmacenDeDocumentos almacen,
        IMotorFiscal motor,
        IUnitOfWorkManager unidades,
        IGuidGenerator guids,
        IClock reloj)
    {
        _operaciones = operaciones;
        _interpretadas = interpretadas;
        _documentos = documentos;
        _perfiles = perfiles;
        _conciliaciones = conciliaciones;
        _calculadora = calculadora;
        _registro = registro;
        _almacen = almacen;
        _motor = motor;
        _unidades = unidades;
        _guids = guids;
        _reloj = reloj;
    }

    /// <summary>Todo lo que se le manda a Claude de una operación, y la huella de eso.</summary>
    public sealed record Entrada(
        Operacion Operacion,
        PerfilParaInterpretar Perfil,
        OperacionParaInterpretar Datos,
        Documento? Factura,
        AnalisisDeDocumento? Control,
        BancoParaInterpretar? Banco,
        string Huella)
    {
        /// <summary>Sin factura, ticket ni banco no hay nada que interpretar.</summary>
        public bool TieneFuentes => Factura != null || Control != null || Banco != null;
    }

    public async Task<List<Entrada>> EntradasAsync(Guid organizacionId, IReadOnlyList<Operacion> ops)
    {
        var p = await _calculadora.PerfilAsync(organizacionId);
        var fila = await _perfiles.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId);
        string? Campo(string clave) => fila?.Dato(clave)?.Valor?.Trim();
        var giro = Campo(CamposDelPerfil.Giro);
        if (string.IsNullOrWhiteSpace(giro))
        {
            giro = Campo(CamposDelPerfil.ActividadesEconomicas)?
                .Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        }

        var perfil = new PerfilParaInterpretar
        {
            Rfc = p.Rfc ?? "",
            Regimenes = p.Regimenes,
            Giro = giro,
            TipoEmpresa = Campo(CamposDelPerfil.TipoDeEmpresa),
            SistemaCostos = Campo(CamposDelPerfil.SistemaDeCostos),
            SistemaInventarios = Campo(CamposDelPerfil.SistemaDeInventarios),
        };

        var docIds = ops.SelectMany(o => new[] { o.FacturaDocumentoId, o.TransaccionDocumentoId, o.ControlDocumentoId }).OfType<Guid>().Distinct().ToList();
        var docs = docIds.Count == 0 ? [] : (await _documentos.GetListAsync(d => docIds.Contains(d.Id))).ToDictionary(d => d.Id);
        AnalisisDeDocumento? Analisis(Guid? id) =>
            id is { } x && docs.TryGetValue(x, out var d) && d.Analisis != null ? JsonSerializer.Deserialize<AnalisisDeDocumento>(d.Analisis) : null;

        // Los movimientos del banco de los meses de estas operaciones, por su clave.
        var delBanco = new Dictionary<string, MovimientoLeido>();
        foreach (var (anio, mes) in ops.Where(o => o.ClaveDeMovimiento != null).Select(o => (o.Fecha.Year, o.Fecha.Month)).Distinct())
        {
            var c = await _conciliaciones.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId && x.Anio == anio && x.Mes == mes);
            if (c == null)
            {
                continue;
            }

            var movs = GestorDeConciliaciones.Leer(c).Movimientos;
            var claves = ClaveDeMovimiento.De(movs);
            for (var i = 0; i < movs.Count; i++)
            {
                delBanco.TryAdd(claves[i], movs[i]);
            }
        }

        var lista = new List<Entrada>();
        foreach (var o in ops)
        {
            var factura = o.FacturaDocumentoId is { } fid && docs.TryGetValue(fid, out var fd) && fd.Tipo == TipoDeDocumento.Cfdi ? fd : null;
            var control = Analisis(o.ControlDocumentoId) ?? Analisis(o.TransaccionDocumentoId);
            var m = o.ClaveDeMovimiento != null && delBanco.TryGetValue(o.ClaveDeMovimiento, out var mv) ? mv : null;
            var banco = m == null
                ? null
                : new BancoParaInterpretar { Fecha = m.Fecha, Concepto = m.Concepto, Referencia = m.Referencia, Contraparte = m.Contraparte, Importe = m.Importe };
            var datos = new OperacionParaInterpretar
            {
                Fecha = o.Fecha.ToString("yyyy-MM-dd"),
                Sentido = o.Sentido == SentidoDeOperacion.Entrada ? "entrada" : "salida",
                Importe = o.Importe,
                Descripcion = o.Descripcion,
                Contraparte = o.Contraparte,
                Forma = o.TransaccionForma switch
                {
                    FormaDeTransaccion.Efectivo => "efectivo",
                    FormaDeTransaccion.Credito => "credito",
                    FormaDeTransaccion.Banco => "banco",
                    _ => null,
                },
            };
            lista.Add(new Entrada(o, perfil, datos, factura, control, banco, Huella(perfil, datos, factura?.Sha256, control, banco)));
        }

        return lista;
    }

    /// <summary>Las fuentes con que se interpreta: si cambia algo (otra factura, otro ticket, el giro), ya no está al día.</summary>
    public static string Huella(PerfilParaInterpretar perfil, OperacionParaInterpretar datos, string? facturaSha256,
        AnalisisDeDocumento? control, BancoParaInterpretar? banco) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { Version, perfil, datos, facturaSha256, control, banco }, Json))))[..OperacionInterpretada.HuellaLength];

    /// <summary>Lo interpretado que sigue al día con sus fuentes, por operación.</summary>
    public async Task<Dictionary<Guid, InterpretacionDelMotor>> VigentesAsync(IReadOnlyList<Entrada> entradas)
    {
        var ids = entradas.Select(e => e.Operacion.Id).ToList();
        var filas = ids.Count == 0 ? [] : await _interpretadas.GetListAsync(x => ids.Contains(x.OperacionId) && x.Resultado != null);
        var huellas = entradas.ToDictionary(e => e.Operacion.Id, e => e.Huella);
        return filas.Where(f => huellas.GetValueOrDefault(f.OperacionId) == f.Huella)
            .ToDictionary(f => f.OperacionId, f => JsonSerializer.Deserialize<InterpretacionDelMotor>(f.Resultado!)!);
    }

    public async Task<List<OperacionInterpretada>> FilasAsync(Guid organizacionId, IReadOnlyList<Guid> operacionIds) =>
        operacionIds.Count == 0 ? [] : await _interpretadas.GetListAsync(x => x.OrganizacionId == organizacionId && operacionIds.Contains(x.OperacionId));

    /// <summary>
    /// Deja en la fila las operaciones del mes (o solo <paramref name="operacionId"/>) que tienen fuentes y no están al día.
    /// -> cuántas se pidieron.
    /// </summary>
    public async Task<int> PedirAsync(Guid organizacionId, int anio, int mes, Guid? operacionId = null)
    {
        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1);
        await _registro.AgregarLoQueFaltaAsync(organizacionId, desde, hasta);
        var ops = await _operaciones.GetListAsync(o => o.OrganizacionId == organizacionId && o.Fecha >= desde && o.Fecha < hasta
                                                      && (operacionId == null || o.Id == operacionId));
        var entradas = await EntradasAsync(organizacionId, ops);
        var filas = (await FilasAsync(organizacionId, ops.Select(o => o.Id).ToList())).ToDictionary(f => f.OperacionId);
        var ahora = _reloj.Now.ToUniversalTime();
        var pedidas = 0;
        foreach (var e in entradas.Where(e => e.TieneFuentes))
        {
            var f = filas.GetValueOrDefault(e.Operacion.Id);
            var alDia = f?.Resultado != null && f.Huella == e.Huella;
            if (f?.Pedida == true || (alDia && operacionId == null))
            {
                continue;
            }

            if (f == null)
            {
                f = await _interpretadas.InsertAsync(new OperacionInterpretada(_guids.Create(), organizacionId, e.Operacion.Id, anio, mes), autoSave: true);
            }

            f.Pedir(ahora);
            await _interpretadas.UpdateAsync(f, autoSave: true);
            pedidas++;
        }

        return pedidas;
    }

    /// <summary>Interpreta las que están en la fila (las más viejas primero). -> cuántas se interpretaron.</summary>
    public async Task<int> ProcesarAsync(int maximo = 2)
    {
        List<Guid> ids;
        using (var uow = _unidades.Begin(requiresNew: true))
        {
            var ahora = _reloj.Now.ToUniversalTime();
            ids = [.. (await _interpretadas.GetQueryableAsync())
                .Where(x => x.Pedida && x.PedidaUtc <= ahora).OrderBy(x => x.PedidaUtc).Select(x => x.Id).Take(maximo)];
            await uow.CompleteAsync();
        }

        var hechas = 0;
        foreach (var id in ids)
        {
            try
            {
                using var uow = _unidades.Begin(requiresNew: true);
                var fila = await _interpretadas.GetAsync(id);
                try
                {
                    await InterpretarAsync(fila);
                    hechas++;
                }
                catch (MotorFiscalNoDisponibleException ex)
                {
                    fila.Fallar("Claude o el motor no respondieron: " + ex.Message, _reloj.Now.ToUniversalTime());
                }
                catch (Exception ex) when (ex is UserFriendlyException or BusinessException)
                {
                    fila.Detener(ex.Message);
                }

                await _interpretadas.UpdateAsync(fila, autoSave: true);
                await uow.CompleteAsync();
            }
            catch (AbpDbConcurrencyException)
            {
                // Otra petición la tocó mientras tanto: se intenta en la siguiente vuelta.
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "No se pudo interpretar la operación {Id}", id);
            }
        }

        return hechas;
    }

    private async Task InterpretarAsync(OperacionInterpretada fila)
    {
        var op = await _operaciones.FirstOrDefaultAsync(o => o.Id == fila.OperacionId)
                 ?? throw new UserFriendlyException("La operación ya no existe.");
        var e = (await EntradasAsync(fila.OrganizacionId, [op])).Single();
        if (string.IsNullOrWhiteSpace(e.Perfil.Rfc))
        {
            throw new UserFriendlyException("Falta el RFC del cliente en su perfil fiscal.");
        }

        if (!e.TieneFuentes)
        {
            throw new UserFriendlyException("La operación no tiene factura, ticket ni movimiento del banco que leer.");
        }

        var factura = e.Factura == null ? null : new ArchivoParaElMotor(e.Factura.NombreOriginal, await _almacen.LeerAsync(e.Factura));
        InterpretacionDelMotor r;
        using (ClienteDelMotor.Para(fila.OrganizacionId))
        {
            r = await _motor.InterpretarOperacionAsync(e.Perfil, e.Datos, factura, e.Control, e.Banco);
        }
        fila.Guardar(e.Huella, JsonSerializer.Serialize(r), _reloj.Now.ToUniversalTime());
    }
}
