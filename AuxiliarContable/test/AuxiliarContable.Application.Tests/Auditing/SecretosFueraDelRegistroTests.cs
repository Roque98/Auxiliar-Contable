using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Shouldly;
using Volo.Abp.Auditing;
using Xunit;

namespace AuxiliarContable.Auditing;

/// <summary>
/// ABP guarda en AbpAuditLogActions los PARÁMETROS de cada llamada a un servicio, en JSON.
/// Una contraseña o un código en un DTO sin <see cref="DisableAuditingAttribute"/> acaba en
/// la base en claro (y en las copias). Pasó con el registro por invitación: la contraseña
/// elegida quedaba guardada tal cual.
///
/// <para>Esto revisa TODOS los DTO de los contratos: si mañana alguien añade un campo
/// «Password», «Token», «Codigo»… sin marcarlo, el test falla.</para>
/// </summary>
public class SecretosFueraDelRegistroTests
{
    private static readonly Regex NombreSensible = new(
        "(password|contrasena|token|codigo|secret|pin|nip)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void Ningun_campo_sensible_de_un_DTO_entra_al_registro_de_auditoria()
    {
        var ensamblado = typeof(AuxiliarContable.Invitaciones.AceptarInvitacionDto).Assembly;

        var sinMarcar = ensamblado.GetTypes()
            // Las *Options son configuración (IOptions): nunca llegan como parámetro.
            .Where(t => t.IsClass && t.IsPublic && !t.Name.EndsWith("Options"))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(p => (Tipo: t, Propiedad: p)))
            .Where(x => x.Propiedad.PropertyType == typeof(string) && x.Propiedad.CanWrite)
            .Where(x => NombreSensible.IsMatch(x.Propiedad.Name))
            // Salidas que nunca llegan como parámetro: la clave de API recién creada y la
            // pista del token (sus últimos caracteres). ABP no registra valores devueltos.
            .Where(x => x.Propiedad.Name is not ("PlainKey" or "BotTokenHint"))
            .Where(x => !x.Propiedad.IsDefined(typeof(DisableAuditingAttribute), inherit: true))
            .Select(x => $"{x.Tipo.Name}.{x.Propiedad.Name}")
            .ToList();

        sinMarcar.ShouldBeEmpty("Márcalos con [DisableAuditing]: " + string.Join(", ", sinMarcar));
    }
}
