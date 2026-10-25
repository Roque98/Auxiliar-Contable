using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Users;

namespace AuxiliarContable.ApiKeys;

/// <summary>
/// Crea, lista y revoca las claves de API del usuario actual.
/// </summary>
[Authorize(AuxiliarContablePermissions.ApiKeys.Default)]
public class ApiKeyAppService : AuxiliarContableAppService, IApiKeyAppService
{
    private readonly IApiKeyRepository _repositorio;

    public ApiKeyAppService(IApiKeyRepository repositorio)
    {
        _repositorio = repositorio;
    }

    public async Task<List<ApiKeyDto>> GetListAsync()
    {
        var ahora = DateTime.UtcNow;
        var claves = await _repositorio.GetByUserAsync(CurrentUser.GetId());

        return claves.Select(k => Mapear(k, ahora)).ToList();
    }

    public async Task<CreatedApiKeyDto> CreateAsync(CreateApiKeyInput input)
    {
        var userId = CurrentUser.GetId();

        var clave = GenerarClave();
        var expira = input.Days.HasValue
            ? DateTime.UtcNow.AddDays(input.Days.Value)
            : (DateTime?)null;

        var entidad = new ApiKey(
            GuidGenerator.Create(),
            userId,
            CurrentTenant.Id,
            input.Name.Trim(),
            Hash(clave),
            clave[..ApiKeyConsts.PistaLength],
            expira);

        await _repositorio.InsertAsync(entidad, autoSave: true);

        return new CreatedApiKeyDto
        {
            Key = Mapear(entidad, DateTime.UtcNow),
            // La ÚNICA vez que la clave sale de aquí. De ella solo queda el hash.
            PlainKey = clave
        };
    }

    public async Task RevokeAsync(Guid id)
    {
        var clave = await _repositorio.GetAsync(id);

        // Se comprueba el dueño: sin esto, cualquiera con el permiso podría revocar las
        // claves de otro con solo adivinar un id.
        if (clave.UserId != CurrentUser.GetId())
        {
            throw new UserFriendlyException("Esa clave no es tuya.");
        }

        // Se marca, no se borra: interesa saber que existió y cuándo dejó de valer.
        clave.Revocar(DateTime.UtcNow);
        await _repositorio.UpdateAsync(clave, autoSave: true);
    }

    private static ApiKeyDto Mapear(ApiKey k, DateTime ahora)
    {
        return new ApiKeyDto
        {
            Id = k.Id,
            Name = k.Name,
            Pista = k.Pista,
            CreationTime = k.CreationTime,
            ExpiresUtc = k.ExpiresUtc,
            LastUsedUtc = k.LastUsedUtc,
            RevokedUtc = k.RevokedUtc,
            IsUsable = k.EsUtilizable(ahora)
        };
    }

    /// <summary>Clave aleatoria con prefijo reconocible, en base64url.</summary>
    private static string GenerarClave()
    {
        var bytes = RandomNumberGenerator.GetBytes(ApiKeyConsts.KeyBytes);

        var cuerpo = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return ApiKeyConsts.Prefijo + cuerpo;
    }

    /// <summary>
    /// SHA-256, y NO bcrypt/PBKDF2 — que sería lo correcto para una contraseña.
    ///
    /// <para>La diferencia está en la entropía de lo que se protege. Una contraseña la
    /// elige una persona y se puede adivinar probando: por eso su hash tiene que ser lento
    /// a propósito. Esto son 256 bits aleatorios; no hay diccionario que la encuentre, y un
    /// hash lento solo serviría para castigar cada petición legítima que traiga clave.</para>
    ///
    /// <para>Lo que sí importa es que el hash sea determinista, porque se busca POR él.</para>
    /// </summary>
    public static string Hash(string clave)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(clave));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
