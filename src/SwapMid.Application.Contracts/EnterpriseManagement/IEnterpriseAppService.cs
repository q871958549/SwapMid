using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace SwapMid.EnterpriseManagement;

public interface IEnterpriseAppService
    : ICrudAppService<EnterpriseDto, Guid, GetEnterpriseListInput, EnterpriseCreateUpdateDto>
{
    Task SetAsMainAsync(Guid id);
    Task<EnterpriseDto?> GetMainAsync();
}
