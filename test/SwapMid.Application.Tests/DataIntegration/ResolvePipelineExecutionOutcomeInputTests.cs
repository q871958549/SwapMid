using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.DataIntegration;

public class ResolvePipelineExecutionOutcomeInputTests
{
    [Fact]
    public void Resolution_必须为可空类型并通过Required声明必填()
    {
        var property = typeof(ResolvePipelineExecutionOutcomeInput)
            .GetProperty(nameof(ResolvePipelineExecutionOutcomeInput.Resolution))!;

        property.PropertyType.ShouldBe(typeof(PipelineExecutionResolution?));
        property.GetCustomAttribute<RequiredAttribute>().ShouldNotBeNull();
        ((int)PipelineExecutionResolution.ConfirmedApplied).ShouldBe(0);
        ((int)PipelineExecutionResolution.ConfirmedNotApplied).ShouldBe(1);
    }

    [Fact]
    public void VerificationNote_必须禁用参数审计并保留已发布HTTP长度包络()
    {
        var property = typeof(ResolvePipelineExecutionOutcomeInput)
            .GetProperty(nameof(
                ResolvePipelineExecutionOutcomeInput.VerificationNote))!;

        property.GetCustomAttribute<DisableAuditingAttribute>()
            .ShouldNotBeNull();
        var length = property.GetCustomAttribute<StringLengthAttribute>();
        length.ShouldNotBeNull();
        length!.MinimumLength.ShouldBe(8);
        length.MaximumLength.ShouldBe(512);
    }

    [Fact]
    public void JsonAuditSerializer_必须排除旧客户端提交的核验说明正文()
    {
        const string secret = "Server=db;Password=audit-secret";
        var serializer = new JsonAuditSerializer(
            Options.Create(new AbpAuditingOptions()));
        var parameters = new Dictionary<string, object>
        {
            ["input"] = new ResolvePipelineExecutionOutcomeInput
            {
                Resolution = PipelineExecutionResolution.ConfirmedApplied,
                VerificationNote = secret,
            },
        };

        var json = serializer.Serialize(parameters);

        json.ShouldNotContain(secret);
        json.ShouldNotContain("verificationNote", Case.Insensitive);
    }

    [Fact]
    public async Task 协调器绕过模型验证时仍拒绝未提供的Resolution()
    {
        var coordinator = CreateCoordinator();

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            coordinator.ResolveOutcomeAsync(
                Guid.NewGuid(),
                new ResolvePipelineExecutionOutcomeInput
                {
                    Resolution = null,
                    VerificationNote = "incident:RESOLUTION-VALIDATION-0001",
                },
                Guid.NewGuid()));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionResolutionInvalid);
    }

    [Theory]
    [InlineData("Server=db;Database=Han;Password=secret")]
    [InlineData("SELECT * FROM AppPipelineExecutions")]
    [InlineData("https://evidence.example/case?token=secret")]
    [InlineData("incident:INC/2026")]
    [InlineData("incident:short")]
    [InlineData(" incident:INC-2026-0001")]
    public void 旧自由文本必须兼容接收但只生成不可逆安全占位引用(
        string evidence)
    {
        var normalized = DataPipelineExecutionCoordinator
            .NormalizeVerificationNote(
                new ResolvePipelineExecutionOutcomeInput
                {
                    Resolution = PipelineExecutionResolution.ConfirmedApplied,
                    VerificationNote = evidence,
                },
                PipelineExecutionResolution.ConfirmedApplied);

        normalized.ShouldStartWith(
            DataPipelineExecutionCoordinator.LegacyVerificationNotePrefix);
        normalized.ShouldNotContain(evidence);
        normalized.Length.ShouldBe(
            DataPipelineExecutionCoordinator.LegacyVerificationNotePrefix
                .Length + 32);
    }

    [Fact]
    public void 结构化证据引用必须原样保留()
    {
        const string evidence = "incident:RESOLUTION-VALIDATION-0001";

        var normalized = DataPipelineExecutionCoordinator
            .NormalizeVerificationNote(
                new ResolvePipelineExecutionOutcomeInput
                {
                    Resolution = PipelineExecutionResolution.ConfirmedApplied,
                    VerificationNote = evidence,
                },
                PipelineExecutionResolution.ConfirmedApplied);

        normalized.ShouldBe(evidence);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("       ")]
    public void 超出旧契约边界的说明仍必须失败关闭(string evidence)
    {
        var exception = Should.Throw<BusinessException>(() =>
            DataPipelineExecutionCoordinator.NormalizeVerificationNote(
                new ResolvePipelineExecutionOutcomeInput
                {
                    Resolution = PipelineExecutionResolution.ConfirmedApplied,
                    VerificationNote = evidence,
                },
                PipelineExecutionResolution.ConfirmedApplied));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionResolutionInvalid);
    }

    private static DataPipelineExecutionCoordinator CreateCoordinator()
    {
        var unitOfWorkManager = Substitute.For<IUnitOfWorkManager>();
        unitOfWorkManager.Current.Returns((IUnitOfWork?)null);
        return new DataPipelineExecutionCoordinator(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            unitOfWorkManager,
            Substitute.For<ICurrentTenant>(),
            Options.Create(new EtlExecutionOptions()),
            Substitute.For<ILogger<DataPipelineExecutionCoordinator>>());
    }
}
