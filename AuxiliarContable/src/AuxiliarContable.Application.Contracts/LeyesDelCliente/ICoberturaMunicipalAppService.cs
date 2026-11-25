using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.LeyesDelCliente;

/// <summary>
/// Mapa de control del despacho (pedidos del 22 y 25-nov-2026): qué leyes estatales y qué reglamentos municipales tenemos de
/// cada estado y municipio, medidos contra el catálogo completo (tenemos / no aplica y por qué / no encontrado / sin revisar);
/// qué municipios no se eligieron y por qué; y qué clientes están en un municipio que no cubrimos (de ellos solo sabemos lo
/// federal y lo estatal). Solo el equipo del despacho.
/// </summary>
public interface ICoberturaMunicipalAppService : IApplicationService
{
    Task<CoberturaMunicipalDto> GetReporteAsync();

    /// <summary>El expediente de un estado: su ficha estatal y cada municipio con ficha, norma por norma.</summary>
    Task<ExpedienteDeEstadoDto> GetExpedienteAsync(string estado);

    /// <summary>Los municipios de todos los clientes («EDO/municipio», la carpeta del vigía municipal) para
    /// <c>_vigia/prioridad.txt</c>: la revisión mensual se enfoca en ellos. Sin nombres ni domicilios.</summary>
    Task<PrioridadDelVigiaDto> GetPrioridadDelVigiaAsync();
}

public class CoberturaMunicipalDto
{
    public string? Criterio { get; set; }
    public int Cubiertos { get; set; }

    /// <summary>Con algunos reglamentos (ley de ingresos, bando…), no todos.</summary>
    public int Parciales { get; set; }

    /// <summary>Se revisaron y no publican nada en línea (portal caído, bloqueado o sin reglamentos).</summary>
    public int SinFuente { get; set; }

    /// <summary>Mínimo de reglamentos propios para contar un municipio como cubierto.</summary>
    public int MinimoCubierto { get; set; }

    public int EnProceso { get; set; }
    public int NoCubiertos { get; set; }

    /// <summary>Municipios elegidos por el criterio (los demás, en <see cref="CoberturaDeEstadoDto.NoElegidos"/> con su motivo).</summary>
    public int Elegidos { get; set; }

    /// <summary>Municipios que ya tienen todo lo que les aplica (nada sin revisar ni sin encontrar).</summary>
    public int Completos { get; set; }

    /// <summary>Normas del catálogo: 40 municipales y 72 estatales.</summary>
    public int UniversoMunicipal { get; set; }

    public int UniversoEstatal { get; set; }

    /// <summary>Texto de cada motivo de <see cref="MunicipioFueraDto.Motivo"/>.</summary>
    public Dictionary<string, string> MotivosFuera { get; set; } = [];

    /// <summary>Clientes cuyo municipio no está cubierto (o no se supo): primero lo que el despacho debe atender.</summary>
    public List<ClienteSinCoberturaDto> Clientes { get; set; } = [];

    public List<CoberturaDeEstadoDto> Estados { get; set; } = [];

    /// <summary>Si el motor no respondió.</summary>
    public string? Aviso { get; set; }
}

public class ClienteSinCoberturaDto
{
    public Guid OrganizacionId { get; set; }
    public string Nombre { get; set; } = "";
    public string? Estado { get; set; }
    public string? Municipio { get; set; }

    /// <summary>parcial, sin_fuente, seleccionado (en proceso), no_cubierto o sin_municipio (no se supo por el domicilio).</summary>
    public string Cobertura { get; set; } = "";
}

public class CoberturaDeEstadoDto
{
    public string Estado { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int Municipios { get; set; }
    public int Elegidos { get; set; }
    public double? PoblacionCubiertaPct { get; set; }
    public double? PoblacionSeleccionadaPct { get; set; }
    public List<string> Cubiertos { get; set; } = [];
    public List<string> Parciales { get; set; } = [];
    public List<string> SinFuente { get; set; } = [];
    public List<string> EnProceso { get; set; } = [];
    public List<string> NoCubiertos { get; set; } = [];

    /// <summary>La ficha estatal contra las 72 leyes del catálogo.</summary>
    public BalanceDto Estatal { get; set; } = new();

    /// <summary>Los municipios con ficha (elegidos), de mayor a menor población, con su balance.</summary>
    public List<MunicipioDeCoberturaDto> ConFicha { get; set; } = [];

    /// <summary>Los que no se eligieron, con el motivo (clave de <see cref="CoberturaMunicipalDto.MotivosFuera"/>).</summary>
    public List<MunicipioFueraDto> NoElegidos { get; set; } = [];
}

/// <summary>Cómo se reparte el catálogo en una ficha.</summary>
public class BalanceDto
{
    public int Universo { get; set; }
    public int Tenemos { get; set; }
    public int NoAplica { get; set; }

    /// <summary>A) lo regula el estado, la federación o el municipio.</summary>
    public int NoAplicaPorOtraAutoridad { get; set; }

    /// <summary>B) no existe en ese lugar o la cubre otra norma.</summary>
    public int NoAplicaPorNoExistir { get; set; }

    /// <summary>C) debería existir y no se encontró el texto oficial.</summary>
    public int NoEncontrado { get; set; }

    public int SinRevisar { get; set; }
    public double AvancePct { get; set; }
    public bool Completo { get; set; }
    public string? Revisado { get; set; }
}

public class MunicipioDeCoberturaDto : BalanceDto
{
    public string Cvegeo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int Poblacion { get; set; }

    /// <summary>cubierto, parcial o sin_fuente.</summary>
    public string Nivel { get; set; } = "";
}

public class MunicipioFueraDto
{
    public string Nombre { get; set; } = "";
    public int Poblacion { get; set; }
    public string Motivo { get; set; } = "";
}

public class ExpedienteDeEstadoDto
{
    public string Estado { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? Revisado { get; set; }
    public BalanceConNormasDto Estatal { get; set; } = new();
    public List<MunicipioConNormasDto> Municipios { get; set; } = [];
}

public class BalanceConNormasDto : BalanceDto
{
    public List<NormaDeCoberturaDto> Normas { get; set; } = [];
}

public class MunicipioConNormasDto : BalanceConNormasDto
{
    public string Cvegeo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int Poblacion { get; set; }
    public string? Nivel { get; set; }
}

/// <summary>Una norma del catálogo en una ficha: en qué clase cae y por qué.</summary>
public class NormaDeCoberturaDto
{
    public string Id { get; set; } = "";
    public string? Nombre { get; set; }
    public string? Bloque { get; set; }

    /// <summary>tenemos, no_aplica, no_encontrado o sin_revisar.</summary>
    public string Clase { get; set; } = "";

    /// <summary>«A) Lo regula el estado», «B) No existe…», «C) No se encontró el texto oficial».</summary>
    public string? Motivo { get; set; }

    /// <summary>El nombre real de la norma en ese lugar.</summary>
    public string? Titulo { get; set; }

    public string? Nota { get; set; }
    public string? UltimaReforma { get; set; }
    public string? Url { get; set; }

    /// <summary>Sin reglamento propio: qué otros reglamentos del lugar tratan el tema.</summary>
    public List<ReglamentoQueLoTrataDto> LoTratan { get; set; } = [];
}

public class PrioridadDelVigiaDto
{
    /// <summary>«EDO/municipio», sin repetir, en orden.</summary>
    public List<string> Municipios { get; set; } = [];

    /// <summary>Clientes de los que no se pudo saber el municipio por su domicilio.</summary>
    public int SinMunicipio { get; set; }

    public string? Aviso { get; set; }
}
