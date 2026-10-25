using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.EstadosDeCuenta;

/// <summary>
/// Un estado de cuenta bancario leído de su documento. Los movimientos los extrae el modelo; si
/// CUADRA lo decide el código (<see cref="Cuadrar"/>): saldo inicial + abonos − cargos = saldo
/// final. Si no cuadra, va a revisión humana y no se usa para conciliar hasta que alguien lo
/// corrija o lo dé por bueno (CLAUDE.md §1.0: la IA no decide cifras).
/// </summary>
public class EstadoDeCuenta : FullAuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }
    public Guid DocumentoId { get; protected set; }
    public string Banco { get; protected set; } = string.Empty;

    /// <summary>Solo los últimos dígitos de la cuenta: identifica sin guardar el número completo.</summary>
    public string? Cuenta { get; protected set; }

    public DateTime? PeriodoInicio { get; protected set; }
    public DateTime? PeriodoFin { get; protected set; }
    public string Moneda { get; protected set; } = "MXN";
    public decimal? SaldoInicial { get; protected set; }
    public decimal? SaldoFinal { get; protected set; }
    public decimal TotalAbonos { get; protected set; }
    public decimal TotalCargos { get; protected set; }

    /// <summary>Saldo final según los movimientos menos el que dice el banco. 0 = cuadra.</summary>
    public decimal? Diferencia { get; protected set; }

    /// <summary>Renglones cuyo saldo corrido no sigue del anterior: dónde mirar si no cuadra.</summary>
    public int SaltosDeSaldo { get; protected set; }

    public RevisionDelEstadoDeCuenta Revision { get; protected set; }

    public ICollection<MovimientoBancario> Movimientos { get; protected set; } = [];

    protected EstadoDeCuenta()
    {
    }

    public EstadoDeCuenta(Guid id, Guid organizacionId, Guid documentoId) : base(id)
    {
        OrganizacionId = organizacionId;
        DocumentoId = documentoId;
    }

    /// <summary>Lo que leyó el modelo, entero. Reemplaza lo anterior (volver a leer) y vuelve a cuadrar.</summary>
    public void Llenar(
        string banco, string? cuenta, DateTime? inicio, DateTime? fin, string moneda,
        decimal? saldoInicial, decimal? saldoFinal, IEnumerable<MovimientoBancario> movimientos)
    {
        Banco = Recortar(string.IsNullOrWhiteSpace(banco) ? "Banco sin identificar" : banco.Trim(), EstadoDeCuentaConsts.MaxBancoLength)!;
        Cuenta = Recortar(SoloUltimos(cuenta), EstadoDeCuentaConsts.MaxCuentaLength);
        PeriodoInicio = inicio;
        PeriodoFin = fin;
        Moneda = string.IsNullOrWhiteSpace(moneda) ? "MXN" : moneda.Trim().ToUpperInvariant();
        SaldoInicial = saldoInicial;
        SaldoFinal = saldoFinal;
        Movimientos.Clear();
        foreach (var m in movimientos.Take(EstadoDeCuentaConsts.MaxMovimientos))
        {
            Movimientos.Add(m);
        }

        Cuadrar();
    }

    /// <summary>La regla, por código. Se repite cada vez que cambian los movimientos.</summary>
    public void Cuadrar()
    {
        TotalAbonos = Movimientos.Sum(m => m.Abono);
        TotalCargos = Movimientos.Sum(m => m.Cargo);
        Diferencia = SaldoInicial is { } inicial && SaldoFinal is { } final
            ? inicial + TotalAbonos - TotalCargos - final
            : null;

        SaltosDeSaldo = 0;
        var anterior = SaldoInicial;
        foreach (var m in Movimientos.OrderBy(x => x.Orden))
        {
            if (m.Saldo is { } saldo)
            {
                if (anterior is { } a && Math.Abs(a + m.Abono - m.Cargo - saldo) > EstadoDeCuentaConsts.Tolerancia)
                {
                    SaltosDeSaldo++;
                }

                anterior = saldo;
            }
            else
            {
                anterior = anterior is { } a ? a + m.Abono - m.Cargo : null;
            }
        }

        if (Revision != RevisionDelEstadoDeCuenta.Confirmado)
        {
            Revision = Cuadra ? RevisionDelEstadoDeCuenta.PorConfirmar : RevisionDelEstadoDeCuenta.RequiereRevision;
        }
    }

    public bool Cuadra => Diferencia is { } d && Math.Abs(d) <= EstadoDeCuentaConsts.Tolerancia;

    /// <summary>
    /// Una persona lo da por bueno. Si no cuadra hace falta decirlo explícitamente
    /// (<paramref name="aunqueNoCuadre"/>): queda en la auditoría quién lo aceptó así.
    /// </summary>
    public void Confirmar(bool aunqueNoCuadre)
    {
        if (!Cuadra && !aunqueNoCuadre)
        {
            throw new BusinessException("AuxiliarContable:EstadoDeCuentaNoCuadra").WithData("diferencia", Diferencia?.ToString("N2") ?? "sin saldos");
        }

        Revision = RevisionDelEstadoDeCuenta.Confirmado;
    }

    /// <summary>Corrige un renglón (lo leyó mal el modelo). Vuelve a cuadrar y a pedir confirmación.</summary>
    public void CorregirMovimiento(Guid movimientoId, DateTime fecha, string concepto, decimal cargo, decimal abono)
    {
        var m = Movimientos.FirstOrDefault(x => x.Id == movimientoId)
                ?? throw new EntityNotFoundException(typeof(MovimientoBancario), movimientoId);
        m.Corregir(fecha, concepto, cargo, abono);
        Revision = RevisionDelEstadoDeCuenta.PorConfirmar;
        Cuadrar();
    }

    public void CorregirSaldos(decimal? saldoInicial, decimal? saldoFinal)
    {
        SaldoInicial = saldoInicial;
        SaldoFinal = saldoFinal;
        Revision = RevisionDelEstadoDeCuenta.PorConfirmar;
        Cuadrar();
    }

    private static string? SoloUltimos(string? cuenta)
    {
        if (string.IsNullOrWhiteSpace(cuenta))
        {
            return null;
        }

        var digitos = new string(cuenta.Where(char.IsDigit).ToArray());
        return digitos.Length > 4 ? digitos[^4..] : digitos.Length > 0 ? digitos : cuenta.Trim();
    }

    private static string? Recortar(string? texto, int max) => texto is null || texto.Length <= max ? texto : texto[..max];
}

