using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace IgasConsole.BackgroundJobs;

/// <summary>
/// El ejecutor de ABP, más una copia de la excepción antes de que se pierda.
///
/// <para>ABP la registra en el log y la envuelve en un <c>BackgroundJobExecutionException</c>
/// que el worker atrapa con un <c>catch</c> SIN parámetro: a partir de ahí solo queda un
/// <c>TryCount</c> más alto. Esta clase la deja anotada en <see cref="IUltimoFalloDeJob"/>
/// para que el worker pueda guardarla junto a la fila del trabajo.</para>
///
/// <para>Se hereda y se llama a <c>base</c> en vez de copiar el cuerpo: la lógica de
/// invocar el trabajo —cambiar de tenant, propagar el token de cancelación, distinguir
/// <c>Execute</c> de <c>ExecuteAsync</c>— sigue siendo la de ABP y se actualiza con él.</para>
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IBackgroundJobExecuter), typeof(BackgroundJobExecuter))]
public class EjecutorQueRecuerdaElFallo : BackgroundJobExecuter, ITransientDependency
{
    private readonly IUltimoFalloDeJob _ultimoFallo;

    public EjecutorQueRecuerdaElFallo(
        IOptions<AbpBackgroundJobOptions> options,
        ICurrentTenant currentTenant,
        IUltimoFalloDeJob ultimoFallo)
        : base(options, currentTenant)
    {
        _ultimoFallo = ultimoFallo;
    }

    public override async Task ExecuteAsync(JobExecutionContext context)
    {
        try
        {
            await base.ExecuteAsync(context);
        }
        catch (BackgroundJobExecutionException ex)
        {
            // La interesante es la de dentro: la de fuera solo dice "falló un trabajo".
            _ultimoFallo.Registrar(ex.InnerException ?? ex);
            throw;
        }
        catch (Exception ex)
        {
            // ABP envuelve todo lo que lanza el trabajo, así que por aquí solo pasan los
            // fallos previos a ejecutarlo (el tipo no está en el contenedor, o no
            // implementa la interfaz esperada). Anotarlos también: son precisamente los
            // que dejan un trabajo abandonado al primer intento.
            _ultimoFallo.Registrar(ex);
            throw;
        }
    }
}
