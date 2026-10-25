using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using IgasConsole.Sessions;
using Microsoft.AspNetCore.Identity;
using Volo.Abp.AspNetCore.WebClientInfo;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace IgasConsole.HealthChecks;

/// <summary>
/// Registra una sesión al iniciar sesión y vuelve a firmar la cookie con su identificador.
///
/// <para>⚠️ ABP open source trae la entidad <c>IdentitySession</c>, su repositorio y la
/// tabla, pero <b>NADA las llena</b>: poblarlas es de Account Pro. Esto es esa pieza que
/// falta.</para>
///
/// <para>Lo que sí regala el tier abierto es el transporte: si el principal lleva un claim
/// <c>session_id</c>, <c>AbpDefaultOpenIddictClaimsPrincipalHandler</c> ya lo copia al
/// access token y al id token. Por eso basta con meterlo en la cookie: de ahí viaja solo a
/// los tokens que use el front.</para>
/// </summary>
public class RegistradorDeSesiones : ITransientDependency
{
    private readonly IIdentitySessionRepository _sesiones;
    private readonly IWebClientInfoProvider _infoCliente;
    private readonly IGuidGenerator _guids;

    public RegistradorDeSesiones(
        IIdentitySessionRepository sesiones,
        IWebClientInfoProvider infoCliente,
        IGuidGenerator guids)
    {
        _sesiones = sesiones;
        _infoCliente = infoCliente;
        _guids = guids;
    }

    /// <summary>
    /// Crea la fila de sesión y REFIRMA la cookie para que lleve el claim.
    /// <para>Hay que refirmar porque <c>PasswordSignInAsync</c> y
    /// <c>TwoFactorSignInAsync</c> ya emitieron la cookie sin él, y un claim que no está en
    /// la cookie no llega ni a las páginas ni, después, a los tokens.</para>
    /// </summary>
    public async Task RegistrarAsync(
        SignInManager<IdentityUser> signInManager,
        IdentityUser usuario,
        bool recordarme)
    {
        var sessionId = GenerarSessionId();

        var sesion = new IdentitySession(
            _guids.Create(),
            sessionId,
            device: SessionConsts.WebClientId,
            deviceInfo: Recortar(_infoCliente.BrowserInfo, IdentitySessionConsts.MaxDeviceInfoLength),
            userId: usuario.Id,
            tenantId: usuario.TenantId,
            clientId: SessionConsts.WebClientId,
            ipAddresses: Recortar(_infoCliente.ClientIpAddress, IdentitySessionConsts.MaxIpAddressesLength),
            signedIn: DateTime.UtcNow,
            lastAccessed: DateTime.UtcNow);

        await _sesiones.InsertAsync(sesion, autoSave: true);

        await signInManager.SignInWithClaimsAsync(
            usuario,
            recordarme,
            [new Claim(AbpClaimTypes.SessionId, sessionId)]);
    }

    /// <summary>
    /// Identificador aleatorio. No es adivinable a propósito: aunque solo sirve para
    /// reconocer una sesión, viaja en los tokens y aparece en la lista de sesiones.
    /// </summary>
    private static string GenerarSessionId()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

    private static string Recortar(string? valor, int maximo)
    {
        if (valor.IsNullOrWhiteSpace())
        {
            return string.Empty;
        }

        // La columna tiene tope y un User-Agent largo lo pasa sin esfuerzo. Recortar aquí
        // evita que un navegador raro haga fallar el login entero.
        return valor!.Length <= maximo ? valor : valor[..maximo];
    }
}
