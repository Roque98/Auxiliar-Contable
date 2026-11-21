using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
/// El pago provisional de un mes con las facturas del cliente (sin usuario: la usa también el actualizador). Lee el perfil
/// directo del repositorio, manda al motor las facturas del ejercicio (y las del anterior, por los complementos de pago) y
/// guarda el resultado en <see cref="ImpuestosDelMes"/>.
/// </summary>
public class CalculadoraDeImpuestos : ITransientDependency
{
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly IRepository<ImpuestosDelMes, Guid> _impuestos;
    private readonly IRepository<PolizasDelMes, Guid> _polizas;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly IMotorFiscal _motor;
    private readonly IGuidGenerator _guids;
    private readonly IClock _reloj;

    public CalculadoraDeImpuestos(
        IRepository<Documento, Guid> documentos,
        IRepository<PerfilFiscal, Guid> perfiles,
        IRepository<ImpuestosDelMes, Guid> impuestos,
        IRepository<PolizasDelMes, Guid> polizas,
        AlmacenDeDocumentos almacen,
        IMotorFiscal motor,
        IGuidGenerator guids,
        IClock reloj)
    {
        _documentos = documentos;
        _perfiles = perfiles;
        _impuestos = impuestos;
        _polizas = polizas;
        _almacen = almacen;
        _motor = motor;
        _guids = guids;
        _reloj = reloj;
    }

