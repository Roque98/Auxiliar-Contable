using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Seguridad;
using AuxiliarContable.Telegram;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Account.Web.Pages.Account;
using Volo.Abp.Identity;
using Volo.Abp.Identity.AspNetCore;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace AuxiliarContable.Pages.Account;

/// <summary>
/// El segundo paso del inicio de sesión: pide el código que se acaba de mandar por correo,
/// SMS o Telegram. La persona elige el canal entre los que tiene verificados.
/// <para>Es una página nueva: el módulo Account open source no trae ninguna equivalente
/// (deja el segundo factor como punto de extensión), así que aquí sí hay vista propia.</para>
/// </summary>
public class TwoFactorModel : AccountPageModel
{
    /// <summary>El orden en que se ofrecen: el correo primero, que lo tiene todo el mundo.</summary>
    private static readonly string[] Preferencia =
        [TokenOptions.DefaultEmailProvider, TokenOptions.DefaultPhoneProvider, TelegramTwoFactorConsts.ProviderName];

    // Sin [Required]: esta página tiene dos formularios (código y código de recuperación) y el
    // atributo haría inválido el otro. Se comprueba a mano en cada manejador.
    [StringLength(16)]
    [BindProperty]
    public string? Codigo { get; set; }

    /// <summary>Uno de los códigos de recuperación que la persona guardó al activar los dos pasos.</summary>
    [StringLength(32)]
    [BindProperty]
    public string? CodigoRecuperacion { get; set; }

    /// <summary>Se entra por el enlace «usar un código de recuperación»: no se manda ningún código.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Recuperacion { get; set; }

    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrlHash { get; set; }

    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public bool RememberMe { get; set; }

    /// <summary>Canal elegido: «Email», «Phone» o «Telegram».</summary>
    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public string? Metodo { get; set; }

    /// <summary>Los canales que la persona puede usar, para ofrecer cambiar.</summary>
    public List<string> Metodos { get; private set; } = [];

    /// <summary>A dónde fue el código, enmascarado: «a***@gmail.com», «•••• 5678», «@usuario».</summary>
    public string? Destino { get; private set; }

    public int ValidezMinutos => SeguridadConsts.MinutosDeValidez;

    /// <summary>
    /// Un aviso para la persona, pintado en la propia vista. Los <c>Alerts</c> de ABP no se
    /// ven en el layout de cuenta de este tema, y un error de envío tiene que verse.
    /// </summary>
    public string? Aviso { get; private set; }

    /// <summary>«danger», «warning» o «info» (clases de alerta de Bootstrap).</summary>
    public string AvisoTipo { get; private set; } = "info";

    /// <summary>Si en esta visita no salió el código (para no decir «te enviamos»).</summary>
    public bool EnvioFallido { get; private set; }

    private void Avisar(string tipo, string texto)
    {
        AvisoTipo = tipo;
        Aviso = texto;
    }

    private readonly ITelegramMessenger _telegram;
    private readonly ITelegramLinkRepository _vinculos;
    private readonly IEnviadorDeCodigos _enviador;
    private readonly LimitadorDeCodigos _limitador;
    private readonly AuxiliarContable.HealthChecks.RegistradorDeSesiones _sesiones;

    public TwoFactorModel(
        ITelegramMessenger telegram,
        ITelegramLinkRepository vinculos,
        IEnviadorDeCodigos enviador,
        LimitadorDeCodigos limitador,
        AuxiliarContable.HealthChecks.RegistradorDeSesiones sesiones)
    {
        _telegram = telegram;
        _vinculos = vinculos;
        _enviador = enviador;
        _limitador = limitador;
        _sesiones = sesiones;
    }

    public virtual async Task<IActionResult> OnGetAsync()
    {
        var usuario = await SignInManager.GetTwoFactorAuthenticationUserAsync();
        if (usuario == null)
        {
            // Sin la cookie intermedia no hay nada que verificar: o se entró por la URL a
            // pelo, o caducó. Se vuelve al login en vez de mostrar un formulario que no
            // podría funcionar.
            return VolverAlLogin();
        }

        await PrepararAsync(usuario);
        if (!Recuperacion)
        {
            await EnviarCodigoAsync(usuario, avisarSiEspera: false);
        }

        return Page();
    }

