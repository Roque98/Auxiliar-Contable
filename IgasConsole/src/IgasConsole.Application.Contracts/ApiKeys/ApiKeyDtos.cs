using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace IgasConsole.ApiKeys;

public class ApiKeyDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Primeros caracteres, para reconocerla. Nunca la clave entera.</summary>
    public string Pista { get; set; } = string.Empty;

    public DateTime CreationTime { get; set; }

    public DateTime? ExpiresUtc { get; set; }

    public DateTime? LastUsedUtc { get; set; }

    public DateTime? RevokedUtc { get; set; }

    /// <summary>False si está revocada o caducada. Lo calcula el servidor.</summary>
    public bool IsUsable { get; set; }
}

/// <summary>
/// Respuesta de la creación. Es la ÚNICA vez que se devuelve la clave en claro.
/// </summary>
public class CreatedApiKeyDto
{
    public ApiKeyDto Key { get; set; } = new();

    /// <summary>
    /// La clave completa. <b>No se guarda y no se puede volver a consultar</b>: de ella
    /// solo queda el hash. Si se pierde, se revoca y se crea otra.
    /// </summary>
    public string PlainKey { get; set; } = string.Empty;
}

public class CreateApiKeyInput
{
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Días de validez. Null = no caduca.</summary>
    [Range(1, 3650)]
    public int? Days { get; set; }
}

/// <summary>
/// Claves de API del USUARIO ACTUAL. Cada uno gestiona las suyas y solo las suyas: el id
/// de usuario sale de la sesión, nunca de la petición.
/// <para>Lleva permiso propio (<c>IgasConsole.ApiKeys</c>) aunque sea sobre lo propio, al
/// revés que la foto de perfil: una clave permite llamar a la API sin navegador y sin
/// segundo factor, así que quién puede crearlas debería poder decidirse.</para>
/// </summary>
public interface IApiKeyAppService : IApplicationService
{
    Task<List<ApiKeyDto>> GetListAsync();

    Task<CreatedApiKeyDto> CreateAsync(CreateApiKeyInput input);

    Task RevokeAsync(Guid id);
}