    public sealed record Perfil(string? Rfc, string? Nombre, List<string> Regimenes, decimal? Coeficiente,
        string? Plataformas = null, string? ActividadDePlataformas = null)
    {
        /// <summary>
        /// Si cambia algo de esto, el cálculo guardado ya no vale. Lo de plataformas solo entra si se contestó: así la huella
        /// de los demás clientes no cambia y no se recalcula todo.
        /// </summary>
        public string Huella => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{Rfc}|{string.Join(";", Regimenes)}|{Coeficiente?.ToString(CultureInfo.InvariantCulture)}"
            + (Plataformas == null && ActividadDePlataformas == null ? "" : $"|{Plataformas}|{ActividadDePlataformas}"))))[..32];
    }

    public sealed record Calculo(PagosProvisionales Resultado, int Facturas, int Canceladas, int Danadas, int SinEstatus, string HuellaDelPerfil);

    public async Task<Perfil> PerfilAsync(Guid organizacionId)
    {
        var perfil = await _perfiles.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId);
        string? Campo(string clave) => perfil?.Dato(clave)?.Valor?.Trim();
        var rfc = Campo(CamposDelPerfil.Rfc)?.ToUpperInvariant();
        decimal? coeficiente = decimal.TryParse(Campo(CamposDelPerfil.CoeficienteDeUtilidad), NumberStyles.Number, CultureInfo.InvariantCulture, out var c)
            ? c
            : null;
        return new Perfil(string.IsNullOrWhiteSpace(rfc) ? null : rfc, Campo(CamposDelPerfil.Nombre), Lista(Campo(CamposDelPerfil.Regimenes)), coeficiente,
            Campo(CamposDelPerfil.PagoDePlataformas), Campo(CamposDelPerfil.ActividadEnPlataformas));
    }

    /// <summary>
    /// Lanza <see cref="UserFriendlyException"/> si falta el RFC o no hay facturas del ejercicio, y
    /// <see cref="MotorFiscalNoDisponibleException"/> si el motor no responde.
    /// </summary>
    public async Task<Calculo> CalcularAsync(Guid organizacionId, int anio, int mes, bool conExcel)
    {
        var perfil = await PerfilAsync(organizacionId);
        if (perfil.Rfc == null)
        {
            throw new UserFriendlyException("Falta el RFC del cliente en su perfil fiscal.");
        }

        var (facturas, buenas) = await FacturasAsync(organizacionId, anio, mes);
        var delEjercicio = buenas.Where(d => Fecha(d).Year == anio).ToList();
        if (delEjercicio.Count == 0)
        {
            throw new UserFriendlyException($"No hay facturas de {anio}: sube las del cliente o bájalas del SAT con el Descargador 1.");
        }

        var (archivos, canceladas) = await ParaElMotorAsync(buenas);
        var resultado = await _motor.PagosProvisionalesAsync(
            perfil.Rfc, perfil.Regimenes, $"{anio:0000}-{mes:00}", perfil.Nombre, archivos, canceladas, perfil.Coeficiente, conExcel,
            perfil.Plataformas, perfil.ActividadDePlataformas, await CobrosDelBancoAsync(organizacionId, anio, mes));

        var sinEstatus = delEjercicio.Count(d => d.Uuid != null && d.EstatusSat == null);
        if (sinEstatus > 0)
        {
            resultado.Avisos.Insert(0, $"{sinEstatus} facturas del ejercicio no tienen su estatus del SAT: si alguna está cancelada, cuenta de más. Corre una orden del Descargador 1 para revisarlas.");
        }

        var danadas = facturas.Count(d => d.XmlDanado && (d.FechaDelDocumento ?? d.CreationTime).Year == anio);
        if (danadas > 0)
        {
            resultado.Avisos.Insert(0, $"{danadas} XML del ejercicio están dañados y no entran al cálculo: bájalos otra vez con el Descargador 1.");
        }

        return new Calculo(resultado, delEjercicio.Count, canceladas.Count, danadas, sinEstatus, perfil.Huella);
    }

    /// <summary>
    /// La DIOT del mes (la del Armador, en el motor): por flujo de pagos, con lo que no entra y el archivo de carga masiva.
    /// Lanza <see cref="UserFriendlyException"/> si falta el RFC o no hay facturas del mes, y
    /// <see cref="MotorFiscalNoDisponibleException"/> si el motor no responde.
    /// </summary>
    public async Task<DiotDelMotor> DiotAsync(Guid organizacionId, int anio, int mes)
    {
        var perfil = await PerfilAsync(organizacionId);
        if (perfil.Rfc == null)
        {
            throw new UserFriendlyException("Falta el RFC del cliente en su perfil fiscal.");
        }

        var (facturas, buenas) = await FacturasAsync(organizacionId, anio, mes);
        bool DelMes(Documento d) => Fecha(d).Year == anio && Fecha(d).Month == mes;
        var delMes = buenas.Where(DelMes).ToList();
        if (delMes.Count == 0)
        {
            throw new UserFriendlyException($"No hay facturas de {mes:00}/{anio}: sube las del cliente o bájalas del SAT con el Descargador 1.");
        }

        var (archivos, canceladas) = await ParaElMotorAsync(buenas);
        var resultado = await _motor.DiotAsync(perfil.Rfc, $"{anio:0000}-{mes:00}", archivos, canceladas, perfil.Regimenes);

        var sinEstatus = delMes.Count(d => d.Uuid != null && d.EstatusSat == null);
        if (sinEstatus > 0)
        {
            resultado.Avisos.Insert(0, $"{sinEstatus} facturas del mes no tienen su estatus del SAT: si alguna está cancelada, cuenta de más. Corre una orden del Descargador 1 para revisarlas.");
        }

        var danadas = facturas.Count(d => d.XmlDanado && DelMes(d));
        if (danadas > 0)
        {
            resultado.Avisos.Insert(0, $"{danadas} XML del mes están dañados y no entran a la DIOT: bájalos otra vez con el Descargador 1.");
        }

        return resultado;
    }

    /// <summary>
    /// La declaración anual del ejercicio con sus facturas y lo capturado. Lanza <see cref="UserFriendlyException"/> si
    /// falta el RFC o no hay facturas del ejercicio, y <see cref="MotorFiscalNoDisponibleException"/> si el motor no responde.
    /// </summary>
    public async Task<AnualDelMotor> AnualAsync(Guid organizacionId, int anio, CapturaDeAnual captura)
    {
        var perfil = await PerfilAsync(organizacionId);
        if (perfil.Rfc == null)
        {
            throw new UserFriendlyException("Falta el RFC del cliente en su perfil fiscal.");
        }

        var (facturas, buenas) = await FacturasAsync(organizacionId, anio, 12);
        var delEjercicio = buenas.Where(d => Fecha(d).Year == anio).ToList();
        if (delEjercicio.Count == 0)
        {
            throw new UserFriendlyException($"No hay facturas de {anio}: sube las del cliente o bájalas del SAT con el Descargador 1.");
        }

        var (archivos, canceladas) = await ParaElMotorAsync(buenas);
        var resultado = await _motor.AnualAsync(perfil.Rfc, perfil.Regimenes, anio, archivos, canceladas, perfil.Coeficiente,
            perfil.Plataformas, perfil.ActividadDePlataformas, captura.ParaElMotor());

        var sinEstatus = delEjercicio.Count(d => d.Uuid != null && d.EstatusSat == null);
        if (sinEstatus > 0)
        {
            resultado.Avisos.Insert(0, $"{sinEstatus} facturas del ejercicio no tienen su estatus del SAT: si alguna está cancelada, cuenta de más. Corre una orden del Descargador 1 para revisarlas.");
        }

        var danadas = facturas.Count(d => d.XmlDanado && Fecha(d).Year == anio);
        if (danadas > 0)
        {
            resultado.Avisos.Insert(0, $"{danadas} XML del ejercicio están dañados y no entran a la anual: bájalos otra vez con el Descargador 1.");
        }

        return resultado;
    }

    private static DateTime Fecha(Documento d) => d.FechaDelDocumento ?? d.CreationTime;

    /// <summary>
    /// Las facturas del ejercicio hasta el mes y las del anterior (un complemento de este año puede pagar una factura PPD
    /// del pasado). -> (todas, las que tienen el XML bueno)
    /// </summary>
    private async Task<(List<Documento> Facturas, List<Documento> Buenas)> FacturasAsync(Guid organizacionId, int anio, int mes)
    {
        var desde = new DateTime(anio - 1, 1, 1);
        var hasta = new DateTime(anio, mes, 1).AddMonths(1);
        var facturas = await _documentos.GetListAsync(d => d.OrganizacionId == organizacionId && d.Tipo == TipoDeDocumento.Cfdi
            && ((d.FechaDelDocumento != null && d.FechaDelDocumento >= desde && d.FechaDelDocumento < hasta)
                || (d.FechaDelDocumento == null && d.CreationTime >= desde && d.CreationTime < hasta)));
        return (facturas, facturas.Where(d => !d.XmlDanado).ToList());
    }

    private async Task<(List<ArchivoParaElMotor> Archivos, List<string> Canceladas)> ParaElMotorAsync(List<Documento> buenas)
    {
        var archivos = new List<ArchivoParaElMotor>();
        foreach (var d in buenas)
        {
            archivos.Add(new ArchivoParaElMotor($"{d.Uuid ?? d.Id.ToString()}.xml", await _almacen.LeerAsync(d)));
        }

        return (archivos, buenas.Where(d => d.Cancelada && d.Uuid != null).Select(d => d.Uuid!).ToList());
    }

    /// <summary>La fila del mes (la crea si no existe). <paramref name="pedidoUtc"/> solo cuenta al crearla.</summary>
    public async Task<ImpuestosDelMes> FilaAsync(Guid organizacionId, int anio, int mes, DateTime pedidoUtc)
    {
        var fila = await _impuestos.FirstOrDefaultAsync(i => i.OrganizacionId == organizacionId && i.Anio == anio && i.Mes == mes);
        if (fila != null)
        {
            return fila;
        }

        return await _impuestos.InsertAsync(new ImpuestosDelMes(_guids.Create(), organizacionId, anio, mes, pedidoUtc), autoSave: true);
    }

    /// <summary>
    /// Facturas PPD que la contabilidad del ejercicio (hasta el mes) muestra cobradas o pagadas por el banco y sin
    /// complemento de pago (revisión «PPD_SIN_COMPLEMENTO» con su póliza): el motor las toma por flujo en la fecha del
    /// banco. Pedido 19-nov-2026: «el día 07 ya la pagaron… ahora sí se tomaría para el cálculo de impuestos».
    /// </summary>
    public async Task<List<CobroDelBanco>> CobrosDelBancoAsync(Guid organizacionId, int anio, int mes)
    {
        var meses = await _polizas.GetListAsync(p => p.OrganizacionId == organizacionId && p.Anio == anio && p.Mes <= mes);
        var cobros = new List<CobroDelBanco>();
        foreach (var m in meses)
        {
            ResultadoContable? r;
            try
            {
                r = JsonSerializer.Deserialize<ResultadoContable>(m.Resultado);
            }
            catch (JsonException)
            {
                continue;
            }

            if (r == null)
            {
                continue;
            }

            var ops = r.Revision.Where(x => x.Codigo == "PPD_SIN_COMPLEMENTO" && x.Operacion != null).Select(x => x.Operacion!).ToHashSet();
            foreach (var p in r.Polizas.Where(p => ops.Contains(p.Operacion) && !string.IsNullOrWhiteSpace(p.Uuid) && p.Total is > 0))
            {
                // Cobro: abona clientes (105); pago: carga proveedores (201).
                var cobro = p.Movimientos.Any(x => x.Cuenta.StartsWith("105") && x.Haber > 0);
                var pago = p.Movimientos.Any(x => x.Cuenta.StartsWith("201") && x.Debe > 0);
                if (!cobro && !pago)
                {
                    continue;
                }

                cobros.Add(new CobroDelBanco
                {
                    Uuid = p.Uuid!, Fecha = p.Fecha.Length >= 10 ? p.Fecha[..10] : p.Fecha, Importe = p.Total!.Value,
                    Lado = cobro ? "emitida" : "recibida",
                });
            }
        }

        return [.. cobros.DistinctBy(c => (c.Uuid.ToUpperInvariant(), c.Fecha))];
    }

    public async Task GuardarAsync(ImpuestosDelMes fila, Calculo c, DateTime pedidoLeido)
    {
        // El Excel no se guarda: pesa, y se arma de nuevo al pedirlo.
        var archivo = c.Resultado.Archivo;
        c.Resultado.Archivo = null;
        var json = JsonSerializer.Serialize(c.Resultado);
        c.Resultado.Archivo = archivo;
        fila.Guardar(json, c.Facturas, c.Canceladas, c.Danadas, c.SinEstatus, c.HuellaDelPerfil, _reloj.Now.ToUniversalTime(), pedidoLeido);
        await _impuestos.UpdateAsync(fila, autoSave: true);
    }

    public static List<string> Lista(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return [];
        }

        try
        {
            using var json = JsonDocument.Parse(valor);
            if (json.RootElement.ValueKind == JsonValueKind.Array)
            {
                return json.RootElement.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
                    .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToList();
            }
        }
        catch (JsonException)
        {
            // Un texto suelto: se toma tal cual.
        }

        return [valor.Trim()];
    }
}

