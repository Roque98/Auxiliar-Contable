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
    private readonly IRepository<PolizasDelMes, Guid> _polizas;
    private readonly IRepository<SaldosDeApertura, Guid> _aperturas;
    private readonly IRepository<ContabilidadDelMes, Guid> _meses;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly IPerfilFiscalAppService _perfiles;
    private readonly IMotorFiscal _motor;
    private readonly IOperacionesAppService _operacionesApp;
    private readonly GestorDePeriodos _periodos;
    private readonly Contabilizador _contabilizador;
    private readonly ContabilidadAlDia _alDia;
    private readonly Autorizador _autorizador;

    public MesaDeTrabajoAppService(
        IRepository<Operacion, Guid> operaciones,
        IRepository<Documento, Guid> documentos,
        IRepository<ConciliacionBancaria, Guid> conciliaciones,
        IRepository<PolizasDelMes, Guid> polizas,
        IRepository<SaldosDeApertura, Guid> aperturas,
        IRepository<ContabilidadDelMes, Guid> meses,
        AlmacenDeDocumentos almacen,
        IPerfilFiscalAppService perfiles,
        IMotorFiscal motor,
        IOperacionesAppService operacionesApp,
        GestorDePeriodos periodos,
        Contabilizador contabilizador,
        ContabilidadAlDia alDia,
        Autorizador autorizador)
    {
        _operaciones = operaciones;
        _documentos = documentos;
        _conciliaciones = conciliaciones;
        _polizas = polizas;
        _aperturas = aperturas;
        _meses = meses;
        _almacen = almacen;
        _perfiles = perfiles;
        _motor = motor;
        _operacionesApp = operacionesApp;
        _periodos = periodos;
        _contabilizador = contabilizador;
        _alDia = alDia;
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
                using (ClienteDelMotor.Para(organizacionId))
                {
                    a = await _motor.AnalizarDocumentoAsync(new ArchivoParaElMotor(d.NombreOriginal, await _almacen.LeerAsync(d)), rfc, giro);
                }
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
        var (desde, _) = Mes(anio, mes);
        return await _contabilizador.AmarrarAsync(organizacionId, anio, mes, conClaude: true)
               ?? throw new UserFriendlyException($"Primero concilia {desde:MM/yyyy} en «Cierre del mes»: de ahí salen los movimientos del banco.");
    }

    public async Task<PolizasDelMesDto?> GetPolizasAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        Mes(anio, mes);
        var p = await _polizas.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId && x.Anio == anio && x.Mes == mes);
        return p == null ? null : Describir(p, await InventarioAnteriorAsync(organizacionId, anio, mes));
    }

    /// <summary>El inventario final que se capturó el mes anterior.</summary>
    private async Task<decimal?> InventarioAnteriorAsync(Guid organizacionId, int anio, int mes)
    {
        var antes = new DateTime(anio, mes, 1).AddMonths(-1);
        var p = await _polizas.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId && x.Anio == antes.Year && x.Mes == antes.Month);
        return p?.DatosDeCierre == null ? null : JsonSerializer.Deserialize<DatosDeCierre>(p.DatosDeCierre)?.InventarioFinal;
    }

    public async Task<PolizasDelMesDto> GenerarPolizasAsync(Guid organizacionId, int anio, int mes, DatosDeCierre cierreCapturado)
    {
        var cierre = cierreCapturado is { InventarioInicial: null, InventarioFinal: null, CostoVendido: null } ? null : cierreCapturado;
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        Mes(anio, mes);
        PolizasDelMes guardadas;
        try
        {
            guardadas = await _contabilizador.ContabilizarAsync(organizacionId, anio, mes, cierre, conservarCierre: false);
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor no pudo contabilizar");
            throw new UserFriendlyException("El motor contable no está disponible en este momento. Intenta de nuevo en unos minutos.");
        }

        // Recién hecha a mano: la contabilidad al día no tiene que repetirla.
        await _alDia.HechoAsync(organizacionId, anio, mes);
        return Describir(guardadas, await InventarioAnteriorAsync(organizacionId, anio, mes));
    }

    public async Task<ContabilidadAlDiaDto> GetContabilidadAlDiaAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        Mes(anio, mes);
        var fila = await _meses.FirstOrDefaultAsync(m => m.OrganizacionId == organizacionId && m.Anio == anio && m.Mes == mes);
        return Describir(anio, mes, fila);
    }

    public async Task<ContabilidadAlDiaDto> ActualizarContabilidadAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        var (desde, _) = Mes(anio, mes);
        await _periodos.ExigirAbiertoAsync(organizacionId, desde);
        return Describir(anio, mes, await _alDia.PedirAhoraAsync(organizacionId, anio, mes));
    }

    private static ContabilidadAlDiaDto Describir(int anio, int mes, ContabilidadDelMes? fila) => new()
    {
        Anio = anio,
        Mes = mes,
        Actualizando = fila?.PorActualizar == true,
        SeActualizaUtc = fila?.PorActualizar == true ? fila.PedidoUtc + ContabilidadAlDia.Espera : null,
        ActualizadaUtc = fila?.ActualizadaUtc,
        Resumen = fila?.Resumen,
        Error = fila?.Error,
    };

    public async Task<SaldosDeAperturaDto?> GetAperturaAsync(Guid organizacionId)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        var a = await _aperturas.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId);
        return a == null ? null : new SaldosDeAperturaDto
        {
            Anio = a.Anio, Mes = a.Mes, Saldos = JsonSerializer.Deserialize<List<SaldoDeApertura>>(a.Saldos) ?? [],
        };
    }

    public async Task<SaldosDeAperturaDto> GuardarAperturaAsync(Guid organizacionId, SaldosDeAperturaDto input)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        Mes(input.Anio, input.Mes);
        var saldos = (input.Saldos ?? []).Where(s => s.Deudor != 0 || s.Acreedor != 0).Select(s => new SaldoDeApertura
        {
            Cuenta = (s.Cuenta ?? "").Trim(), Deudor = decimal.Round(s.Deudor, 2), Acreedor = decimal.Round(s.Acreedor, 2),
        }).ToList();
        var mal = saldos.FirstOrDefault(s => !System.Text.RegularExpressions.Regex.IsMatch(s.Cuenta, @"^\d{3}(\.\d{2,3})?$")
                                            || s.Deudor < 0 || s.Acreedor < 0 || (s.Deudor > 0 && s.Acreedor > 0));
        if (mal != null)
        {
            throw new UserFriendlyException($"Revisa la cuenta «{mal.Cuenta}»: va con el código agrupador (como 102.01) y un saldo deudor o acreedor, no los dos.");
        }

        if (saldos.GroupBy(s => s.Cuenta).Any(g => g.Count() > 1))
        {
            throw new UserFriendlyException("Hay cuentas repetidas en los saldos de apertura.");
        }

        var deudores = saldos.Sum(s => s.Deudor);
        var acreedores = saldos.Sum(s => s.Acreedor);
        if (deudores != acreedores)
        {
            throw new UserFriendlyException($"Los saldos de apertura no cuadran: deudores {deudores:N2} y acreedores {acreedores:N2}.");
        }

        var a = await _aperturas.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId);
        var nueva = a == null;
        a ??= new SaldosDeApertura(GuidGenerator.Create(), organizacionId);
        a.Poner(input.Anio, input.Mes, JsonSerializer.Serialize(saldos));
        if (nueva)
        {
            await _aperturas.InsertAsync(a, autoSave: true);
        }
        else
        {
            await _aperturas.UpdateAsync(a, autoSave: true);
        }

        return new SaldosDeAperturaDto { Anio = a.Anio, Mes = a.Mes, Saldos = saldos };
    }

    public async Task<ContabilidadElectronicaDto> ContabilidadElectronicaAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        Mes(anio, mes);
        var (rfc, _) = await RfcYGiroAsync(organizacionId);
        if (rfc == null)
        {
            throw new UserFriendlyException("Falta el RFC del cliente en su perfil fiscal.");
        }

        var clave = anio * 100 + mes;
        var meses = (await _polizas.GetListAsync(p => p.OrganizacionId == organizacionId && p.Anio * 100 + p.Mes <= clave))
            .OrderBy(p => p.Anio).ThenBy(p => p.Mes).ToList();
        if (!meses.Any(p => p.Anio == anio && p.Mes == mes))
        {
            throw new UserFriendlyException($"Primero genera las pólizas de {mes:00}/{anio} en esta mesa.");
        }

        var apertura = await _aperturas.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId);
        if (apertura != null)
        {
            meses = meses.Where(p => p.Anio * 100 + p.Mes >= apertura.Anio * 100 + apertura.Mes).ToList();
        }

        // Un mes sin pólizas entre la apertura y este deja la balanza incompleta.
        var avisos = new List<string>();
        var desde = apertura != null ? new DateTime(apertura.Anio, apertura.Mes, 1) : new DateTime(meses[0].Anio, meses[0].Mes, 1);
        for (var f = desde; f < new DateTime(anio, mes, 1); f = f.AddMonths(1))
        {
            if (!meses.Any(p => p.Anio == f.Year && p.Mes == f.Month))
            {
                avisos.Add($"Faltan las pólizas de {f:MM/yyyy}: la balanza no las incluye.");
            }
        }

        // El catálogo propio del cliente: el último que se subió.
        var catalogo = (await _documentos.GetListAsync(d => d.OrganizacionId == organizacionId && d.Tipo == TipoDeDocumento.CatalogoDeCuentas))
            .OrderByDescending(d => d.CreationTime).FirstOrDefault();
        var catalogoParaElMotor = catalogo == null ? null : new ArchivoParaElMotor(catalogo.NombreOriginal, await _almacen.LeerAsync(catalogo));

        ContabilidadElectronica resultado;
        try
        {
            resultado = await _motor.ContabilidadElectronicaAsync(rfc, anio, mes,
                apertura == null
                    ? null
                    : new AperturaParaElMotor
                    {
                        Periodo = $"{apertura.Anio:0000}-{apertura.Mes:00}",
                        Saldos = JsonSerializer.Deserialize<List<SaldoDeApertura>>(apertura.Saldos) ?? [],
                    },
                meses.Select(p => new MesParaElMotor
                {
                    Periodo = $"{p.Anio:0000}-{p.Mes:00}",
                    Polizas = (JsonSerializer.Deserialize<ResultadoContable>(p.Resultado) ?? new ResultadoContable()).Polizas,
                }).ToList(),
                catalogoParaElMotor);
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor no pudo generar la contabilidad electrónica");
            throw new UserFriendlyException("El motor contable no está disponible en este momento. Intenta de nuevo en unos minutos.");
        }

        resultado.Avisos.InsertRange(0, avisos);
        return new ContabilidadElectronicaDto { Anio = anio, Mes = mes, Resultado = resultado };
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

    private static PolizasDelMesDto Describir(PolizasDelMes p, decimal? inventarioAnterior)
    {
        var r = JsonSerializer.Deserialize<ResultadoContable>(p.Resultado) ?? new ResultadoContable();
        return new PolizasDelMesDto
        {
            Anio = p.Anio,
            Mes = p.Mes,
            GeneradoUtc = p.GeneradoUtc,
            Resultado = r,
            Detenidas = r.Revision.Where(x => x.Nivel == "detiene").Select(x => x.Operacion ?? "").Distinct().Count(),
            Cierre = p.DatosDeCierre == null ? null : JsonSerializer.Deserialize<DatosDeCierre>(p.DatosDeCierre),
            InventarioInicialSugerido = inventarioAnterior,
        };
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

    private static string Corto(string texto, int max) => texto.Trim().Length <= max ? texto.Trim() : texto.Trim()[..max];
}
