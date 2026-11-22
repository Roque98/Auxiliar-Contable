using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Documentos;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Nomina;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace AuxiliarContable.Concentrado;

/// <summary>Ver <see cref="IConcentradoAppService"/>.</summary>
[Authorize]
public class ConcentradoAppService : AuxiliarContableAppService, IConcentradoAppService
{
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    private readonly IRepository<ImpuestosDelMes, Guid> _impuestos;
    private readonly IRepository<PagoDeObligacion, Guid> _pagos;
    private readonly IRepository<PeriodoDeNomina, Guid> _periodos;
    private readonly IRepository<Trabajador, Guid> _trabajadores;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IIdentityUserRepository _usuarios;
    private readonly CalculadoraDeImpuestos _calculadora;
    private readonly Autorizador _autorizador;
    private readonly IMotorFiscal _motor;
    private readonly HojaContable _hoja;

    public ConcentradoAppService(
        IRepository<ImpuestosDelMes, Guid> impuestos,
        IRepository<PagoDeObligacion, Guid> pagos,
        IRepository<PeriodoDeNomina, Guid> periodos,
        IRepository<Trabajador, Guid> trabajadores,
        IRepository<PerfilFiscal, Guid> perfiles,
        IRepository<Documento, Guid> documentos,
        IIdentityUserRepository usuarios,
        CalculadoraDeImpuestos calculadora,
        Autorizador autorizador,
        IMotorFiscal motor,
        HojaContable hoja)
    {
        _hoja = hoja;
        _impuestos = impuestos;
        _pagos = pagos;
        _periodos = periodos;
        _trabajadores = trabajadores;
        _perfiles = perfiles;
        _documentos = documentos;
        _usuarios = usuarios;
        _calculadora = calculadora;
        _autorizador = autorizador;
        _motor = motor;
    }

    // ------------------------------------------------------------------------------------------- deudas fiscales

