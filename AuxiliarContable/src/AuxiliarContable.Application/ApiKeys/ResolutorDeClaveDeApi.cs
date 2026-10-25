using System;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;

namespace AuxiliarContable.ApiKeys;

/// <summary>Una clave que sí sirve, con todo lo que hace falta para autenticar con ella.</summary>
public sealed record ClaveResuelta(ApiKey Registro, IdentityUser Usuario);

/// <summary>
/// Decide si una clave de API puede autenticar ahora mismo, y a quién representa.
///
/// <para>Esta regla vivía dentro de <c>ApiKeyMiddleware</c>. Se sacó de ahí por una razón muy
/// concreta: el middleware está en el Host, y los tests de integración <b>no cargan el
/// Host</b> — así que la decisión más sensible del proyecto era justo la que no se podía
/// probar. Con esto el middleware se queda con lo suyo (leer la cabecera, construir el
/// principal, escribir el 401) y la regla se comprueba de verdad.</para>
///
/// <para><b>El orden de las comprobaciones importa</b> y va de lo más barato a lo más caro:
/// primero la fila, luego su estado, luego el inquilino (caché) y por último el usuario
/// (base). Quien pruebe claves a ciegas no llega a costar una consulta de usuario.</para>
/// </summary>
public class ResolutorDeClaveDeApi : ITransientDependency
{
    private readonly IApiKeyRepository _claves;
    private readonly IdentityUserManager _usuarios;
    private readonly ICurrentTenant _inquilinoActual;

    public ResolutorDeClaveDeApi(
        IApiKeyRepository claves,
        IdentityUserManager usuarios,
        ICurrentTenant inquilinoActual)
    {
        _claves = claves;
        _usuarios = usuarios;
        _inquilinoActual = inquilinoActual;
    }

    /// <summary>
    /// <c>null</c> si la clave no sirve, por el motivo que sea. Quien llama responde
    /// <b>lo mismo</b> en todos los casos: distinguir «no existe» de «revocada» le diría a
    /// quien prueba claves si acertó con una que existió alguna vez.
    /// </summary>
    public async Task<ClaveResuelta?> ResolverAsync(string claveEnClaro, DateTime ahoraUtc)
    {
        if (string.IsNullOrWhiteSpace(claveEnClaro))
        {
            return null;
        }

        var registro = await _claves.FindByHashAsync(ApiKeyAppService.Hash(claveEnClaro));

        if (registro == null || !registro.EsUtilizable(ahoraUtc))
        {
            return null;
        }

        // El inquilino se restaura ANTES de buscar al usuario: con el filtro de multi-tenancy
        // activo, buscarlo sin inquilino no lo encontraría.
        using (_inquilinoActual.Change(registro.TenantId))
        {
            var usuario = await _usuarios.FindByIdAsync(registro.UserId.ToString());

            // El usuario se borró y la clave se quedó huérfana.
            return usuario == null ? null : new ClaveResuelta(registro, usuario);
        }
    }
}
