using System.Collections.Generic;
using AuxiliarContable.Identity;
using AuxiliarContable.Organizaciones;
using Shouldly;
using Xunit;

namespace AuxiliarContable.Organizaciones;

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
    [InlineData("admin", null, AuxiliarRoles.NivelDeveloper)]
    [InlineData("owner", null, AuxiliarRoles.NivelOwner)]
    [InlineData("administrador", null, AuxiliarRoles.NivelAdministrador)]
    [InlineData("Analista", null, AuxiliarRoles.NivelUsuario)]
    // Con dato guardado: manda el dato.
    [InlineData("Supervisor", AuxiliarRoles.NivelAdministrador, AuxiliarRoles.NivelAdministrador)]
    [InlineData("Socio", AuxiliarRoles.NivelOwner, AuxiliarRoles.NivelOwner)]
    public void El_nivel_sale_del_dato_del_rol(string rol, int? guardado, int esperado)
    {
        var mapa = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        if (guardado.HasValue)
        {
            mapa[rol] = guardado.Value;
        }

        AuxiliarRoles.Nivel(rol, mapa).ShouldBe(esperado);
    }

    [Theory]
    // Un rol creado a mano nunca llega a Developer; admin siempre lo es.
    [InlineData("Supervisor", AuxiliarRoles.NivelDeveloper, AuxiliarRoles.NivelOwner)]
    [InlineData("Supervisor", -4, AuxiliarRoles.NivelUsuario)]
    [InlineData("admin", AuxiliarRoles.NivelUsuario, AuxiliarRoles.NivelDeveloper)]
    public void El_nivel_que_se_guarda_se_recorta(string rol, int pedido, int guardado)
    {
        AuxiliarRoles.NivelValido(rol, pedido).ShouldBe(guardado);
    }
}