    /// <summary>
    /// Entrar con un código de recuperación: para quien no recibe ningún código (sin correo,
    /// sin teléfono, Brevo caído). Cada código vale una vez; ABP aplica las mismas
    /// comprobaciones que al login normal (activo, bloqueado).
    /// </summary>
    public virtual async Task<IActionResult> OnPostRecuperacionAsync()
    {
        var usuario = await SignInManager.GetTwoFactorAuthenticationUserAsync();
        if (usuario == null)
        {
            return VolverAlLogin();
        }

        Recuperacion = true;
        await PrepararAsync(usuario);

        var codigo = (CodigoRecuperacion ?? string.Empty).Replace(" ", string.Empty).Trim();
        if (codigo.Length == 0)
        {
            Avisar("warning", "Escribe uno de tus códigos de recuperación.");
            return Page();
        }

        var resultado = await SignInManager.TwoFactorRecoveryCodeSignInAsync(codigo);

        await IdentitySecurityLogManager.SaveAsync(new IdentitySecurityLogContext
        {
            Identity = IdentitySecurityLogIdentityConsts.Identity,
            Action = resultado.Succeeded ? "InicioConCodigoDeRecuperacion" : resultado.ToIdentitySecurityLogAction(),
            UserName = usuario.UserName
        });

        if (resultado.IsLockedOut)
        {
            Avisar("warning", "La cuenta está bloqueada temporalmente por demasiados intentos. Vuelve a intentarlo en unos minutos.");
            return Page();
        }

        if (!resultado.Succeeded)
        {
            Avisar("danger", "Ese código de recuperación no es válido o ya se usó.");
            return Page();
        }

        await _sesiones.RegistrarAsync(SignInManager, usuario, RememberMe);
        return await RedirectSafelyAsync(ReturnUrl ?? "/", ReturnUrlHash);
    }

    /// <summary>Reenvía el código (o lo manda por otro canal) sin gastar un intento.</summary>
    public virtual async Task<IActionResult> OnPostReenviarAsync()
    {
        var usuario = await SignInManager.GetTwoFactorAuthenticationUserAsync();
        if (usuario == null)
        {
            return VolverAlLogin();
        }

        await PrepararAsync(usuario);
        if (await EnviarCodigoAsync(usuario, avisarSiEspera: true))
        {
            Avisar("info", "Te enviamos un código nuevo.");
        }

        return Page();
    }

    public virtual async Task<IActionResult> OnPostAsync()
    {
        var usuario = await SignInManager.GetTwoFactorAuthenticationUserAsync();
        if (usuario == null)
        {
            return VolverAlLogin();
        }

        await PrepararAsync(usuario);

        if (string.IsNullOrWhiteSpace(Codigo))
        {
            Avisar("warning", "Escribe el código que te enviamos.");
            return Page();
        }

        var resultado = await SignInManager.TwoFactorSignInAsync(
            Metodo!,
            Codigo.Replace(" ", string.Empty).Trim(),
            isPersistent: RememberMe,
            // rememberClient: false — no se ofrece «confiar en este dispositivo». Guardar esa
            // cookie deja el segundo factor desactivado en ese navegador durante días, y
            // quien lo activó normalmente quiere justo lo contrario.
            rememberClient: false);

        await IdentitySecurityLogManager.SaveAsync(new IdentitySecurityLogContext
        {
            Identity = IdentitySecurityLogIdentityConsts.Identity,
            Action = resultado.ToIdentitySecurityLogAction(),
            UserName = usuario.UserName
        });

        if (resultado.IsLockedOut)
        {
            Avisar("warning", "La cuenta está bloqueada temporalmente por demasiados intentos. Vuelve a intentarlo en unos minutos.");
            return Page();
        }

        if (!resultado.Succeeded)
        {
            // El motivo no se detalla más: distinguir «código incorrecto» de «código
            // caducado» le sirve más a quien está probando códigos que a quien se equivocó.
            Avisar("danger", "El código no es válido o ya venció.");
            return Page();
        }

        // El segundo factor es OTRA puerta de entrada: sin registrar aquí, quien lo tenga
        // activado no aparecería en la lista de sesiones y no podría cerrarlas en remoto.
        await _sesiones.RegistrarAsync(SignInManager, usuario, RememberMe);

        // "Safely" no es decorativo: valida que la URL sea propia, para que nadie use este
        // flujo como trampolín a un sitio ajeno.
        return await RedirectSafelyAsync(ReturnUrl ?? "/", ReturnUrlHash);
    }

    /// <summary>
    /// Los canales válidos: los que ASP.NET Identity da por buenos (correo o teléfono
    /// confirmados, Telegram vinculado) Y que hoy se pueden usar de verdad (Brevo con correo
    /// o con SMS). El elegido, si no vale, pasa al primero de la lista.
    /// </summary>
    private async Task PrepararAsync(IdentityUser usuario)
    {
        var validos = await UserManager.GetValidTwoFactorProvidersAsync(usuario);
        Metodos = Preferencia
            .Where(validos.Contains)
            .Where(m => m != TokenOptions.DefaultEmailProvider
                        || (_enviador.CorreoDisponible && !SeguridadConsts.EsCorreoProvisional(usuario.Email)))
            .Where(m => m != TokenOptions.DefaultPhoneProvider || _enviador.SmsDisponible)
            .ToList();

        if (Metodo == null || !Metodos.Contains(Metodo))
        {
            Metodo = Metodos.FirstOrDefault();
        }

        Destino = Metodo switch
        {
            var m when m == TokenOptions.DefaultEmailProvider => EnmascararCorreo(usuario.Email),
            var m when m == TokenOptions.DefaultPhoneProvider => EnmascararTelefono(usuario.PhoneNumber),
            var m when m == TelegramTwoFactorConsts.ProviderName =>
                (await _vinculos.FindByUserIdAsync(usuario.Id))?.TelegramUserName is { } u ? "@" + u : "tu Telegram",
            _ => null,
        };
    }

