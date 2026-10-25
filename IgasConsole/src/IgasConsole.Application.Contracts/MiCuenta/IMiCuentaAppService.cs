using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace IgasConsole.MiCuenta;

public interface IMiCuentaAppService : IApplicationService
{
    Task<MiCuentaDto> GetAsync();
}
