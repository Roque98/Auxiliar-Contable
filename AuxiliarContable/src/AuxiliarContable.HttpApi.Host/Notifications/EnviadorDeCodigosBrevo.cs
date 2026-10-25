using System.Threading.Tasks;
using AuxiliarContable.Emails;
using AuxiliarContable.Notifications.Brevo;
using AuxiliarContable.Seguridad;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Emailing;

namespace AuxiliarContable.Notifications;

/// <summary>
/// Los códigos de un solo uso por Brevo: correo con la plantilla de la consola (<c>Emails/Codigo.tpl</c>)
/// o SMS.
///
/// <para>⚠️ Los SMS van SIN acentos ni eñes a propósito. Un solo carácter fuera del alfabeto
/// GSM-7 pasa el mensaje entero a UCS-2: de 160 caracteres por SMS a 70, y el mismo texto se
/// cobra como dos o tres mensajes.</para>
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IEnviadorDeCodigos))]
public class EnviadorDeCodigosBrevo : IEnviadorDeCodigos, ITransientDependency
{
    private readonly IEmailSender _correo;
    private readonly IMensajeria _mensajeria;
    private readonly PlantillasDeCorreo _plantillas;
    private readonly IEstadoDelCorreo _estadoDelCorreo;
    private readonly BrevoOptions _opciones;

    public EnviadorDeCodigosBrevo(
        IEmailSender correo,
        IMensajeria mensajeria,
        PlantillasDeCorreo plantillas,
        IEstadoDelCorreo estadoDelCorreo,
        IOptions<BrevoOptions> opciones)
    {
        _correo = correo;
        _mensajeria = mensajeria;
        _plantillas = plantillas;
        _estadoDelCorreo = estadoDelCorreo;
        _opciones = opciones.Value;
    }

    public bool CorreoDisponible => _estadoDelCorreo.PuedeEnviar;

    public bool SmsDisponible => _opciones.TieneCredenciales && _opciones.SmsHabilitado;

    public async Task EnviarPorCorreoAsync(string correo, string codigo, PropositoDeCodigo proposito)
    {
        var (asunto, titulo, intro, aviso) = proposito switch
        {
            PropositoDeCodigo.IniciarSesion => (
                "Tu código para entrar a Auxiliar contable",
                "Tu código de acceso",
                "Alguien está entrando a tu cuenta de Auxiliar contable con tu contraseña. Si eres tú, escribe este código para terminar.",
                "¿No eres tú? Alguien conoce tu contraseña: cámbiala cuanto antes y avisa a quien administra la consola."),
            _ => (
                "Confirma tu correo en Auxiliar contable",
                "Confirma tu correo",
                "Escribe este código en Auxiliar contable para confirmar que este correo es tuyo. Con él podrás recuperar tu cuenta y recibir códigos de acceso.",
                "¿No lo pediste tú? Ignora este correo: no se hará ningún cambio."),
        };

        var cuerpo = await _plantillas.RenderAsync(
            AuxiliarEmailTemplates.Codigo,
            new { titulo, intro, codigo, minutos = SeguridadConsts.MinutosDeValidez, aviso },
            $"{codigo} es tu código. Vence en {SeguridadConsts.MinutosDeValidez} minutos.");

        await _correo.SendAsync(correo, asunto, cuerpo);
    }

    public Task EnviarPorSmsAsync(string telefono, string codigo, PropositoDeCodigo proposito)
    {
        var minutos = SeguridadConsts.MinutosDeValidez;
        var texto = proposito == PropositoDeCodigo.IniciarSesion
            ? $"Auxiliar contable: Tu codigo para iniciar sesion es {codigo}. Vence en {minutos} min. Si no fuiste tu, cambia tu contrasena."
            : $"Auxiliar contable: Tu codigo de verificacion es {codigo}. Vence en {minutos} min. No lo compartas con nadie.";

        return _mensajeria.EnviarSmsAsync(telefono, texto);
    }
}
