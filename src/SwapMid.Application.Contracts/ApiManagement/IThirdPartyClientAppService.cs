using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace SwapMid.ApiManagement;

/// <summary>
/// 第三方应用/凭据管理：封装 OpenIddict ClientCredentials 客户端的签发/吊销（见 ADR-0015）。
/// 每个客户一个 client，仅授予 product-query 窄 scope，凭 client_credentials 换 token 调对外查询 API。
/// </summary>
public interface IThirdPartyClientAppService : IApplicationService
{
    /// <summary>列出全部第三方应用（仅含授予 product-query scope 的客户端）。</summary>
    Task<List<ThirdPartyClientDto>> GetListAsync();

    /// <summary>创建第三方应用，返回明文凭据（client_id + client_secret，仅此一次）。</summary>
    Task<ThirdPartyClientCredentialDto> CreateAsync(CreateThirdPartyClientDto input);

    /// <summary>重置指定客户端的 secret，返回新明文凭据（旧 secret 立即失效）。</summary>
    Task<ThirdPartyClientCredentialDto> ResetSecretAsync(string clientId);

    /// <summary>删除（吊销）第三方应用。</summary>
    Task DeleteAsync(string clientId);
}
