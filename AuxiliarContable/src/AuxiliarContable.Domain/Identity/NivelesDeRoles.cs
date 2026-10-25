using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp.Caching;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace AuxiliarContable.Identity;

/// <summary>
/// El nivel de cada rol (nombre → nivel), leído de la propiedad
/// <see cref="AuxiliarRoles.PropiedadNivel"/> de cada rol. Es lo que usa la jerarquía en vez de
/// deducir el nivel del nombre.
///
/// <para>Va en caché porque se pregunta en casi cada petición y los roles casi no cambian. El
/// servicio de roles la invalida al crear, editar o borrar uno; y caduca sola por si acaso.</para>
/// </summary>
public class NivelesDeRoles : ITransientDependency
{
    private const string Clave = "todos";

    private readonly IIdentityRoleRepository _roles;
    private readonly IDistributedCache<NivelesDeRolesCacheItem> _cache;

    public NivelesDeRoles(IIdentityRoleRepository roles, IDistributedCache<NivelesDeRolesCacheItem> cache)
    {
        _roles = roles;
        _cache = cache;
    }

    public async Task<IReadOnlyDictionary<string, int>> MapaAsync()
    {
        var item = await _cache.GetOrAddAsync(
            Clave,
            async () => new NivelesDeRolesCacheItem
            {
                Niveles = (await _roles.GetListAsync()).ToDictionary(r => r.Name, Leer),
            },
            () => new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });

        return new Dictionary<string, int>(item!.Niveles, StringComparer.OrdinalIgnoreCase);
    }

    public Task InvalidarAsync() => _cache.RemoveAsync(Clave);

    /// <summary>
    /// El nivel guardado en un rol. Sin el dato (un rol de antes), el de su nombre: los cuatro
    /// propios lo tienen fijo y cualquier otro vale 0.
    /// </summary>
    public static int Leer(IdentityRole rol)
    {
        var guardado = Convertir(rol.GetProperty(AuxiliarRoles.PropiedadNivel));
        return guardado.HasValue ? AuxiliarRoles.NivelValido(rol.Name, guardado.Value) : AuxiliarRoles.Nivel(rol.Name);
    }

    /// <summary>La propiedad puede llegar como enum, número o texto («Owner») según de dónde venga.</summary>
    private static int? Convertir(object? valor) => valor switch
    {
        null => null,
        NivelDeRol n => (int)n,
        int i => i,
        long l => (int)l,
        JsonElement { ValueKind: JsonValueKind.Number } j => j.GetInt32(),
        JsonElement { ValueKind: JsonValueKind.String } j => Convertir(j.GetString()),
        string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
        string s when Enum.TryParse<NivelDeRol>(s, ignoreCase: true, out var n) => (int)n,
        string => null,
        IConvertible c => Convert.ToInt32(c, CultureInfo.InvariantCulture),
        _ => null,
    };
}

[CacheName("AuxiliarContable.NivelesDeRoles")]
public class NivelesDeRolesCacheItem
{
    public Dictionary<string, int> Niveles { get; set; } = [];
}
