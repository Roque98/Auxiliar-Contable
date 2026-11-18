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

    /// <summary>Contra un .zip armado a mano: lo que se descomprime y cuántos archivos.</summary>
    private const long MaxBytesDescomprimidos = 400L * 1024 * 1024;

    private const int MaxArchivosEnElPaquete = 30000;
    private const int MaxBytesPorXml = 5 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonDeLaOrden = new() { WriteIndented = true };

    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<MesSinEntrega, Guid> _sinEntrega;
    private readonly GestorDeDocumentos _gestor;
    private readonly AlmacenDeDocumentos _almacen;
    private readonly IPerfilFiscalAppService _perfiles;
    private readonly IMotorFiscal _motor;
    private readonly GestorDePeriodos _periodos;
    private readonly Autorizador _autorizador;

    public DescargaDelSatAppService(
        IRepository<Documento, Guid> documentos,
        IRepository<MesSinEntrega, Guid> sinEntrega,
        GestorDeDocumentos gestor,
        AlmacenDeDocumentos almacen,
        IPerfilFiscalAppService perfiles,
        IMotorFiscal motor,
        GestorDePeriodos periodos,
        Autorizador autorizador)
    {
        _documentos = documentos;
        _sinEntrega = sinEntrega;
        _gestor = gestor;
        _almacen = almacen;
        _perfiles = perfiles;
        _motor = motor;
        _periodos = periodos;
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

        if (!emitidas && !recibidas)
        {
            throw new UserFriendlyException("Elige emitidas, recibidas o las dos.");
        }

        var (rfc, _, _) = await PerfilAsync(organizacionId);
        if (rfc == null)
        {
            throw new UserFriendlyException("Falta el RFC del cliente en su perfil fiscal.");
        }

        var facturas = await FacturasAsync(organizacionId, desde, hasta);
        var tengo = facturas.Where(d => d.Uuid != null).Select(d => d.Uuid!).Distinct().Order(StringComparer.Ordinal).ToList();
        var danadas = facturas.Count(d => d.XmlDanado);
        var desdeTexto = $"{desdeAnio:0000}-{desdeMes:00}";
        var hastaTexto = $"{hastaAnio:0000}-{hastaMes:00}";
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
            ["version"] = 1,
            ["rfc"] = rfc,
            ["desde"] = desdeTexto,
            ["hasta"] = hastaTexto,
            ["tipos"] = tipos,
            ["tengo"] = tengo,
            ["danados"] = danadas,
            ["generada"] = Clock.Now.ToString("s", CultureInfo.InvariantCulture),
        };

        return new OrdenDeDescargaDto
        {
            NombreDeArchivo = $"orden SAT {rfc} {desdeTexto} a {hastaTexto}.json",
            Contenido = JsonSerializer.Serialize(orden, JsonDeLaOrden),
            Tengo = tengo.Count,
            Danadas = danadas,
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
            if (zip.Entries.Count > MaxArchivosEnElPaquete)
            {
                throw new UserFriendlyException("El paquete trae demasiados archivos.");
            }

            var resumen = LeerResumen(zip);
            if (!string.Equals(resumen.Rfc, rfc, StringComparison.OrdinalIgnoreCase))
            {
                // No se dice qué RFC trae: solo que no es el de este cliente.
                throw new UserFriendlyException("Ese paquete es de otro RFC, no del de este cliente: súbelo en el cliente correcto.");
            }

            var r = new ResultadoDelPaqueteDto { Desde = resumen.Desde, Hasta = resumen.Hasta, SinXmlEnElSat = resumen.NoDisponibles };
            if (resumen.Danados > 0)
            {
                r.Avisos.Add($"{resumen.Danados} XML llegaron dañados del portal: vuelve a correr la orden más tarde.");
            }

            var danadasDelCliente = await _documentos.GetListAsync(d => d.OrganizacionId == organizacionId && d.Tipo == TipoDeDocumento.Cfdi && d.XmlDanado);
            long descomprimidos = 0;
            foreach (var entrada in zip.Entries.Where(e => e.FullName.StartsWith("xml/", StringComparison.Ordinal)
                                                          && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            {
                if (entrada.Length > MaxBytesPorXml)
                {
                    r.Rechazadas++;
                    continue;
                }

                descomprimidos += entrada.Length;
                if (descomprimidos > MaxBytesDescomprimidos)
                {
                    throw new UserFriendlyException("El paquete descomprimido es demasiado grande.");
                }

                var xml = await LeerAsync(entrada);
                if (xml == null || xml.Length == 0)
                {
                    r.Rechazadas++;
                    continue;
                }

                var datos = DatosDelCfdi.Leer(xml);
                var uuid = datos?.Uuid;
                var esDelCliente = datos != null && (string.Equals(datos.EmisorRfc, rfc, StringComparison.OrdinalIgnoreCase)
                                                     || string.Equals(datos.ReceptorRfc, rfc, StringComparison.OrdinalIgnoreCase));
                if (uuid is not { Length: DocumentoConsts.UuidLength } || !esDelCliente || FechaDelCfdi.Leer(xml) is not { } dia)
                {
                    r.Rechazadas++;
                    continue;
                }

                if (await _documentos.AnyAsync(d => d.OrganizacionId == organizacionId && d.Uuid == uuid))
                {
                    r.Repetidas++;
                    continue;
                }

                var fecha = FechaDelCfdi.ComoFecha(dia);
                if (await _periodos.EstaCerradoAsync(organizacionId, fecha.Year, fecha.Month))
                {
                    r.EnMesCerrado++;
                    continue;
                }

                var (documento, duplicado) = await _gestor.RecibirAsync(organizacionId, TipoDeDocumento.Cfdi, uuid + ".xml", xml,
                    mime => mime == DetectorDeDocumento.Xml, fecha);
                if (duplicado)
                {
                    r.Repetidas++;
                    continue;
                }

                r.Nuevas++;
                // El dañado que el cliente subió de esta misma factura (su nombre trae el UUID): ya sobra.
                foreach (var danada in danadasDelCliente.Where(d => d.NombreOriginal.Contains(uuid, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    await _documentos.DeleteAsync(danada, autoSave: true);
                    danadasDelCliente.Remove(danada);
                    r.Reemplazadas++;
                }
            }

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

            if (danadasDelCliente.Count > 0)
            {
                r.Avisos.Add($"Quedan {danadasDelCliente.Count} XML dañados que no se pudieron cambiar (su nombre no dice su UUID): revísalos y bórralos si ya llegó el bueno.");
            }

            return r;
        }
    }

    public async Task<ImpuestosDelMesDto> CalcularImpuestosAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        Validar(anio, mes);
        var (rfc, nombre, regimenes) = await PerfilAsync(organizacionId);
        if (rfc == null)
        {
            throw new UserFriendlyException("Falta el RFC del cliente en su perfil fiscal.");
        }

        // El ejercicio hasta el mes y el anterior: un complemento de este año puede pagar una factura PPD del pasado.
        var facturas = await FacturasAsync(organizacionId, new DateTime(anio - 1, 1, 1), new DateTime(anio, mes, 1).AddMonths(1));
        var buenas = facturas.Where(d => !d.XmlDanado).ToList();
        var delEjercicio = buenas.Where(d => (d.FechaDelDocumento ?? d.CreationTime).Year == anio).ToList();
        if (delEjercicio.Count == 0)
        {
            throw new UserFriendlyException($"No hay facturas de {anio}: sube las del cliente o bájalas del SAT con el Descargador 1.");
        }

        var archivos = new List<ArchivoParaElMotor>();
        foreach (var d in buenas)
        {
            archivos.Add(new ArchivoParaElMotor($"{d.Uuid ?? d.Id.ToString()}.xml", await _almacen.LeerAsync(d)));
        }

        var canceladas = buenas.Where(d => d.Cancelada && d.Uuid != null).Select(d => d.Uuid!).ToList();
        PagosProvisionales resultado;
        try
        {
            resultado = await _motor.PagosProvisionalesAsync(rfc, regimenes, $"{anio:0000}-{mes:00}", nombre, archivos, canceladas);
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor no pudo calcular los pagos provisionales");
            throw new UserFriendlyException("El motor fiscal no está disponible en este momento. Intenta de nuevo en unos minutos.");
        }

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

        return new ImpuestosDelMesDto
        {
            Anio = anio,
            Mes = mes,
            SinEntregaDelCliente = await _sinEntrega.AnyAsync(s => s.OrganizacionId == organizacionId && s.Anio == anio && s.Mes == mes),
            FacturasEnviadas = archivos.Count,
            Canceladas = canceladas.Count,
            Danadas = danadas,
            SinEstatus = sinEstatus,
            Resultado = resultado,
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
                Cuantos(raiz, "no_disponibles"));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new UserFriendlyException("El resumen del paquete está dañado: vuelve a correr la orden en el Descargador 1.");
        }
    }

    /// <summary>Con tope: el tamaño que declara el .zip puede mentir. Null si se pasa.</summary>
    private static async Task<byte[]?> LeerAsync(ZipArchiveEntry entrada)
    {
        await using var flujo = entrada.Open();
        using var memoria = new MemoryStream();
        var bufer = new byte[81920];
        int leidos;
        while ((leidos = await flujo.ReadAsync(bufer)) > 0)
        {
            if (memoria.Length + leidos > MaxBytesPorXml)
            {
                return null;
            }

            memoria.Write(bufer, 0, leidos);
        }

        return memoria.ToArray();
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

    private sealed record ResumenDelPaquete(string Rfc, string Desde, string Hasta, int Danados, int NoDisponibles);
}
