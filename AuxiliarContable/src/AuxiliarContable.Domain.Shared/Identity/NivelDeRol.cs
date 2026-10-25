namespace AuxiliarContable.Identity;

/// <summary>
/// El nivel que se elige al crear un rol (<see cref="AuxiliarRoles.PropiedadNivel"/>). Decide a
/// quién alcanza quien lo tenga: solo a los de nivel ESTRICTAMENTE inferior. El de Developer
/// existe para que la lista de roles lo nombre, pero solo lo tiene el rol <c>admin</c> de ABP:
/// pedirlo para cualquier otro se rechaza.
/// </summary>
public enum NivelDeRol
{
    /// <summary>No gestiona a nadie (los clientes, o un rol que solo «hace cosas»).</summary>
    Usuario = AuxiliarRoles.NivelUsuario,

    /// <summary>Gestiona a los de nivel Usuario.</summary>
    Administrador = AuxiliarRoles.NivelAdministrador,

    /// <summary>Gestiona a Administradores y Usuarios.</summary>
    Owner = AuxiliarRoles.NivelOwner,

    /// <summary>Solo el rol <c>admin</c> de ABP.</summary>
    Developer = AuxiliarRoles.NivelDeveloper,
}
