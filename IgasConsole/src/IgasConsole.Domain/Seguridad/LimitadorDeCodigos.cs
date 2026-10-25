using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Timing;

namespace IgasConsole.Seguridad;

/// <summary>
/// Pone techo a los códigos de un solo uso: cada cuánto se puede pedir uno, cuántos por hora
/// y cuántos intentos fallidos antes de bloquear un rato.
///
/// <para>Por cuenta y no por IP: quien tiene la contraseña de alguien (o su sesión) puede
/// cambiar de IP, pero no de cuenta. Y un SMS cuesta dinero: sin esto, cualquiera con una
/// sesión podría gastar el saldo de Brevo pulsando «reenviar».</para>
///
/// <para>Vive en la caché distribuida (en este despliegue, la de memoria del proceso): si se
/// reinicia el servicio se olvida, y está bien — el peor caso es permitir unos pocos envíos de
/// más, no dejar pasar un código malo.</para>
/// </summary>
public class LimitadorDeCodigos : ITransientDependency
{
    private readonly IDistributedCache<EstadoDeCodigos> _cache;
    private readonly IClock _reloj;

    public LimitadorDeCodigos(IDistributedCache<EstadoDeCodigos> cache, IClock reloj)
    {
        _cache = cache;
        _reloj = reloj;
    }

    public async Task ExigirPuedeEnviarAsync(Guid usuarioId, CanalDeCodigo canal)
    {
        var estado = await LeerAsync(usuarioId);
        var ahora = _reloj.Now;
        var envios = estado.Envios(canal).Where(e => e > ahora.AddHours(-1)).ToList();

        if (envios.Count > 0 && (ahora - envios.Max()).TotalSeconds < SeguridadConsts.SegundosEntreEnvios)
        {
            var espera = SeguridadConsts.SegundosEntreEnvios - (int)(ahora - envios.Max()).TotalSeconds;
            throw new BusinessException("IgasConsole:CodigoEsperaEntreEnvios").WithData("segundos", Math.Max(1, espera));
        }

        if (envios.Count >= SeguridadConsts.MaxEnviosPorHora)
        {
            throw new BusinessException("IgasConsole:CodigoDemasiadosEnvios");
        }
    }

    public async Task RegistrarEnvioAsync(Guid usuarioId, CanalDeCodigo canal)
    {
        var estado = await LeerAsync(usuarioId);
        var ahora = _reloj.Now;
        var lista = estado.Envios(canal);
        lista.RemoveAll(e => e <= ahora.AddHours(-1));
        lista.Add(ahora);
        await GuardarAsync(usuarioId, estado);
    }

    public async Task ExigirPuedeIntentarAsync(Guid usuarioId)
    {
        var estado = await LeerAsync(usuarioId);
        if (estado.BloqueadoHasta.HasValue && estado.BloqueadoHasta.Value > _reloj.Now)
        {
            throw new BusinessException("IgasConsole:CodigoDemasiadosFallos")
                .WithData("minutos", Math.Max(1, (int)Math.Ceiling((estado.BloqueadoHasta.Value - _reloj.Now).TotalMinutes)));
        }
    }

    public async Task RegistrarFalloAsync(Guid usuarioId)
    {
        var estado = await LeerAsync(usuarioId);
        estado.Fallos++;
        if (estado.Fallos >= SeguridadConsts.MaxFallos)
        {
            estado.BloqueadoHasta = _reloj.Now.AddMinutes(SeguridadConsts.MinutosDeBloqueo);
            estado.Fallos = 0;
        }

        await GuardarAsync(usuarioId, estado);
    }

    public async Task RegistrarAciertoAsync(Guid usuarioId)
    {
        var estado = await LeerAsync(usuarioId);
        estado.Fallos = 0;
        estado.BloqueadoHasta = null;
        await GuardarAsync(usuarioId, estado);
    }

    private async Task<EstadoDeCodigos> LeerAsync(Guid usuarioId)
    {
        return await _cache.GetAsync(Clave(usuarioId)) ?? new EstadoDeCodigos();
    }

    private Task GuardarAsync(Guid usuarioId, EstadoDeCodigos estado)
    {
        return _cache.SetAsync(Clave(usuarioId), estado, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2),
        });
    }

    private static string Clave(Guid usuarioId) => "codigos:" + usuarioId.ToString("N");
}

[CacheName("IgasConsole.EstadoDeCodigos")]
public class EstadoDeCodigos
{
    public List<DateTime> EnviosCorreo { get; set; } = [];

    public List<DateTime> EnviosSms { get; set; } = [];

    public int Fallos { get; set; }

    public DateTime? BloqueadoHasta { get; set; }

    public List<DateTime> Envios(CanalDeCodigo canal) => canal == CanalDeCodigo.Sms ? EnviosSms : EnviosCorreo;
}
