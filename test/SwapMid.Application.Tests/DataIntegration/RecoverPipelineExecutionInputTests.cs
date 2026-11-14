using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Shouldly;
using Volo.Abp.Auditing;
using Xunit;

namespace SwapMid.DataIntegration;

public class RecoverPipelineExecutionInputTests
{
    [Fact]
    public void 恢复输入必须固定审计字段类型与长度边界()
    {
        var type = typeof(RecoverPipelineExecutionInput);

        type.GetProperty(nameof(RecoverPipelineExecutionInput.RecoveryRequestId))!
            .PropertyType.ShouldBe(typeof(Guid));
        type.GetProperty(nameof(RecoverPipelineExecutionInput.ExpectedConcurrencyStamp))!
            .ShouldHaveStringLength(40, 1);
        type.GetProperty(nameof(RecoverPipelineExecutionInput.DrainStartedAt))!
            .PropertyType.ShouldBe(typeof(DateTimeOffset));
        type.GetProperty(nameof(RecoverPipelineExecutionInput.OldOwnerExitVerifiedAt))!
            .PropertyType.ShouldBe(typeof(DateTimeOffset));
        type.GetProperty(nameof(RecoverPipelineExecutionInput.TargetSessionExitVerifiedAt))!
            .PropertyType.ShouldBe(typeof(DateTimeOffset));
        type.GetProperty(nameof(RecoverPipelineExecutionInput.EvidenceReference))!
            .ShouldHaveStringLength(141, 15);

        foreach (var propertyName in new[]
                 {
                     nameof(RecoverPipelineExecutionInput.RecoveryRequestId),
                     nameof(RecoverPipelineExecutionInput.ExpectedConcurrencyStamp),
                     nameof(RecoverPipelineExecutionInput.DrainStartedAt),
                     nameof(RecoverPipelineExecutionInput.OldOwnerExitVerifiedAt),
                     nameof(RecoverPipelineExecutionInput.TargetSessionExitVerifiedAt),
                     nameof(RecoverPipelineExecutionInput.EvidenceReference),
                 })
        {
            type.GetProperty(propertyName)!
                .GetCustomAttribute<RequiredAttribute>()
                .ShouldNotBeNull($"{propertyName} 必须显式声明必填");
        }
    }

    [Fact]
    public void 模型验证必须拒绝空并发戳与过短证据引用()
    {
        var input = new RecoverPipelineExecutionInput
        {
            RecoveryRequestId = Guid.NewGuid(),
            ExpectedConcurrencyStamp = string.Empty,
            DrainStartedAt = DateTimeOffset.UtcNow.AddMinutes(-8),
            OldOwnerExitVerifiedAt = DateTimeOffset.UtcNow.AddMinutes(-7),
            TargetSessionExitVerifiedAt = DateTimeOffset.UtcNow.AddMinutes(-6),
            EvidenceReference = "short",
        };
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
                input,
                new ValidationContext(input),
                results,
                validateAllProperties: true)
            .ShouldBeFalse();
        results.ShouldContain(result =>
            result.MemberNames.Contains(
                nameof(RecoverPipelineExecutionInput.ExpectedConcurrencyStamp)));
        results.ShouldContain(result =>
            result.MemberNames.Contains(
                nameof(RecoverPipelineExecutionInput.EvidenceReference)));
    }

    [Fact]
    public void 证据引用必须禁止进入ABP方法参数审计()
    {
        typeof(RecoverPipelineExecutionInput)
            .GetProperty(nameof(RecoverPipelineExecutionInput.EvidenceReference))!
            .GetCustomAttribute<DisableAuditingAttribute>()
            .ShouldNotBeNull(
                "结构化引用仍可能含敏感工单编号，禁止进入通用 action 审计参数");
    }
}

internal static class RecoverInputPropertyAssertionExtensions
{
    public static void ShouldHaveStringLength(
        this PropertyInfo property,
        int maximumLength,
        int minimumLength)
    {
        property.PropertyType.ShouldBe(typeof(string));
        property.GetCustomAttribute<RequiredAttribute>().ShouldNotBeNull();
        var length = property.GetCustomAttribute<StringLengthAttribute>();
        length.ShouldNotBeNull();
        length.MaximumLength.ShouldBe(maximumLength);
        length.MinimumLength.ShouldBe(minimumLength);
    }
}
