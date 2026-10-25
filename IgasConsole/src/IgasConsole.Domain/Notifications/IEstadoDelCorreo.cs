using Volo.Abp.DependencyInjection;

namespace IgasConsole.Notifications;

/// <summary>
/// ¿Puede salir un correo de verdad? Lo implementa el host mirando la configuración de
/// Brevo (API key y remitente). Sirve para no aceptar invitaciones que nunca llegarían y
/// para avisarlo en pantalla.
/// </summary>
public interface IEstadoDelCorreo
{
    bool PuedeEnviar { get; }
}

/// <summary>Sin host (DbMigrator, tests) no hay por dónde mandar nada.</summary>
[Dependency(TryRegister = true)]
[ExposeServices(typeof(IEstadoDelCorreo))]
public class CorreoSinConfigurar : IEstadoDelCorreo, ISingletonDependency
{
    public bool PuedeEnviar => false;
}
