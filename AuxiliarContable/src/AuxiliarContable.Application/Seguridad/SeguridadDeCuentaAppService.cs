using System;
using System.Threading.Tasks;
using AuxiliarContable.Notifications;
using AuxiliarContable.Telegram;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.Users;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace AuxiliarContable.Seguridad;

/// <summary>
/// Ver <see cref="ISeguridadDeCuentaAppService"/>.
///
/// <para>Los códigos son los de ABP para «Email» y «Phone» (AbpTwoFactorTokenProvider, verificado
/// en <c>refs/abp</c>): de un solo uso, cifrados en AbpUserTokens y con caducidad. El propósito
/// lleva el destino, así que un código pedido para un correo no confirma otro, y pedir uno
/// nuevo anula el anterior.</para>
/// </summary>
[Authorize]
public class SeguridadDeCuentaAppService : AuxiliarContableAppService, ISeguridadDeCuentaAppService
{
    private const string PropositoCorreo = "auxiliar:verificar-correo:";

    private readonly IdentityUserManager _usuarios;
    private readonly IEnviadorDeCodigos _enviador;
    private readonly LimitadorDeCodigos _limitador;
    private readonly ITelegramLinkRepository _telegram;

    public SeguridadDeCuentaAppService(
        IdentityUserManager usuarios,
        IEnviadorDeCodigos enviador,
        LimitadorDeCodigos limitador,
        ITelegramLinkRepository telegram)
    {
        _usuarios = usuarios;
        _enviador = enviador;
        _limitador = limitador;
        _telegram = telegram;
    }

    public async Task<EstadoDeSeguridadDto> GetAsync()
    {
        return await EstadoAsync(await UsuarioActualAsync());
    }

    public async Task EnviarCodigoCorreoAsync(CorreoDto input)
    {
        if (!_enviador.CorreoDisponible)
        {
            throw new BusinessException("AuxiliarContable:InvitacionCorreoNoConfigurado");
        }

        var usuario = await UsuarioActualAsync();
        var correo = NormalizarCorreo(input.Correo);
        await ExigirCorreoLibreAsync(usuario, correo);
        await _limitador.ExigirPuedeEnviarAsync(usuario.Id, CanalDeCodigo.Correo);

        var codigo = await _usuarios.GenerateUserTokenAsync(usuario, TokenOptions.DefaultEmailProvider, PropositoCorreo + correo);
        await _enviador.EnviarPorCorreoAsync(correo, codigo, PropositoDeCodigo.VerificarCorreo);
        await _limitador.RegistrarEnvioAsync(usuario.Id, CanalDeCodigo.Correo);
    }

    public async Task<EstadoDeSeguridadDto> ConfirmarCorreoAsync(ConfirmarCorreoDto input)
    {
        var usuario = await UsuarioActualAsync();
        var correo = NormalizarCorreo(input.Correo);
        await _limitador.ExigirPuedeIntentarAsync(usuario.Id);

        var valido = await _usuarios.VerifyUserTokenAsync(
            usuario, TokenOptions.DefaultEmailProvider, PropositoCorreo + correo, LimpiarCodigo(input.Codigo));
        if (!valido)
        {
            await _limitador.RegistrarFalloAsync(usuario.Id);
            throw new BusinessException("AuxiliarContable:CodigoIncorrecto");
        }

        await ExigirCorreoLibreAsync(usuario, correo);
        (await _usuarios.SetEmailAsync(usuario, correo)).CheckErrors();
        usuario.SetEmailConfirmed(true);
        (await _usuarios.UpdateAsync(usuario)).CheckErrors();
        await _limitador.RegistrarAciertoAsync(usuario.Id);

        return await EstadoAsync(usuario);
    }

    public async Task EnviarCodigoTelefonoAsync(TelefonoDto input)
    {
        if (!_enviador.SmsDisponible)
        {
            throw new BusinessException("AuxiliarContable:SmsNoDisponible");
        }

        var usuario = await UsuarioActualAsync();
        var telefono = "+" + NumeroDeTelefono.Normalizar(input.Telefono);
        await _limitador.ExigirPuedeEnviarAsync(usuario.Id, CanalDeCodigo.Sms);

        var codigo = await _usuarios.GenerateChangePhoneNumberTokenAsync(usuario, telefono);
        await _enviador.EnviarPorSmsAsync(telefono, codigo, PropositoDeCodigo.VerificarTelefono);
        await _limitador.RegistrarEnvioAsync(usuario.Id, CanalDeCodigo.Sms);
    }

    public async Task<EstadoDeSeguridadDto> ConfirmarTelefonoAsync(ConfirmarTelefonoDto input)
    {
        var usuario = await UsuarioActualAsync();
        var telefono = "+" + NumeroDeTelefono.Normalizar(input.Telefono);
        await _limitador.ExigirPuedeIntentarAsync(usuario.Id);

        // ChangePhoneNumberAsync valida el código contra ESE número y, si vale, lo guarda
        // confirmado y renueva el sello de seguridad.
        var resultado = await _usuarios.ChangePhoneNumberAsync(usuario, telefono, LimpiarCodigo(input.Codigo));
        if (!resultado.Succeeded)
        {
            await _limitador.RegistrarFalloAsync(usuario.Id);
            throw new BusinessException("AuxiliarContable:CodigoIncorrecto");
        }

        await _limitador.RegistrarAciertoAsync(usuario.Id);
        return await EstadoAsync(usuario);
    }

