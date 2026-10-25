using System.Collections.Generic;
using IgasConsole.Identity;
using IgasConsole.Organizaciones;
using Shouldly;
using Xunit;

namespace IgasConsole.Organizaciones;

/// <summary>
/// La autorización como TABLA de casos: qué permite cada papel en una organización y qué nivel
/// tiene cada rol. Si alguien cambia una regla, la fila que la contradice falla.
/// </summary>
public class PermisosEnOrganizacionTests
{
    [Theory]
    [InlineData(RolEnOrganizacion.Lector, AccionEnOrganizacion.Ver, true)]
    [InlineData(RolEnOrganizacion.Lector, AccionEnOrganizacion.Subir, false)]
    [InlineData(RolEnOrganizacion.Lector, AccionEnOrganizacion.GestionarPersonas, false)]
    [InlineData(RolEnOrganizacion.Miembro, AccionEnOrganizacion.Ver, true)]
    [InlineData(RolEnOrganizacion.Miembro, AccionEnOrganizacion.Subir, true)]
    [InlineData(RolEnOrganizacion.Miembro, AccionEnOrganizacion.GestionarPersonas, false)]
    [InlineData(RolEnOrganizacion.Administrador, AccionEnOrganizacion.Ver, true)]
    [InlineData(RolEnOrganizacion.Administrador, AccionEnOrganizacion.Subir, true)]
    [InlineData(RolEnOrganizacion.Administrador, AccionEnOrganizacion.GestionarPersonas, true)]
    public void Cada_papel_permite_lo_suyo(RolEnOrganizacion rol, AccionEnOrganizacion accion, bool permitido)
    {
        PermisosEnOrganizacion.Permite(rol, accion).ShouldBe(permitido);
    }

    [Theory]
    // Sin dato guardado: los cuatro propios por su nombre; cualquier otro, 0.
    [InlineData("admin", null, IgasRoles.NivelDeveloper)]
    [InlineData("owner", null, IgasRoles.NivelOwner)]
    [InlineData("administrador", null, IgasRoles.NivelAdministrador)]
    [InlineData("Analista", null, IgasRoles.NivelUsuario)]
    // Con dato guardado: manda el dato.
    [InlineData("Supervisor", IgasRoles.NivelAdministrador, IgasRoles.NivelAdministrador)]
    [InlineData("Socio", IgasRoles.NivelOwner, IgasRoles.NivelOwner)]
    public void El_nivel_sale_del_dato_del_rol(string rol, int? guardado, int esperado)
    {
        var mapa = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        if (guardado.HasValue)
        {
            mapa[rol] = guardado.Value;
        }

        IgasRoles.Nivel(rol, mapa).ShouldBe(esperado);
    }

    [Theory]
    // Un rol creado a mano nunca llega a Developer; admin siempre lo es.
    [InlineData("Supervisor", IgasRoles.NivelDeveloper, IgasRoles.NivelOwner)]
    [InlineData("Supervisor", -4, IgasRoles.NivelUsuario)]
    [InlineData("admin", IgasRoles.NivelUsuario, IgasRoles.NivelDeveloper)]
    public void El_nivel_que_se_guarda_se_recorta(string rol, int pedido, int guardado)
    {
        IgasRoles.NivelValido(rol, pedido).ShouldBe(guardado);
    }
}
