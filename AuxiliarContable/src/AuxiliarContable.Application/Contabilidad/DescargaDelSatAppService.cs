using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Credenciales;
using AuxiliarContable.Documentos;
using AuxiliarContable.Files;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Contabilidad;

/// <summary>Ver <see cref="IDescargaDelSatAppService"/>.</summary>
[Authorize]
public partial class DescargaDelSatAppService : AuxiliarContableAppService, IDescargaDelSatAppService
{
    public const string TipoDeOrden = "orden-descargador-1";
    public const string TipoDePaquete = "paquete-descargador-1";

    /// <summary>Meses que abarca una orden: el portal busca mes por mes y la sesión del SAT caduca.</summary>
    public const int MaxMesesPorOrden = 24;

    public const int MaxDiasPorOrden = 366;
    public const int MaxFoliosPorOrden = 500;

    public const string EnProcesoDeCancelacion = "En proceso de cancelación";

    private static readonly JsonSerializerOptions JsonDeLaOrden = new() { WriteIndented = true };

    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<MesSinEntrega, Guid> _sinEntrega;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly IPerfilFiscalAppService _perfiles;
    private readonly IMotorFiscal _motor;
    private readonly Autorizador _autorizador;
    private readonly CalculadoraDeImpuestos _calculadora;
    private readonly RevisorDeEstatusSat _revisor;
    private readonly ImportadorDeCfdi _importador;
    private readonly DescargadorMasivo _masiva;
    private readonly IRepository<SolicitudDeDescargaMasiva, Guid> _solicitudesMasivas;
    private readonly IRepository<CredencialesDelCliente, Guid> _credenciales;

    public DescargaDelSatAppService(
        IRepository<Documento, Guid> documentos,
        IRepository<MesSinEntrega, Guid> sinEntrega,
        AlmacenDeDocumentos almacen,
        IPerfilFiscalAppService perfiles,
        IMotorFiscal motor,
        Autorizador autorizador,
        CalculadoraDeImpuestos calculadora,
        RevisorDeEstatusSat revisor,
        ImportadorDeCfdi importador,
        DescargadorMasivo masiva,
        IRepository<SolicitudDeDescargaMasiva, Guid> solicitudesMasivas,
        IRepository<CredencialesDelCliente, Guid> credenciales)
    {
        _solicitudesMasivas = solicitudesMasivas;
        _credenciales = credenciales;
        _importador = importador;
        _masiva = masiva;
        _calculadora = calculadora;
        _revisor = revisor;
        _documentos = documentos;
        _sinEntrega = sinEntrega;
        _almacen = almacen;
        _perfiles = perfiles;
        _motor = motor;
        _autorizador = autorizador;
    }

    public async Task<DescargaDelSatDto> GetAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        Validar(anio, mes);
        var desde = new DateTime(anio, 1, 1);
        var hasta = new DateTime(anio, mes, 1).AddMonths(1);
        var facturas = await FacturasAsync(organizacionId, desde, hasta);
        var sinEntrega = (await _sinEntrega.GetListAsync(s => s.OrganizacionId == organizacionId && s.Anio == anio && s.Mes <= mes))
            .Select(s => s.Mes).ToHashSet();
        var (rfc, _, _) = await PerfilAsync(organizacionId);

