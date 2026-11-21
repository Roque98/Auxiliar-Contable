using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace AuxiliarContable.Concentrado;

/// <summary>
/// Las pestañas contables de la hoja concentrado: saldos por cuenta del código agrupador al cierre del mes, desde la
/// balanza de apertura más las pólizas de cada mes (las mismas que van a la contabilidad electrónica), y de cada
/// cuenta hasta sus pólizas y el archivo de donde salieron.
/// </summary>
public class HojaContable : ITransientDependency
{
    /// <summary>Pasivos que son impuestos o contribuciones: van en «Deudas fiscales», no en las contables.</summary>
    public static readonly IReadOnlySet<string> PasivosFiscales = new HashSet<string> { "207", "208", "209", "211", "212", "213", "216" };

    /// <summary>Ingresos contables: ventas (y sus devoluciones), otros ingresos, productos financieros y otros productos.</summary>
    public static readonly IReadOnlySet<string> Ingresos = new HashSet<string> { "401", "402", "403", "702", "704" };

    public const int MaxMovimientos = 500;

    private static readonly Lazy<Dictionary<string, CuentaDelCatalogo>> Catalogo = new(Cargar);

    private readonly IRepository<PolizasDelMes, Guid> _polizas;
    private readonly IRepository<SaldosDeApertura, Guid> _aperturas;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IAsyncQueryableExecuter _ejecutor;

    public HojaContable(IRepository<PolizasDelMes, Guid> polizas, IRepository<SaldosDeApertura, Guid> aperturas,
        IRepository<Documento, Guid> documentos, IAsyncQueryableExecuter ejecutor)
    {
        _polizas = polizas;
        _aperturas = aperturas;
        _documentos = documentos;
        _ejecutor = ejecutor;
    }

    public sealed record CuentaDelCatalogo(string Codigo, string Nombre, string Naturaleza, string Seccion, string Rubro, string Nif);

    private sealed record Mov(int Clave, string Fecha, PolizaContable Poliza, MovimientoContable M);

    /// <summary>Lo contabilizado hasta el mes: saldos de apertura y movimientos de cada mes (clave AAAAMM).</summary>
    private sealed record Libro(List<SaldoDeApertura> Apertura, int? ClaveApertura, List<Mov> Movimientos, int Meses, DateTime? Ultima,
        List<string> Avisos);

    public static CuentaDelCatalogo? DelCatalogo(string codigo) => Catalogo.Value.GetValueOrDefault(codigo);