    public async Task<EstadoDeSeguridadDto> GuardarTelefonoAsync(TelefonoDto input)
    {
        var usuario = await UsuarioActualAsync();
        var telefono = "+" + NumeroDeTelefono.Normalizar(input.Telefono);

        if (usuario.PhoneNumber != telefono)
        {
            // SetPhoneNumberAsync lo deja SIN confirmar: un número que nadie verificó no
            // puede servir de segundo factor.
            (await _usuarios.SetPhoneNumberAsync(usuario, telefono)).CheckErrors();
        }

        return await EstadoAsync(usuario);
    }

    public async Task<EstadoDeSeguridadDto> SetSegundoFactorAsync(SegundoFactorDto input)
    {
        var usuario = await UsuarioActualAsync();

        if (input.Activo && !await TieneMetodoVerificadoAsync(usuario))
        {
            throw new BusinessException("AuxiliarContable:SegundoFactorSinMetodo");
        }

        (await _usuarios.SetTwoFactorEnabledAsync(usuario, input.Activo)).CheckErrors();
        return await EstadoAsync(usuario);
    }

    public async Task<CodigosDeRecuperacionDto> GenerarCodigosDeRecuperacionAsync()
    {
        var usuario = await UsuarioActualAsync();
        if (!usuario.TwoFactorEnabled)
        {
            throw new BusinessException("AuxiliarContable:RecuperacionSinSegundoFactor");
        }

        // ASP.NET Identity los guarda en AbpUserTokens (IdentityUserStore.ReplaceCodesAsync) y
        // borra cada uno al usarlo. Generar otros diez invalida los anteriores.
        var codigos = await _usuarios.GenerateNewTwoFactorRecoveryCodesAsync(usuario, SeguridadConsts.CodigosDeRecuperacion);
        return new CodigosDeRecuperacionDto { Codigos = [.. codigos ?? []] };
    }

    // --------------------------------------------------------------------------------

    private async Task<IdentityUser> UsuarioActualAsync()
    {
        return await _usuarios.GetByIdAsync(CurrentUser.GetId());
    }

    private async Task<EstadoDeSeguridadDto> EstadoAsync(IdentityUser usuario)
    {
        var provisional = SeguridadConsts.EsCorreoProvisional(usuario.Email);
        var telegram = (await _telegram.FindByUserIdAsync(usuario.Id))?.EstaVinculado ?? false;

        var dto = new EstadoDeSeguridadDto
        {
            Correo = provisional ? null : usuario.Email,
            CorreoConfirmado = !provisional && usuario.EmailConfirmed,
            Telefono = usuario.PhoneNumber,
            TelefonoConfirmado = usuario.PhoneNumberConfirmed,
            SegundoFactorActivo = usuario.TwoFactorEnabled,
            TelegramVinculado = telegram,
            CorreoDisponible = _enviador.CorreoDisponible,
            SmsDisponible = _enviador.SmsDisponible,
            CodigosDeRecuperacionRestantes = await _usuarios.CountRecoveryCodesAsync(usuario),
        };

        // Sin SMS, un teléfono guardado es lo más que se puede pedir.
        var telefonoPendiente = _enviador.SmsDisponible ? !dto.TelefonoConfirmado : string.IsNullOrEmpty(dto.Telefono);
        dto.Pendientes = (dto.CorreoConfirmado ? 0 : 1) + (telefonoPendiente ? 1 : 0) + (dto.SegundoFactorActivo ? 0 : 1);
        return dto;
    }

    /// <summary>Lo que hace falta para no quedarse fuera con la verificación en dos pasos activa.</summary>
    private async Task<bool> TieneMetodoVerificadoAsync(IdentityUser usuario)
    {
        var correo = _enviador.CorreoDisponible && usuario.EmailConfirmed && !SeguridadConsts.EsCorreoProvisional(usuario.Email);
        var sms = _enviador.SmsDisponible && usuario.PhoneNumberConfirmed;
        var telegram = (await _telegram.FindByUserIdAsync(usuario.Id))?.EstaVinculado ?? false;
        return correo || sms || telegram;
    }

    private async Task ExigirCorreoLibreAsync(IdentityUser usuario, string correo)
    {
        if (SeguridadConsts.EsCorreoProvisional(correo))
        {
            throw new BusinessException("AuxiliarContable:CorreoNoValido");
        }

        var otro = await _usuarios.FindByEmailAsync(correo);
        if (otro != null && otro.Id != usuario.Id)
        {
            throw new BusinessException("AuxiliarContable:InvitacionCorreoConCuenta").WithData("email", correo);
        }
    }

    private static string NormalizarCorreo(string correo) => correo.Trim().ToLowerInvariant();

    private static string LimpiarCodigo(string codigo) => codigo.Replace(" ", string.Empty).Trim();
}
