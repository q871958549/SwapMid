using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace SwapMid.AccountManagement;

public interface IAccountPermissionAppService : IApplicationService
{
    Task<AccountRolePermissionDto> GetRolePermissionsAsync(string roleName);
    Task UpdateRolePermissionsAsync(string roleName, AccountPermissionUpdateDto input);
}
