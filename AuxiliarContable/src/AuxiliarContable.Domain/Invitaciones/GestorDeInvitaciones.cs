using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AuxiliarContable.Identity;
using AuxiliarContable.Notifications;
using AuxiliarContable.Organizaciones;
using Microsoft.AspNetCore.Identity;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Identity;

namespace AuxiliarContable.Invitaciones;

/// <summary>
/// Las reglas de las invitaciones: a quién se puede invitar, cómo nace y se comprueba el
/// enlace, y qué cuenta se crea al aceptarla.
/// </summary>
public class GestorDeInvitaciones : DomainService
{
    private readonly IRepository<Invitacion, Guid> _invitaciones;
    private readonly IdentityUserManager _usuarios;
    private readonly IIdentityRoleRepository _roles;
    private readonly JerarquiaDeUsuarios _jerarquia;
    private readonly IEstadoDelCorreo _correo;
    private readonly MembresiasDeOrganizacion _membresias;

    public GestorDeInvitaciones(
        IRepository<Invitacion, Guid> invitaciones,
        IdentityUserManager usuarios,
        IIdentityRoleRepository roles,
        JerarquiaDeUsuarios jerarquia,
        IEstadoDelCorreo correo,
        MembresiasDeOrganizacion membresias)
    {
        _membresias = membresias;
        _invitaciones = invitaciones;
        _usuarios = usuarios;
        _roles = roles;
        _jerarquia = jerarquia;
        _correo = correo;
    }

    /// <summary>
    /// Crea la invitación (sin enlace todavía: lo pone el trabajo que envía el correo).
    ///
    /// <para>El rol tiene que estar estrictamente por debajo de quien invita — la misma
    /// regla que para asignarlo a mano (<see cref="AuxiliarRoles.PuedeAsignar"/>). Así una
    /// invitación no es un atajo para subir a nadie de nivel.</para>
    ///
    /// <para>Un cliente (rol de nivel Usuario) se invita DENTRO de una organización y con su
    /// papel en ella; al equipo no se le pone ninguna aunque llegue (ve todas).</para>
    ///
    /// <para><paramref name="porAdministradorDeOrganizacion"/>: invita un administrador DE LA
    /// ORGANIZACIÓN (un cliente, nivel 0), que no alcanza a nadie en la jerarquía global. Solo
    /// puede invitar al rol de cliente y a SU organización: lo comprueba quien llama
    /// (<c>MiOrganizacionAppService</c>) y aquí se exige que el rol sea de nivel Usuario.</para>
    /// </summary>
    public async Task<Invitacion> CrearAsync(
        string email, string rol, Guid? organizacionId = null,
        RolEnOrganizacion rolEnOrganizacion = RolEnOrganizacion.Miembro,
        bool porAdministradorDeOrganizacion = false)
    {
        ExigirCorreoConfigurado();

        var normalizado = Invitacion.NormalizarEmail(email);

        var rolReal = await _roles.FindByNormalizedNameAsync(_usuarios.NormalizeName(rol));
        var nivelDelRol = rolReal == null ? 0 : await _jerarquia.NivelDeRolAsync(rolReal.Name);
        var permitido = porAdministradorDeOrganizacion
            ? nivelDelRol == AuxiliarRoles.NivelUsuario && organizacionId.HasValue
            : nivelDelRol < AuxiliarRoles.NivelDeveloper && rolReal != null && await _jerarquia.PuedeAsignarAsync(rolReal.Name);
        if (rolReal == null || !permitido)
        {
            throw new BusinessException("AuxiliarContable:InvitacionRolFueraDeAlcance");
        }

        if (await _usuarios.FindByEmailAsync(normalizado) != null)
        {
            throw new BusinessException("AuxiliarContable:InvitacionCorreoConCuenta").WithData("email", normalizado);
        }

        if (MembresiasDeOrganizacion.NecesitaOrganizacion(nivelDelRol))
        {
            await _membresias.ExigirExistenteAsync(organizacionId);
        }
        else
        {
            organizacionId = null;
        }

        var ahora = Clock.Now;
        var abiertas = await _invitaciones.GetListAsync(i =>
            i.Email == normalizado && i.AceptadaEn == null && i.RevocadaEn == null);
        if (abiertas.Any(i => i.Estado(ahora) == EstadoInvitacion.Pendiente))
        {
            throw new BusinessException("AuxiliarContable:InvitacionYaPendiente").WithData("email", normalizado);
        }

        return await _invitaciones.InsertAsync(
            new Invitacion(GuidGenerator.Create(), normalizado, rolReal.Name, organizacionId,
                organizacionId.HasValue ? rolEnOrganizacion : RolEnOrganizacion.Miembro),
            autoSave: true);
    }

