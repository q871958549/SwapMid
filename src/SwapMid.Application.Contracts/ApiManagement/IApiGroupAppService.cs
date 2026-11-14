using System;
using Volo.Abp.Application.Services;

namespace SwapMid.ApiManagement;

public interface IApiGroupAppService
    : ICrudAppService<ApiGroupDto, Guid, GetApiGroupListInput, ApiGroupCreateUpdateDto>
{
}
