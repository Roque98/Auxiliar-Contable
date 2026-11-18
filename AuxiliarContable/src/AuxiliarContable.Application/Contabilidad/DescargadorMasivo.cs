using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using AuxiliarContable.Credenciales;
using AuxiliarContable.Documentos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Baja las facturas de un cliente con el servicio oficial de Descarga Masiva del SAT y su e.firma guardada: pide
/// (emitidas y recibidas de un mes), revisa cada rato si el SAT ya armó los paquetes y, cuando sí, los baja y agrega a la
/// bóveda solo los UUID que faltan (<see cref="ImportadorDeCfdi"/>). Lo nuevo mueve solo el impuesto del mes.
///
/// <para>La e.firma se descifra en memoria solo durante la vuelta y nunca se escribe ni se registra; cada uso queda en la
/// bitácora de credenciales del cliente.</para>
///
/// <para>Al cerrar el mes (día 3 en adelante), si el cliente no mandó nada —marcó «no entregó nada» o no hay ni una
/// factura del mes—, se pide sola.</para>
/// </summary>
public class DescargadorMasivo : ITransientDependency
{
    /// <summary>Cada cuánto se pregunta al SAT por una solicitud en proceso.</summary>
    public static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(15);

    /// <summary>El día del mes desde el que se pide sola la del mes anterior (el SAT tarda en tener todo timbrado).</summary>
    public const int DiaDelCierre = 3;

    /// <summary>Token del SAT: dura 5 minutos; se pide otro antes.</summary>
    private static readonly TimeSpan VidaDelToken = TimeSpan.FromMinutes(4);

    private const string Aceptada = "5000";
    private const string SinInformacion = "5004";

    private readonly IRepository<SolicitudDeDescargaMasiva, Guid> _solicitudes;
    private readonly IRepository<CredencialesDelCliente, Guid> _credenciales;
    private readonly IRepository<UsoDeCredencial, Guid> _usos;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<MesSinEntrega, Guid> _sinEntrega;
    private readonly CifradorDeCredenciales _cifrador;
    private readonly CalculadoraDeImpuestos _calculadora;
    private readonly ImportadorDeCfdi _importador;
    private readonly IServicioDescargaMasiva _sat;
    private readonly IUnitOfWorkManager _unidades;
    private readonly IGuidGenerator _guids;
    private readonly IClock _reloj;

    public ILogger<DescargadorMasivo> Logger { get; set; } = NullLogger<DescargadorMasivo>.Instance;

    public DescargadorMasivo(
        IRepository<SolicitudDeDescargaMasiva, Guid> solicitudes,
        IRepository<CredencialesDelCliente, Guid> credenciales,
        IRepository<UsoDeCredencial, Guid> usos,
        IRepository<Documento, Guid> documentos,
        IRepository<MesSinEntrega, Guid> sinEntrega,
        CifradorDeCredenciales cifrador,
        CalculadoraDeImpuestos calculadora,
        ImportadorDeCfdi importador,
        IServicioDescargaMasiva sat,
        IUnitOfWorkManager unidades,
        IGuidGenerator guids,
        IClock reloj)
    {
        _solicitudes = solicitudes;
        _credenciales = credenciales;
        _usos = usos;
        _documentos = documentos;
        _sinEntrega = sinEntrega;
        _cifrador = cifrador;
        _calculadora = calculadora;
        _importador = importador;
        _sat = sat;
        _unidades = unidades;
        _guids = guids;
        _reloj = reloj;
    }

    public bool EstaConfigurado => _sat.EstaConfigurado;

    public sealed class Vuelta
    {
        public int Revisadas { get; set; }
        public int Terminadas { get; set; }
        public int Nuevas { get; set; }
        public int Fallaron { get; set; }
        public bool SatSinResponder { get; set; }
    }

