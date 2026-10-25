using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Identity;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Uow;

namespace AuxiliarContable.Organizaciones;

/// <summary>
/// La regla de alta: <b>quien entra como cliente entra dentro de una organización</b>.
///
/// <para>Un cliente es cualquiera de nivel Usuario (<see cref="AuxiliarRoles.NivelUsuario"/>:
/// el rol Usuario y los roles creados a mano). Sin organización no vería ninguna auditoría,
/// así que una cuenta así no sirve de nada; por eso el alta —a mano o por invitación— la
/// exige. El equipo (Administrador, Owner) ve todo y no la necesita.</para>
/// </summary>
public class MembresiasDeOrganizacion : DomainService
{
    private readonly IRepository<Organizacion, Guid> _organizaciones;
    private readonly IUnitOfWorkManager _unidades;

    public MembresiasDeOrganizacion(IRepository<Organizacion, Guid> organizaciones, IUnitOfWorkManager unidades)
    {
        _organizaciones = organizaciones;
        _unidades = unidades;
    }

    /// <summary>¿Este nivel necesita organización? Solo el de cliente.</summary>
    public static bool NecesitaOrganizacion(int nivel) => nivel <= AuxiliarRoles.NivelUsuario;

    /// <summary>Lanza si falta o no existe (o está borrada).</summary>
    public async Task ExigirExistenteAsync(Guid? organizacionId)
    {
        if (organizacionId == null)
        {
            throw new BusinessException("AuxiliarContable:OrganizacionObligatoria");
        }

        if (!await _organizaciones.AnyAsync(o => o.Id == organizacionId.Value))
        {
            throw new BusinessException("AuxiliarContable:OrganizacionNoExiste");
        }
    }

    /// <summary>Mete a la persona en la organización (si ya estaba, no hace nada).</summary>
    public async Task AgregarAsync(Guid organizacionId, Guid userId, RolEnOrganizacion rol = RolEnOrganizacion.Miembro)
    {
        var organizacion = await ObtenerAsync(organizacionId);
        if (organizacion.AgregarMiembro(userId, rol))
        {
            // ⚠️ SaveChanges y no UpdateAsync: la fila hija lleva clave compuesta ya puesta y
            // Update la marcaría como modificada en vez de nueva (ver OrganizacionAppService).
            await _unidades.Current!.SaveChangesAsync();
        }
    }

    public async Task<bool> PerteneceAAlgunaAsync(Guid userId)
    {
        return await _organizaciones.AnyAsync(o => o.Miembros.Any(m => m.UserId == userId));
    }

    private async Task<Organizacion> ObtenerAsync(Guid id)
    {
        var consulta = (await _organizaciones.WithDetailsAsync(o => o.Miembros)).Where(o => o.Id == id);
        return await AsyncExecuter.FirstOrDefaultAsync(consulta)
               ?? throw new BusinessException("AuxiliarContable:OrganizacionNoExiste");
    }
}
