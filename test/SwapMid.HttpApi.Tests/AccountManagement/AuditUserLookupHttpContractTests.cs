using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading;
using SwapMid.AccountManagement;
using SwapMid.Controllers.AccountManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Auditing;
using Volo.Abp.Uow;

namespace SwapMid.HttpApi.Tests.AccountManagement;

public class AuditUserLookupHttpContractTests
{
    [Fact]
    public void Endpoint_Should_Require_Platform_Scope_And_Disable_Auditing()
    {
        AssertAuthenticationOnlyAndAuditingDisabled(typeof(AuditUserLookupController));
        AssertAuthenticationOnlyAndAuditingDisabled(typeof(AuditUserLookupAppService));
        AssertNonTransactionalUnitOfWork(typeof(AuditUserLookupController));
        AssertNonTransactionalUnitOfWork(typeof(AuditUserLookupAppService));

        var controllerType = typeof(AuditUserLookupController);
        controllerType.GetCustomAttribute<RouteAttribute>(inherit: true)!
            .Template.ShouldBe("api/v1/audit-users");
        controllerType.GetMethod(nameof(IAuditUserLookupAppService.LookupAsync))!
            .GetCustomAttribute<HttpPostAttribute>(inherit: true)!
            .Template.ShouldBe("lookup");
        typeof(IActionFilter).IsAssignableFrom(controllerType).ShouldBeTrue();
    }

    [Fact]
    public void Response_Dto_Should_Not_Expose_Contact_Or_Employee_Pii()
    {
        typeof(AuditUserLookupDto)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ShouldBe(new[]
            {
                nameof(AuditUserLookupDto.DisplayName),
                nameof(AuditUserLookupDto.Id),
                nameof(AuditUserLookupDto.UserName),
            });
    }

    [Fact]
    public async Task Endpoint_Should_Reject_Anonymous_But_Allow_Authenticated_User_Without_Domain_Permissions()
    {
        var service = Substitute.For<IAuditUserLookupAppService>();
        var userId = Guid.NewGuid();
        service.LookupAsync(Arg.Any<AuditUserLookupInput>(), Arg.Any<CancellationToken>())
            .Returns(new ListResultDto<AuditUserLookupDto>(
            [
                new AuditUserLookupDto { Id = userId, DisplayName = "审计用户" },
            ]));
        using var host = CreateHost(service);
        using var anonymousClient = host.CreateClient(authenticated: false);
        using var thirdPartyClient = host.CreateClientWithScopes(
            authenticated: true,
            scopes: ["external-integration"]);
        using var authenticatedClient = host.CreateClient(authenticated: true);

        var anonymousResponse = await anonymousClient.PostAsJsonAsync(
            "/api/v1/audit-users/lookup",
            new AuditUserLookupInput { Ids = [userId] });
        var authenticatedResponse = await authenticatedClient.PostAsJsonAsync(
            "/api/v1/audit-users/lookup",
            new AuditUserLookupInput { Ids = [userId] });
        var thirdPartyResponse = await thirdPartyClient.PostAsJsonAsync(
            "/api/v1/audit-users/lookup",
            new AuditUserLookupInput { Ids = [userId] });

        anonymousResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        thirdPartyResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        authenticatedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await authenticatedResponse.Content
            .ReadFromJsonAsync<ListResultDto<AuditUserLookupDto>>();
        payload.ShouldNotBeNull();
        payload!.Items.ShouldHaveSingleItem().DisplayName.ShouldBe("审计用户");
        await service.Received(1).LookupAsync(
            Arg.Is<AuditUserLookupInput>(input => input.Ids.SequenceEqual(new[] { userId })),
            Arg.Any<CancellationToken>());
    }

    private static ContractTestHost CreateHost(IAuditUserLookupAppService service)
        => new(configureServices: services => services.AddSingleton(service));

    private static void AssertAuthenticationOnlyAndAuditingDisabled(Type type)
    {
        type.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).ShouldBeEmpty();
        var authorize = type.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .ShouldHaveSingleItem();
        authorize.Policy.ShouldBeNullOrWhiteSpace();
        type.GetCustomAttribute<DisableAuditingAttribute>(inherit: true).ShouldNotBeNull();
    }

    private static void AssertNonTransactionalUnitOfWork(Type type)
    {
        var unitOfWork = type.GetCustomAttribute<UnitOfWorkAttribute>(inherit: true);
        unitOfWork.ShouldNotBeNull();
        unitOfWork!.IsTransactional.ShouldBe(false);
    }
}