        return new DescargaDelSatDto
        {
            Anio = anio,
            Mes = mes,
            SinEntregaDelCliente = sinEntrega.Contains(mes),
            TieneRfc = rfc != null,
            Meses = Enumerable.Range(1, mes).Select(m =>
            {
                var delMes = facturas.Where(d => MesDe(d) == m).ToList();
                return new FacturasDelSatDelMesDto
                {
                    Anio = anio,
                    Mes = m,
                    Facturas = delMes.Count(d => !d.XmlDanado),
                    Canceladas = delMes.Count(d => d.Cancelada),
                    EnProceso = delMes.Count(d => d.EstatusSat == EnProcesoDeCancelacion),
                    NoEncontradas = delMes.Count(d => d.EstatusSat == EstatusDelSat.NoEncontrado),
                    SinEstatus = delMes.Count(d => d.Uuid != null && d.EstatusSat == null),
                    Danadas = delMes.Count(d => d.XmlDanado),
                    SinEntregaDelCliente = sinEntrega.Contains(m),
                };
            }).ToList(),
            Danadas = facturas.Where(d => d.XmlDanado).OrderBy(d => d.FechaDelDocumento ?? d.CreationTime).Take(50)
                .Select(d => new FacturaDanadaDto { DocumentoId = d.Id, Nombre = d.NombreOriginal, Fecha = d.FechaDelDocumento })
                .ToList(),
            UltimoEstatusUtc = facturas.Max(d => d.EstatusSatUtc),
        };
    }

    public async Task MarcarSinEntregaAsync(Guid organizacionId, int anio, int mes, bool sinEntrega)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        Validar(anio, mes);
        var marca = await _sinEntrega.FirstOrDefaultAsync(s => s.OrganizacionId == organizacionId && s.Anio == anio && s.Mes == mes);
        if (sinEntrega && marca == null)
        {
            await _sinEntrega.InsertAsync(new MesSinEntrega(GuidGenerator.Create(), organizacionId, anio, mes), autoSave: true);
        }
        else if (!sinEntrega && marca != null)
        {
            await _sinEntrega.DeleteAsync(marca, autoSave: true);
        }
    }

    public async Task<OrdenDeDescargaDto> GetOrdenAsync(
        Guid organizacionId, int desdeAnio, int desdeMes, int hastaAnio, int hastaMes, bool emitidas, bool recibidas)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        Validar(desdeAnio, desdeMes);
        Validar(hastaAnio, hastaMes);
        var desde = new DateTime(desdeAnio, desdeMes, 1);
        var hasta = new DateTime(hastaAnio, hastaMes, 1).AddMonths(1);
        var meses = (hastaAnio - desdeAnio) * 12 + hastaMes - desdeMes + 1;
        if (meses < 1)
        {
            throw new UserFriendlyException("El mes final es antes del inicial.");
        }

        if (meses > MaxMesesPorOrden)
        {
            throw new UserFriendlyException($"Una orden abarca hasta {MaxMesesPorOrden} meses: pártela en dos.");
        }

        var desdeTexto = $"{desdeAnio:0000}-{desdeMes:00}";
        var hastaTexto = $"{hastaAnio:0000}-{hastaMes:00}";
        return await OrdenAsync(organizacionId, "meses", desdeTexto, hastaTexto, desde, hasta, null, emitidas, recibidas,
            desdeTexto == hastaTexto ? desdeTexto : $"{desdeTexto} a {hastaTexto}");
    }

    public async Task<OrdenDeDescargaDto> GetOrdenPorDiasAsync(Guid organizacionId, DateTime desde, DateTime hasta, bool emitidas, bool recibidas)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        desde = desde.Date;
        hasta = hasta.Date;
        if (hasta < desde)
        {
            throw new UserFriendlyException("El día final es antes del inicial.");
        }

        if ((hasta - desde).TotalDays > MaxDiasPorOrden)
        {
            throw new UserFriendlyException($"Una orden por días abarca hasta {MaxDiasPorOrden} días: para más, pídela por meses.");
        }

        var desdeTexto = desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var hastaTexto = hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return await OrdenAsync(organizacionId, "dias", desdeTexto, hastaTexto, desde, hasta.AddDays(1), null, emitidas, recibidas,
            $"del {desdeTexto} al {hastaTexto}");
    }

    public async Task<OrdenDeDescargaDto> CrearOrdenPorFoliosAsync(Guid organizacionId, OrdenPorFoliosDto input)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        var folios = Folios(input.Folios);
        if (folios.Count == 0)
        {
            throw new UserFriendlyException("Escribe o pega al menos un folio fiscal (UUID).");
        }

        if (folios.Count > MaxFoliosPorOrden)
        {
            throw new UserFriendlyException($"Una orden por folio lleva hasta {MaxFoliosPorOrden} UUID: pártela en varias.");
        }

        return await OrdenAsync(organizacionId, "folios", null, null, null, null, folios, input.Emitidas, input.Recibidas,
            $"{folios.Count} folios");
    }

    /// <summary>Los UUID de un texto pegado (separados por lo que sea), sin repetir, en mayúsculas.</summary>
    public static List<string> Folios(IEnumerable<string>? textos) =>
        (textos ?? []).SelectMany(t => UuidEnTexto().Matches(t ?? "").Select(m => m.Value.ToUpperInvariant())).Distinct().ToList();

    private async Task<OrdenDeDescargaDto> OrdenAsync(Guid organizacionId, string modo, string? desdeTexto, string? hastaTexto,
        DateTime? desde, DateTime? hasta, List<string>? folios, bool emitidas, bool recibidas, string descripcion)
    {
        if (!emitidas && !recibidas)
        {
            throw new UserFriendlyException("Elige emitidas, recibidas o las dos.");
        }

        var (rfc, _, _) = await PerfilAsync(organizacionId);
        if (rfc == null)
        {
            throw new UserFriendlyException("Falta el RFC del cliente en su perfil fiscal.");
        }

        List<Documento> facturas;
        if (folios != null)
        {
            facturas = await _documentos.GetListAsync(d => d.OrganizacionId == organizacionId && d.Tipo == TipoDeDocumento.Cfdi
                                                           && d.Uuid != null && folios.Contains(d.Uuid));
        }
        else
        {
            facturas = await FacturasAsync(organizacionId, desde!.Value, hasta!.Value);
        }

        var tengo = facturas.Where(d => d.Uuid != null).Select(d => d.Uuid!).Distinct().Order(StringComparer.Ordinal).ToList();
        var danadas = facturas.Count(d => d.XmlDanado);
        var tipos = new List<string>();
        if (recibidas)
        {
            tipos.Add("recibidas");
        }

        if (emitidas)
        {
            tipos.Add("emitidas");
        }

        var orden = new Dictionary<string, object>
        {
            ["tipo"] = TipoDeOrden,
            ["version"] = 2,
            ["modo"] = modo,
            ["rfc"] = rfc,
            ["tipos"] = tipos,
            ["tengo"] = tengo,
            ["danados"] = danadas,
            ["generada"] = Clock.Now.ToString("s", CultureInfo.InvariantCulture),
        };
        if (desdeTexto != null)
        {
            orden["desde"] = desdeTexto;
            orden["hasta"] = hastaTexto!;
        }

        if (folios != null)
        {
            orden["folios"] = folios;
        }

        return new OrdenDeDescargaDto
        {
            NombreDeArchivo = $"orden SAT {rfc} {descripcion}.json",
            Contenido = JsonSerializer.Serialize(orden, JsonDeLaOrden),
            Tengo = tengo.Count,
            Danadas = danadas,
        };
    }

    public async Task<RevisionDeEstatusDto> RevisarEstatusAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        Validar(anio, mes);
        var r = await _revisor.RevisarMesAsync(organizacionId, anio, mes);
        return new RevisionDeEstatusDto
        {
            Revisadas = r.Revisadas,
            Cambiaron = r.Cambiaron,
            Canceladas = r.Canceladas,
            EnProceso = r.EnProceso,
            NoEncontradas = r.NoEncontradas,
            Fallaron = r.Fallaron,
            Faltan = r.Faltan,
            SatSinResponder = r.SatSinResponder,
        };
    }

    public async Task<ResultadoDelPaqueteDto> SubirPaqueteAsync(Guid organizacionId, IRemoteStreamContent paquete)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        if (paquete == null)
        {
            throw new UserFriendlyException("No llegó ningún archivo.");
        }

        const string demasiado = "El paquete pesa demasiado (máximo 20 MB): pide al Descargador 1 menos meses por orden.";
        var bytes = await LectorConTope.LeerAsync(paquete.GetStream(), DocumentoConsts.MaxBytes, demasiado);
        var (rfc, _, _) = await PerfilAsync(organizacionId);
        if (rfc == null)
        {
            throw new UserFriendlyException("Falta el RFC del cliente en su perfil fiscal.");
        }

        ZipArchive zip;
        try
        {
            zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw new UserFriendlyException("Ese archivo no es el paquete .zip que deja el Descargador 1.");
        }

        using (zip)
        {
            var resumen = LeerResumen(zip);
            if (!string.Equals(resumen.Rfc, rfc, StringComparison.OrdinalIgnoreCase))
            {
                // No se dice qué RFC trae: solo que no es el de este cliente.
                throw new UserFriendlyException("Ese paquete es de otro RFC, no del de este cliente: súbelo en el cliente correcto.");
            }

            var r = new ResultadoDelPaqueteDto { Desde = resumen.Desde, Hasta = resumen.Hasta, SinXmlEnElSat = resumen.NoDisponibles };
            if (resumen.NoEncontrados > 0)
            {
                r.Avisos.Add($"{resumen.NoEncontrados} folios no aparecieron en el portal con el RFC del cliente: revisa que sean suyos.");
            }
            if (resumen.Danados > 0)
            {
                r.Avisos.Add($"{resumen.Danados} XML llegaron dañados del portal: vuelve a correr la orden más tarde.");
            }

            var c = await _importador.AgregarAsync(organizacionId, rfc, zip, e => e.FullName.StartsWith("xml/", StringComparison.Ordinal));
            r.Nuevas = c.Nuevas;
            r.Repetidas = c.Repetidas;
            r.Rechazadas = c.Rechazadas;
            r.EnMesCerrado = c.EnMesCerrado;
            r.Reemplazadas = c.Reemplazadas;
            await PonerEstatusAsync(organizacionId, zip, r);
            if (r.EnMesCerrado > 0)
            {
                r.Avisos.Add($"{r.EnMesCerrado} facturas son de meses ya cerrados: no se agregaron (reabre el mes si hace falta).");
            }

            if (r.Rechazadas > 0)
            {
                r.Avisos.Add($"{r.Rechazadas} archivos no se agregaron: no abren como CFDI o no son del RFC del cliente.");
            }

            if (r.SinXmlEnElSat > 0)
            {
                r.Avisos.Add($"{r.SinXmlEnElSat} facturas están en el SAT pero el portal no dio su XML (casi siempre canceladas).");
            }

            if (c.DanadasQueQuedan > 0)
            {
                r.Avisos.Add($"Quedan {c.DanadasQueQuedan} XML dañados que no se pudieron cambiar (su nombre no dice su UUID): revísalos y bórralos si ya llegó el bueno.");
            }

            return r;
        }
    }

    public async Task<DiotDto> GetDiotAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        Validar(anio, mes);
        try
        {
            return new DiotDto { Anio = anio, Mes = mes, Resultado = await _calculadora.DiotAsync(organizacionId, anio, mes) };
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor no pudo armar la DIOT");
            throw new UserFriendlyException("El motor fiscal no está disponible en este momento. Intenta de nuevo en unos minutos.");
        }
    }

    public async Task<ImpuestosDelMesDto> CalcularImpuestosAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        Validar(anio, mes);
        var fila = await _calculadora.FilaAsync(organizacionId, anio, mes, Clock.Now.ToUniversalTime());
        var pedido = fila.PedidoUtc;
        CalculadoraDeImpuestos.Calculo c;
        try
        {
            c = await _calculadora.CalcularAsync(organizacionId, anio, mes, conExcel: true);
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor no pudo calcular los pagos provisionales");
            throw new UserFriendlyException("El motor fiscal no está disponible en este momento. Intenta de nuevo en unos minutos.");
        }

        // Lo que se calcula a mano también deja al día el cálculo guardado.
        await _calculadora.GuardarAsync(fila, c, pedido);
        return new ImpuestosDelMesDto
        {
            Anio = anio,
            Mes = mes,
            SinEntregaDelCliente = await _sinEntrega.AnyAsync(s => s.OrganizacionId == organizacionId && s.Anio == anio && s.Mes == mes),
            FacturasEnviadas = c.Facturas,
            Canceladas = c.Canceladas,
            Danadas = c.Danadas,
            SinEstatus = c.SinEstatus,
            Resultado = c.Resultado,
        };
    }

    public async Task<DescargaMasivaDto> GetDescargaMasivaAsync(Guid organizacionId)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        return await DescargaMasivaAsync(organizacionId, null);
    }

    public async Task<DescargaMasivaDto> SolicitarDescargaMasivaAsync(Guid organizacionId, SolicitarDescargaMasivaDto input)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        Validar(input.Anio, input.Mes);
        if (!_masiva.EstaConfigurado)
        {
            throw new UserFriendlyException("La descarga masiva del SAT no está encendida en este servidor.");
        }

        try
        {
            await _masiva.PedirAsync(organizacionId, input.Anio, input.Mes, input.Emitidas, input.Recibidas, automatica: false);
        }
        catch (DescargaMasivaNoDisponibleException ex)
        {
            Logger.LogInformation("La descarga masiva del SAT no respondió: {Mensaje}", ex.Message);
            throw new UserFriendlyException("El SAT no responde en este momento. Intenta de nuevo en unos minutos (o usa el Descargador 1).");
        }

        return await DescargaMasivaAsync(organizacionId, null);
    }

    public async Task<DescargaMasivaDto> AvanzarDescargaMasivaAsync(Guid organizacionId)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        var v = await _masiva.AvanzarAsync(organizacionId);
        var aviso = v.SatSinResponder ? "El SAT no respondió: se vuelve a intentar solo en unos minutos."
            : v.Fallaron > 0 ? "No se pudo revisar: revisa la e.firma del cliente."
            : v.Revisadas == 0 ? "No hay descargas en proceso."
            : v.Nuevas > 0 ? $"Llegaron {v.Nuevas} facturas nuevas."
            : null;
        return await DescargaMasivaAsync(organizacionId, aviso);
    }

    private async Task<DescargaMasivaDto> DescargaMasivaAsync(Guid organizacionId, string? aviso)
    {
        var lista = (await _solicitudesMasivas.GetQueryableAsync())
            .Where(s => s.OrganizacionId == organizacionId)
            .OrderByDescending(s => s.SolicitadaUtc)
            .Take(20)
            .ToList();
        return new DescargaMasivaDto
        {
            Disponible = _masiva.EstaConfigurado,
            Problema = await _masiva.ProblemaConLaEfirmaAsync(organizacionId),
            EfirmaVigenteHastaUtc = (await _credenciales.FirstOrDefaultAsync(c => c.OrganizacionId == organizacionId))?.VigenteHastaUtc,
            Aviso = aviso,
            Solicitudes = lista.Select(s => new SolicitudDeDescargaMasivaDto
            {
                Id = s.Id,
                Anio = s.Anio,
                Mes = s.Mes,
                Emitidas = s.Emitidas,
                Desde = s.Desde,
                Hasta = s.Hasta,
                Automatica = s.Automatica,
                Estado = s.Estado.ToString(),
                Mensaje = s.Mensaje,
                NumeroCfdis = s.NumeroCfdis,
                Paquetes = s.ListaDePaquetes.Count,
                PaquetesBajados = s.PaquetesBajados,
                Nuevas = s.Nuevas,
                Repetidas = s.Repetidas,
                Rechazadas = s.Rechazadas,
                SolicitadaUtc = s.SolicitadaUtc,
                RevisadaUtc = s.RevisadaUtc,
                TerminadaUtc = s.TerminadaUtc,
            }).ToList(),
        };
    }

    private async Task PonerEstatusAsync(Guid organizacionId, ZipArchive zip, ResultadoDelPaqueteDto r)
    {
        var entrada = zip.GetEntry("estatus.csv");
        if (entrada == null)
        {
            r.Avisos.Add("El paquete no trae el estatus del SAT (estatus.csv).");
            return;
        }

        var estatus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var lector = new StreamReader(entrada.Open(), Encoding.UTF8))
        {
            var encabezado = (await lector.ReadLineAsync())?.Split(',') ?? [];
            var iUuid = Array.IndexOf(encabezado, "uuid");
            var iEstado = Array.IndexOf(encabezado, "estado");
            if (iUuid < 0 || iEstado < 0)
            {
                r.Avisos.Add("El estatus del SAT del paquete no se pudo leer.");
                return;
            }

            while (await lector.ReadLineAsync() is { } linea)
            {
                // Las columnas que se usan (uuid, consulta, estado) van primero y nunca llevan comas.
                var celdas = linea.Split(',');
                if (celdas.Length > Math.Max(iUuid, iEstado) && UuidValido().IsMatch(celdas[iUuid].Trim())
                    && !string.IsNullOrWhiteSpace(celdas[iEstado]))
                {
                    estatus[celdas[iUuid].Trim().ToUpperInvariant()] = celdas[iEstado].Trim();
                }
            }
        }

        if (estatus.Count == 0)
        {
            return;
        }

        var uuids = estatus.Keys.ToList();
        var ahora = Clock.Now.ToUniversalTime();
        foreach (var d in await _documentos.GetListAsync(d => d.OrganizacionId == organizacionId && d.Uuid != null && uuids.Contains(d.Uuid)))
        {
            d.PonerEstatusSat(estatus[d.Uuid!], ahora);
            await _documentos.UpdateAsync(d);
            r.EstatusPuestos++;
            r.Canceladas += d.Cancelada ? 1 : 0;
            r.EnProceso += d.EstatusSat == EnProcesoDeCancelacion ? 1 : 0;
        }
    }

    private static ResumenDelPaquete LeerResumen(ZipArchive zip)
    {
        var entrada = zip.GetEntry("resumen.json")
                      ?? throw new UserFriendlyException("Ese archivo no es el paquete .zip que deja el Descargador 1.");
        try
        {
            using var flujo = entrada.Open();
            using var json = JsonDocument.Parse(flujo);
            var raiz = json.RootElement;
            if (raiz.GetProperty("tipo").GetString() != TipoDePaquete)
            {
                throw new UserFriendlyException("Ese archivo no es el paquete .zip que deja el Descargador 1.");
            }

            static int Cuantos(JsonElement e, string nombre) =>
                e.TryGetProperty(nombre, out var v) ? v.ValueKind == JsonValueKind.Array ? v.GetArrayLength() : v.TryGetInt32(out var n) ? n : 0 : 0;

            return new ResumenDelPaquete(
                raiz.GetProperty("rfc").GetString() ?? "",
                raiz.TryGetProperty("desde", out var desde) ? desde.GetString() ?? "" : "",
                raiz.TryGetProperty("hasta", out var hasta) ? hasta.GetString() ?? "" : "",
                Cuantos(raiz, "danados"),
                Cuantos(raiz, "no_disponibles"),
                Cuantos(raiz, "no_encontrados"));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new UserFriendlyException("El resumen del paquete está dañado: vuelve a correr la orden en el Descargador 1.");
        }
    }

    /// <summary>Facturas (XML) del cliente de [desde, hasta): por la fecha del CFDI; las dañadas sin fecha, por cuándo se subieron.</summary>
    private async Task<List<Documento>> FacturasAsync(Guid organizacionId, DateTime desde, DateTime hasta) =>
        await _documentos.GetListAsync(d => d.OrganizacionId == organizacionId && d.Tipo == TipoDeDocumento.Cfdi
            && ((d.FechaDelDocumento != null && d.FechaDelDocumento >= desde && d.FechaDelDocumento < hasta)
                || (d.FechaDelDocumento == null && d.CreationTime >= desde && d.CreationTime < hasta)));

    private static int MesDe(Documento d) => (d.FechaDelDocumento ?? d.CreationTime).Month;

    private async Task<(string? Rfc, string? Nombre, List<string> Regimenes)> PerfilAsync(Guid organizacionId)
    {
        var perfil = await _perfiles.GetAsync(organizacionId);
        string? Campo(string clave) => perfil.Campos.FirstOrDefault(c => c.Clave == clave)?.Valor?.Trim();
        var rfc = Campo(CamposDelPerfil.Rfc)?.ToUpperInvariant();
        return (string.IsNullOrWhiteSpace(rfc) ? null : rfc, Campo(CamposDelPerfil.Nombre), Lista(Campo(CamposDelPerfil.Regimenes)));
    }

    /// <summary>Los campos de lista del perfil se guardan como arreglo JSON de textos.</summary>
    private static List<string> Lista(string? valor)
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

    private async Task ExigirEquipoAsync(Guid organizacionId, AccionEnOrganizacion accion)
    {
        await _autorizador.ExigirAsync(organizacionId, accion);
        if (!(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new AbpAuthorizationException("La descarga del SAT es del despacho.");
        }
    }

    private static void Validar(int anio, int mes)
    {
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }
    }

    [GeneratedRegex("^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$")]
    private static partial Regex UuidValido();

    [GeneratedRegex("[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}")]
    private static partial Regex UuidEnTexto();

    private sealed record ResumenDelPaquete(string Rfc, string Desde, string Hasta, int Danados, int NoDisponibles, int NoEncontrados);
}
