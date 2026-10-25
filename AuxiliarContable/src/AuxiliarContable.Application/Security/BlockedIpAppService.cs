using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AuxiliarContable.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Auditing;

namespace AuxiliarContable.Security;

/// <summary>
/// Administra las IPs vetadas a mano.
/// <para>Complementa al rate limiting, no lo sustituye: aquel cuenta peticiones por
/// ventana y vive en memoria —se reinicia en cada despliegue—, esto es una decisión que
/// persiste.</para>
/// </summary>
// Ver la lista es el permiso base; bloquear y desbloquear piden el hijo, declarado
// metodo a metodo. Consultar quien esta vetado y decidir quien lo esta no son la misma
// responsabilidad, y con un solo permiso no se podia dar la primera sin la segunda.
[Authorize(AuxiliarContablePermissions.Security.BlockedIps)]
public class BlockedIpAppService : AuxiliarContableAppService, IBlockedIpAppService
{
    /// <summary>Tope del listado. La lista no debería crecer tanto; si lo hace, el
    /// problema es otro y no se arregla paginando un panel.</summary>
    private const int MaxFilas = 500;

    private readonly IBlockedIpRepository _repositorio;
    private readonly IBlockedIpCache _cache;
    private readonly ICallerIpProvider _infoCliente;

    public BlockedIpAppService(
        IBlockedIpRepository repositorio,
        IBlockedIpCache cache,
        ICallerIpProvider infoCliente)
    {
        _repositorio = repositorio;
        _cache = cache;
        _infoCliente = infoCliente;
    }

    public async Task<List<BlockedIpDto>> GetListAsync()
    {
        var ahora = DateTime.UtcNow;
        var filas = await _repositorio.GetAllAsync(MaxFilas);

        return filas.Select(b => new BlockedIpDto
        {
            Id = b.Id,
            IpAddress = b.IpAddress,
            Reason = b.Reason,
            CreationTime = b.CreationTime,
            ExpiresUtc = b.ExpiresUtc,
            IsAutomatic = b.IsAutomatic,
            IsActive = b.EstaVigente(ahora)
        }).ToList();
    }

    [Authorize(AuxiliarContablePermissions.Security.ManageBlockedIps)]
    public async Task<BlockedIpDto> BlockAsync(BlockIpInput input)
    {
        var ip = (input.IpAddress ?? string.Empty).Trim();

        if (!IPAddress.TryParse(ip, out _))
        {
            // Se valida el formato antes de guardarlo. Una cadena que no es una IP nunca
            // coincidiría con nadie: quedaría en la tabla dando una sensación de protección
            // que no existe.
            throw new UserFriendlyException($"«{ip}» no es una dirección IP válida.");
        }

        // ⚠️ EL GUARDARRAÍL QUE IMPORTA: no dejar que alguien se bloquee a sí mismo.
        // Este panel se administra por la misma web que se está protegiendo, así que
        // bloquear la propia IP te deja fuera de la herramienta con la que desbloquearías
        // — y la única salida sería entrar a la base a mano.
        var propia = _infoCliente.IpActual;
        if (!propia.IsNullOrWhiteSpace() && string.Equals(propia, ip, StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFriendlyException(
                "Esa es tu propia dirección. Si la bloquearas, perderías el acceso al panel " +
                "desde el que se desbloquea.");
        }

        var existente = await _repositorio.FindByIpAsync(ip);
        if (existente != null)
        {
            throw new UserFriendlyException($"La IP {ip} ya está en la lista.");
        }

        var expira = input.Hours.HasValue
            ? DateTime.UtcNow.AddHours(input.Hours.Value)
            : (DateTime?)null;

        var bloqueo = new BlockedIp(GuidGenerator.Create(), ip, input.Reason.Trim(), expira);
        await _repositorio.InsertAsync(bloqueo, autoSave: true);

        // Al instante, sin esperar al refresco periódico: si no, bloquear una IP y verla
        // seguir entrando durante medio minuto parece que el botón no ha hecho nada.
        await _cache.RefrescarAsync();

        return new BlockedIpDto
        {
            Id = bloqueo.Id,
            IpAddress = bloqueo.IpAddress,
            Reason = bloqueo.Reason,
            CreationTime = bloqueo.CreationTime,
            ExpiresUtc = bloqueo.ExpiresUtc,
            IsAutomatic = false,
            IsActive = true
        };
    }

    [Authorize(AuxiliarContablePermissions.Security.ManageBlockedIps)]
    public async Task UnblockAsync(Guid id)
    {
        // Borrado de verdad, no soft delete: una fila oculta obligaría al middleware a
        // aprender a ignorarla, y ese es justo el tipo de detalle que se olvida.
        await _repositorio.DeleteAsync(id, autoSave: true);
        await _cache.RefrescarAsync();
    }
}
