using System;
using System.Threading.Tasks;
using AuxiliarContable.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Volo.Abp.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace AuxiliarContable.Seguridad;

/// <summary>
/// Ver <see cref="ISeguridadDeUsuariosAppService"/>. Mismo permiso que editar usuarios y la
/// misma jerarquía (<see cref="JerarquiaDeUsuarios"/>): lo que no se alcanza responde 404, y
/// uno mismo tampoco — lo propio se gestiona desde «Seguridad de tu cuenta», no desde aquí.
/// </summary>
[Authorize(IdentityPermissions.Users.Update)]
public class SeguridadDeUsuariosAppService : AuxiliarContableAppService, ISeguridadDeUsuariosAppService
{
    private readonly IdentityUserManager _usuarios;
    private readonly JerarquiaDeUsuarios _jerarquia;
    private readonly IdentitySecurityLogManager _registro;

    public SeguridadDeUsuariosAppService(
        IdentityUserManager usuarios,
        JerarquiaDeUsuarios jerarquia,
        IdentitySecurityLogManager registro)
    {
        _usuarios = usuarios;
        _jerarquia = jerarquia;
        _registro = registro;
    }

    public async Task<SeguridadDeUsuarioDto> GetAsync(Guid id)
    {
        var usuario = await _usuarios.GetByIdAsync(id);
        await _jerarquia.ExigirGestionableAsync(usuario);
        return await DtoAsync(usuario);
    }

    public async Task<SeguridadDeUsuarioDto> QuitarSegundoFactorAsync(Guid id)
    {
        var usuario = await _usuarios.GetByIdAsync(id);
        await _jerarquia.ExigirGestionableAsync(usuario);

        if (usuario.TwoFactorEnabled)
        {
            (await _usuarios.SetTwoFactorEnabledAsync(usuario, false)).CheckErrors();

            // Queda en el registro de seguridad de ESA persona: quitarle un factor a alguien es
            // justo lo que haría quien le quiere robar la cuenta, y tiene que poder verse.
            await _registro.SaveAsync(new IdentitySecurityLogContext
            {
                Identity = IdentitySecurityLogIdentityConsts.Identity,
                Action = "SegundoFactorQuitadoPorAdministrador",
                UserName = usuario.UserName,
                ExtraProperties = { ["QuitadoPor"] = CurrentUser.UserName ?? string.Empty },
            });
        }

        return await DtoAsync(usuario);
    }

    private async Task<SeguridadDeUsuarioDto> DtoAsync(IdentityUser usuario)
    {
        return new SeguridadDeUsuarioDto
        {
            SegundoFactorActivo = usuario.TwoFactorEnabled,
            CorreoConfirmado = usuario.EmailConfirmed && !SeguridadConsts.EsCorreoProvisional(usuario.Email),
            SinCorreo = SeguridadConsts.EsCorreoProvisional(usuario.Email),
            TelefonoConfirmado = usuario.PhoneNumberConfirmed,
            CodigosDeRecuperacionRestantes = await _usuarios.CountRecoveryCodesAsync(usuario),
        };
    }
}
