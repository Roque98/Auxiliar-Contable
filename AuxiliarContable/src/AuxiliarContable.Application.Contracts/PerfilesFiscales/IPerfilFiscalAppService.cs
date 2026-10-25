using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using AuxiliarContable.Documentos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AuxiliarContable.PerfilesFiscales;

/// <summary>
/// El perfil fiscal de un cliente (organización). Lo ve quien puede ver la organización; lo
/// cambia (sube la CSF, confirma, corrige) quien puede subir en ella. El equipo del despacho,
/// en todas.
/// </summary>
public interface IPerfilFiscalAppService : IApplicationService
{
    /// <summary>Los clientes cuyo perfil puede ver quien pregunta: todos para el despacho, los suyos para un cliente.</summary>
    Task<List<ClienteVisibleDto>> GetClientesAsync();

    Task<PerfilFiscalDto> GetAsync(Guid organizacionId);

    /// <summary>Sube la Constancia de Situación Fiscal (PDF). Se lee en segundo plano: el estado sale en <see cref="PerfilFiscalDto.Csf"/>.</summary>
    Task<PerfilFiscalDto> SubirCsfAsync(Guid organizacionId, IRemoteStreamContent archivo);

    /// <summary>«Sí, es correcto»: da por bueno un dato extraído o inferido.</summary>
    Task<PerfilFiscalDto> ConfirmarAsync(Guid organizacionId, string campo);

    /// <summary>Confirma de una vez todo lo extraído de la constancia que siga sin confirmar (no lo inferido).</summary>
    Task<PerfilFiscalDto> ConfirmarConstanciaAsync(Guid organizacionId);

    /// <summary>Escribe o corrige un dato. Queda confirmado y con el origen de quien lo dice.</summary>
    Task<PerfilFiscalDto> ResponderAsync(Guid organizacionId, ResponderDatoDelPerfilDto input);
}

public class ClienteVisibleDto
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public bool PuedeEditar { get; set; }
}

public class ResponderDatoDelPerfilDto
{
    [Required]
    [StringLength(64)]
    public string Campo { get; set; } = string.Empty;

    [Required]
    [StringLength(CamposDelPerfil.MaxValorLength)]
    public string Valor { get; set; } = string.Empty;
}

public class PerfilFiscalDto
{
    public Guid OrganizacionId { get; set; }

    /// <summary>¿Puede subir, confirmar y corregir? (El papel «Solo ve» no.)</summary>
    public bool PuedeEditar { get; set; }

    /// <summary>Todos los campos del catálogo, en orden, con su valor si lo hay.</summary>
    public List<DatoDelPerfilDto> Campos { get; set; } = [];

    /// <summary>La última constancia subida, o null si nunca se subió.</summary>
    public CsfDelPerfilDto? Csf { get; set; }
}

public class DatoDelPerfilDto
{
    public string Clave { get; set; } = string.Empty;
    public string Etiqueta { get; set; } = string.Empty;
    public GrupoDeCampos Grupo { get; set; }
    public TipoDeCampo Tipo { get; set; }
    public bool VieneEnLaCsf { get; set; }

    public string? Valor { get; set; }
    public OrigenDelDato? Origen { get; set; }
    public bool Confirmado { get; set; }
    public Guid? DocumentoId { get; set; }
    public DateTime? ActualizadoUtc { get; set; }
}

public class CsfDelPerfilDto
{
    public Guid DocumentoId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public DateTime SubidaUtc { get; set; }
    public EstadoDeDocumento Estado { get; set; }
    public string? Motivo { get; set; }
    public List<string> Avisos { get; set; } = [];
}