/// <summary>
/// Recalcula los meses «por actualizar» que llevan un momento sin cambios. Lo llama el trabajo en segundo plano del
/// host cada pocos segundos; cada mes en su propia unidad de trabajo (si uno falla, los demás siguen).
/// </summary>
public class ActualizadorDeImpuestos : ITransientDependency
{
    /// <summary>
    /// Sin cambios durante esto antes de recalcular (pedido 17-nov-2026: «10 o 15 minutos después de la última
    /// información»): un paquete de cien XML, o una mañana de facturas, se calcula una vez y no cien.
    /// </summary>
    public static readonly TimeSpan Espera = TimeSpan.FromMinutes(10);

    /// <summary>La hora de México: a las 12 de la noche se recalcula todo lo del mes en curso y el anterior.</summary>
    public static TimeZoneInfo ZonaDeMexico { get; } = BuscarZona();

    private readonly IRepository<ImpuestosDelMes, Guid> _impuestos;
    private readonly CalculadoraDeImpuestos _calculadora;
    private readonly IUnitOfWorkManager _unidades;
    private readonly IClock _reloj;

    public ILogger<ActualizadorDeImpuestos> Logger { get; set; } = NullLogger<ActualizadorDeImpuestos>.Instance;

    public ActualizadorDeImpuestos(
        IRepository<ImpuestosDelMes, Guid> impuestos, CalculadoraDeImpuestos calculadora, IUnitOfWorkManager unidades, IClock reloj)
    {
        _impuestos = impuestos;
        _calculadora = calculadora;
        _unidades = unidades;
        _reloj = reloj;
    }

