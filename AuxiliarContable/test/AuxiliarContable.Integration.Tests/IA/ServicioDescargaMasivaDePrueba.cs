using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.Contabilidad;

namespace AuxiliarContable;

/// <summary>
/// La Descarga Masiva de los tests: acepta cada solicitud con un id nuevo, dice el avance que programe el test y da los
/// paquetes (.zip) que el test ponga. Apunta qué se le pidió y con qué RFC.
/// </summary>
public class ServicioDescargaMasivaDePrueba : IServicioDescargaMasiva
{
    public bool EstaConfigurado => true;

    public bool Caido { get; set; }

    /// <summary>Código con que contesta la solicitud (5000 aceptada, 5004 sin información…).</summary>
    public string CodigoDeSolicitud { get; set; } = "5000";

    /// <summary>EstadoSolicitud que contesta la verificación: 2 en proceso, 3 terminada.</summary>
    public int Estado { get; set; } = 2;

    /// <summary>Paquetes por id; la verificación terminada los entrega todos.</summary>
    public Dictionary<string, byte[]> Paquetes { get; } = [];

    public List<(string Rfc, PedidoDeDescargaMasiva Pedido)> Pedidos { get; } = [];

    public int Autenticaciones { get; private set; }

    public void Reiniciar()
    {
        Caido = false;
        CodigoDeSolicitud = "5000";
        Estado = 2;
        Paquetes.Clear();
        Pedidos.Clear();
        Autenticaciones = 0;
    }

    private void Exigir()
    {
        if (Caido)
        {
            throw new DescargaMasivaNoDisponibleException("El SAT no responde.");
        }
    }

    public Task<string> AutenticarAsync(EfirmaAbierta efirma, CancellationToken cancellationToken = default)
    {
        Exigir();
        Autenticaciones++;
        return Task.FromResult("token-de-prueba");
    }

    public Task<SolicitudAceptada> SolicitarAsync(string token, EfirmaAbierta efirma, PedidoDeDescargaMasiva pedido, CancellationToken cancellationToken = default)
    {
        Exigir();
        Pedidos.Add((efirma.Rfc, pedido));
        return Task.FromResult(new SolicitudAceptada(CodigoDeSolicitud == "5000" ? $"SOL-{Pedidos.Count}" : null, CodigoDeSolicitud, "Solicitud Aceptada"));
    }

    public Task<AvanceDeSolicitud> VerificarAsync(string token, EfirmaAbierta efirma, string idSolicitud, CancellationToken cancellationToken = default)
    {
        Exigir();
        var paquetes = Estado == 3 ? new List<string>(Paquetes.Keys) : [];
        return Task.FromResult(new AvanceDeSolicitud("5000", Estado, "5000", paquetes.Count, "Solicitud Aceptada", paquetes));
    }

    public Task<PaqueteDelSat> DescargarAsync(string token, EfirmaAbierta efirma, string idPaquete, CancellationToken cancellationToken = default)
    {
        Exigir();
        return Task.FromResult(new PaqueteDelSat("5000", "Solicitud Aceptada", Paquetes.GetValueOrDefault(idPaquete)));
    }
}
