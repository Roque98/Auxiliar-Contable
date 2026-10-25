using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AuxiliarContable.Identity;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.Privacidad;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Invitaciones;

/// <summary>
/// Ver <see cref="IRegistroAppService"/>. Anónimo por definición: quien llega aún no tiene
/// cuenta. Lo único que prueba algo es el token del correo.
/// </summary>
[AllowAnonymous]
public class RegistroAppService : AuxiliarContableAppService, IRegistroAppService
{
    private readonly GestorDeInvitaciones _gestor;
    private readonly IRepository<Organizacion, Guid> _organizaciones;
    private readonly RegistroDeConsentimientos _consentimientos;

    public RegistroAppService(
        GestorDeInvitaciones gestor,
        IRepository<Organizacion, Guid> organizaciones,
        RegistroDeConsentimientos consentimientos)
    {
        _gestor = gestor;
        _organizaciones = organizaciones;
        _consentimientos = consentimientos;
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
            RolEtiqueta = AuxiliarRoles.Etiqueta(invitacion.Rol),
            UsuarioSugerido = SugerirUsuario(invitacion.Email),
            // Solo el nombre: quien tiene el enlace ya sabe a qué cliente lo invitan.
            Organizacion = invitacion.OrganizacionId.HasValue
                ? (await _organizaciones.FindAsync(invitacion.OrganizacionId.Value))?.Nombre
                : null,
        };
    }

    /// <summary>
    /// Crea la cuenta y deja constancia de que aceptó el aviso de privacidad, en la misma
    /// unidad de trabajo: o las dos cosas o ninguna. El aviso se comprueba ANTES, para no
    /// gastar el intento en validar usuario y contraseña de algo que se va a rechazar.
    /// </summary>
    public async Task AceptarAsync(AceptarInvitacionDto input)
    {
        if (input.VersionDelAvisoAceptada != AvisoDePrivacidadConsts.VersionVigente)
        {
            throw new BusinessException("AuxiliarContable:AvisoDePrivacidadDesactualizado");
        }

        var usuario = await _gestor.AceptarAsync(input.Token, input.UserName, input.Name, input.Surname, input.Password);
        await _consentimientos.AceptarAsync(usuario.Id, input.VersionDelAvisoAceptada);
    }

    /// <summary>La parte local del correo, con solo lo que Identity acepta en un usuario.</summary>
    private static string SugerirUsuario(string email)
    {
        var local = email.Split('@').First();
        return Regex.Replace(local, "[^a-zA-Z0-9._-]", string.Empty);
    }
}