    /// <returns>Cuántos meses se recalcularon.</returns>
    public async Task<int> ActualizarAsync(int maximo = 3, TimeSpan? espera = null)
    {
        List<Guid> ids;
        using (var uow = _unidades.Begin(requiresNew: true))
        {
            var limite = _reloj.Now.ToUniversalTime() - (espera ?? Espera);
            var q = (await _impuestos.GetQueryableAsync())
                .Where(i => i.PorActualizar && i.PedidoUtc <= limite)
                .OrderBy(i => i.PedidoUtc)
                .Select(i => i.Id)
                .Take(maximo);
            ids = [.. q];
            await uow.CompleteAsync();
        }

        var hechos = 0;
        foreach (var id in ids)
        {
            try
            {
                using var uow = _unidades.Begin(requiresNew: true);
                var fila = await _impuestos.GetAsync(id);
                var pedido = fila.PedidoUtc;
                try
                {
                    var c = await _calculadora.CalcularAsync(fila.OrganizacionId, fila.Anio, fila.Mes, conExcel: false);
                    await _calculadora.GuardarAsync(fila, c, pedido);
                    hechos++;
                }
                catch (Exception ex) when (ex is UserFriendlyException or MotorFiscalNoDisponibleException)
                {
                    fila.Fallar(ex.Message, _reloj.Now.ToUniversalTime());
                    await _impuestos.UpdateAsync(fila, autoSave: true);
                }

                await uow.CompleteAsync();
            }
            catch (AbpDbConcurrencyException)
            {
                // Otra petición la tocó mientras tanto: se vuelve a intentar en la siguiente vuelta.
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "No se pudo actualizar el impuesto {Id}", id);
                await AnotarFallaAsync(id, "No se pudo calcular; se vuelve a intentar en un momento.");
            }
        }

