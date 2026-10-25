using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace IgasConsole.Seguridad;

/// <summary>
/// Entrega un código de un solo uso por correo o por SMS, con la plantilla de ZIA. Lo
/// implementa el host (Brevo). El código se manda EN LÍNEA: nunca pasa por la cola de
/// trabajos. En la base solo queda cifrado, donde lo guarda ABP para validarlo.
/// </summary>
public interface IEnviadorDeCodigos
{
    bool CorreoDisponible { get; }

    /// <summary>Apagado hasta que la cuenta de Brevo tenga créditos de SMS (Brevo:SmsHabilitado).</summary>
    bool SmsDisponible { get; }

    Task EnviarPorCorreoAsync(string correo, string codigo, PropositoDeCodigo proposito);

    Task EnviarPorSmsAsync(string telefono, string codigo, PropositoDeCodigo proposito);
}

/// <summary>Sin host (migrador, tests) no hay por dónde mandar nada.</summary>
[Dependency(TryRegister = true)]
[ExposeServices(typeof(IEnviadorDeCodigos))]
public class EnviadorDeCodigosNulo : IEnviadorDeCodigos, ISingletonDependency
{
    public bool CorreoDisponible => false;

    public bool SmsDisponible => false;

    public Task EnviarPorCorreoAsync(string correo, string codigo, PropositoDeCodigo proposito) => Task.CompletedTask;

    public Task EnviarPorSmsAsync(string telefono, string codigo, PropositoDeCodigo proposito) => Task.CompletedTask;
}