/// <summary>Un renglón del estado de cuenta. Cargo y abono nunca negativos; uno de los dos es 0.</summary>
public class MovimientoBancario : Entity<Guid>
{
    public Guid EstadoDeCuentaId { get; protected set; }

    /// <summary>Repetido aquí para buscar y conciliar sin pasar por el estado de cuenta.</summary>
    public Guid OrganizacionId { get; protected set; }

    /// <summary>El orden en que aparece en el estado de cuenta (el saldo corrido depende de él).</summary>
    public int Orden { get; protected set; }

    public DateTime Fecha { get; protected set; }
    public string Concepto { get; protected set; } = string.Empty;
    public string? Referencia { get; protected set; }
    public decimal Cargo { get; protected set; }
    public decimal Abono { get; protected set; }
    public decimal? Saldo { get; protected set; }

    protected MovimientoBancario()
    {
    }

    public MovimientoBancario(
        Guid id, Guid estadoDeCuentaId, Guid organizacionId, int orden, DateTime fecha, string concepto,
        string? referencia, decimal cargo, decimal abono, decimal? saldo) : base(id)
    {
        EstadoDeCuentaId = estadoDeCuentaId;
        OrganizacionId = organizacionId;
        Orden = orden;
        Referencia = referencia is { Length: > EstadoDeCuentaConsts.MaxReferenciaLength } r ? r[..EstadoDeCuentaConsts.MaxReferenciaLength] : referencia;
        Saldo = saldo;
        Corregir(fecha, concepto, cargo, abono);
    }

    public void Corregir(DateTime fecha, string concepto, decimal cargo, decimal abono)
    {
        if (cargo < 0 || abono < 0)
        {
            throw new BusinessException("AuxiliarContable:MovimientoNegativo");
        }

        Fecha = fecha.Date;
        var c = string.IsNullOrWhiteSpace(concepto) ? "(sin concepto)" : concepto.Trim();
        Concepto = c.Length <= EstadoDeCuentaConsts.MaxConceptoLength ? c : c[..EstadoDeCuentaConsts.MaxConceptoLength];
        Cargo = cargo;
        Abono = abono;
    }
}