    /// <summary>Comprueba que se puede reenviar antes de encolar el correo.</summary>
    public void ExigirReenviable(Invitacion invitacion)
    {
        ExigirCorreoConfigurado();

        if (!invitacion.AdmiteEnvio(Clock.Now))
        {
            throw new BusinessException("AuxiliarContable:InvitacionCerrada");
        }

        if (invitacion.Envios >= InvitacionConsts.MaxEnvios)
        {
            throw new BusinessException("AuxiliarContable:InvitacionDemasiadosEnvios")
                .WithData("maximo", InvitacionConsts.MaxEnvios);
        }
    }

    /// <summary>
    /// Genera un enlace nuevo y guarda su hash. Devuelve el token EN CLARO, que solo debe ir
    /// al correo: no se puede volver a obtener.
    /// </summary>
    public async Task<string> EmitirTokenAsync(Invitacion invitacion)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        invitacion.RegistrarEnvio(Hash(token), Clock.Now);
        await _invitaciones.UpdateAsync(invitacion, autoSave: true);
        return token;
    }

    /// <summary>La invitación pendiente de ese token, o null (no existe, se usó, venció…).</summary>
    public async Task<Invitacion?> BuscarVigenteAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
        {
            return null;
        }

        var hash = Hash(token.Trim());
        var invitacion = await _invitaciones.FirstOrDefaultAsync(i => i.TokenHash == hash);

        return invitacion != null && invitacion.Estado(Clock.Now) == EstadoInvitacion.Pendiente
            ? invitacion
            : null;
    }

    /// <summary>
    /// Crea la cuenta con el correo ya confirmado y el rol de la invitación, y la cierra.
    /// Si Identity rechaza algo (usuario repetido, contraseña débil) no se guarda nada: todo
    /// va en la misma unidad de trabajo.
    /// </summary>
    public async Task<IdentityUser> AceptarAsync(
        string token,
        string userName,
        string? nombre,
        string? apellido,
        string password)
    {
        var invitacion = await BuscarVigenteAsync(token)
                         ?? throw new BusinessException("AuxiliarContable:InvitacionNoValida");

        // Pudo crearse una cuenta con ese correo después de invitar.
        if (await _usuarios.FindByEmailAsync(invitacion.Email) != null)
        {
            throw new BusinessException("AuxiliarContable:InvitacionCorreoConCuenta").WithData("email", invitacion.Email);
        }

        // La organización, ANTES de crear la cuenta: si se borró después de invitar, no se
        // crea un cliente que no vería nada. Se pide una invitación nueva.
        if (invitacion.OrganizacionId.HasValue)
        {
            try
            {
                await _membresias.ExigirExistenteAsync(invitacion.OrganizacionId);
            }
            catch (BusinessException e) when (e.Code == "AuxiliarContable:OrganizacionNoExiste")
            {
                throw new BusinessException("AuxiliarContable:InvitacionSinOrganizacion");
            }
        }

        var usuario = new IdentityUser(GuidGenerator.Create(), userName.Trim(), invitacion.Email, CurrentTenant.Id)
        {
            Name = string.IsNullOrWhiteSpace(nombre) ? null : nombre.Trim(),
            Surname = string.IsNullOrWhiteSpace(apellido) ? null : apellido.Trim()
        };

        // El enlace llegó a ese buzón: abrirlo es la verificación.
        usuario.SetEmailConfirmed(true);

        (await _usuarios.CreateAsync(usuario, password)).CheckErrors();
        (await _usuarios.AddToRoleAsync(usuario, invitacion.Rol)).CheckErrors();

        if (invitacion.OrganizacionId.HasValue)
        {
            await _membresias.AgregarAsync(invitacion.OrganizacionId.Value, usuario.Id, invitacion.RolEnOrganizacion);
        }

        invitacion.Aceptar(usuario.Id, Clock.Now);
        await _invitaciones.UpdateAsync(invitacion, autoSave: true);

        return usuario;
    }

    private void ExigirCorreoConfigurado()
    {
        if (!_correo.PuedeEnviar)
        {
            throw new BusinessException("AuxiliarContable:InvitacionCorreoNoConfigurado");
        }
    }

    public static string Hash(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }
}
