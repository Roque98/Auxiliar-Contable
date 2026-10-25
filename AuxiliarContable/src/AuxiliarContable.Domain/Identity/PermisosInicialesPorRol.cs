using System.Collections.Generic;

namespace AuxiliarContable.Identity;

/// <summary>
/// Con qué permisos NACE cada rol. Solo se aplican al crear el rol: a partir de ahí los
/// cambia el Owner desde la pantalla de roles, y una migración no se los vuelve a poner
/// (ver <see cref="AuxiliarRolesDataSeedContributor"/>).
///
/// <para>Van como texto porque el dominio no ve la capa de contratos, que es donde viven las
/// constantes. Un test (<c>PermisosInicialesPorRolTests</c>) comprueba que cada nombre de
/// aquí es un permiso definido de verdad: una errata no fallaría en ningún otro sitio,
/// simplemente el rol nacería sin ese permiso.</para>
///
/// <para>El Developer no aparece: es el rol <c>admin</c> de ABP y ABP le concede todo.</para>
/// </summary>
public static class PermisosInicialesPorRol
{
    private const string Usuarios = "AbpIdentity.Users";
    private const string Roles = "AbpIdentity.Roles";
    private const string Organizaciones = "AuxiliarContable.Organizaciones";
    private const string Actividad = "AuxiliarContable.Actividad";

    public static readonly IReadOnlyDictionary<string, string[]> Mapa = new Dictionary<string, string[]>
    {
        // El dueño del despacho: administra a su gente y sus roles, a los clientes y la marca.
        // Nada técnico (métricas, trabajos, seguridad, auditoría): no le sirve y enseñaría la
        // actividad del Developer.
        [AuxiliarRoles.Owner] =
        [
            Usuarios, Usuarios + ".Create", Usuarios + ".Update", Usuarios + ".Delete",
            Usuarios + ".Update.ManageRoles", Usuarios + ".ManagePermissions",
            Roles, Roles + ".Create", Roles + ".Update", Roles + ".Delete", Roles + ".ManagePermissions",
            Organizaciones, Actividad, "AuxiliarContable.Branding",
        ],

        // El equipo del despacho (contadores): gestiona a los usuarios finales y a los
        // clientes. Sin roles ni permisos: eso es del Owner.
        [AuxiliarRoles.Administrador] =
        [
            Usuarios, Usuarios + ".Create", Usuarios + ".Update", Usuarios + ".Delete",
            Usuarios + ".Update.ManageRoles",
            Organizaciones, Actividad,
        ],

        // Un Usuario es, por defecto, alguien de un cliente: ve y sube lo de sus
        // organizaciones. Sus permisos llegan con los módulos de negocio.
        [AuxiliarRoles.Usuario] = [],
    };
}

/// <summary>
/// Permisos que aparecieron DESPUÉS de crear los roles. <see cref="PermisosInicialesPorRol"/>
/// solo se aplica al crear un rol, así que un permiso nuevo no llegaría nunca a los roles de
/// producción. Cada versión se concede UNA vez a los roles existentes
/// (<c>AuxiliarRolesDataSeedContributor</c> recuerda la última aplicada): si luego el Owner lo
/// quita, no vuelve en el siguiente despliegue.
///
/// <para>⚠️ Solo se AÑADEN versiones al final, nunca se edita una ya publicada. Mientras no
/// haya producción, un permiso nuevo va directo en <see cref="PermisosInicialesPorRol"/>.</para>
/// </summary>
public static class PermisosNuevosPorVersion
{
    public static readonly IReadOnlyList<(int Version, IReadOnlyDictionary<string, string[]> Mapa)> Versiones = [];
}
