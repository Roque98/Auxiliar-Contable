using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AuxiliarContable.MotorFiscal;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Reconoce un movimiento del banco entre una conciliación y la siguiente. El motor no les pone id,
/// así que la clave sale de lo que el banco imprime (cuenta, fecha, cargo o abono, importe,
/// referencias y concepto). Dos movimientos idénticos el mismo día se distinguen por su orden de
/// aparición: el segundo lleva «#2».
/// </summary>
public static class ClaveDeMovimiento
{
    public static List<string> De(IReadOnlyList<MovimientoLeido> movimientos)
    {
        var vistos = new Dictionary<string, int>();
        var claves = new List<string>(movimientos.Count);
        foreach (var m in movimientos)
        {
            var texto = string.Join('|',
                Normalizar(m.Cuenta), Normalizar(m.Fecha), Normalizar(m.Naturaleza),
                m.Importe.ToString("0.00", CultureInfo.InvariantCulture),
                Normalizar(m.Referencia), Normalizar(m.Rastreo), Normalizar(m.Concepto));
            var vez = vistos.GetValueOrDefault(texto) + 1;
            vistos[texto] = vez;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(vez == 1 ? texto : $"{texto}#{vez}"));
            claves.Add(Convert.ToHexString(hash)[..ContabilidadConsts.ClaveLength].ToLowerInvariant());
        }

        return claves;
    }

    /// <summary>«02/11/2026 · SPEI RECIBIDO ACME · abono $11,600.00», para enseñarlo.</summary>
    public static string Describir(MovimientoLeido m)
    {
        var importe = m.Importe.ToString("C2", CultureInfo.GetCultureInfo("es-MX"));
        return $"{m.Fecha} · {m.Concepto.Trim()} · {m.Naturaleza} {importe}";
    }

    private static string Normalizar(string? texto) =>
        string.Join(' ', (texto ?? string.Empty).Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
