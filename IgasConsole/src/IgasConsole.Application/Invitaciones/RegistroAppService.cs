using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IgasConsole.Identity;
using IgasConsole.Organizaciones;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace IgasConsole.Invitaciones;

/// <summary>
/// Ver <see cref="IRegistroAppService"/>. Anónimo por definición: quien llega aún no tiene
/// cuenta. Lo único que prueba algo es el token del correo.
/// </summary>
[AllowAnonymous]
public class RegistroAppService : IgasConsoleAppService, IRegistroAppService
{
    private readonly GestorDeInvitaciones _gestor;
    private readonly IRepository<Organizacion, Guid> _organizaciones;

    public RegistroAppService(GestorDeInvitaciones gestor, IRepository<Organizacion, Guid> organizaciones)
    {
        _gestor = gestor;
        _organizaciones = organizaciones;
    }

    /// <summary>
    /// Para pintar la pantalla: a qué correo y con qué rol. Un token malo solo dice «no
    /// vale», sin distinguir si existió: no da pistas a quien pruebe tokens.
    /// </summary>
    public async Task<InvitacionPublicaDto> ConsultarAsync(TokenDeInvitacionDto input)
    {
        var invitacion = await _gestor.BuscarVigenteAsync(input.Token);
        if (invitacion == null)
        {
            return new InvitacionPublicaDto { Vigente = false };
        }

        return new InvitacionPublicaDto
        {
            Vigente = true,
            Email = invitacion.Email,
            RolEtiqueta = IgasRoles.Etiqueta(invitacion.Rol),
            UsuarioSugerido = SugerirUsuario(invitacion.Email),
            // Solo el nombre: quien tiene el enlace ya sabe a qué cliente lo invitan.
            Organizacion = invitacion.OrganizacionId.HasValue
                ? (await _organizaciones.FindAsync(invitacion.OrganizacionId.Value))?.Nombre
                : null,
        };
    }

    public async Task AceptarAsync(AceptarInvitacionDto input)
    {
        await _gestor.AceptarAsync(input.Token, input.UserName, input.Name, input.Surname, input.Password);
    }

    /// <summary>La parte local del correo, con solo lo que Identity acepta en un usuario.</summary>
    private static string SugerirUsuario(string email)
    {
        var local = email.Split('@').First();
        return Regex.Replace(local, "[^a-zA-Z0-9._-]", string.Empty);
    }
}