    /// <summary>
    /// Por qué no se puede usar la e.firma del cliente para la descarga masiva (null si sí se puede). No descifra nada.
    /// </summary>
    public async Task<string?> ProblemaConLaEfirmaAsync(Guid organizacionId)
    {
        var c = await _credenciales.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId);
        var perfil = await _calculadora.PerfilAsync(organizacionId);
        return Problema(c, perfil.Rfc, _reloj.Now.ToUniversalTime());
    }

    private static string? Problema(CredencialesDelCliente? c, string? rfc, DateTime ahoraUtc)
    {
        if (c is not { TieneEfirma: true })
        {
            return "El cliente no tiene su e.firma guardada en Credenciales.";
        }

        if (c.ConsentimientoUtc == null)
        {
            return "Falta el consentimiento del cliente para usar su e.firma.";
        }

        if (c.VigenteHastaUtc is { } hasta && hasta < ahoraUtc)
        {
            return "La e.firma del cliente ya venció: pídele la nueva.";
        }

        if (rfc == null)
        {
            return "Falta el RFC del cliente en su perfil fiscal.";
        }

        if (!string.Equals(c.EfirmaRfc, rfc, StringComparison.OrdinalIgnoreCase))
        {
            return "La e.firma guardada es de otro RFC, no del de este cliente.";
        }

        return null;
    }

    /// <summary>
    /// Pide al SAT las emitidas y/o recibidas de un mes. Lo que ya está en proceso para ese mes no se vuelve a pedir.
    /// Si el mes es el actual, hasta este momento.
    /// </summary>
    public async Task<List<SolicitudDeDescargaMasiva>> PedirAsync(Guid organizacionId, int anio, int mes, bool emitidas, bool recibidas, bool automatica)
    {
        if (!emitidas && !recibidas)
        {
            throw new UserFriendlyException("Elige emitidas, recibidas o las dos.");
        }

        var ahoraUtc = _reloj.Now.ToUniversalTime();
        var hoy = TimeZoneInfo.ConvertTimeFromUtc(ahoraUtc, ActualizadorDeImpuestos.ZonaDeMexico);
        var desde = new DateTime(anio, mes, 1);
        if (desde > hoy)
        {
            throw new UserFriendlyException("Ese mes todavía no empieza.");
        }

        var finDelMes = desde.AddMonths(1).AddSeconds(-1);
        var hasta = finDelMes < hoy ? finDelMes : new DateTime(hoy.Year, hoy.Month, hoy.Day, hoy.Hour, hoy.Minute, 0).AddMinutes(-1);
        var activas = await _solicitudes.GetListAsync(s => s.OrganizacionId == organizacionId && s.Anio == anio && s.Mes == mes
                                                           && s.Estado == EstadoDeDescargaMasiva.EnProceso);
        var tipos = new List<bool>();
        if (emitidas && !activas.Any(s => s.Emitidas))
        {
            tipos.Add(true);
        }

        if (recibidas && !activas.Any(s => !s.Emitidas))
        {
            tipos.Add(false);
        }

        if (tipos.Count == 0)
        {
            throw new UserFriendlyException("Ya hay una descarga de ese mes en proceso: el SAT avisa cuando esté lista.");
        }

        using var efirma = await AbrirAsync(organizacionId);
        var token = await _sat.AutenticarAsync(efirma);
        var hechas = new List<SolicitudDeDescargaMasiva>();
        foreach (var esEmitida in tipos)
        {
            var respuesta = await _sat.SolicitarAsync(token, efirma, new PedidoDeDescargaMasiva(esEmitida, desde, hasta));
            var s = new SolicitudDeDescargaMasiva(_guids.Create(), organizacionId, anio, mes, esEmitida, desde, hasta, automatica, ahoraUtc);
            if (respuesta.CodEstatus == Aceptada && !string.IsNullOrWhiteSpace(respuesta.IdSolicitud))
            {
                s.Aceptar(respuesta.IdSolicitud, respuesta.Mensaje);
            }
            else if (respuesta.CodEstatus == SinInformacion)
            {
                s.Terminar(EstadoDeDescargaMasiva.SinFacturas, ahoraUtc, respuesta.Mensaje);
            }
            else
            {
                s.Terminar(EstadoDeDescargaMasiva.Rechazada, ahoraUtc, Explicar(respuesta.CodEstatus, respuesta.Mensaje));
            }

            await _solicitudes.InsertAsync(s, autoSave: true);
            hechas.Add(s);
        }

        return hechas;
    }

    /// <summary>
    /// Pregunta por las solicitudes en proceso (las de un cliente, o de todos las que toca revisar) y baja lo que ya
    /// esté listo. Cada cliente en su propia unidad de trabajo: uno que falle no detiene a los demás.
    /// </summary>
    public async Task<Vuelta> AvanzarAsync(Guid? organizacionId = null, int maximo = 30)
    {
        var ahoraUtc = _reloj.Now.ToUniversalTime();
        var antes = ahoraUtc - Intervalo;
        List<(Guid Id, Guid Org)> pendientes;
        using (var uow = _unidades.Begin(requiresNew: true))
        {
            var q = (await _solicitudes.GetQueryableAsync())
                .Where(s => s.Estado == EstadoDeDescargaMasiva.EnProceso && s.IdSolicitud != null)
                .Where(s => organizacionId == null ? s.RevisadaUtc == null || s.RevisadaUtc < antes : s.OrganizacionId == organizacionId)
                .OrderBy(s => s.RevisadaUtc ?? s.SolicitadaUtc)
                .Take(maximo)
                .Select(s => new { s.Id, s.OrganizacionId });
            pendientes = [.. q.ToList().Select(x => (x.Id, x.OrganizacionId))];
            await uow.CompleteAsync();
        }

        var r = new Vuelta();
        foreach (var grupo in pendientes.GroupBy(p => p.Org))
        {
            try
            {
                await AvanzarClienteAsync(grupo.Key, grupo.Select(p => p.Id).ToList(), r);
            }
            catch (DescargaMasivaNoDisponibleException ex)
            {
                r.Fallaron++;
                r.SatSinResponder = true;
                Logger.LogInformation("La descarga masiva del SAT no respondió: {Mensaje}", ex.Message);
            }
            catch (Exception ex) when (ex is UserFriendlyException or CryptographicException or AbpException or IOException)
            {
                r.Fallaron++;
                Logger.LogWarning("No se pudo avanzar la descarga masiva del cliente {Organizacion}: {Mensaje}", grupo.Key, ex.Message);
            }
        }

        return r;
    }

    private async Task AvanzarClienteAsync(Guid organizacionId, List<Guid> ids, Vuelta r)
    {
        string rfc;
        EfirmaAbierta efirma;
        using (var uow = _unidades.Begin(requiresNew: true))
        {
            efirma = await AbrirAsync(organizacionId);
            rfc = efirma.Rfc;
            await uow.CompleteAsync();
        }

        using (efirma)
        {
            var token = await _sat.AutenticarAsync(efirma);
            var desde = DateTime.UtcNow;
            async Task<string> TokenAsync()
            {
                if (DateTime.UtcNow - desde > VidaDelToken)
                {
                    token = await _sat.AutenticarAsync(efirma);
                    desde = DateTime.UtcNow;
                }

                return token;
            }

            foreach (var id in ids)
            {
                using var uow = _unidades.Begin(requiresNew: true);
                var s = await _solicitudes.GetAsync(id);
                var ahoraUtc = _reloj.Now.ToUniversalTime();
                var avance = await _sat.VerificarAsync(await TokenAsync(), efirma, s.IdSolicitud!);
                r.Revisadas++;
                if (avance.CodEstatus == SinInformacion || avance.CodigoEstadoSolicitud == SinInformacion)
                {
                    s.Terminar(EstadoDeDescargaMasiva.SinFacturas, ahoraUtc, avance.Mensaje);
                    r.Terminadas++;
                }
                else if (avance.CodEstatus != Aceptada)
                {
                    VencerOSeguir(s, ahoraUtc, Explicar(avance.CodEstatus, avance.Mensaje));
                }
                else if (avance.EstadoSolicitud is 1 or 2)
                {
                    VencerOSeguir(s, ahoraUtc, avance.Mensaje);
                }
                else if (avance.EstadoSolicitud == 3)
                {
                    s.PonerPaquetes(avance.Paquetes, avance.NumeroCfdis);
                    await _solicitudes.UpdateAsync(s, autoSave: true);
                    await BajarPaquetesAsync(s, organizacionId, rfc, efirma, TokenAsync, r);
                }
                else
                {
                    s.Terminar(EstadoDeDescargaMasiva.Rechazada, ahoraUtc, avance.EstadoSolicitud switch
                    {
                        5 => "El SAT rechazó la solicitud." + Detalle(avance.Mensaje),
                        6 => "La solicitud venció en el SAT antes de bajarla: pídela otra vez." + Detalle(avance.Mensaje),
                        _ => "El SAT no pudo armar la solicitud." + Detalle(avance.Mensaje),
                    });
                    r.Terminadas++;
                }

                await _solicitudes.UpdateAsync(s);
                await uow.CompleteAsync();
            }
        }
    }

    private async Task BajarPaquetesAsync(SolicitudDeDescargaMasiva s, Guid organizacionId, string rfc, EfirmaAbierta efirma,
        Func<Task<string>> token, Vuelta r)
    {
        var paquetes = s.ListaDePaquetes;
        for (var i = s.PaquetesBajados; i < paquetes.Count; i++)
        {
            var paquete = await _sat.DescargarAsync(await token(), efirma, paquetes[i]);
            if (paquete.Zip is not { Length: > 0 } bytes)
            {
                // Se reintenta en la siguiente vuelta (sigue «en proceso» hasta que venza).
                VencerOSeguir(s, _reloj.Now.ToUniversalTime(), Explicar(paquete.CodEstatus, paquete.Mensaje));
                return;
            }

            ImportadorDeCfdi.Conteo c;
            try
            {
                using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                c = await _importador.AgregarAsync(organizacionId, rfc, zip, _ => true);
            }
            catch (InvalidDataException)
            {
                VencerOSeguir(s, _reloj.Now.ToUniversalTime(), "El SAT dio un paquete dañado: se vuelve a intentar.");
                return;
            }

            s.PaqueteBajado(c.Nuevas, c.Repetidas + c.EnMesCerrado, c.Rechazadas);
            r.Nuevas += c.Nuevas;
            await _solicitudes.UpdateAsync(s, autoSave: true);
        }

        s.Terminar(paquetes.Count == 0 ? EstadoDeDescargaMasiva.SinFacturas : EstadoDeDescargaMasiva.Descargada, _reloj.Now.ToUniversalTime(),
            paquetes.Count == 0 ? "El SAT no encontró facturas en ese rango." : null);
        r.Terminadas++;
    }

    /// <summary>
    /// Al cerrar el mes: pide sola la descarga del mes anterior de cada cliente con e.firma que no mandó nada (lo marcó o
    /// no hay ni una factura de ese mes) y al que todavía no se le pidió.
    /// </summary>
    public async Task<int> PedirCierreDeMesAsync(DateTime hoyEnMexico)
    {
        if (hoyEnMexico.Day < DiaDelCierre || !_sat.EstaConfigurado)
        {
            return 0;
        }

        var mesAnterior = new DateTime(hoyEnMexico.Year, hoyEnMexico.Month, 1).AddMonths(-1);
        var (anio, mes) = (mesAnterior.Year, mesAnterior.Month);
        var desde = mesAnterior;
        var hasta = mesAnterior.AddMonths(1);
        var ahoraUtc = _reloj.Now.ToUniversalTime();
        List<Guid> clientes;
        using (var uow = _unidades.Begin(requiresNew: true))
        {
            var conEfirma = (await _credenciales.GetListAsync(c => c.CerCifrado != null && c.ConsentimientoUtc != null
                                                                   && (c.VigenteHastaUtc == null || c.VigenteHastaUtc > ahoraUtc)))
                .Select(c => c.OrganizacionId).ToList();
            var yaPedidos = (await _solicitudes.GetListAsync(s => s.Anio == anio && s.Mes == mes && conEfirma.Contains(s.OrganizacionId)))
                .Select(s => s.OrganizacionId).ToHashSet();
            var sinEntrega = (await _sinEntrega.GetListAsync(s => s.Anio == anio && s.Mes == mes && conEfirma.Contains(s.OrganizacionId)))
                .Select(s => s.OrganizacionId).ToHashSet();
            var conFacturas = (await _documentos.GetQueryableAsync())
                .Where(d => conEfirma.Contains(d.OrganizacionId) && d.Tipo == TipoDeDocumento.Cfdi
                            && d.FechaDelDocumento >= desde && d.FechaDelDocumento < hasta)
                .Select(d => d.OrganizacionId).Distinct().ToHashSet();
            clientes = conEfirma.Where(o => !yaPedidos.Contains(o) && (sinEntrega.Contains(o) || !conFacturas.Contains(o))).ToList();
            await uow.CompleteAsync();
        }

        var pedidos = 0;
        foreach (var org in clientes)
        {
            try
            {
                using var uow = _unidades.Begin(requiresNew: true);
                if (await ProblemaConLaEfirmaAsync(org) is { } problema)
                {
                    Logger.LogInformation("Descarga masiva del cierre: el cliente {Organizacion} no se pide ({Problema}).", org, problema);
                    await uow.CompleteAsync();
                    continue;
                }

                await PedirAsync(org, anio, mes, emitidas: true, recibidas: true, automatica: true);
                await uow.CompleteAsync();
                pedidos++;
            }
            catch (DescargaMasivaNoDisponibleException ex)
            {
                // El SAT no responde: mañana se intenta otra vez (no quedó nada guardado).
                Logger.LogInformation("La descarga masiva del SAT no respondió al pedir el cierre: {Mensaje}", ex.Message);
                break;
            }
            catch (Exception ex) when (ex is UserFriendlyException or CryptographicException or AbpException)
            {
                Logger.LogWarning("No se pudo pedir la descarga masiva del cierre del cliente {Organizacion}: {Mensaje}", org, ex.Message);
            }
        }

        return pedidos;
    }

    /// <summary>Descifra la e.firma del cliente (en memoria) y anota el uso en la bitácora.</summary>
    private async Task<EfirmaAbierta> AbrirAsync(Guid organizacionId)
    {
        var c = await _credenciales.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId);
        var perfil = await _calculadora.PerfilAsync(organizacionId);
        if (Problema(c, perfil.Rfc, _reloj.Now.ToUniversalTime()) is { } problema)
        {
            throw new UserFriendlyException(problema);
        }

        var cer = _cifrador.Descifrar(c!.Id, CredencialesDelCliente.CampoCer, c.CerCifrado);
        var key = _cifrador.Descifrar(c.Id, CredencialesDelCliente.CampoKey, c.KeyCifrado);
        var contrasena = _cifrador.DescifrarTexto(c.Id, CredencialesDelCliente.CampoContrasenaDeEfirma, c.ContrasenaDeEfirmaCifrada);
        var certificado = X509CertificateLoader.LoadCertificate(cer);
        var llave = RSA.Create();
        try
        {
            llave.ImportEncryptedPkcs8PrivateKey(contrasena.AsSpan(), key, out _);
        }
        catch
        {
            llave.Dispose();
            certificado.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        await _usos.InsertAsync(new UsoDeCredencial(_guids.Create(), organizacionId, TipoDeCredencial.Efirma, AccionSobreCredencial.UsarEnDescargaMasiva),
            autoSave: true);
        return new EfirmaAbierta(certificado, llave, perfil.Rfc!);
    }

    private static void VencerOSeguir(SolicitudDeDescargaMasiva s, DateTime ahoraUtc, string? mensaje)
    {
        if (ahoraUtc - s.SolicitadaUtc > SolicitudDeDescargaMasiva.Vigencia)
        {
            s.Terminar(EstadoDeDescargaMasiva.Rechazada, ahoraUtc, "Pasaron 72 horas y el SAT no la entregó: pídela otra vez." + Detalle(mensaje));
        }
        else
        {
            s.Revisada(ahoraUtc, mensaje);
        }
    }

    /// <summary>Los códigos del SAT que más salen, en palabras.</summary>
    public static string Explicar(string? codigo, string? mensaje) => codigo switch
    {
        "5002" => "El SAT ya no acepta más veces esta misma solicitud (límite de por vida)." + Detalle(mensaje),
        "5003" => "Tope máximo de facturas por solicitud: pide un rango más corto." + Detalle(mensaje),
        "5005" => "Ya hay una solicitud igual en el SAT." + Detalle(mensaje),
        "5011" => "Límite de descargas del día por folio." + Detalle(mensaje),
        "300" => "El SAT no reconoce al usuario de la e.firma." + Detalle(mensaje),
        "301" => "El SAT no entendió la solicitud." + Detalle(mensaje),
        "302" => "El SAT dice que la firma no es válida." + Detalle(mensaje),
        "303" => "El SAT dice que la e.firma no es del RFC solicitante." + Detalle(mensaje),
        "304" => "El SAT dice que el certificado de la e.firma está revocado o caducó." + Detalle(mensaje),
        "305" => "El SAT dice que el certificado no es válido." + Detalle(mensaje),
        _ => $"El SAT respondió {codigo}." + Detalle(mensaje),
    };

    private static string Detalle(string? mensaje) => string.IsNullOrWhiteSpace(mensaje) ? "" : $" ({mensaje.Trim()})";
}
