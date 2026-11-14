using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace SwapMid.ApiManagement;

public interface IApiRouteAppService
    : ICrudAppService<ApiRouteDto, Guid, GetApiRouteListInput, ApiRouteCreateUpdateDto>
{
    Task EnableAsync(Guid id);
    Task DisableAsync(Guid id);
}
