using System.Net;
using System.Linq;
using System.Reflection;
using SwapMid.Controllers.DataIntegration;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Formatters;
using Shouldly;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.Http;
using Xunit;

namespace SwapMid.DataIntegration;

public class DataIntegrationHttpContractTests
{
    [Theory]
    [InlineData(nameof(PipelineExecutionController.ResolveOutcomeAsync), "{id}/resolve-outcome")]
    [InlineData(nameof(PipelineExecutionController.RecoverRunningAsync), "{id}/recover-running")]
    [InlineData(nameof(PipelineExecutionController.VerifyTargetReceiptAsync), "{id}/verify-target-receipt")]
    public void 高风险对账Action必须精确声明冲突与暂不可用响应(
        string methodName,
        string routeTemplate)
    {
        var action = typeof(PipelineExecutionController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(candidate =>
                candidate.DeclaringType == typeof(PipelineExecutionController) &&
                candidate.Name == methodName &&
                candidate.GetCustomAttribute<HttpPostAttribute>() is not null);

        action.GetCustomAttribute<NonActionAttribute>().ShouldBeNull();
        action.GetCustomAttribute<HttpPostAttribute>()!.Template.ShouldBe(routeTemplate);

        var responses = action
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .OrderBy(attribute => attribute.StatusCode)
            .ToArray();

        responses.Length.ShouldBe(3);
        responses.Select(attribute => attribute.StatusCode).ShouldBe(new[]
        {
            StatusCodes.Status200OK,
            StatusCodes.Status409Conflict,
            StatusCodes.Status503ServiceUnavailable,
        });

        foreach (var response in responses)
        {
            response.Type.ShouldBe(
                response.StatusCode == StatusCodes.Status200OK
                    ? typeof(PipelineExecutionDto)
                    : typeof(RemoteServiceErrorResponse));
            GetContentTypes(response).ShouldBe(
                response.StatusCode == StatusCodes.Status200OK
                    ? Array.Empty<string>()
                    : new[] { "application/json" },
                "成功响应必须继续由 MVC 推断既有媒体类型；错误响应才固定为 JSON 包络");
        }
    }

    [Fact]
    public void 高风险Controller全部重载必须声明精确子权限()
    {
        var cases = new[]
        {
            (typeof(DataPipelineController), "ExecuteAsync", 2,
                SwapMidPermissions.DataIntegration.DataPipelines.Execute),
            (typeof(DataPipelineController), "ActivateAsync", 2,
                SwapMidPermissions.DataIntegration.DataPipelines.Update),
            (typeof(PipelineExecutionController), "ResolveOutcomeAsync", 2,
                SwapMidPermissions.DataIntegration.PipelineExecutions.ResolveOutcome),
            (typeof(PipelineExecutionController), "RecoverRunningAsync", 2,
                SwapMidPermissions.DataIntegration.PipelineExecutions.RecoverRunning),
            (typeof(PipelineExecutionController), "VerifyTargetReceiptAsync", 2,
                SwapMidPermissions.DataIntegration.PipelineExecutions.VerifyTargetReceipt),
        };

        foreach (var (controllerType, methodName, expectedCount, policy) in cases)
        {
            var methods = controllerType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public)
                .Where(candidate =>
                    candidate.DeclaringType == controllerType &&
                    candidate.Name == methodName)
                .ToArray();
            methods.Length.ShouldBe(expectedCount);
            foreach (var method in methods)
            {
                method.GetCustomAttributes<AuthorizeAttribute>()
                    .Select(attribute => attribute.Policy)
                    .ShouldBe(new[] { policy });
            }
        }
    }

    [Fact]
    public void 写Controller必须在转发前声明精确子权限()
    {
        var cases = new[]
        {
            (typeof(DataSourceController), "CreateAsync",
                SwapMidPermissions.DataIntegration.DataSources.Create),
            (typeof(DataSourceController), "UpdateAsync",
                SwapMidPermissions.DataIntegration.DataSources.Update),
            (typeof(DataSourceController), "DeleteAsync",
                SwapMidPermissions.DataIntegration.DataSources.Delete),
            (typeof(DataSourceController), "TestConnectionAsync",
                SwapMidPermissions.DataIntegration.DataSources.Create),
            (typeof(DataSourceController), "TestConnectionByIdAsync",
                SwapMidPermissions.DataIntegration.DataSources.Update),
            (typeof(DataPipelineController), "CreateAsync",
                SwapMidPermissions.DataIntegration.DataPipelines.Create),
            (typeof(DataPipelineController), "UpdateAsync",
                SwapMidPermissions.DataIntegration.DataPipelines.Update),
            (typeof(DataPipelineController), "DeleteAsync",
                SwapMidPermissions.DataIntegration.DataPipelines.Delete),
            (typeof(DataPipelineController), "PauseAsync",
                SwapMidPermissions.DataIntegration.DataPipelines.Update),
        };

        foreach (var (controllerType, methodName, policy) in cases)
        {
            var method = controllerType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public)
                .Single(candidate =>
                    candidate.DeclaringType == controllerType &&
                    candidate.Name == methodName);
            method.GetCustomAttributes<AuthorizeAttribute>()
                .Select(attribute => attribute.Policy)
                .ShouldBe(new[] { policy });
        }
    }

    [Fact]
    public void 执行冲突协调故障与未知态必须使用稳定HTTP状态()
    {
        var options = new AbpExceptionHttpStatusCodeOptions();

        SwapMidHttpApiHostModule
            .ConfigureDataIntegrationStatusCodes(options);

        var mappings = options.ErrorCodeToHttpStatusCodeMappings;
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionInputInvalid]
            .ShouldBe(HttpStatusCode.BadRequest);
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionBusy]
            .ShouldBe(HttpStatusCode.Conflict);
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable]
            .ShouldBe(HttpStatusCode.ServiceUnavailable);
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionOutcomeUnknown]
            .ShouldBe(HttpStatusCode.Conflict);
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionOutcomeUnresolved]
            .ShouldBe(HttpStatusCode.Conflict);
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionResolutionInvalid]
            .ShouldBe(HttpStatusCode.Conflict);
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionTargetBusy]
            .ShouldBe(HttpStatusCode.Conflict);
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCapacityExhausted]
            .ShouldBe(HttpStatusCode.TooManyRequests);
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .PipelineTargetResourceGroupRequired]
            .ShouldBe(HttpStatusCode.BadRequest);
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .DataSourceTargetResourceGroupImmutable]
            .ShouldBe(HttpStatusCode.Conflict);
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionRecoveryInvalid]
            .ShouldBe(HttpStatusCode.BadRequest);
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionRecoveryConflict]
            .ShouldBe(HttpStatusCode.Conflict);
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .PipelineTargetReceiptInvalid]
            .ShouldBe(HttpStatusCode.BadRequest);
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .PipelineTargetReceiptUnavailable]
            .ShouldBe(HttpStatusCode.ServiceUnavailable);
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionReceiptInconclusive]
            .ShouldBe(HttpStatusCode.Conflict);
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionReceiptVerificationRequired]
            .ShouldBe(HttpStatusCode.Conflict);
        mappings[SwapMidDomainErrorCodes.DataIntegration
                .PipelineScheduleCronInvalid]
            .ShouldBe(HttpStatusCode.BadRequest);
        mappings.Count.ShouldBe(17);
    }

    private static string[] GetContentTypes(
        ProducesResponseTypeAttribute attribute)
    {
        var contentTypes = new MediaTypeCollection();
        ((IApiResponseMetadataProvider)attribute).SetContentTypes(contentTypes);
        return contentTypes.ToArray();
    }
}