    /// <returns>Si salió un código.</returns>
    private async Task<bool> EnviarCodigoAsync(IdentityUser usuario, bool avisarSiEspera)
    {
        if (Metodo == null)
        {
            Avisar("danger", "Tu cuenta no tiene ahora mismo ningún medio para recibir el código. Pide ayuda a quien administra la consola.");
            return false;
        }

        var canal = Metodo == TokenOptions.DefaultPhoneProvider ? CanalDeCodigo.Sms : CanalDeCodigo.Correo;
        try
        {
            await _limitador.ExigirPuedeEnviarAsync(usuario.Id, canal);
        }
        catch (BusinessException)
        {
            // Recargar la página no debe mandar otro código ni asustar: el anterior sigue
            // valiendo. Solo se avisa cuando alguien pidió reenviar a propósito.
            if (avisarSiEspera)
            {
                Avisar("warning", "Acabamos de enviarte un código; espera un momento antes de pedir otro.");
            }

            return false;
        }

        // Correo y SMS: cada código nuevo anula el anterior (los de ABP son de un solo uso).
        // Telegram es TOTP: en la misma ventana sale el mismo. Por eso el limitador de arriba
        // frena los reenvíos seguidos.
        var codigo = await UserManager.GenerateTwoFactorTokenAsync(usuario, Metodo);

        if (Metodo == TelegramTwoFactorConsts.ProviderName)
        {
            var vinculo = await _vinculos.FindByUserIdAsync(usuario.Id);
            var entregado = vinculo?.ChatId != null && await _telegram.SendToChatAsync(
                vinculo.ChatId.Value,
                "🔐 Código para iniciar sesión en Auxiliar contable\n\n" +
                $"{codigo}\n\n" +
                $"Vence en {SeguridadConsts.MinutosDeValidez} minutos. " +
                "Si no estás intentando entrar, alguien conoce tu contraseña: cámbiala.");
            if (!entregado)
            {
                Avisar("danger", "No se pudo enviar el código por Telegram. Prueba otro medio o inténtalo en un momento.");
                EnvioFallido = true;
                return false;
            }
        }
        else
        {
            try
            {
                if (Metodo == TokenOptions.DefaultPhoneProvider)
                {
                    await _enviador.EnviarPorSmsAsync(usuario.PhoneNumber!, codigo, PropositoDeCodigo.IniciarSesion);
                }
                else
                {
                    await _enviador.EnviarPorCorreoAsync(usuario.Email!, codigo, PropositoDeCodigo.IniciarSesion);
                }
            }
            catch (System.Exception ex)
            {
                // Si Brevo falla, la persona tiene que poder elegir otro medio: un 500 aquí la
                // dejaría fuera de su cuenta sin explicación.
                Logger.LogError(ex, "No se pudo enviar el código de acceso por {Metodo}.", Metodo);
                Avisar("danger", $"No se pudo enviar el código por {NombreDe(Metodo)}. Inténtalo en un momento o elige otro medio.");
                EnvioFallido = true;
                return false;
            }
        }

        await _limitador.RegistrarEnvioAsync(usuario.Id, canal);
        return true;
    }

    public static string NombreDe(string metodo) => metodo switch
    {
        var m when m == TokenOptions.DefaultEmailProvider => "correo",
        var m when m == TokenOptions.DefaultPhoneProvider => "SMS",
        _ => "Telegram",
    };

    private IActionResult VolverAlLogin()
    {
        return RedirectToPage("./Login", new { returnUrl = ReturnUrl, returnUrlHash = ReturnUrlHash });
    }

    private static string? EnmascararCorreo(string? correo)
    {
        if (string.IsNullOrEmpty(correo) || !correo.Contains('@'))
        {
            return null;
        }

        var partes = correo.Split('@');
        var local = partes[0];
        return (local.Length <= 2 ? local[..1] : local[..2]) + "•••@" + partes[1];
    }

    private static string? EnmascararTelefono(string? telefono)
    {
        var digitos = new string((telefono ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digitos.Length >= 4 ? "•••• " + digitos[^4..] : null;
    }
}
