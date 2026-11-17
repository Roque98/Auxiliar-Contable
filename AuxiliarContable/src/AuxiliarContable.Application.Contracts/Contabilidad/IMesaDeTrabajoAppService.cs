using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// La mesa de trabajo del despacho: el mes de un cliente con todas sus operaciones (completas o no) y lo que el
/// auxiliar hace con ellas — analizar con Claude los tickets, notas y comprobantes, y amarrar el banco conciliado.
/// Solo el equipo del despacho; el cliente ajeno = 404.
/// </summary>
public interface IMesaDeTrabajoAppService : IApplicationService
{
    Task<MesaDelMesDto> GetAsync(Guid organizacionId, int anio, int mes);

    /// <summary>
    /// Analiza con Claude hasta <see cref="MesaDeTrabajoConsts.AnalisisPorVuelta"/> documentos del mes que aún no se
    /// analizan (tickets, notas, comprobantes, fotos) y completa su operación con lo leído. Se llama otra vez mientras
    /// <see cref="ResultadoDeAnalisisDto.Faltan"/> sea mayor que cero.
    /// </summary>
    Task<ResultadoDeAnalisisDto> AnalizarAsync(Guid organizacionId, int anio, int mes);

    /// <summary>
    /// Cada movimiento del estado de cuenta conciliado se vuelve una operación y se amarra: por UUID (lo que concilió el
    /// motor), por regla (mismo importe y sentido, fecha cercana, una sola candidata) y, lo que quede, con Claude (lo
    /// seguro se amarra; lo dudoso vuelve como sugerencia).
    /// </summary>
    Task<ResultadoDeAmarreDto> AmarrarBancoAsync(Guid organizacionId, int anio, int mes);
}

public static class MesaDeTrabajoConsts
{
    public const int AnalisisPorVuelta = 3;

    /// <summary>Hasta cuántos días antes del movimiento se busca su operación (un PPD puede pagarse después).</summary>
    public const int DiasHaciaAtras = 60;

    /// <summary>Un pago puede salir unos días antes de que llegue la factura.</summary>
    public const int DiasHaciaAdelante = 5;
}

public class MesaDelMesDto
{
    public OperacionesDelMesDto Operaciones { get; set; } = new();

    /// <summary>Tickets, notas, comprobantes y fotos del mes que Claude aún no lee.</summary>
    public int PorAnalizar { get; set; }

    public int Analizados { get; set; }

    /// <summary>Cuándo se concilió el mes (null = aún no).</summary>
    public DateTime? ConciliadaUtc { get; set; }

    public int MovimientosDelBanco { get; set; }

    /// <summary>Movimientos del banco que ya son operación.</summary>
    public int MovimientosComoOperacion { get; set; }
}

public class ResultadoDeAnalisisDto
{
    public int Analizados { get; set; }
    public int Faltan { get; set; }

    /// <summary>Operaciones que se completaron con lo leído (importe, fecha, UUID, referencia…).</summary>
    public int Completadas { get; set; }

    public List<string> Avisos { get; set; } = [];
}

public class ResultadoDeAmarreDto
{
    public int OperacionesNuevas { get; set; }
    public int PorUuid { get; set; }
    public int PorRegla { get; set; }
    public int PorClaude { get; set; }

    /// <summary>Lo que Claude propone con duda: se acepta con «Juntar».</summary>
    public List<SugerenciaDeAmarreDto> Sugerencias { get; set; } = [];

    public List<string> Avisos { get; set; } = [];
}

public class SugerenciaDeAmarreDto
{
    /// <summary>La operación del movimiento del banco.</summary>
    public Guid MovimientoId { get; set; }

    public string Movimiento { get; set; } = "";

    /// <summary>La operación que se propone (factura, ticket…).</summary>
    public Guid OperacionId { get; set; }

    public string Operacion { get; set; } = "";
    public string Confianza { get; set; } = "";
    public string Porque { get; set; } = "";
}
