using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.Telegram;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable;

/// <summary>
/// Doble de prueba de <see cref="ITelegramMessenger"/>. Hace falta por lo mismo que
/// <see cref="AlertNotifierDePrueba"/>: la implementación real vive en HttpApi.Host y estos
/// tests cargan Application + EntityFrameworkCore, pero no el Host. Sin esto,
/// <c>TelegramLinkAppService</c> no se puede ni construir.
/// <para>No sale a la red: apunta lo que se le pidió enviar, por si algún test quiere
/// comprobarlo.</para>
/// </summary>
/// <remarks>
/// ExposeServices es OBLIGATORIO: el registro por convención de ABP solo expone
/// automáticamente las interfaces cuyo nombre es sufijo del de la clase.
/// "TelegramMessengerDePrueba" no termina en "TelegramMessenger".
/// </remarks>
[ExposeServices(typeof(ITelegramMessenger))]
public class TelegramMessengerDePrueba : ITelegramMessenger, ISingletonDependency
{
    public List<(long ChatId, string Texto)> Enviados { get; } = new();

    /// <summary>Para simular que Telegram rechaza el mensaje.</summary>
    public bool EntregaCorrecta { get; set; } = true;

    public Task<bool> SendToChatAsync(long chatId, string texto, CancellationToken cancellationToken = default)
    {
        Enviados.Add((chatId, texto));
        return Task.FromResult(EntregaCorrecta);
    }

    public Task<string?> GetBotUserNameAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>("bot_de_prueba");
    }

    public Task<bool> IsBotConfiguredAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}