    public async Task<DeudasFiscalesDto> GetDeudasFiscalesAsync(Guid organizacionId, int anio, int mes)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        Validar(anio, mes);
        var equipo = (await _autorizador.QuienMiraAsync()).Equipo;
        var hoy = Clock.Now.Date;
        var (fila, resultado) = await CalculoAsync(organizacionId, anio, mes);
        var periodo = Periodo(anio, mes);
        var delMes = resultado?.Meses.FirstOrDefault(m => m.Periodo == periodo);
        var pagos = await _pagos.GetListAsync(p => p.OrganizacionId == organizacionId && p.Anio == anio && p.Mes <= mes);
        var perfil = await _perfiles.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId);
        string? Campo(string clave) => perfil?.Dato(clave)?.Valor?.Trim();
        var obligacionesDelSat = CalculadoraDeImpuestos.Lista(Campo(CamposDelPerfil.Obligaciones));
        bool DiceElSat(params string[] palabras) => obligacionesDelSat.Any(o => palabras.Any(p => Normal(o).Contains(Normal(p))));

        var dto = new DeudasFiscalesDto
        {
            Anio = anio,
            Mes = mes,
            Calculado = resultado != null,
            CalculadoUtc = fila.CalculadoUtc,
            Actualizando = fila.PorActualizar,
            Error = fila.Error,
            PuedeRegistrarPagos = equipo,
            Regimen = resultado?.Regimen.Nombre is { Length: > 0 } n ? $"{resultado.Regimen.Principal} {n}" : resultado?.Regimen.Principal,
            ClaseDelCalculo = resultado?.Detalle?.Clase,
            Fuente = new FuenteDelCalculoDto
            {
                Facturas = fila.Facturas,
                Canceladas = fila.Canceladas,
                Supuestos = resultado?.Supuestos ?? [],
            },
        };
        dto.Avisos.AddRange(resultado?.Avisos ?? []);
        var ultima = (await _documentos.GetQueryableAsync())
            .Where(d => d.OrganizacionId == organizacionId && d.Tipo == TipoDeDocumento.Cfdi)
            .OrderByDescending(d => d.CreationTime).Select(d => (DateTime?)d.CreationTime);
        dto.Fuente.UltimaFacturaUtc = await AsyncExecuter.FirstOrDefaultAsync(ultima);

        var obligaciones = new List<(ObligacionFiscalDto Dto, List<(int Mes, decimal Importe)> PorMes, bool Accesorios)>();

        // ISR propio: el pago provisional del mes (o el definitivo de plataformas / RESICO)
        var isrPorMes = resultado?.Meses.Select(m => (Mes(m.Periodo), m.IsrAPagar ?? 0m)).ToList() ?? [];
        obligaciones.Add((new ObligacionFiscalDto
        {
            Clave = "isr", Nombre = "ISR propio", Nivel = "federal",
            Descripcion = "El impuesto sobre la renta de tu actividad: el pago provisional del mes.",
            Impuesto = delMes?.IsrAPagar, AFavor = delMes?.IsrAFavor ?? 0,
            Renglones = delMes?.RenglonesIsr.ToList() ?? [],
            Fundamento = delMes?.Metodo is { Length: > 0 } metodo
                ? $"Pago provisional, {metodo} LISR. Se presenta a más tardar el día 17 del mes siguiente."
                : "Pagos provisionales del ISR, a más tardar el día 17 del mes siguiente (arts. 14, 106 y 113-E LISR).",
        }, isrPorMes, true));

        // IVA: si el SAT se lo tiene como obligación o mueve IVA
        var ivaPorMes = resultado?.Meses.Select(m => (Mes(m.Periodo), m.IvaAPagar)).ToList() ?? [];
        var mueveIva = resultado?.Meses.Any(m => m.IvaCobrado != 0 || m.IvaPagado != 0) ?? false;
        if (DiceElSat("IVA", "valor agregado") || mueveIva || obligacionesDelSat.Count == 0)
        {
            obligaciones.Add((new ObligacionFiscalDto
            {
                Clave = "iva", Nombre = "IVA", Nivel = "federal",
                Descripcion = "El IVA que cobraste menos el que pagaste y te retuvieron.",
                Impuesto = delMes?.IvaAPagar, AFavor = delMes?.IvaSaldoAFavor ?? 0,
                Renglones = delMes?.RenglonesIva.ToList() ?? [],
                Fundamento = "Pago mensual definitivo, art. 5o-D LIVA: a más tardar el día 17 del mes siguiente.",
            }, ivaPorMes, true));
        }
        else
        {
            dto.NoAplican.Add("IVA: tus obligaciones ante el SAT no lo incluyen y no hay IVA en tus facturas.");
        }

        // IEPS: si el SAT se lo tiene como obligación o hay IEPS en sus facturas
        var iepsPorMes = resultado?.Meses.Where(m => m.IepsAPagar != null).Select(m => (Mes(m.Periodo), m.IepsAPagar ?? 0m)).ToList() ?? [];
        if (DiceElSat("IEPS", "produccion y servicios") || iepsPorMes.Count > 0)
        {
            obligaciones.Add((new ObligacionFiscalDto
            {
                Clave = "ieps", Nombre = "IEPS", Nivel = "federal",
                Descripcion = "Impuesto especial sobre producción y servicios de lo que vendes: el que cobraste menos el que pagaste de la misma clase de bien.",
                Impuesto = delMes?.IepsAPagar ?? (iepsPorMes.Count > 0 || resultado != null ? 0 : null),
                AFavor = delMes?.IepsSaldoAFavor ?? 0,
                Renglones = delMes?.RenglonesIeps.ToList() ?? [],
                Fundamento = "Art. 5o LIEPS: pago mensual a más tardar el día 17 del mes siguiente; se causa al cobrar (art. 10).",
                Nota = iepsPorMes.Count > 0
                    ? "Solo se acredita el IEPS de compras de la misma clase de bien que vendes sin modificarlo (art. 4o LIEPS): aquí se emparejan por tasa o cuota; tu contador revisa que sean de la misma clase."
                    : "No hay IEPS en tus facturas del ejercicio.",
            }, iepsPorMes, true));
        }
        else
        {
            dto.NoAplican.Add("IEPS: no está en tus obligaciones ante el SAT ni en tus facturas.");
        }

        // Retenciones que el cliente hizo a terceros (honorarios, arrendamiento, fletes…) al pagar sus facturas
        var deducciones = resultado?.Detalle?.Deducciones ?? [];
        List<(int, decimal)> RetPorMes(string campo) => Enumerable.Range(1, mes)
            .Select(k => (k, deducciones.Sum(d => d.PorMes.TryGetValue(Periodo(anio, k), out var x) && x.TryGetValue(campo, out var v) ? v : 0m)))
            .ToList();
        var retIsr = RetPorMes("ret_isr");
        var retIva = RetPorMes("ret_iva");
        if (retIsr.Any(r => r.Item2 != 0) || DiceElSat("retenciones de ISR por servicios profesionales", "retenciones por arrendamiento", "retenciones de ISR por arrendamiento"))
        {
            obligaciones.Add((new ObligacionFiscalDto
            {
                Clave = "ret_isr", Nombre = "Retenciones de ISR a terceros", Nivel = "federal",
                Descripcion = "El ISR que retuviste al pagar honorarios, rentas o fletes a personas físicas.",
                Impuesto = retIsr.FirstOrDefault(r => r.Item1 == mes).Item2,
                Renglones =
                [
                    new RenglonDelPapel { Concepto = "ISR retenido en las facturas que pagaste en el mes", Importe = retIsr.FirstOrDefault(r => r.Item1 == mes).Item2,
                        Fundamento = "arts. 106, último párrafo, y 116 LISR", Detalle = "retenciones_a_terceros_isr" },
                ],
                Fundamento = "Arts. 106 y 116 LISR: se entera a más tardar el día 17 del mes siguiente al pago.",
            }, retIsr, true));
        }

        if (retIva.Any(r => r.Item2 != 0) || DiceElSat("retenciones de IVA"))
        {
            obligaciones.Add((new ObligacionFiscalDto
            {
                Clave = "ret_iva", Nombre = "Retenciones de IVA", Nivel = "federal",
                Descripcion = "El IVA que retuviste al pagar servicios a personas físicas, fletes o subcontratación.",
                Impuesto = retIva.FirstOrDefault(r => r.Item1 == mes).Item2,
                Renglones =
                [
                    new RenglonDelPapel { Concepto = "IVA retenido en las facturas que pagaste en el mes", Importe = retIva.FirstOrDefault(r => r.Item1 == mes).Item2,
                        Fundamento = "art. 1o-A LIVA", Detalle = "retenciones_a_terceros_iva" },
                ],
                Fundamento = "Art. 1o-A LIVA: se entera junto con la declaración del mes en que se pagó.",
            }, retIva, true));
        }

        // Nómina: solo si el cliente tiene trabajadores
        var periodos = await _periodos.GetListAsync(p => p.OrganizacionId == organizacionId && p.Fin >= new DateTime(anio, 1, 1)
                                                          && p.Fin < new DateTime(anio, mes, 1).AddMonths(1));
        dto.Fuente.PeriodosDeNomina = periodos.Count;
        var tieneTrabajadores = periodos.Count > 0
                                || await _trabajadores.AnyAsync(t => t.OrganizacionId == organizacionId && t.FechaBaja == null)
                                || Normal(Campo(CamposDelPerfil.TieneTrabajadores) ?? "") is "si" or "true"
                                || DiceElSat("sueldos y salarios", "salarios");
        var dias = new Dictionary<string, int>();
        var recargosEstatales = new Dictionary<string, IsnPorEstado.Regla>();
        if (tieneTrabajadores)
        {
            var regla = await NominaAsync(obligaciones, periodos, anio, mes, dto, Campo(CamposDelPerfil.DomicilioFiscal));
            if (regla?.Dia is { } dia)
            {
                dias["isn"] = dia;
            }

            if (regla?.Tasa != null)
            {
                recargosEstatales["isn"] = regla;
            }
        }
        else
        {
            dto.NoAplican.Add("Nómina (retenciones de ISR por salarios, cuotas al IMSS, INFONAVIT e impuesto sobre nómina): no tienes trabajadores registrados.");
        }

        // Lo pagado, los atrasos y sus accesorios
        var adeudos = new List<(ObligacionFiscalDto Dto, AdeudoAnteriorDto Adeudo)>();
        foreach (var (o, porMes, conAccesorios) in obligaciones)
        {
            o.Periodo = o.Periodo is { Length: > 0 } ? o.Periodo : NombreDelMes(anio, mes);
            var diaLimite = dias.GetValueOrDefault(o.Clave, 17);
            o.Limite ??= Limite(anio, mes, diaLimite);
            o.Pagos = pagos.Where(p => p.Clave == o.Clave && p.Mes == mes).Select(ADto).ToList();
            o.Pagado = o.Pagos.Sum(p => p.Importe);
            foreach (var (k, importe) in porMes.Where(x => x.Mes < mes))
            {
                var pagado = pagos.Where(p => p.Clave == o.Clave && p.Mes == k).Sum(p => p.Importe);
                var saldo = Math.Round(importe - pagado, 2);
                if (saldo <= 0.5m)
                {
                    continue;
                }

                var a = new AdeudoAnteriorDto
                {
                    Mes = k, Periodo = NombreDelMes(anio, k), Impuesto = importe, Pagado = pagado, Limite = Limite(anio, k, diaLimite), Total = saldo,
                };
                o.Adeudos.Add(a);
                if (conAccesorios && a.Limite < hoy)
                {
                    adeudos.Add((o, a));
                }
                else if (a.Limite < hoy && recargosEstatales.TryGetValue(o.Clave, out var r))
                {
                    // Contribución estatal: recargos a la tasa mensual de la ley del estado, por mes o fracción.
                    a.MesesDeAtraso = MesesDeMora(a.Limite, hoy);
                    a.Factor = 1;
                    a.Recargos = Math.Round(saldo * r.Tasa!.Value * a.MesesDeAtraso, 2);
                    a.Total = Math.Round(saldo + a.Recargos, 2);
                    a.Fundamento = $"Recargos al {r.Tasa.Value * 100:0.##} % mensual ({r.FundamentoRecargos}); sin actualización.";
                }
            }
        }

        await AccesoriosAsync(adeudos, hoy, dto);
        foreach (var (o, _, _) in obligaciones)
        {
            Cerrar(o, hoy);
            dto.Obligaciones.Add(o);
        }

        dto.TotalAPagar = dto.Obligaciones.Sum(o => o.Total);
        return dto;
    }

    /// <summary>Las obligaciones de nómina; devuelve la regla del ISN del estado (plazo y recargos), si se conoce.</summary>
    private async Task<IsnPorEstado.Regla?> NominaAsync(List<(ObligacionFiscalDto, List<(int Mes, decimal Importe)>, bool)> obligaciones,
        List<PeriodoDeNomina> periodos, int anio, int mes, DeudasFiscalesDto dto, string? domicilio)
    {
        var calculados = periodos.Where(p => p.Resultado != null)
            .Select(p => (p.Fin.Month, Nomina: JsonSerializer.Deserialize<NominaDelMotor>(p.Resultado!)!)).ToList();
        var sinCalcular = periodos.Count(p => p.Resultado == null && p.Fin.Month == mes);
        if (sinCalcular > 0)
        {
            dto.Avisos.Add($"{sinCalcular} periodo(s) de nómina del mes sin calcular: no están en las cifras de nómina.");
        }

        var delMes = calculados.Where(c => c.Month == mes).Select(c => c.Nomina).ToList();
        List<(int, decimal)> PorMes(Func<NominaDelMotor, decimal> f) =>
            Enumerable.Range(1, mes).Select(k => (k, calculados.Where(c => c.Month == k).Sum(c => f(c.Nomina)))).ToList();

        var isr = PorMes(n => n.Totales.IsrRetenido);
        obligaciones.Add((new ObligacionFiscalDto
        {
            Clave = "ret_isr_salarios", Nombre = "Retenciones de ISR por salarios", Nivel = "federal",
            Descripcion = "El ISR que retuviste a tus trabajadores en sus recibos de nómina.",
            Impuesto = delMes.Count > 0 ? delMes.Sum(n => n.Totales.IsrRetenido) : null,
            Renglones =
            [
                new RenglonDelPapel { Concepto = $"ISR de la tarifa en {delMes.Count} periodo(s) de nómina", Importe = delMes.Sum(n => n.Totales.Isr), Fundamento = "art. 96 LISR" },
                new RenglonDelPapel { Concepto = "(−) Subsidio para el empleo", Importe = delMes.Sum(n => n.Totales.Subsidio), Fundamento = "Decreto de subsidio para el empleo" },
                new RenglonDelPapel { Concepto = "(=) ISR retenido a enterar", Importe = delMes.Sum(n => n.Totales.IsrRetenido), Tipo = "resultado" },
            ],
            Fundamento = "Art. 96 LISR: las retenciones se enteran a más tardar el día 17 del mes siguiente.",
            Nota = delMes.Count == 0 ? "No hay nómina calculada del mes." : null,
        }, isr, true));

        // IMSS (mensual) e INFONAVIT con retiro, cesantía y vejez (bimestral): las cuotas las suma el motor
        async Task<CuotasDeNomina?> CuotasAsync(List<NominaDelMotor> calculos)
        {
            if (calculos.Count == 0)
            {
                return null;
            }

            try
            {
                return await _motor.CuotasDeNominaAsync(calculos);
            }
            catch (MotorFiscalNoDisponibleException ex)
            {
                Logger.LogWarning(ex, "El motor no pudo sumar las cuotas del concentrado");
                if (!dto.Avisos.Any(a => a.Contains("IMSS")))
                {
                    dto.Avisos.Add("El motor fiscal no respondió: las cuotas al IMSS e INFONAVIT no se pudieron sumar ahora.");
                }

                return null;
            }
        }

        var bimestral = mes % 2 == 0;
        var cuotas = await CuotasAsync(delMes);
        // Los meses anteriores también: si no se pagaron, llevan actualización y recargos como el CFF (art. 40-A LSS).
        var imssPorMes = new List<(int Mes, decimal Importe)>();
        foreach (var k in calculados.Select(c => c.Month).Where(k => k < mes).Distinct().Order())
        {
            var c = await CuotasAsync(calculados.Where(x => x.Month == k).Select(x => x.Nomina).ToList());
            if (c != null)
            {
                imssPorMes.Add((k, c.TotalMensual));
            }
        }

        var cuotasDelBimestre = bimestral
            ? await CuotasAsync(calculados.Where(c => c.Month == mes || c.Month == mes - 1).Select(c => c.Nomina).ToList())
            : null;
        obligaciones.Add((new ObligacionFiscalDto
        {
            Clave = "imss", Nombre = "Cuotas obrero-patronales al IMSS", Nivel = "federal",
            Descripcion = "Las cuotas mensuales del seguro social de tus trabajadores (la parte del patrón y la que les retuviste).",
            Impuesto = cuotas?.TotalMensual,
            Renglones = cuotas == null ? [] : cuotas.Mensuales.Select(c => new RenglonDelPapel
            {
                Concepto = $"{c.Ramo} (patrón {c.Patron.ToString("C", Mx)}, trabajador {c.Obrero.ToString("C", Mx)})", Importe = c.Total, Fundamento = "LSS",
            }).Append(new RenglonDelPapel { Concepto = "(=) Cuotas del mes", Importe = cuotas.TotalMensual, Tipo = "resultado" }).ToList(),
            Fundamento = "Arts. 39 y 39-A LSS: las cuotas mensuales se pagan a más tardar el día 17 del mes siguiente; si no, llevan actualización y recargos en los términos del CFF (art. 40-A LSS).",
            Nota = "Los meses anteriores salen de la nómina calculada en la plataforma: si pagaste con el SUA, registra el pago para que no aparezcan como adeudo.",
        }, imssPorMes, true));

        var bimestre = (mes + 1) / 2;
        obligaciones.Add((new ObligacionFiscalDto
        {
            Clave = "infonavit", Nombre = "INFONAVIT y retiro (bimestral)", Nivel = "federal",
            Descripcion = "Aportaciones a la vivienda y al retiro, cesantía y vejez de tus trabajadores: se pagan cada dos meses.",
            Periodo = $"{bimestre}.º bimestre de {anio}",
            Impuesto = cuotasDelBimestre?.TotalBimestral,
            Renglones = cuotasDelBimestre == null ? [] : cuotasDelBimestre.Bimestrales
                .Select(c => new RenglonDelPapel { Concepto = c.Ramo, Importe = c.Total, Fundamento = "LINFONAVIT / LSS" })
                .Append(new RenglonDelPapel { Concepto = "(=) Aportaciones del bimestre", Importe = cuotasDelBimestre.TotalBimestral, Tipo = "resultado" }).ToList(),
            Fundamento = "Art. 29-II LINFONAVIT y art. 39 LSS: a más tardar el día 17 del mes siguiente al bimestre.",
            Nota = bimestral ? null : $"El bimestre termina en {NombreDelMes(anio, mes + 1)}: se paga con ese mes.",
        }, [], false));

        var isn = PorMes(n => n.Isn.Importe);
        var muestra = delMes.Select(n => n.Isn).FirstOrDefault(i => i.Estado != null)
                      ?? calculados.Select(c => c.Nomina.Isn).LastOrDefault(i => i.Estado != null);
        var regla = IsnPorEstado.Buscar(muestra?.Estado, anio) ?? IsnPorEstado.Buscar(domicilio, anio);
        if (regla?.HeredadoDe is { } heredado)
        {
            dto.Avisos.Add($"Las leyes estatales de {anio} aún no están cargadas en el sistema: la fecha límite del impuesto sobre nómina "
                           + $"es la de {heredado} y sus recargos no se calculan hasta cargar la tasa de {anio}.");
        }

        obligaciones.Add((new ObligacionFiscalDto
        {
            Clave = "isn", Nombre = "Impuesto sobre nómina", Nivel = "estatal",
            Descripcion = $"El impuesto de tu estado{(muestra?.Estado is { } e ? $" ({e})" : "")} sobre lo que pagas a tus trabajadores.",
            Impuesto = delMes.Count > 0 ? delMes.Sum(n => n.Isn.Importe) : null,
            LimiteAproximado = regla?.Dia == null,
            Renglones = muestra == null ? [] :
            [
                new RenglonDelPapel { Concepto = "Tasa del estado", Importe = muestra.Tasa * 100, Tipo = "tasa", Fundamento = muestra.Fundamento },
                new RenglonDelPapel { Concepto = "(=) Impuesto sobre nómina del mes", Importe = delMes.Sum(n => n.Isn.Importe), Tipo = "resultado" },
            ],
            Fundamento = muestra?.Fundamento is { Length: > 0 } f ? f : "Ley de hacienda de tu estado.",
            Nota = (regla?.Dia != null
                       ? $"Fecha límite: día {regla.Dia} del mes siguiente ({regla.FundamentoPlazo}"
                         + (regla.HeredadoDe is { } h ? $", ley vigente en {h}" : "") + "). "
                       : "La fecha límite la fija la ley de tu estado; se muestra el día 17 del mes siguiente, que es el usual. ")
                   + (regla?.Tasa != null
                       ? $"Si se paga tarde: recargos del {regla.Tasa.Value * 100:0.##} % mensual ({regla.FundamentoRecargos})."
                       : regla?.HeredadoDe != null
                           ? $"Los recargos de tu estado no se calculan aquí: la tasa de {anio} sale de su ley de ingresos {anio}, que aún no está cargada."
                           : "Los recargos de tu estado no se calculan aquí: su tasa no está en las leyes que tiene el sistema."),
        }, isn, false));
        return regla;
    }

    private async Task AccesoriosAsync(List<(ObligacionFiscalDto Dto, AdeudoAnteriorDto Adeudo)> adeudos, DateTime hoy, DeudasFiscalesDto dto)
    {
        if (adeudos.Count == 0)
        {
            return;
        }

        try
        {
            var r = await _motor.AccesoriosAsync(
                adeudos.Select((a, i) => new AdeudoParaElMotor($"{i}", a.Adeudo.Total, a.Adeudo.Limite)).ToList(), hoy);
            foreach (var x in r.Adeudos)
            {
                if (!int.TryParse(x.Clave, out var i) || i < 0 || i >= adeudos.Count)
                {
                    continue;
                }

                var a = adeudos[i].Adeudo;
                a.MesesDeAtraso = x.Meses;
                a.Factor = x.Factor;
                a.Actualizacion = x.Actualizacion;
                a.Recargos = x.Recargos;
                a.Total = x.Total;
                a.Fundamento = x.Fundamento;
                if (x.Aviso is { Length: > 0 } aviso && !dto.Avisos.Contains(aviso))
                {
                    dto.Avisos.Add(aviso);
                }
            }
        }
        catch (MotorFiscalNoDisponibleException ex)
        {
            Logger.LogWarning(ex, "El motor no pudo calcular los accesorios");
            dto.Avisos.Add("El motor fiscal no respondió: los meses atrasados se muestran sin actualización ni recargos.");
        }
    }

    /// <summary>Suma lo atrasado al cálculo de la obligación y decide su estado.</summary>
    private static void Cerrar(ObligacionFiscalDto o, DateTime hoy)
    {
        o.Actualizacion = o.Adeudos.Sum(a => a.Actualizacion);
        o.Recargos = o.Adeudos.Sum(a => a.Recargos);
        var delPeriodo = Math.Max(0, (o.Impuesto ?? 0) - o.Pagado);
        o.Total = Math.Round(delPeriodo + o.Adeudos.Sum(a => a.Total), 2);
        if (o.Adeudos.Count > 0)
        {
            o.Renglones.Add(new RenglonDelPapel { Concepto = "Lo que falta pagar de meses anteriores", Tipo = "nota" });
            foreach (var a in o.Adeudos)
            {
                o.Renglones.Add(new RenglonDelPapel
                {
                    Concepto = $"(+) {Primera(a.Periodo)}: sin pagar", Importe = a.Impuesto - a.Pagado,
                    Fundamento = $"debió pagarse el {a.Limite:dd/MM/yyyy}",
                });
                if (a.Actualizacion != 0)
                {
                    o.Renglones.Add(new RenglonDelPapel
                    {
                        Concepto = $"(+) Actualización de {a.Periodo} (factor {a.Factor:0.0000})", Importe = a.Actualizacion, Fundamento = "art. 17-A CFF",
                    });
                }

                if (a.Recargos != 0)
                {
                    o.Renglones.Add(new RenglonDelPapel
                    {
                        Concepto = $"(+) Recargos de {a.Periodo} ({a.MesesDeAtraso} mes(es))", Importe = a.Recargos, Fundamento = "art. 21 CFF",
                    });
                }
            }
        }

        if (o.Pagado > 0)
        {
            o.Renglones.Add(new RenglonDelPapel { Concepto = "(−) Ya pagado de este periodo", Importe = o.Pagado });
        }

        if (o.Adeudos.Count > 0 || o.Pagado > 0)
        {
            o.Renglones.Add(new RenglonDelPapel { Concepto = "(=) Total a pagar hoy", Importe = o.Total, Tipo = "resultado" });
        }

        o.Estado = o.Impuesto == null && o.Adeudos.Count == 0 ? (o.Nota != null && o.Clave == "infonavit" ? "en_curso" : "sin_calcular")
            : o.Total > 0.5m ? (o.Adeudos.Any(a => a.Limite < hoy) || (o.Limite < hoy && delPeriodo > 0.5m) ? "vencido" : "por_pagar")
            : o.AFavor > 0.5m ? "a_favor"
            : o.Pagado > 0 ? "pagado"
            : "sin_impuesto";
    }

    // ------------------------------------------------------------------------------------------- detalle

    public async Task<DetalleDeRenglonDto> GetDetalleAsync(Guid organizacionId, int anio, int mes, string detalle, bool soloDelMes)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        Validar(anio, mes);
        var (_, resultado) = await CalculoAsync(organizacionId, anio, mes);
        var d = resultado?.Detalle ?? throw new UserFriendlyException("El cálculo de este mes todavía no está listo; intenta en un momento.");
        var periodo = Periodo(anio, mes);
        decimal DelMes(FacturaDelCalculo f, string campo) => f.PorMes.TryGetValue(periodo, out var x) && x.TryGetValue(campo, out var v) ? v : 0;
        decimal Hasta(FacturaDelCalculo f, string campo) =>
            f.PorMes.Where(p => string.CompareOrdinal(p.Key, periodo) <= 0).Sum(p => p.Value.TryGetValue(campo, out var v) ? v : 0);
        decimal Aporta(FacturaDelCalculo f, string campo) => soloDelMes ? DelMes(f, campo) : Hasta(f, campo);
        bool EsDelPeriodo(FacturaDelCalculo f) => soloDelMes ? f.Fecha.StartsWith(periodo, StringComparison.Ordinal) : true;
        var dto = new DetalleDeRenglonDto { Detalle = detalle, SoloDelMes = soloDelMes };
        var alcance = soloDelMes ? $"de {NombreDelMes(anio, mes)}" : $"de enero a {NombreDelMes(anio, mes)}";

        switch (detalle)
        {
            case "ingresos":
            case "iva_cobrado":
            case "retenciones_isr":
            case "retenciones_iva":
            {
                var campo = detalle switch { "iva_cobrado" => "iva", "retenciones_isr" => "ret_isr", "retenciones_iva" => "ret_iva", _ => "base" };
                var nominal = d.Clase == "pm" && detalle == "ingresos";
                dto.Titulo = detalle switch
                {
                    "iva_cobrado" => $"IVA cobrado {alcance}",
                    "retenciones_isr" => $"ISR que te retuvieron tus clientes {alcance}",
                    "retenciones_iva" => $"IVA que te retuvieron tus clientes {alcance}",
                    _ => nominal ? $"Ingresos facturados {alcance}" : $"Ingresos cobrados {alcance}",
                };
                dto.Explicacion = nominal
                    ? "Como persona moral, en los pagos provisionales cuentan los ingresos facturados aunque no estén cobrados (art. 14 LISR)."
                    : "Cuentan cuando se cobran: las facturas de contado (PUE) en su fecha y las de crédito (PPD) cuando llega su complemento de pago o el banco muestra el cobro (arts. 102 LISR y 1o-B LIVA).";
                var cuentan = d.Ingresos.Where(f => Aporta(f, campo) != 0).ToList();
                dto.Grupos.Add(Grupo("consideradas", nominal ? "Facturadas" : "Cobradas (sí cuentan)", "", cuentan, f => Aporta(f, campo)));
                if (!nominal && campo is "base" or "iva")
                {
                    var porCobrar = d.Ingresos.Where(f => f.Estado is "por_cobrar" or "cobro_parcial" && EsDelPeriodo(f) && Aporta(f, campo) == 0).ToList();
                    dto.Grupos.Add(Grupo("por_definir", "Por cobrar (todavía no cuentan)",
                        "Facturas a crédito (PPD) sin cobro: se acumulan cuando las cobres.", porCobrar, f => campo == "iva" ? f.Iva : f.Base));
                    var noCuentan = d.Ingresos.Where(f => f.Estado == "no_cuenta" && EsDelPeriodo(f)).ToList();
                    dto.Grupos.Add(Grupo("no_consideradas", "No cuentan", "Notas de crédito que aplican un anticipo o rebajan una factura PPD: su complemento ya paga el saldo neto.",
                        noCuentan, f => f.Base));
                }

                break;
            }

            case "deducciones":
            case "iva_acreditable":
            case "retenciones_a_terceros_isr":
            case "retenciones_a_terceros_iva":
            {
                var campo = detalle switch { "iva_acreditable" => "iva_acreditable", "retenciones_a_terceros_isr" => "ret_isr", "retenciones_a_terceros_iva" => "ret_iva", _ => "deducible" };
                dto.Titulo = detalle switch
                {
                    "iva_acreditable" => $"IVA acreditable {alcance}",
                    "retenciones_a_terceros_isr" => $"ISR que retuviste {alcance}",
                    "retenciones_a_terceros_iva" => $"IVA que retuviste {alcance}",
                    _ => $"Deducciones autorizadas {alcance}",
                };
                dto.Explicacion = d.Clase switch
                {
                    "pm" when detalle == "deducciones" => "Como persona moral, tus pagos provisionales no restan deducciones (se paga sobre ingresos × coeficiente, art. 14 LISR): las deducciones cuentan en la declaración anual. La lista te sirve para revisarlas.",
                    "resico_pf" when detalle == "deducciones" => "En RESICO no hay deducciones para el ISR (art. 113-E LISR); la lista sirve para tu IVA acreditable.",
                    "arrendamiento" when detalle == "deducciones" => "Con la deducción opcional del 35 % (art. 115 LISR) las facturas no restan para el ISR; sí cuentan para el IVA acreditable.",
                    _ => "Cuentan cuando se pagan y si la factura cumple los requisitos para tu régimen (arts. 27 y 28 LISR para personas morales; 103, 105 y 147 para personas físicas). El IVA de lo no deducible tampoco se acredita (art. 5o-I LIVA).",
                };
                var si = d.Deducciones.Where(f => Aporta(f, campo) != 0).ToList();
                dto.Grupos.Add(Grupo("consideradas", "Consideradas", "", si, f => Aporta(f, campo)));
                if (campo is "deducible" or "iva_acreditable")
                {
                    var no = d.Deducciones.Where(f => (f.Estado == "no_considerada" || f.Estado == "parcial") && (EsDelPeriodo(f) || f.Meses.Contains(periodo))).ToList();
                    dto.Grupos.Add(Grupo("no_consideradas", "No consideradas",
                        "No se tomaron (o solo en parte): toca una para ver el porqué.", no,
                        f => campo == "iva_acreditable" ? f.Iva - Aporta(f, campo) : f.Base - Aporta(f, campo)));
                    var pendientes = d.Deducciones.Where(f => f.Estado is "por_definir" or "pago_parcial" && EsDelPeriodo(f) && Aporta(f, campo) == 0).ToList();
                    dto.Grupos.Add(Grupo("por_definir", "Pendientes por definir",
                        "Todavía no cuentan: falta el pago, su complemento o una respuesta tuya.", pendientes, f => campo == "iva_acreditable" ? f.Iva : f.Base));
                }

                break;
            }

            default:
                throw new UserFriendlyException("Ese renglón no tiene detalle.");
        }

        return dto;
    }

    private static GrupoDeFacturasDto Grupo(string clave, string titulo, string descripcion, List<FacturaDelCalculo> facturas, Func<FacturaDelCalculo, decimal> importe)
    {
        var lista = facturas.Select(f => new FacturaResumenDto
        {
            Uuid = f.Uuid, Fecha = f.Fecha, Rfc = f.Rfc, Nombre = f.Nombre, Concepto = f.Concepto, Total = f.Total,
            Importe = Math.Round(importe(f), 2), Estado = f.Estado,
            Motivo = f.Motivos.Select(m => m.Hipotesis).FirstOrDefault(),
        }).OrderByDescending(f => Math.Abs(f.Importe)).ToList();
        return new GrupoDeFacturasDto { Clave = clave, Titulo = titulo, Descripcion = descripcion, Facturas = lista, Importe = lista.Sum(f => f.Importe) };
    }

    // ------------------------------------------------------------------------------------------- una factura

    public async Task<FacturaExplicadaDto> GetFacturaAsync(Guid organizacionId, int anio, int mes, string uuid)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        Validar(anio, mes);
        var (_, resultado) = await CalculoAsync(organizacionId, anio, mes);
        var u = (uuid ?? "").Trim().ToUpperInvariant();
        var d = resultado?.Detalle ?? throw new UserFriendlyException("El cálculo de este mes todavía no está listo; intenta en un momento.");
        var ingreso = d.Ingresos.FirstOrDefault(f => f.Uuid == u);
        var factura = ingreso ?? d.Deducciones.FirstOrDefault(f => f.Uuid == u)
                      ?? throw new EntityNotFoundException(typeof(FacturaDelCalculo), u);
        var dto = new FacturaExplicadaDto
        {
            Factura = factura,
            Lado = ingreso != null ? "ingreso" : "deduccion",
            Regimen = resultado.Regimen.Nombre is { Length: > 0 } n ? $"{resultado.Regimen.Principal} {n}" : resultado.Regimen.Principal,
            Resumen = Resumen(factura, ingreso != null, d.Clase),
        };
        if (u.Length > 0)
        {
            var doc = await _documentos.FirstOrDefaultAsync(x => x.OrganizacionId == organizacionId && x.Uuid == u);
            if (doc != null)
            {
                dto.Origen = new OrigenDeFacturaDto
                {
                    DocumentoId = doc.Id, Archivo = doc.NombreOriginal, EntroUtc = doc.CreationTime, EstatusSat = doc.EstatusSat,
                    EstatusSatUtc = doc.EstatusSatUtc, Quien = await QuienAsync(doc.CreatorId),
                };
            }
        }

        return dto;
    }

    private static string Resumen(FacturaDelCalculo f, bool ingreso, string clase)
    {
        string Pesos(decimal v) => v.ToString("C", Mx);
        string Meses() => f.Meses.Count == 0 ? "" : " (" + string.Join(", ", f.Meses.Select(m => NombreDelMes(int.Parse(m[..4]), int.Parse(m[5..])))) + ")";
        if (ingreso)
        {
            return f.Estado switch
            {
                "cobrada" or "cobro_parcial" when f.Acumulable != 0 => $"Se acumuló como ingreso: {Pesos(f.Acumulable)} sin IVA{Meses()}."
                    + (f.Estado == "cobro_parcial" ? " Solo lo cobrado; el resto contará cuando lo cobres." : ""),
                "facturada" => $"Se acumuló como ingreso al facturarse: {Pesos(f.Acumulable)} sin IVA (art. 14 LISR, personas morales).",
                "por_cobrar" => "Todavía no se acumula: es una factura a crédito (PPD) sin cobro. Cuenta cuando la cobres y emitas su complemento de pago (art. 102 LISR).",
                "nota_de_credito" => $"Nota de crédito: rebaja tus ingresos en {Pesos(-f.Acumulable)}.",
                "no_cuenta" => "Esta nota de crédito no mueve tus ingresos: " + (f.Motivos.FirstOrDefault()?.Hipotesis ?? "el complemento de la factura ya paga el saldo neto."),
                _ => "Factura emitida.",
            };
        }

        var inicio = f.Estado switch
        {
            "considerada" => $"Se consideró deducible: {Pesos(f.Deducible)}{Meses()}.",
            "pago_parcial" => $"Se dedujo solo lo pagado: {Pesos(f.Deducible)}{Meses()}; el resto contará cuando se pague.",
            "parcial" => $"Se dedujo solo en parte: {Pesos(f.Deducible)} de {Pesos(f.Base)}.",
            "no_considerada" => "No se consideró deducible.",
            "por_definir" => f.Metodo == "PPD" && f.Deducible == 0
                ? "Todavía no cuenta: es una factura a crédito (PPD) sin pago. Se deduce cuando la pagues y el proveedor emita su complemento de pago (arts. 27-VIII y 105-I LISR)."
                : "Está pendiente por definir: hace falta un dato para saber si cuenta.",
            _ => "",
        };
        if (clase == "pm")
        {
            inicio += " En los pagos provisionales de una persona moral no se restan deducciones; cuenta en la anual.";
        }
        else if (clase == "resico_pf")
        {
            inicio += " En RESICO no se restan deducciones para el ISR; sí cuenta para el IVA acreditable.";
        }

        return inicio;
    }

    private async Task<string> QuienAsync(Guid? creador)
    {
        if (creador == null)
        {
            return "entró de forma automática (descarga del SAT o herramienta del despacho)";
        }

        if (creador == CurrentUser.Id)
        {
            return "la subiste tú";
        }

        var u = await _usuarios.FindAsync(creador.Value);
        var nombre = u == null ? null : $"{u.Name} {u.Surname}".Trim();
        return $"la subió {(string.IsNullOrWhiteSpace(nombre) ? u?.UserName ?? "otra persona" : nombre)}";
    }

    // ------------------------------------------------------------------------------------------- pestañas contables

    public async Task<RubrosContablesDto> GetRubrosAsync(Guid organizacionId, int anio, int mes, string seccion)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        Validar(anio, mes);
        return await _hoja.RubrosAsync(organizacionId, anio, mes, seccion);
    }

    public async Task<CuentaContableDto> GetCuentaAsync(Guid organizacionId, int anio, int mes, string cuenta)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        Validar(anio, mes);
        return await _hoja.CuentaAsync(organizacionId, anio, mes, cuenta);
    }

    // ------------------------------------------------------------------------------------------- pagos

    public async Task<PagoDeObligacionDto> RegistrarPagoAsync(Guid organizacionId, RegistrarPagoDto dto)
    {
        await ExigirEquipoAsync(organizacionId);
        Validar(dto.Anio, dto.Mes);
        if (!ObligacionesFiscales.Claves.Contains(dto.Clave))
        {
            throw new UserFriendlyException("Obligación no válida.");
        }

        if (dto.FechaDePago.Date > Clock.Now.Date)
        {
            throw new UserFriendlyException("La fecha del pago no puede ser futura.");
        }

        var p = await _pagos.InsertAsync(new PagoDeObligacion(GuidGenerator.Create(), organizacionId, dto.Anio, dto.Mes, dto.Clave, dto.Importe,
            dto.FechaDePago, dto.Nota), autoSave: true);
        return ADto(p);
    }

    public async Task BorrarPagoAsync(Guid organizacionId, Guid pagoId)
    {
        await ExigirEquipoAsync(organizacionId);
        var p = await _pagos.FirstOrDefaultAsync(x => x.Id == pagoId && x.OrganizacionId == organizacionId)
                ?? throw new EntityNotFoundException(typeof(PagoDeObligacion), pagoId);
        await _pagos.DeleteAsync(p, autoSave: true);
    }

    private async Task ExigirEquipoAsync(Guid organizacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        if (!(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new AbpAuthorizationException("Los pagos los registra el despacho.");
        }
    }

    // ------------------------------------------------------------------------------------------- ayudas

    /// <summary>El cálculo guardado del mes; si no lo hay, es de otro perfil o no trae el detalle, se pide de nuevo.</summary>
    private async Task<(ImpuestosDelMes Fila, PagosProvisionales? Resultado)> CalculoAsync(Guid organizacionId, int anio, int mes)
    {
        var ya = Clock.Now.ToUniversalTime() - ActualizadorDeImpuestos.Espera;
        var fila = await _calculadora.FilaAsync(organizacionId, anio, mes, ya);
        var resultado = fila.Resultado == null ? null : JsonSerializer.Deserialize<PagosProvisionales>(fila.Resultado);
        if (!fila.PorActualizar && (resultado?.Detalle == null && fila.Error == null
                                    || fila.HuellaDelPerfil != (await _calculadora.PerfilAsync(organizacionId)).Huella))
        {
            fila.Pedir(ya);
            await _impuestos.UpdateAsync(fila, autoSave: true);
        }

        return (fila, resultado);
    }

    private static PagoDeObligacionDto ADto(PagoDeObligacion p) => new()
    {
        Id = p.Id, Anio = p.Anio, Mes = p.Mes, Clave = p.Clave, Importe = p.Importe, FechaDePago = p.FechaDePago, Nota = p.Nota,
    };

    private static void Validar(int anio, int mes)
    {
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }
    }

    private static string Periodo(int anio, int mes) => $"{anio:0000}-{mes:00}";

    /// <summary>Meses o fracción desde el día siguiente al límite hasta hoy (hasta cinco años).</summary>
    public static int MesesDeMora(DateTime limite, DateTime pago)
    {
        if (pago <= limite)
        {
            return 0;
        }

        var n = (pago.Year - limite.Year) * 12 + pago.Month - limite.Month + (pago.Day > limite.Day ? 1 : 0);
        return Math.Clamp(n, 1, 60);
    }

    private static int Mes(string periodo) => int.Parse(periodo[5..7], CultureInfo.InvariantCulture);

    /// <summary>El día límite (17 si no se dice otro) del mes siguiente; si cae en sábado o domingo, el lunes (art. 12 CFF).</summary>
    public static DateTime Limite(int anio, int mes, int dia = 17)
    {
        var d = new DateTime(anio, mes, 1).AddMonths(1);
        d = d.AddDays(Math.Min(dia, DateTime.DaysInMonth(d.Year, d.Month)) - 1);
        return d.DayOfWeek switch { DayOfWeek.Saturday => d.AddDays(2), DayOfWeek.Sunday => d.AddDays(1), _ => d };
    }

    private static string NombreDelMes(int anio, int mes)
    {
        if (mes > 12)
        {
            (anio, mes) = (anio + 1, mes - 12);
        }

        return $"{Mx.DateTimeFormat.GetMonthName(mes)} de {anio}";
    }

    private static string Primera(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Mx) + s[1..];

    private static string Normal(string s) =>
        new string(s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant().Trim();
}

/// <summary>Las obligaciones que maneja la hoja concentrado.</summary>
public static class ObligacionesFiscales
{
    public static readonly IReadOnlySet<string> Claves =
        new HashSet<string> { "isr", "iva", "ieps", "ret_isr", "ret_iva", "ret_isr_salarios", "imss", "infonavit", "isn" };
}
