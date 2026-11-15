using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OpenIddict.Abstractions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.OpenIddict.Scopes;
using Volo.Abp.Uow;

namespace SwapMid.OpenIddict;

public class OpenIddictDataSeedContributor : OpenIddictDataSeedContributorBase, IDataSeedContributor, ITransientDependency
{
    public const string ExternalIntegrationScope = "external-integration";

    public OpenIddictDataSeedContributor(
        IConfiguration configuration,
        IOpenIddictApplicationRepository openIddictApplicationRepository,
        IAbpApplicationManager applicationManager,
        IOpenIddictScopeRepository openIddictScopeRepository,
        IOpenIddictScopeManager scopeManager)
        : base(configuration, openIddictApplicationRepository, applicationManager, openIddictScopeRepository, scopeManager)
    {
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        await CreateScopesAsync();
        await CreateApplicationsAsync();
    }

    private async Task CreateScopesAsync()
    {
        await CreateScopesAsync(new OpenIddictScopeDescriptor
        {
            Name = "SwapMid",
            DisplayName = "SwapMid Platform API",
            Resources = { "SwapMid" }
        });

        await CreateScopesAsync(new OpenIddictScopeDescriptor
        {
            Name = ExternalIntegrationScope,
            DisplayName = "SwapMid External Integration Scope",
            Resources = { "SwapMid" }
        });
    }

    private async Task CreateApplicationsAsync()
    {
        var commonScopes = new List<string>
        {
            OpenIddictConstants.Permissions.Scopes.Address,
            OpenIddictConstants.Permissions.Scopes.Email,
            OpenIddictConstants.Permissions.Scopes.Phone,
            OpenIddictConstants.Permissions.Scopes.Profile,
            OpenIddictConstants.Permissions.Scopes.Roles,
            "SwapMid"
        };

        var configurationSection = Configuration.GetSection("OpenIddict:Applications");

        var consoleAndAngularClientId = configurationSection["SwapMid_App:ClientId"];
        if (!consoleAndAngularClientId.IsNullOrWhiteSpace())
        {
            await CreateOrUpdateApplicationAsync(
                applicationType: OpenIddictConstants.ApplicationTypes.Web,
                name: consoleAndAngularClientId!,
                type: OpenIddictConstants.ClientTypes.Public,
                consentType: OpenIddictConstants.ConsentTypes.Implicit,
                displayName: "SwapMid Web Console",
                secret: null,
                grantTypes: new List<string>
                {
                    OpenIddictConstants.GrantTypes.Password,
                    OpenIddictConstants.GrantTypes.ClientCredentials,
                    OpenIddictConstants.GrantTypes.RefreshToken,
                    "LinkLogin",
                    "Impersonation",
                    "external_oidc"
                },
                scopes: commonScopes,
                redirectUris: null,
                postLogoutRedirectUris: null,
                clientUri: null,
                logoUri: null
            );
        }

        var swaggerClientId = configurationSection["SwapMid_Swagger:ClientId"];
        if (!swaggerClientId.IsNullOrWhiteSpace())
        {
            var swaggerRootUrl = configurationSection["SwapMid_Swagger:RootUrl"]?.TrimEnd('/');

            await CreateOrUpdateApplicationAsync(
                applicationType: OpenIddictConstants.ApplicationTypes.Web,
                name: swaggerClientId!,
                type: OpenIddictConstants.ClientTypes.Public,
                consentType: OpenIddictConstants.ConsentTypes.Implicit,
                displayName: "SwapMid Swagger UI",
                secret: null,
                grantTypes: new List<string> { OpenIddictConstants.GrantTypes.AuthorizationCode },
                scopes: commonScopes,
                redirectUris: swaggerRootUrl.IsNullOrWhiteSpace()
                    ? null
                    : new List<string> { $"{swaggerRootUrl}/swagger/oauth2-redirect.html" },
                postLogoutRedirectUris: null,
                clientUri: swaggerRootUrl.IsNullOrWhiteSpace() ? null : swaggerRootUrl!.EnsureEndsWith('/'),
                logoUri: "/images/clients/swagger.svg"
            );
        }
    }
}
