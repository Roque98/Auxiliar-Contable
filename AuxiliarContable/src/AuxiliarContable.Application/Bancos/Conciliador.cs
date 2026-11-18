using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.PerfilesFiscales;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace AuxiliarContable.Bancos;

/// <summary>
/// La conciliación bancaria de un mes (sin usuario: la usan la pantalla y la contabilidad al día). Manda al motor los
/// estados de cuenta y las facturas del mes y la guarda. Lanza <see cref="UserFriendlyException"/> si falta algo y
/// <see cref="MotorFiscalNoDisponibleException"/> si el motor no responde.
/// </summary>
public class Conciliador : ITransientDependency
{
    /// <summary>Los mismos topes que acepta el motor (servicio/app.py).</summary>
    public const int MaxEstadosDeCuenta = 24;

    public const int MaxFacturas = 5000;

    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly IRepository<ConciliacionBancaria, Guid> _guardadas;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly IMotorFiscal _motor;
    private readonly GestorDePeriodos _periodos;
    private readonly GestorDeConciliaciones _conciliaciones;
    private readonly IAsyncQueryableExecuter _consultas;

    public Conciliador(
        IRepository<Documento, Guid> documentos,
        IRepository<PerfilFiscal, Guid> perfiles,
        IRepository<ConciliacionBancaria, Guid> guardadas,
        AlmacenDeDocumentos almacen,
        IMotorFiscal motor,
        GestorDePeriodos periodos,
        GestorDeConciliaciones conciliaciones,
        IAsyncQueryableExecuter consultas)
    {
        _documentos = documentos;
        _perfiles = perfiles;
        _guardadas = guardadas;
        _almacen = almacen;
        _motor = motor;
        _periodos = periodos;
        _conciliaciones = conciliaciones;
        _consultas = consultas;
    }

    public sealed record Conciliada(ConciliacionBancaria Guardada, ResultadoDeConciliacion Resultado);

    public async Task<Conciliada> ConciliarAsync(Guid organizacionId, int anio, int mes)
    {
        // Un periodo cerrado se queda con la conciliación con la que se cerró.
        if (await _periodos.EstaCerradoAsync(organizacionId, anio, mes))
        {
            throw new UserFriendlyException(
                $"El periodo {mes:00}/{anio} ya está cerrado: se queda con la conciliación con la que se cerró. Para volver a conciliarlo hay que reabrirlo.");
        }

        var rfc = await RfcAsync(organizacionId);
        var delMes = await DelMesAsync(organizacionId, anio, mes);
        var estados = delMes.Where(d => d.Tipo == TipoDeDocumento.EstadoDeCuenta).ToList();
        var facturas = delMes.Where(d => d.Tipo == TipoDeDocumento.Cfdi).ToList();
        var desde = new DateTime(anio, mes, 1);
        if (estados.Count == 0)
        {
            throw new UserFriendlyException($"No hay estados de cuenta de {desde:MM/yyyy} en la bóveda. Súbelos en «Documentos» eligiendo ese mes; si ya los subiste, ahí mismo cámbialos de mes.");
        }

        if (estados.Count > MaxEstadosDeCuenta || facturas.Count > MaxFacturas)
        {
            throw new UserFriendlyException($"Son demasiados archivos para un mes (máximo {MaxEstadosDeCuenta} estados de cuenta y {MaxFacturas} facturas).");
        }

        var resultado = await _motor.ConciliarAsync(rfc, await ArchivosAsync(estados), await ArchivosAsync(facturas));

        // Se guarda: es la que cuenta para el cierre del mes mientras no cambien sus archivos.
        var guardada = await _conciliaciones.RegistrarAsync(organizacionId, anio, mes, rfc, estados.Count, facturas.Count,
            GestorDePeriodos.HuellaDe(delMes), resultado);
        return new Conciliada(guardada, resultado);
    }

    /// <summary>
    /// ¿Hay que (re)conciliar? Hay estados de cuenta del mes y no hay conciliación guardada, o sus archivos ya no son los
    /// de la guardada (entró una factura, se cambió un estado de cuenta…).
    /// </summary>
    public async Task<bool> HaceFaltaAsync(Guid organizacionId, int anio, int mes)
    {
        var delMes = await DelMesAsync(organizacionId, anio, mes);
        if (!delMes.Any(d => d.Tipo == TipoDeDocumento.EstadoDeCuenta))
        {
            return false;
        }

        var guardada = await _guardadas.FirstOrDefaultAsync(c => c.OrganizacionId == organizacionId && c.Anio == anio && c.Mes == mes);
        return guardada == null || guardada.Huella != GestorDePeriodos.HuellaDe(delMes);
    }

    private async Task<List<Documento>> DelMesAsync(Guid organizacionId, int anio, int mes)
    {
        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1);
        return await _consultas.ToListAsync(
            from d in await _documentos.GetQueryableAsync()
            where d.OrganizacionId == organizacionId
                  && (d.Tipo == TipoDeDocumento.EstadoDeCuenta || d.Tipo == TipoDeDocumento.Cfdi)
                  && (d.FechaDelDocumento ?? d.CreationTime) >= desde
                  && (d.FechaDelDocumento ?? d.CreationTime) < hasta
            select d);
    }

    private async Task<List<ArchivoParaElMotor>> ArchivosAsync(IEnumerable<Documento> documentos)
    {
        var lista = new List<ArchivoParaElMotor>();
        foreach (var d in documentos)
        {
            lista.Add(new ArchivoParaElMotor(d.NombreOriginal, await _almacen.LeerAsync(d)));
        }

        return lista;
    }

    /// <summary>Emitidas y recibidas se distinguen por el RFC del cliente: sin él no se sabe de qué lado va cada factura.</summary>
    private async Task<string> RfcAsync(Guid organizacionId)
    {
        var perfil = await _perfiles.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId);
        var rfc = perfil?.Dato(CamposDelPerfil.Rfc)?.Valor?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(rfc))
        {
            throw new UserFriendlyException("Falta el RFC del cliente en su perfil fiscal: sin él no se sabe qué facturas emitió y cuáles recibió.");
        }

        return rfc;
    }
}
