using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.MiCuenta;

public interface IMiCuentaAppService : IApplicationService
{
    Task<MiCuentaDto> GetAsync();
}
