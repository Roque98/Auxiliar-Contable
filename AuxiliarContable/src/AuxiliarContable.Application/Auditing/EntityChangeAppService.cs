using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Auditing;

namespace AuxiliarContable.Auditing;

/// <summary>
/// Historial de cambios de entidades. Solo lee: lo escribe la auditoría de ABP.
/// <para>Mismo permiso que el registro de peticiones: las dos cosas son el rastro de la
/// actividad de personas concretas, y quien puede ver una debería poder ver la otra.</para>
/// </summary>
[Authorize(AuxiliarContablePermissions.AuditLogs.Default)]
[DisableAuditing]
public class EntityChangeAppService : AuxiliarContableAppService, IEntityChangeAppService
{
    private readonly IEntityChangeRepository _cambios;

    public EntityChangeAppService(IEntityChangeRepository cambios)
    {
        _cambios = cambios;
    }

    public async Task<PagedResultDto<EntityChangeDto>> GetListAsync(GetEntityChangesInput input)
    {
        var desde = DateTime.UtcNow.AddDays(-input.Days);

        var filas = await _cambios.GetRecentAsync(
            desde,
            input.MaxResultCount,
            input.EntityTypeFullName,
            input.EntityId,
            input.UserName);

        var total = await _cambios.GetCountAsync(
            desde, input.EntityTypeFullName, input.EntityId, input.UserName);

        return new PagedResultDto<EntityChangeDto>(total, filas.Select(Mapear).ToList());
    }

    public async Task<EntityChangeFiltersDto> GetFiltersAsync()
    {
        return new EntityChangeFiltersDto
        {
            EntityTypes = await _cambios.GetTrackedTypesAsync()
        };
    }

    private static EntityChangeDto Mapear(EntityChangeEntry e)
    {
        return new EntityChangeDto
        {
            Id = e.Id,
            ChangeTime = e.ChangeTime,
            ChangeType = e.ChangeType,
            EntityTypeFullName = e.EntityTypeFullName,
            // "AuxiliarContable.Products.Product" -> "Product". El nombre completo hace falta
            // para filtrar, pero en pantalla solo estorba.
            EntityName = NombreCorto(e.EntityTypeFullName),
            EntityId = e.EntityId,
            UserName = e.UserName,
            ClientIpAddress = e.ClientIpAddress,
            Properties = e.Properties.Select(p => new PropertyChangeDto
            {
                PropertyName = p.PropertyName,
                OriginalValue = Legible(p.OriginalValue),
                NewValue = Legible(p.NewValue)
            }).ToList()
        };
    }

    /// <summary>
    /// ABP guarda los valores serializados en JSON, así que un texto llega entre comillas
    /// (<c>"Teclado"</c>) y un nulo llega como la cadena <c>"null"</c>.
    ///
    /// <para>Se limpia AQUÍ y no en la pantalla porque es una peculiaridad de cómo se
    /// almacena, no una decisión de presentación: cualquier cliente de la API se
    /// encontraría lo mismo y tendría que repetir el apaño.</para>
    /// </summary>
    private static string? Legible(string? valor)
    {
        if (string.IsNullOrEmpty(valor) || valor == "null")
        {
            return null;
        }

        // Solo se quitan las comillas de un texto JSON; un número, un booleano o un objeto
        // se dejan tal cual.
        if (valor.Length >= 2 && valor[0] == '"' && valor[^1] == '"')
        {
            return valor[1..^1].Replace("\\\"", "\"");
        }

        return valor;
    }

    private static string NombreCorto(string nombreCompleto)
    {
        if (string.IsNullOrWhiteSpace(nombreCompleto))
        {
            return string.Empty;
        }

        var punto = nombreCompleto.LastIndexOf('.');
        return punto >= 0 && punto < nombreCompleto.Length - 1
            ? nombreCompleto[(punto + 1)..]
            : nombreCompleto;
    }
}
