using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AuxiliarContable.MotorFiscal;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Las facturas del cliente que se bajan del portal del SAT con el Descargador 1 (en la computadora del despacho: alguien
/// inicia sesión con el captcha) y el pago provisional de ISR e IVA solo con ellas. Sirve sobre todo cuando el cliente no
/// entregó nada: se marca el mes, se baja una ORDEN para el Descargador 1 (meses, emitidas y/o recibidas, los UUID que ya
/// están bien aquí), el robot deja un PAQUETE (.zip) y al subirlo solo entra lo que faltaba, los XML dañados se cambian
/// por el bueno y cada factura queda con su estatus del SAT (vigente o cancelada). Solo el equipo del despacho.
/// </summary>
public interface IDescargaDelSatAppService : IApplicationService
{
    /// <summary>Del ejercicio hasta el mes: cuántas facturas, cuántas canceladas, sin estatus o con el XML dañado.</summary>
    Task<DescargaDelSatDto> GetAsync(Guid organizacionId, int anio, int mes);

    /// <summary>El cliente no entregó nada ese mes (o sí, al quitar la marca).</summary>
    Task MarcarSinEntregaAsync(Guid organizacionId, int anio, int mes, bool sinEntrega);

    /// <summary>La orden (.json) para el Descargador 1: «Orden de la plataforma…» en su ventana o <c>--orden</c>.</summary>
    Task<OrdenDeDescargaDto> GetOrdenAsync(Guid organizacionId, int desdeAnio, int desdeMes, int hastaAnio, int hastaMes, bool emitidas, bool recibidas);

    /// <summary>La orden por días (un día, una semana… hasta un año): emitidas de corrido; recibidas, día por día.</summary>
    Task<OrdenDeDescargaDto> GetOrdenPorDiasAsync(Guid organizacionId, DateTime desde, DateTime hasta, bool emitidas, bool recibidas);

    /// <summary>La orden por folio fiscal (UUID): para revisar su estatus o bajar unas facturas en particular.</summary>
    Task<OrdenDeDescargaDto> CrearOrdenPorFoliosAsync(Guid organizacionId, OrdenPorFoliosDto input);

    /// <summary>
    /// Pregunta ya al SAT (servicio público de verificación) el estatus de las facturas del mes, salvo las canceladas.
    /// Lo mismo corre solo cada noche para todos los clientes.
    /// </summary>
    Task<RevisionDeEstatusDto> RevisarEstatusAsync(Guid organizacionId, int anio, int mes);

    /// <summary>El paquete (.zip) que dejó el Descargador 1.</summary>
    Task<ResultadoDelPaqueteDto> SubirPaqueteAsync(Guid organizacionId, IRemoteStreamContent paquete);

    /// <summary>Pago provisional de ISR e IVA del mes solo con las facturas (las canceladas en el SAT no cuentan).</summary>
    Task<ImpuestosDelMesDto> CalcularImpuestosAsync(Guid organizacionId, int anio, int mes);

    /// <summary>
    /// La Descarga Masiva con la e.firma del cliente (servicio oficial del SAT, sin captcha ni robot): si se puede usar y
    /// las últimas solicitudes con su avance.
    /// </summary>
    Task<DescargaMasivaDto> GetDescargaMasivaAsync(Guid organizacionId);

    /// <summary>Pide ya al SAT las emitidas y/o recibidas de un mes. El SAT las arma en minutos u horas; se bajan solas.</summary>
    Task<DescargaMasivaDto> SolicitarDescargaMasivaAsync(Guid organizacionId, SolicitarDescargaMasivaDto input);

    /// <summary>Pregunta ya al SAT por las solicitudes en proceso del cliente y baja lo que esté listo.</summary>
    Task<DescargaMasivaDto> AvanzarDescargaMasivaAsync(Guid organizacionId);
}

public class SolicitarDescargaMasivaDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public bool Emitidas { get; set; } = true;
    public bool Recibidas { get; set; } = true;
}

public class DescargaMasivaDto
{
    /// <summary>El servidor tiene la descarga masiva encendida.</summary>
    public bool Disponible { get; set; }

    /// <summary>Por qué no se puede usar la e.firma (null si sí se puede).</summary>
    public string? Problema { get; set; }

    public DateTime? EfirmaVigenteHastaUtc { get; set; }

    /// <summary>Lo que pasó al avanzar a mano (p. ej. «el SAT no respondió»).</summary>
    public string? Aviso { get; set; }

