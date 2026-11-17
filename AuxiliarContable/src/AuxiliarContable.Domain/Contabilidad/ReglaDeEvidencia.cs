using System.Collections.Generic;
using System.Linq;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Qué le falta a una operación para estar documentada (control interno). Solo se exige en las SALIDAS de dinero que
/// no son traspasos entre cuentas propias: es lo que se deduce y lo que el SAT revisa.
///
/// <list type="bullet">
/// <item>Con factura: falta probar que ocurrió (contrato, entregable o foto, pedido o cotización): la jurisprudencia del
/// TFJA (p. ej. IX-J-2aS-115) dice que el registro contable y la factura solos no acreditan la materialidad.</item>
/// <item>Sin factura: falta cualquier comprobante o el testimonio del cliente de qué fue (no será deducible, pero hay
/// que saber qué fue esa salida de dinero).</item>
/// </list>
/// </summary>
public static class ReglaDeEvidencia
{
    public static EstadoDeEvidencia Estado(bool esSalida, bool esTraspaso, bool tieneFactura,
        IEnumerable<EvidenciaDeOperacion> evidencias, bool explicadaEnLaConciliacion = false)
    {
        if (!esSalida || esTraspaso)
        {
            return EstadoDeEvidencia.NoAplica;
        }

        var lista = evidencias.ToList();
        if (tieneFactura)
        {
            return lista.Any(e => e.ProbaraMaterialidad) ? EstadoDeEvidencia.Completa : EstadoDeEvidencia.FaltaMaterialidad;
        }

        return lista.Count > 0 || explicadaEnLaConciliacion ? EstadoDeEvidencia.Completa : EstadoDeEvidencia.FaltaComprobante;
    }
}
