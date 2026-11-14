using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Volo.Abp.Auditing;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.DataIntegration;

public sealed class TargetReceiptApplicationContractTests
{
    [Fact]
    public void AppService与接口双重载必须禁用Uow且仅CT重载传播取消()
    {
        AssertOverloads(
            typeof(PipelineExecutionAppService),
            requireAuthorize: true);
        AssertOverloads(
            typeof(IPipelineExecutionAppService),
            requireAuthorize: false);
    }

    [Fact]
    public void PipelineExecutionDto不得暴露目标回执身份或内容摘要()
    {
        var propertyNames = typeof(PipelineExecutionDto)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        propertyNames.ShouldNotContain("TargetResourceHash");
        propertyNames.ShouldNotContain("VerifiedTargetResourceHash");
        propertyNames.ShouldNotContain("ExecutionContractHash");
        propertyNames.ShouldNotContain("TargetResourceInstanceId");
        propertyNames.ShouldNotContain("TargetSchemaHash");
        propertyNames.ShouldNotContain("BatchDigest");
        propertyNames.ShouldNotContain("ReceiptPreparedRows");
        propertyNames.ShouldContain(nameof(
            PipelineExecutionDto.ReconciliationStrategy));
        propertyNames.ShouldContain(nameof(
            PipelineExecutionDto.ReceiptVerificationEligible));
        propertyNames.ShouldContain(nameof(
            PipelineExecutionDto.ReceiptVerifiedAt));
        propertyNames.ShouldContain(nameof(
            PipelineExecutionDto.ReceiptVerificationSource));
    }

    [Theory]
    [InlineData(typeof(DataPipeline), nameof(DataPipeline.TransformConfig))]
    [InlineData(typeof(DataPipelineCreateUpdateDto),
        nameof(DataPipelineCreateUpdateDto.TransformConfig))]
    public void TransformConfig实体与DTO必须禁止审计(
        Type type,
        string propertyName)
    {
        type.GetProperty(propertyName,
                BindingFlags.Instance | BindingFlags.Public)!
            .GetCustomAttribute<DisableAuditingAttribute>()
            .ShouldNotBeNull(
                $"{type.Name}.{propertyName} 含源 SQL，不得进入通用审计参数");
    }

    private static void AssertOverloads(
        Type type,
        bool requireAuthorize)
    {
        var methods = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == "VerifyTargetReceiptAsync")
            .ToArray();
        methods.Length.ShouldBe(2);

        foreach (var method in methods)
        {
            var unitOfWork = method.GetCustomAttribute<UnitOfWorkAttribute>(
                inherit: true);
            unitOfWork.ShouldNotBeNull();
            unitOfWork!.IsDisabled.ShouldBeTrue();

            var policies = method
                .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .Select(attribute => attribute.Policy)
                .ToArray();
            if (requireAuthorize)
            {
                policies.ShouldBe(new[]
                {
                    SwapMid.Permissions
                        .SwapMidPermissions.DataIntegration
                        .PipelineExecutions.VerifyTargetReceipt,
                });
            }
            else
            {
                policies.ShouldBeEmpty(
                    "授权事实源在 AppService；接口仍须显式声明 UoW 边界");
            }
        }

        var cancellationOverload = methods.Single(method =>
            method.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(CancellationToken)));
        cancellationOverload.GetParameters()[^1].ParameterType.ShouldBe(
            typeof(CancellationToken));
        cancellationOverload.GetParameters()[^1].HasDefaultValue.ShouldBeTrue();
        methods.Single(method => method != cancellationOverload)
            .GetParameters().Length.ShouldBe(1);
    }
}
