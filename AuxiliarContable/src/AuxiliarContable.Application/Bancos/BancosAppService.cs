using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Bancos;

/// <summary>Ver <see cref="IBancosAppService"/>.</summary>
[Authorize]
public class BancosAppService : AuxiliarContableAppService, IBancosAppService
{
    /// <summary>Los mismos topes que acepta el motor (servicio/app.py).</summary>
    public const int MaxEstadosDeCuenta = 24;

    public const int MaxFacturas = 5000;

    private readonly IRepository<Documento, Guid> _documentos;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly Autorizador _autorizador;
    private readonly IPerfilFiscalAppService _perfiles;
    private readonly IMotorFiscal _motor;
    private readonly GestorDePeriodos _periodos;
    private readonly GestorDeConciliaciones _conciliaciones;

    public BancosAppService(
        IRepository<Documento, Guid> documentos,
        AlmacenDeDocumentos almacen,
        Autorizador autorizador,
        IPerfilFiscalAppService perfiles,
        IMotorFiscal motor,
        GestorDePeriodos periodos,
        GestorDeConciliaciones conciliaciones)
    {
        _documentos = documentos;
        _almacen = almacen;
        _autorizador = autorizador;
        _perfiles = perfiles;
        _motor = motor;
        _periodos = periodos;
        _conciliaciones = conciliaciones;
    }

    public async Task<LecturaDeEstados> LeerEstadoDeCuentaAsync(Guid organizacionId, Guid documentoId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        var documento = await _documentos.FindAsync(documentoId);
        // De otro cliente = no existe (no se confirma que exista).
        if (documento == null || documento.OrganizacionId != organizacionId)
        {
            throw new EntityNotFoundException(typeof(Documento), documentoId);
        }

        if (documento.Tipo != TipoDeDocumento.EstadoDeCuenta)
        {
            throw new UserFriendlyException("Ese documento no es un estado de cuenta.");
        }

        var archivo = new ArchivoParaElMotor(documento.NombreOriginal, await _almacen.LeerAsync(documento));
        return await AlMotorAsync(() => _motor.LeerEstadosDeCuentaAsync([archivo]));
    }

