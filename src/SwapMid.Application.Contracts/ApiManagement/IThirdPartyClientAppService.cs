using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace SwapMid.ApiManagement;

/// <summary>
/// 第三方应用/凭据管理：封装 OpenIddict ClientCredentials 客户端的签发、密钥轮换、吊销与网关访问治理策略。
/// </summary>
public interface IThirdPartyClientAppService : IApplicationService
{
    /// <summary>列出全部第三方应用（仅含授予 external-integration scope 的客户端）。</summary>
    Task<List<ThirdPartyClientDto>> GetListAsync();

    /// <summary>创建第三方应用，返回明文凭据（client_id + client_secret，仅此一次）。</summary>
    Task<ThirdPartyClientCredentialDto> CreateAsync(CreateThirdPartyClientDto input);

    /// <summary>重置指定客户端的 secret，返回新明文凭据（旧 secret 立即失效）。</summary>
    Task<ThirdPartyClientCredentialDto> ResetSecretAsync(string clientId);

    /// <summary>更新指定客户端的 API 网关治理策略（允许访问的路由分组白名单与每分钟限流配额）。</summary>
    Task<ThirdPartyClientDto> UpdatePolicyAsync(string clientId, UpdateThirdPartyClientPolicyDto input);

    /// <summary>删除（吊销）第三方应用。</summary>
    Task DeleteAsync(string clientId);
}