        return hechos;
    }

    /// <summary>
    /// La vuelta de medianoche: el mes en curso y el anterior de todos los clientes quedan pedidos (sin espera), para que
    /// amanezcan al día aunque no haya entrado nada (estatus del SAT, cambios del perfil…). -> cuántos meses se pidieron.
    /// </summary>
    public async Task<int> PedirLaVueltaDeLaNocheAsync(DateTime hoyEnMexico)
    {
        var actual = new DateTime(hoyEnMexico.Year, hoyEnMexico.Month, 1);
        var anterior = actual.AddMonths(-1);
        using var uow = _unidades.Begin(requiresNew: true);
        var filas = await _impuestos.GetListAsync(i => (i.Anio == actual.Year && i.Mes == actual.Month)
                                                      || (i.Anio == anterior.Year && i.Mes == anterior.Month));
        var ya = _reloj.Now.ToUniversalTime() - Espera;
        foreach (var f in filas.Where(f => !f.PorActualizar))
        {
            f.Pedir(ya);
            await _impuestos.UpdateAsync(f);
        }

        await uow.CompleteAsync();
        return filas.Count;
    }

    /// <summary>Lo que pide el auxiliar con «Actualizar ahora»: sin espera, en la siguiente vuelta.</summary>
    public static DateTime SinEspera(DateTime ahoraUtc) => ahoraUtc - Espera;

    private static TimeZoneInfo BuscarZona()
    {
        foreach (var id in new[] { "America/Mexico_City", "Central Standard Time (Mexico)" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Mexico", TimeSpan.FromHours(-6), "México", "México");
    }

    /// <summary>Que una falla inesperada no lo reintente en cada vuelta: espera como cualquier otro pedido.</summary>
    private async Task AnotarFallaAsync(Guid id, string error)
    {
        try
        {
            using var uow = _unidades.Begin(requiresNew: true);
            var fila = await _impuestos.GetAsync(id);
            fila.Fallar(error, _reloj.Now.ToUniversalTime());
            await _impuestos.UpdateAsync(fila, autoSave: true);
            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Tampoco se pudo anotar la falla del impuesto {Id}", id);
        }
    }
}