    public async Task<RubrosContablesDto> RubrosAsync(Guid organizacionId, int anio, int mes, string seccion)
    {
        var (titulo, explicacion) = seccion switch
        {
            "deudas_contables" => ("Deudas contables",
                "Lo que la empresa debe sin contar impuestos: proveedores, acreedores, nómina por pagar, anticipos de clientes, préstamos y provisiones (NIF C-19, C-9, D-1 y D-3). Los impuestos están en «Deudas fiscales»."),
            "activos" => ("Activos", "Lo que la empresa tiene y le deben: bancos, clientes, inventarios, activo fijo y demás (NIF B-6, C-1, C-3, C-4, C-6)."),
            "capital" => ("Capital", "Lo que es de los dueños: capital aportado, resultados acumulados y el resultado del ejercicio en curso (NIF C-11)."),
            "ingresos_contables" => ("Ingresos contables",
                "Los ingresos según la contabilidad, de enero al mes: ventas menos devoluciones, otros ingresos y productos financieros (NIF B-3 y D-1). Pueden diferir de los fiscales: aquí cuentan al facturarse, no al cobrarse."),
            _ => throw new UserFriendlyException("Sección no válida."),
        };
        var libro = await LibroAsync(organizacionId, anio, mes);
        var dto = new RubrosContablesDto
        {
            Seccion = seccion, Titulo = titulo, Explicacion = explicacion, Anio = anio, Mes = mes,
            HayContabilidad = libro.Meses > 0, MesesConPolizas = libro.Meses, UltimaContabilizacionUtc = libro.Ultima,
            Desde = libro.ClaveApertura is { } ca ? $"saldos de apertura de {ca % 100:00}/{ca / 100}" : libro.Movimientos.Count > 0
                ? $"las pólizas desde {libro.Movimientos.Min(m => m.Clave) % 100:00}/{libro.Movimientos.Min(m => m.Clave) / 100} (sin saldos de apertura)"
                : "",
        };
        dto.Avisos.AddRange(libro.Avisos);
        if (libro.ClaveApertura == null && libro.Meses > 0 && seccion != "ingresos_contables")
        {
            dto.Avisos.Add("No hay saldos de apertura: los saldos son solo de los movimientos registrados en la plataforma. Captúralos en la mesa de trabajo.");
        }

        var resultados = seccion == "ingresos_contables";
        var saldos = Saldos(libro, desdeClave: resultados ? anio * 100 + 1 : null, conApertura: !resultados);
        bool Entra(string rubro) => seccion switch
        {
            "deudas_contables" => rubro.StartsWith('2') && !PasivosFiscales.Contains(rubro),
            "activos" => rubro.StartsWith('1'),
            "capital" => rubro.StartsWith('3'),
            _ => Ingresos.Contains(rubro),
        };

        foreach (var g in saldos.Where(s => Entra(Rubro(s.Key))).GroupBy(s => Rubro(s.Key)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var cat = DelCatalogo(g.Key);
            var cuentas = g.Select(s => new SaldoDeCuentaDto { Clave = s.Key, Nombre = DelCatalogo(s.Key)?.Nombre ?? s.Key, Saldo = Signo(s.Key, s.Value) })
                .Where(c => c.Saldo != 0).OrderBy(c => c.Clave, StringComparer.Ordinal).ToList();
            if (cuentas.Count == 0)
            {
                continue;
            }

            dto.Rubros.Add(new RubroContableDto
            {
                Clave = g.Key, Nombre = cat?.Nombre ?? $"Cuenta {g.Key}", Grupo = cat?.Seccion ?? "", Nif = cat?.Nif ?? "",
                Saldo = Math.Round(cuentas.Sum(c => c.Saldo), 2), Cuentas = cuentas,
            });
        }

        if (seccion == "capital")
        {
            // Los resultados que todavía no se traspasan a capital: el del ejercicio y los de años anteriores sin cerrar.
            decimal Resultado(Func<int, bool> cuando) => Math.Round(libro.Movimientos
                .Where(m => cuando(m.Clave) && "4567".Contains(m.M.Cuenta[0])).Sum(m => m.M.Haber - m.M.Debe), 2);
            var delEjercicio = Resultado(c => c >= anio * 100 + 1);
            var anteriores = Resultado(c => c < anio * 100 + 1)
                             + Math.Round(libro.Apertura.Where(a => "4567".Contains(a.Cuenta[0])).Sum(a => a.Acreedor - a.Deudor), 2);
            if (anteriores != 0)
            {
                dto.Rubros.Add(new RubroContableDto
                {
                    Clave = "resultado_anterior", Nombre = "Resultados de años anteriores sin traspasar", Grupo = "Capital contable", Nif = "C-11",
                    Saldo = anteriores, Virtual = true,
                });
                dto.Avisos.Add("Hay resultados de años anteriores que no se han traspasado a «Resultado de ejercicios anteriores» (304): falta la póliza de cierre.");
            }

            dto.Rubros.Add(new RubroContableDto
            {
                Clave = "resultado_del_ejercicio", Nombre = $"Resultado del ejercicio {anio} (de enero a {mes:00})", Grupo = "Capital contable", Nif = "B-3",
                Saldo = delEjercicio, Virtual = true,
            });
        }

        dto.Total = Math.Round(dto.Rubros.Sum(r => r.Saldo), 2);
        return dto;
    }

    public async Task<CuentaContableDto> CuentaAsync(Guid organizacionId, int anio, int mes, string cuenta)
    {
        cuenta = (cuenta ?? "").Trim();
        if (cuenta.Length < 3 || !char.IsDigit(cuenta[0]))
        {
            throw new UserFriendlyException("Cuenta no válida.");
        }

        var libro = await LibroAsync(organizacionId, anio, mes);
        var rubro = Rubro(cuenta);
        var resultados = "4567".Contains(cuenta[0]);
        var desde = resultados ? anio * 100 + 1 : (int?)null;
        bool EsDeLaCuenta(string c) => c == cuenta || c.StartsWith(cuenta + ".", StringComparison.Ordinal);
        var cat = DelCatalogo(cuenta) ?? DelCatalogo(rubro);
        var naturaleza = DelCatalogo(rubro)?.Naturaleza ?? (cuenta[0] is '1' or '5' or '6' ? "D" : "A");
        decimal Nat(decimal debe, decimal haber) => naturaleza == "D" ? debe - haber : haber - debe;

        var delMes = anio * 100 + mes;
        var anteriores = libro.Movimientos.Where(m => m.Clave < delMes && (desde == null || m.Clave >= desde) && EsDeLaCuenta(m.M.Cuenta));
        var saldoInicial = anteriores.Sum(m => Nat(m.M.Debe, m.M.Haber))
                           + (resultados ? 0 : libro.Apertura.Where(a => EsDeLaCuenta(a.Cuenta)).Sum(a => Nat(a.Deudor, a.Acreedor)));
        var movimientosDelMes = libro.Movimientos.Where(m => m.Clave == delMes && EsDeLaCuenta(m.M.Cuenta)).ToList();
        var dto = new CuentaContableDto
        {
            Clave = cuenta, Nombre = cat?.Nombre ?? $"Cuenta {cuenta}", Nif = cat?.Nif ?? "", Naturaleza = naturaleza,
            SaldoInicial = Math.Round(saldoInicial, 2),
            Cargos = Math.Round(movimientosDelMes.Sum(m => m.M.Debe), 2),
            Abonos = Math.Round(movimientosDelMes.Sum(m => m.M.Haber), 2),
        };
        dto.SaldoFinal = Math.Round(dto.SaldoInicial + Nat(dto.Cargos, dto.Abonos), 2);

        var saldos = Saldos(libro, desde, conApertura: !resultados);
        dto.Subcuentas = saldos.Where(s => s.Key != cuenta && EsDeLaCuenta(s.Key))
            .Select(s => new SaldoDeCuentaDto { Clave = s.Key, Nombre = DelCatalogo(s.Key)?.Nombre ?? s.Key, Saldo = Math.Round(Nat(s.Value.Debe, s.Value.Haber), 2) })
            .Where(s => s.Saldo != 0).OrderBy(s => s.Clave, StringComparer.Ordinal).ToList();

        // De quién es el saldo: por el RFC de la póliza (proveedor, cliente, acreedor…)
        var hasta = libro.Movimientos.Where(m => (desde == null || m.Clave >= desde) && EsDeLaCuenta(m.M.Cuenta)).ToList();
        dto.Terceros = hasta.Where(m => !string.IsNullOrWhiteSpace(m.Poliza.Rfc))
            .GroupBy(m => m.Poliza.Rfc!.ToUpperInvariant())
            .Select(g => new TerceroDeCuentaDto
            {
                Rfc = g.Key, Nombre = g.Select(m => NombreDelTercero(m.Poliza.Concepto)).FirstOrDefault(n => n.Length > 0) ?? "",
                Saldo = Math.Round(g.Sum(m => Nat(m.M.Debe, m.M.Haber)), 2), Movimientos = g.Count(),
            })
            .Where(t => t.Saldo != 0).OrderByDescending(t => Math.Abs(t.Saldo)).ToList();

        var recientes = hasta.OrderByDescending(m => m.Fecha, StringComparer.Ordinal).ThenByDescending(m => m.Poliza.Numero).ToList();
        dto.MovimientosRecortados = recientes.Count > MaxMovimientos;
        recientes = recientes.Take(MaxMovimientos).ToList();
        var uuids = recientes.Select(m => m.Poliza.Uuid?.ToUpperInvariant()).Where(u => !string.IsNullOrEmpty(u)).Distinct().ToList();
        var docs = uuids.Count == 0
            ? []
            : await _ejecutor.ToListAsync((await _documentos.GetQueryableAsync())
                .Where(d => d.OrganizacionId == organizacionId && d.Uuid != null && uuids.Contains(d.Uuid))
                .Select(d => new { d.Uuid, d.NombreOriginal, d.CreationTime }));
        var porUuid = docs.GroupBy(d => d.Uuid!).ToDictionary(g => g.Key, g => g.First());
        dto.Movimientos = recientes.Select(m =>
        {
            var u = m.Poliza.Uuid?.ToUpperInvariant();
            var doc = u != null && porUuid.TryGetValue(u, out var x) ? x : null;
            return new MovimientoDeCuentaDto
            {
                Fecha = m.Fecha, Poliza = $"{m.Poliza.Tipo} {m.Poliza.Numero}", Concepto = m.Poliza.Concepto, Cuenta = m.M.Cuenta, Texto = m.M.Texto,
                Rfc = m.Poliza.Rfc, Uuid = m.Poliza.Uuid, Debe = m.M.Debe, Haber = m.M.Haber, Archivo = doc?.NombreOriginal, EntroUtc = doc?.CreationTime,
            };
        }).ToList();
        return dto;
    }

    // ------------------------------------------------------------------------------------------- ayudas

    private async Task<Libro> LibroAsync(Guid organizacionId, int anio, int mes)
    {
        var clave = anio * 100 + mes;
        var apertura = await _aperturas.FirstOrDefaultAsync(a => a.OrganizacionId == organizacionId);
        int? claveApertura = apertura == null ? null : apertura.Anio * 100 + apertura.Mes;
        if (claveApertura > clave)
        {
            claveApertura = null;
            apertura = null;
        }

        var meses = (await _polizas.GetListAsync(p => p.OrganizacionId == organizacionId && p.Anio * 100 + p.Mes <= clave))
            .Where(p => claveApertura == null || p.Anio * 100 + p.Mes >= claveApertura)
            .OrderBy(p => p.Anio).ThenBy(p => p.Mes).ToList();
        var movs = new List<Mov>();
        foreach (var p in meses)
        {
            var r = JsonSerializer.Deserialize<ResultadoContable>(p.Resultado) ?? new ResultadoContable();
            foreach (var poliza in r.Polizas)
            {
                movs.AddRange(poliza.Movimientos.Where(m => m.Cuenta.Length > 0).Select(m => new Mov(p.Anio * 100 + p.Mes, poliza.Fecha, poliza, m)));
            }
        }

        var avisos = new List<string>();
        if (meses.Count > 0)
        {
            var desde = claveApertura ?? meses[0].Anio * 100 + meses[0].Mes;
            for (var f = new DateTime(desde / 100, desde % 100, 1); f <= new DateTime(anio, mes, 1); f = f.AddMonths(1))
            {
                if (!meses.Any(p => p.Anio == f.Year && p.Mes == f.Month))
                {
                    avisos.Add($"Faltan las pólizas de {f:MM/yyyy}: los saldos no las incluyen.");
                }
            }
        }

        var saldosDeApertura = apertura == null ? [] : JsonSerializer.Deserialize<List<SaldoDeApertura>>(apertura.Saldos) ?? [];
        return new Libro(saldosDeApertura, claveApertura, movs, meses.Count,
            meses.Count == 0 ? null : meses.Max(p => (DateTime?)p.LastModificationTime ?? p.CreationTime), avisos);
    }

    /// <summary>Sumas de cargos y abonos por cuenta (con la apertura si se pide).</summary>
    private static Dictionary<string, (decimal Debe, decimal Haber)> Saldos(Libro libro, int? desdeClave, bool conApertura)
    {
        var s = new Dictionary<string, (decimal Debe, decimal Haber)>();
        void Suma(string c, decimal d, decimal h) => s[c] = s.TryGetValue(c, out var x) ? (x.Debe + d, x.Haber + h) : (d, h);
        if (conApertura)
        {
            foreach (var a in libro.Apertura)
            {
                Suma(a.Cuenta, a.Deudor, a.Acreedor);
            }
        }

        foreach (var m in libro.Movimientos.Where(m => desdeClave == null || m.Clave >= desdeClave))
        {
            Suma(m.M.Cuenta, m.M.Debe, m.M.Haber);
        }

        return s;
    }

    /// <summary>Saldo con el signo de la naturaleza del rubro: positivo si es lo normal de la cuenta.</summary>
    private static decimal Signo(string cuenta, (decimal Debe, decimal Haber) s)
    {
        var naturaleza = DelCatalogo(Rubro(cuenta))?.Naturaleza ?? (cuenta[0] is '1' or '5' or '6' ? "D" : "A");
        return Math.Round(naturaleza == "D" ? s.Debe - s.Haber : s.Haber - s.Debe, 2);
    }

    public static string Rubro(string cuenta)
    {
        var i = cuenta.IndexOf('.');
        return i < 0 ? cuenta : cuenta[..i];
    }

    /// <summary>El concepto de la póliza empieza con la contraparte: «Proveedor · descripción» o «Pago · Proveedor · …».</summary>
    private static string NombreDelTercero(string concepto)
    {
        var partes = concepto.Split(" · ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var i = partes.Length > 1 && partes[0] == "Pago" ? 1 : 0;
        return partes.Length > i ? partes[i] : "";
    }

    private static Dictionary<string, CuentaDelCatalogo> Cargar()
    {
        using var s = typeof(HojaContable).Assembly.GetManifestResourceStream("AuxiliarContable.Concentrado.codigo_agrupador.json")
                      ?? throw new InvalidOperationException("Falta el código agrupador embebido.");
        using var json = JsonDocument.Parse(s);
        var d = new Dictionary<string, CuentaDelCatalogo>(StringComparer.Ordinal);
        foreach (var c in json.RootElement.GetProperty("cuentas").EnumerateArray())
        {
            var codigo = c.GetProperty("c").GetString()!;
            d[codigo] = new CuentaDelCatalogo(codigo, c.GetProperty("n").GetString()!, c.GetProperty("t").GetString()!,
                c.GetProperty("s").GetString()!, c.GetProperty("r").GetString()!, c.GetProperty("f").GetString()!);
        }

        return d;
    }
}