    public async Task<ConciliacionDelMesDto> ConciliarAsync(Guid organizacionId, int anio, int mes)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }

        // Un periodo cerrado se queda con la conciliación con la que se cerró.
        if (await _periodos.EstaCerradoAsync(organizacionId, anio, mes))
        {
            throw new UserFriendlyException(
                $"El periodo {mes:00}/{anio} ya está cerrado: se queda con la conciliación con la que se cerró. Para volver a conciliarlo hay que reabrirlo.");
        }

        var rfc = await RfcDelClienteAsync(organizacionId);

        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1);
        var delMes = await AsyncExecuter.ToListAsync(
            from d in await _documentos.GetQueryableAsync()
            where d.OrganizacionId == organizacionId
                  && (d.Tipo == TipoDeDocumento.EstadoDeCuenta || d.Tipo == TipoDeDocumento.Cfdi)
                  && (d.FechaDelDocumento ?? d.CreationTime) >= desde
                  && (d.FechaDelDocumento ?? d.CreationTime) < hasta
            select d);

        var estados = delMes.Where(d => d.Tipo == TipoDeDocumento.EstadoDeCuenta).ToList();
        var facturas = delMes.Where(d => d.Tipo == TipoDeDocumento.Cfdi).ToList();
        if (estados.Count == 0)
        {
            throw new UserFriendlyException($"No hay estados de cuenta de {desde:MM/yyyy} en la bóveda. Súbelos en «Documentos» eligiendo ese mes; si ya los subiste, ahí mismo cámbialos de mes.");
        }

        if (estados.Count > MaxEstadosDeCuenta || facturas.Count > MaxFacturas)
        {
            throw new UserFriendlyException($"Son demasiados archivos para un mes (máximo {MaxEstadosDeCuenta} estados de cuenta y {MaxFacturas} facturas).");
        }

        var resultado = await AlMotorAsync(async () => await _motor.ConciliarAsync(rfc,
            await ArchivosAsync(estados), await ArchivosAsync(facturas)));

        // Se guarda: es la que cuenta para el cierre del mes mientras no cambien sus archivos.
        var guardada = await _conciliaciones.RegistrarAsync(organizacionId, anio, mes, rfc, estados.Count, facturas.Count,
            GestorDePeriodos.HuellaDe(delMes), resultado);
        return Describir(guardada, resultado);
    }

    /// <summary>Meses anteriores que se mandan al diagnóstico de facturas: ahí suelen estar las PPD que se pagan en el mes.</summary>
    public const int MesesDeHistoria = 3;

    public async Task<DiagnosticoDeFacturas> DiagnosticarFacturasAsync(Guid organizacionId, int anio, int mes)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }

        var rfc = await RfcDelClienteAsync(organizacionId);
        var (archivos, delMes) = await FacturasDelMesAsync(organizacionId, anio, mes);
        return await AlMotorAsync(() => _motor.DiagnosticarFacturasAsync(rfc, archivos, delMes));
    }

    public async Task<FacturasClasificadas> ClasificarFacturasAsync(Guid organizacionId, int anio, int mes)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }

        var rfc = await RfcDelClienteAsync(organizacionId);
        // El giro con que se clasifican las compras: lo que dijo el cliente, o su primera actividad de la constancia.
        var perfil = await _perfiles.GetAsync(organizacionId);
        var giro = perfil.Campos.FirstOrDefault(c => c.Clave == CamposDelPerfil.Giro)?.Valor;
        if (string.IsNullOrWhiteSpace(giro))
        {
            giro = perfil.Campos.FirstOrDefault(c => c.Clave == CamposDelPerfil.ActividadesEconomicas)?.Valor?
                .Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        }

        var (archivos, delMes) = await FacturasDelMesAsync(organizacionId, anio, mes);
        return await AlMotorAsync(() => _motor.ClasificarFacturasAsync(rfc, archivos, giro, delMes));
    }

    /// <summary>
    /// Las facturas del mes (primero) y las de los <see cref="MesesDeHistoria"/> anteriores; y cuántas son del mes. Sin
    /// facturas del mes no hay nada que revisar.
    /// </summary>
    private async Task<(List<ArchivoParaElMotor> Archivos, int DelMes)> FacturasDelMesAsync(Guid organizacionId, int anio, int mes)
    {
        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1);
        var historia = desde.AddMonths(-MesesDeHistoria);
        var facturas = await AsyncExecuter.ToListAsync(
            from d in await _documentos.GetQueryableAsync()
            where d.OrganizacionId == organizacionId
                  && d.Tipo == TipoDeDocumento.Cfdi
                  && (d.FechaDelDocumento ?? d.CreationTime) >= historia
                  && (d.FechaDelDocumento ?? d.CreationTime) < hasta
            select d);

        // Primero las del mes: el motor solo reporta lo que toca a los primeros «delMes» archivos.
        var delMes = facturas.Where(d => (d.FechaDelDocumento ?? d.CreationTime) >= desde).ToList();
        var anteriores = facturas.Where(d => (d.FechaDelDocumento ?? d.CreationTime) < desde)
            .OrderByDescending(d => d.FechaDelDocumento ?? d.CreationTime).ToList();
        if (delMes.Count == 0)
        {
            throw new UserFriendlyException($"No hay facturas de {desde:MM/yyyy} en la bóveda.");
        }

        if (delMes.Count > MaxFacturas)
        {
            throw new UserFriendlyException($"Son demasiadas facturas para un mes (máximo {MaxFacturas}).");
        }

        // Lo que no quepa de la historia se queda fuera (las más viejas primero): las del mes siempre van.
        return (await ArchivosAsync(delMes.Concat(anteriores.Take(MaxFacturas - delMes.Count))), delMes.Count);
    }

    /// <summary>Una conciliación guardada, como la devuelve <see cref="ConciliarAsync"/>.</summary>
    public static ConciliacionDelMesDto Describir(ConciliacionBancaria c, ResultadoDeConciliacion? resultado = null) => new()
    {
        Anio = c.Anio,
        Mes = c.Mes,
        Rfc = c.Rfc,
        EstadosDeCuenta = c.EstadosDeCuenta,
        Facturas = c.Facturas,
        ConciliadaUtc = c.ConciliadaUtc,
        Resultado = resultado ?? GestorDeConciliaciones.Leer(c),
    };

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
    private async Task<string> RfcDelClienteAsync(Guid organizacionId)
    {
        var perfil = await _perfiles.GetAsync(organizacionId);
        var rfc = perfil.Campos.FirstOrDefault(c => c.Clave == CamposDelPerfil.Rfc)?.Valor?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(rfc))
        {
            throw new UserFriendlyException("Falta el RFC del cliente en su perfil fiscal: sin él no se sabe qué facturas emitió y cuáles recibió.");
        }

        return rfc;
    }

    /// <summary>Un motor caído o lento no es un error del usuario: se le dice qué pasa sin detalles técnicos.</summary>
    private async Task<T> AlMotorAsync<T>(Func<Task<T>> llamada)
    {
        try
        {
            return await llamada();
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor fiscal no respondió");
            throw new UserFriendlyException("El motor fiscal no está disponible en este momento. Intenta de nuevo en unos minutos.");
        }
    }
}
