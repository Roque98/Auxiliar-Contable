using System.Collections.Generic;

namespace IgasConsole.Organizaciones;

/// <summary>
/// El papel de una persona DENTRO de una organización (va en su membresía, no en sus roles
/// globales). Es lo que hacen los sistemas B2B (GitHub, Slack, Clerk, WorkOS): el rol se
/// asigna en cada organización a la que perteneces.
///
/// <para>Solo vale dentro de esa organización: un Administrador de organización no ve otras,
/// no toca al equipo de ZIA ni cambia roles o permisos globales.</para>
/// </summary>
public enum RolEnOrganizacion
{
    /// <summary>Ve los análisis de la organización.</summary>
    Lector = 0,

    /// <summary>Además sube sus reportes.</summary>
    Miembro = 1,

    /// <summary>Además invita, quita y cambia el papel de las personas de su organización.</summary>
    Administrador = 2,
}

/// <summary>Lo que se puede hacer dentro de una organización.</summary>
public enum AccionEnOrganizacion
{
    Ver,
    Subir,
    GestionarPersonas,
}

/// <summary>
/// Qué permite cada papel: el único sitio donde se dice. Como los «role sets» de Clerk: un
/// papel es un paquete de acciones, y añadir uno nuevo es añadir una fila aquí.
/// </summary>
public static class PermisosEnOrganizacion
{
    public static readonly IReadOnlyDictionary<RolEnOrganizacion, AccionEnOrganizacion[]> Mapa =
        new Dictionary<RolEnOrganizacion, AccionEnOrganizacion[]>
        {
            [RolEnOrganizacion.Lector] = [AccionEnOrganizacion.Ver],
            [RolEnOrganizacion.Miembro] = [AccionEnOrganizacion.Ver, AccionEnOrganizacion.Subir],
            [RolEnOrganizacion.Administrador] =
                [AccionEnOrganizacion.Ver, AccionEnOrganizacion.Subir, AccionEnOrganizacion.GestionarPersonas],
        };

    public static bool Permite(RolEnOrganizacion rol, AccionEnOrganizacion accion) =>
        Mapa.TryGetValue(rol, out var acciones) && System.Array.IndexOf(acciones, accion) >= 0;

    public static string Etiqueta(RolEnOrganizacion rol) => rol switch
    {
        RolEnOrganizacion.Administrador => "Administrador de la organización",
        RolEnOrganizacion.Lector => "Solo ve",
        _ => "Miembro",
    };
}
