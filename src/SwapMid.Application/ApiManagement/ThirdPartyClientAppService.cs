using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using SwapMid.Permissions;
using SwapMid.Security;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.OpenIddict.Applications;

namespace SwapMid.ApiManagement;

/// <summary>
/// 管理通过 OpenIddict client_credentials 授权调用对外接口的第三方客户端。
/// 通过是否包含 <c>scp:external-integration</c> 权限区分第三方客户端与平台自身客户端（SwapMid_App、SwapMid_Swagger），
/// 避免误删平台客户端导致控制台登录瘫痪。
/// </summary>
[Authorize(SwapMidPermissions.ApiManagement.ThirdPartyClients.Default)]
public class ThirdPartyClientAppService : SwapMidAppService, IThirdPartyClientAppService
{
    private const string ScopePermission =
        OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictScopeChecker.ExternalIntegrationScope;

    private readonly IAbpApplicationManager _applicationManager;
    private readonly IOpenIddictApplicationRepository _applicationRepository;

    public ThirdPartyClientAppService(
        IAbpApplicationManager applicationManager,
        IOpenIddictApplicationRepository applicationRepository)
    {
        _applicationManager = applicationManager;
        _applicationRepository = applicationRepository;
    }

    public async Task<List<ThirdPartyClientDto>> GetListAsync()
    {
        var apps = await _applicationRepository.GetListAsync(
            sorting: nameof(OpenIddictApplication.CreationTime) + " desc",
            maxResultCount: 500,
            skipCount: 0);

        return apps
            .Where(app => ParseJsonArray(app.Permissions).Contains(ScopePermission))
            .Select(app => new ThirdPartyClientDto
            {
                Id = app.Id,
                ClientId = app.ClientId ?? string.Empty,
                DisplayName = app.DisplayName ?? app.ClientId ?? string.Empty,
                CreationTime = app.CreationTime
            })
            .ToList();
    }

    [Authorize(SwapMidPermissions.ApiManagement.ThirdPartyClients.Create)]
    public async Task<ThirdPartyClientCredentialDto> CreateAsync(CreateThirdPartyClientDto input)
    {
        var clientId = string.IsNullOrWhiteSpace(input.ClientId)
            ? $"app-{RandomNumberGenerator.GetHexString(8, lowercase: true)}"
            : input.ClientId.Trim();

        if (await _applicationManager.FindByClientIdAsync(clientId) is not null)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.ApiManagement.ThirdPartyClientIdDuplicate)
                .WithData("ClientId", clientId);
        }

        var secret = GenerateSecret();
        var descriptor = new AbpApplicationDescriptor
        {
            ApplicationType = OpenIddictConstants.ApplicationTypes.Web,
            ClientId = clientId,
            ClientSecret = secret,
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            DisplayName = input.DisplayName.Trim(),
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                ScopePermission
            }
        };

        var created = await _applicationManager.CreateAsync(descriptor);
        var model = (OpenIddictApplicationModel)created;

        return new ThirdPartyClientCredentialDto
        {
            Id = model.Id,
            ClientId = clientId,
            DisplayName = descriptor.DisplayName!,
            ClientSecret = secret
        };
    }

    [Authorize(SwapMidPermissions.ApiManagement.ThirdPartyClients.Update)]
    public async Task<ThirdPartyClientCredentialDto> ResetSecretAsync(string clientId)
    {
        var entity = await FindByClientIdOrThrowAsync(clientId);
        EnsureThirdPartyManagedClient(entity);

        var model = entity.ToModel();
        var descriptor = new AbpApplicationDescriptor();
        await _applicationManager.PopulateAsync(descriptor, model);

        var secret = GenerateSecret();
        descriptor.ClientSecret = secret;
        await _applicationManager.UpdateAsync(model, descriptor);

        return new ThirdPartyClientCredentialDto
        {
            Id = entity.Id,
            ClientId = entity.ClientId ?? string.Empty,
            DisplayName = entity.DisplayName ?? entity.ClientId ?? string.Empty,
            ClientSecret = secret
        };
    }

    [Authorize(SwapMidPermissions.ApiManagement.ThirdPartyClients.Delete)]
    public async Task DeleteAsync(string clientId)
    {
        var entity = await FindByClientIdOrThrowAsync(clientId);
        EnsureThirdPartyManagedClient(entity);

        await _applicationManager.DeleteAsync(entity.ToModel());
    }

    private async Task<OpenIddictApplication> FindByClientIdOrThrowAsync(string clientId)
    {
        var normalized = clientId?.Trim() ?? string.Empty;
        var entity = await _applicationRepository.FindByClientIdAsync(normalized);
        if (entity is null)
        {
            throw new EntityNotFoundException(typeof(OpenIddictApplication), normalized);
        }

        return entity;
    }

    private static void EnsureThirdPartyManagedClient(OpenIddictApplication entity)
    {
        if (!ParseJsonArray(entity.Permissions).Contains(ScopePermission))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.ApiManagement.ThirdPartyClientIdDuplicate)
                .WithData("ClientId", entity.ClientId ?? entity.Id.ToString("D"));
        }
    }

    private static string GenerateSecret()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static HashSet<string> ParseJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var items = JsonSerializer.Deserialize<List<string>>(json);
        return items is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(items, StringComparer.Ordinal);
    }
}