    public List<SolicitudDeDescargaMasivaDto> Solicitudes { get; set; } = [];
}

public class SolicitudDeDescargaMasivaDto
{
    public Guid Id { get; set; }
    public int Anio { get; set; }
    public int Mes { get; set; }
    public bool Emitidas { get; set; }
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public bool Automatica { get; set; }

    /// <summary>EnProceso, Descargada, SinFacturas o Rechazada.</summary>
    public string Estado { get; set; } = "";

    public string? Mensaje { get; set; }
    public int NumeroCfdis { get; set; }
    public int Paquetes { get; set; }
    public int PaquetesBajados { get; set; }
    public int Nuevas { get; set; }
    public int Repetidas { get; set; }
    public int Rechazadas { get; set; }
    public DateTime SolicitadaUtc { get; set; }
    public DateTime? RevisadaUtc { get; set; }
    public DateTime? TerminadaUtc { get; set; }
}

public class DescargaDelSatDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public bool SinEntregaDelCliente { get; set; }
    public bool TieneRfc { get; set; }

    /// <summary>De enero al mes.</summary>
    public List<FacturasDelSatDelMesDto> Meses { get; set; } = [];

    public List<FacturaDanadaDto> Danadas { get; set; } = [];

    /// <summary>Cuándo se subió el último estatus del SAT (null = nunca).</summary>
    public DateTime? UltimoEstatusUtc { get; set; }
}

public class FacturasDelSatDelMesDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public int Facturas { get; set; }
    public int Canceladas { get; set; }

    /// <summary>Vigentes con una solicitud de cancelación pendiente (el receptor aún no responde).</summary>
    public int EnProceso { get; set; }

    /// <summary>Con UUID pero sin estatus del SAT todavía.</summary>
    public int SinEstatus { get; set; }

    /// <summary>El SAT dice que no existen (recién emitidas aún no registradas, o apócrifas): hay que revisarlas.</summary>
    public int NoEncontradas { get; set; }

    public int Danadas { get; set; }
    public bool SinEntregaDelCliente { get; set; }
}

public class FacturaDanadaDto
{
    public Guid DocumentoId { get; set; }
    public string Nombre { get; set; } = "";
    public DateTime? Fecha { get; set; }
}

public class RevisionDeEstatusDto
{
    public int Revisadas { get; set; }
    public int Cambiaron { get; set; }
    public int Canceladas { get; set; }
    public int EnProceso { get; set; }
    public int NoEncontradas { get; set; }
    public int Fallaron { get; set; }
    public int Faltan { get; set; }
    public bool SatSinResponder { get; set; }
}

public class OrdenPorFoliosDto
{
    /// <summary>Lo que se pegó: cada texto puede traer uno o varios UUID, separados por lo que sea.</summary>
    public List<string> Folios { get; set; } = [];

    public bool Emitidas { get; set; } = true;
    public bool Recibidas { get; set; } = true;
}

public class OrdenDeDescargaDto
{
    public string NombreDeArchivo { get; set; } = "";

    /// <summary>El JSON de la orden, tal cual se guarda.</summary>
    public string Contenido { get; set; } = "";

    public int Tengo { get; set; }
    public int Danadas { get; set; }
}

public class ResultadoDelPaqueteDto
{
    public string Desde { get; set; } = "";
    public string Hasta { get; set; } = "";

    /// <summary>Facturas que no estaban y entraron.</summary>
    public int Nuevas { get; set; }

    /// <summary>Ya estaban (por UUID): no se duplican.</summary>
    public int Repetidas { get; set; }

    /// <summary>XML dañados que se cambiaron por el bueno del SAT.</summary>
    public int Reemplazadas { get; set; }

    public int EnMesCerrado { get; set; }

    /// <summary>No abren, no son del RFC del cliente o no traen su UUID.</summary>
    public int Rechazadas { get; set; }

    public int EstatusPuestos { get; set; }
    public int Canceladas { get; set; }
    public int EnProceso { get; set; }

    /// <summary>En el SAT, pero el portal no dio el XML (cancelados, o falla del portal).</summary>
    public int SinXmlEnElSat { get; set; }

    public List<string> Avisos { get; set; } = [];
}

public class ImpuestosDelMesDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public bool SinEntregaDelCliente { get; set; }
    public int FacturasEnviadas { get; set; }
    public int Canceladas { get; set; }
    public int Danadas { get; set; }
    public int SinEstatus { get; set; }
    public PagosProvisionales Resultado { get; set; } = new();
}
