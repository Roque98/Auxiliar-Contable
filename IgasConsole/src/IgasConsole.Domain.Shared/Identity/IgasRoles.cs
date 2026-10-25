using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp.Roles;

namespace IgasConsole.Identity;

/// <summary>
/// Los roles de la organización y su jerarquía. Es la ÚNICA fuente de la regla de quién
/// puede ver, tocar y asignar a quién; los servicios de usuarios, roles y permisos la
/// consultan aquí en vez de decidirlo cada uno a su manera.
///
/// <para>La regla: cada persona tiene el nivel de su rol más alto, y solo alcanza lo que está
/// <b>estrictamente por debajo</b> de ese nivel (más a sí misma, para verse). El Developer es
/// la excepción: lo alcanza todo, y nadie más lo alcanza a él — ni siquiera para verlo.</para>
///
/// <para>⚠️ El Developer es el rol <c>admin</c> de ABP, no uno nuevo. A propósito: ABP le
/// concede <b>todos</b> los permisos definidos (también los que se añadan mañana) en cada
/// migración, y varios de sus servicios le dan paso libre comparando literalmente con
/// <see cref="AbpRoleConsts.AdminRoleName"/>. Un rol «developer» aparte perdería las dos
/// cosas sin ganar nada.</para>
///
/// <para><b>El nivel es un dato del ROL</b> (su propiedad <see cref="PropiedadNivel"/>, que
/// se elige al crearlo: <see cref="NivelDeRol"/>), no se deduce del nombre. Los métodos que
/// reciben <c>niveles</c> (el mapa nombre → nivel que da <c>NivelesDeRoles</c>) son los que
/// usa la aplicación; los que solo reciben el nombre son el respaldo para los cuatro roles
/// propios (y para un rol sin el dato, que vale 0). Un rol creado a mano nunca llega a nivel
/// Developer: ese es solo el <c>admin</c> de ABP.</para>
///
/// <para>Un rol nuevo no puede traer más permisos de los que tiene quien lo asigna: ABP solo
/// deja conceder permisos que uno mismo tiene (verificado en <c>refs/abp</c>,
/// <c>PermissionAppService.FilterInputPermissionsByCurrentUserAsync</c>).</para>
/// </summary>
public static class IgasRoles
{
    public const string Developer = AbpRoleConsts.AdminRoleName;
    public const string Owner = "owner";
    public const string Administrador = "administrador";
    public const string Usuario = "usuario";

    public const int NivelDeveloper = 3;
    public const int NivelOwner = 2;
    public const int NivelAdministrador = 1;
    public const int NivelUsuario = 0;

    /// <summary>La propiedad extra del rol de ABP donde vive su nivel.</summary>
    public const string PropiedadNivel = "Nivel";

    /// <summary>El nivel de un rol según el mapa (si lo trae) o, si no, según su nombre.</summary>
    public static int Nivel(string? rol, IReadOnlyDictionary<string, int>? niveles)
    {
        return rol != null && niveles != null && niveles.TryGetValue(rol, out var n) ? n : Nivel(rol);
    }

    public static int NivelDeRoles(IEnumerable<string>? roles, IReadOnlyDictionary<string, int>? niveles)
    {
        return roles?.Select(r => Nivel(r, niveles)).DefaultIfEmpty(NivelUsuario).Max() ?? NivelUsuario;
    }

    public static bool PuedeAsignar(int actor, string rol, IReadOnlyDictionary<string, int>? niveles)
    {
        return PuedeGestionar(actor, Nivel(rol, niveles));
    }

    /// <summary>
    /// El nivel que se GUARDA para un rol: el del Developer solo lo tiene <c>admin</c>; a
    /// cualquier otro se le recorta a Owner como mucho.
    /// </summary>
    public static int NivelValido(string rol, int nivel)
    {
        if (Iguales(rol, Developer))
        {
            return NivelDeveloper;
        }

        return Math.Clamp(nivel, NivelUsuario, NivelOwner);
    }

    /// <summary>Los roles que siembra la aplicación, de mayor a menor.</summary>
    public static readonly IReadOnlyList<string> Propios = [Developer, Owner, Administrador, Usuario];

    public static int Nivel(string? rol)
    {
        if (Iguales(rol, Developer))
        {
            return NivelDeveloper;
        }

        if (Iguales(rol, Owner))
        {
            return NivelOwner;
        }

        if (Iguales(rol, Administrador))
        {
            return NivelAdministrador;
        }

        return NivelUsuario;
    }

    /// <summary>El nivel de una persona es el de su rol más alto; sin roles, nivel 0.</summary>
    public static int NivelDeRoles(IEnumerable<string>? roles)
    {
        return roles?.Select(Nivel).DefaultIfEmpty(NivelUsuario).Max() ?? NivelUsuario;
    }

    /// <summary>¿Puede quien tiene <paramref name="actor"/> editar, borrar o cambiar los
    /// roles y permisos de alguien con nivel <paramref name="objetivo"/>?</summary>
    public static bool PuedeGestionar(int actor, int objetivo)
    {
        return actor >= NivelDeveloper || actor > objetivo;
    }

    /// <summary>Verse a uno mismo siempre; a los demás, solo si los puede gestionar.</summary>
    public static bool PuedeVer(int actor, int objetivo, bool esUnoMismo)
    {
        return esUnoMismo || PuedeGestionar(actor, objetivo);
    }

    public static bool PuedeAsignar(int actor, string rol)
    {
        return PuedeGestionar(actor, Nivel(rol));
    }

    /// <summary>Cómo se le enseña el rol a una persona (en correos y pantallas).</summary>
    public static string Etiqueta(string rol)
    {
        return Nivel(rol) switch
        {
            NivelDeveloper => "Developer",
            NivelOwner => "Owner",
            NivelAdministrador => "Administrador",
            _ when Iguales(rol, Usuario) => "Usuario",
            _ => rol
        };
    }

    private static bool Iguales(string? a, string b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
