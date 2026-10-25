using System;
using System.Threading.Tasks;
using AuxiliarContable.Emails;
using AuxiliarContable.Identity;
using AuxiliarContable.Invitaciones;
using AuxiliarContable.Notifications;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Emailing;
using Volo.Abp.Identity;
using Volo.Abp.Timing;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.Uow;

namespace AuxiliarContable.BackgroundJobs;

/// <summary>
/// Manda el correo de una invitación con un enlace recién generado.
///
/// <para>Si Brevo falla, el trabajo lanza, la transacción se deshace y el hash nuevo no se
/// guarda: el reintento genera otro enlace y nada queda a medias.</para>
/// </summary>
public class EnviarInvitacionJob : AsyncBackgroundJob<EnviarInvitacionArgs>, ITransientDependency
{
    private readonly IRepository<Invitacion, Guid> _invitaciones;
    private readonly GestorDeInvitaciones _gestor;
    private readonly IdentityUserManager _usuarios;
    private readonly IEmailSender _correo;
    private readonly PlantillasDeCorreo _plantillas;
    private readonly IAppUrlProvider _urls;
    private readonly IClock _reloj;
    private readonly IUnitOfWorkManager _unidadesDeTrabajo;
    private readonly IRepository<AuxiliarContable.Organizaciones.Organizacion, Guid> _organizaciones;

    public EnviarInvitacionJob(
        IRepository<Invitacion, Guid> invitaciones,
        GestorDeInvitaciones gestor,
        IdentityUserManager usuarios,
        IEmailSender correo,
        PlantillasDeCorreo plantillas,
        IAppUrlProvider urls,
        IClock reloj,
        IUnitOfWorkManager unidadesDeTrabajo,
        IRepository<AuxiliarContable.Organizaciones.Organizacion, Guid> organizaciones)
    {
        _organizaciones = organizaciones;
        _invitaciones = invitaciones;
        _gestor = gestor;
        _usuarios = usuarios;
        _correo = correo;
        _plantillas = plantillas;
        _urls = urls;
        _reloj = reloj;
        _unidadesDeTrabajo = unidadesDeTrabajo;
    }

    public override async Task ExecuteAsync(EnviarInvitacionArgs args)
    {
        // Igual que en EnviarEnlaceRecuperacionJob: un job no trae unidad de trabajo.
        // ⚠️ TRANSACCIONAL: sin eso, el autoSave de EmitirTokenAsync confirmaba el enlace
        // nuevo aunque Brevo fallara después, y la lista decía «Pendiente» de un correo que
        // nunca salió (visto probando con Brevo caído).
        using var unidad = _unidadesDeTrabajo.Begin(requiresNew: true, isTransactional: true);

        var invitacion = await _invitaciones.FindAsync(args.InvitacionId);
        if (invitacion == null || !invitacion.AdmiteEnvio(_reloj.Now))
        {
            // Revocada o aceptada entre la petición y el envío: no hay nada que mandar.
            Logger.LogInformation("La invitación {Id} ya no admite envío; se descarta.", args.InvitacionId);
            return;
        }

        var token = await _gestor.EmitirTokenAsync(invitacion);

        // En el FRAGMENTO (#), no en la query: el fragmento no viaja al servidor, así que el
        // token no acaba en el log de nginx ni en el Referer de nadie.
        var consola = (await _urls.GetUrlAsync("Angular")).TrimEnd('/');
        var enlace = $"{consola}/registro#t={token}";

        var cuerpo = await _plantillas.RenderAsync(
            AuxiliarEmailTemplates.Invitacion,
            new
            {
                quien_invita = await QuienInvitaAsync(invitacion),
                rol = AuxiliarRoles.Etiqueta(invitacion.Rol),
                organizacion = await OrganizacionAsync(invitacion),
                enlace,
                dias = InvitacionConsts.DiasDeVigencia
            },
            "Activa tu cuenta en Auxiliar contable.");

        await _correo.SendAsync(invitacion.Email, "Tu invitación a Auxiliar contable", cuerpo);

        await unidad.CompleteAsync();
    }

    private async Task<string?> OrganizacionAsync(Invitacion invitacion)
    {
        return invitacion.OrganizacionId.HasValue
            ? (await _organizaciones.FindAsync(invitacion.OrganizacionId.Value))?.Nombre
            : null;
    }

    /// <summary>
    /// El nombre de quien invita, salvo que sea el Developer: esa cuenta no la ve nadie más,
    /// y un correo con su nombre al futuro Owner la delataría.
    /// </summary>
    private async Task<string?> QuienInvitaAsync(Invitacion invitacion)
    {
        if (invitacion.CreatorId == null)
        {
            return null;
        }

        var autor = await _usuarios.FindByIdAsync(invitacion.CreatorId.Value.ToString());
        if (autor == null || AuxiliarRoles.NivelDeRoles(await _usuarios.GetRolesAsync(autor)) >= AuxiliarRoles.NivelDeveloper)
        {
            return null;
        }

        var nombre = $"{autor.Name} {autor.Surname}".Trim();
        return nombre.Length > 0 ? nombre : autor.UserName;
    }
}
