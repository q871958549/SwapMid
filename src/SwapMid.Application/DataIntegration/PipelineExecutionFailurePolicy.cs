using System;
using System.Collections.Generic;
using System.Text.Json;
using SwapMid.Localization;
using Microsoft.Extensions.Localization;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Tracing;

namespace SwapMid.DataIntegration;

/// <summary>
/// 管道失败边界：把任意执行异常收敛为稳定错误码，并只持久化可安全检索的诊断元数据。
/// 底层异常消息、堆栈、连接字符串和 SQL 均不得进入执行记录、API 或通知。
/// </summary>
public sealed class PipelineExecutionFailurePolicy : ITransientDependency
{
    public const string ErrorCodePropertyName = "PipelineFailure.ErrorCode";
    public const string CorrelationIdPropertyName = "PipelineFailure.CorrelationId";
    public const string ExceptionTypePropertyName = "PipelineFailure.ExceptionType";

    private const int MaxCorrelationIdLength = 64;
    private const int MaxExceptionTypeLength = 256;

    private static readonly HashSet<string> AllowedErrorCodes =
    [
        SwapMidDomainErrorCodes.DataIntegration.PipelineNotActive,
        SwapMidDomainErrorCodes.DataIntegration.PipelineTargetNotConfigured,
        SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid,
        SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed,
        SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionCoordinationUnavailable,
        SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionOutcomeUnknown,
        SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionReconciledNotApplied,
    ];

    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly IStringLocalizer<SwapMidResource> _localizer;

    public PipelineExecutionFailurePolicy(
        ICorrelationIdProvider correlationIdProvider,
        IStringLocalizer<SwapMidResource> localizer)
    {
        _correlationIdProvider = correlationIdProvider;
        _localizer = localizer;
    }

    /// <summary>将异常分类并写入执行记录；实体 ErrorMessage 始终留空。</summary>
    public FailureDescriptor Record(
        PipelineExecution execution,
        Exception exception,
        DateTime finishedAt)
    {
        var descriptor = Describe(
            exception,
            execution.Id,
            forcedErrorCode: null);
        return Record(execution, descriptor, finishedAt);
    }

    /// <summary>以已安全化的有限描述写入普通失败终态。</summary>
    public FailureDescriptor Record(
        PipelineExecution execution,
        FailureDescriptor descriptor,
        DateTime finishedAt)
    {
        execution.Fail(finishedAt);
        Store(execution, descriptor);
        return descriptor;
    }

    /// <summary>记录执行权在提交点前丢失；目标正式事务尚未越过提交边界。</summary>
    public FailureDescriptor RecordCoordinationFailure(
        PipelineExecution execution,
        Exception exception,
        DateTime finishedAt)
    {
        var descriptor = Describe(
            exception,
            execution.Id,
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        return RecordCoordinationFailure(execution, descriptor, finishedAt);
    }

    /// <summary>以已安全化的有限描述写入执行权丢失失败终态。</summary>
    public FailureDescriptor RecordCoordinationFailure(
        PipelineExecution execution,
        FailureDescriptor descriptor,
        DateTime finishedAt)
    {
        execution.Fail(finishedAt);
        Store(execution, descriptor);
        return descriptor;
    }

    /// <summary>目标提交结果无法确认；此状态会阻止该管道再次执行，直至人工对账。</summary>
    public FailureDescriptor RecordOutcomeUnknown(
        PipelineExecution execution,
        Exception exception,
        DateTime finishedAt)
    {
        var descriptor = Describe(
            exception,
            execution.Id,
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionOutcomeUnknown);
        return RecordOutcomeUnknown(execution, descriptor, finishedAt);
    }

    /// <summary>以已安全化的有限描述写入结果未知阻塞态。</summary>
    public FailureDescriptor RecordOutcomeUnknown(
        PipelineExecution execution,
        FailureDescriptor descriptor,
        DateTime finishedAt)
    {
        execution.MarkOutcomeUnknown(finishedAt);
        Store(execution, descriptor);
        return descriptor;
    }

    /// <summary>
    /// 在完整摘流并确认旧 owner 与目标活跃事务均退出后，将遗留 Running 收敛到仍需
    /// 人工核对的 OutcomeUnknown。诊断只保存稳定类型与 ETL:015，不携带证据正文。
    /// </summary>
    public FailureDescriptor RecordControlledRecoveryOutcomeUnknown(
        PipelineExecution execution,
        DateTime recoveredAt)
    {
        var descriptor = Describe(
            new PipelineExecutionControlledRecoveryException(),
            execution.Id,
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionOutcomeUnknown);
        return RecordOutcomeUnknown(execution, descriptor, recoveredAt);
    }

    /// <summary>
    /// 在 catch 边界立即把底层异常压缩成有限描述；返回值不含消息、堆栈、Data 或 inner exception。
    /// </summary>
    public FailureDescriptor DescribeFailure(
        Exception exception,
        Guid executionId)
        => Describe(exception, executionId, forcedErrorCode: null);

    /// <summary>把执行权故障压缩成稳定错误码和安全异常类型。</summary>
    public FailureDescriptor DescribeCoordinationFailure(
        Exception exception,
        Guid executionId)
        => Describe(
            exception,
            executionId,
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);

    /// <summary>把目标提交结果未知压缩成稳定错误码和安全异常类型。</summary>
    public FailureDescriptor DescribeOutcomeUnknown(
        Exception exception,
        Guid executionId)
        => Describe(
            exception,
            executionId,
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionOutcomeUnknown);

    /// <summary>人工确认目标库未应用本次变更后，将未知态收敛为可重试的失败态。</summary>
    public FailureDescriptor RecordReconciledNotApplied(
        PipelineExecution execution,
        DateTime resolvedAt)
    {
        var descriptor = Describe(
            new PipelineExecutionReconciledNotAppliedException(),
            execution.Id,
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionReconciledNotApplied);
        execution.ResolveAsFailed(resolvedAt);
        Store(execution, descriptor);
        return descriptor;
    }

    private static void Store(
        PipelineExecution execution,
        FailureDescriptor descriptor)
    {
        execution.SetProperty(ErrorCodePropertyName, descriptor.ErrorCode);
        execution.SetProperty(ExceptionTypePropertyName, descriptor.ExceptionType);
        if (descriptor.CorrelationId != null)
        {
            execution.SetProperty(CorrelationIdPropertyName, descriptor.CorrelationId);
        }

    }

    /// <summary>
    /// 给 DTO 填充安全失败信息。历史记录即使 ErrorMessage 含底层文本也会被忽略，
    /// 没有新元数据的历史失败统一显示通用错误。
    /// </summary>
    public void PopulateDto(PipelineExecution execution, PipelineExecutionDto dto)
    {
        if (execution.Status is not (
                ExecutionStatus.Failed or ExecutionStatus.OutcomeUnknown))
        {
            dto.ErrorCode = null;
            dto.ErrorMessage = null;
            dto.CorrelationId = null;
            dto.ExceptionType = null;
            return;
        }

        var storedCode = ReadString(execution, ErrorCodePropertyName, 32);
        var safeCode = storedCode != null && AllowedErrorCodes.Contains(storedCode)
            ? storedCode
            : execution.Status == ExecutionStatus.OutcomeUnknown
                ? SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionOutcomeUnknown
                : SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionFailed;

        dto.ErrorCode = safeCode;
        dto.ErrorMessage = Localize(safeCode);
        dto.CorrelationId = ReadString(execution, CorrelationIdPropertyName, MaxCorrelationIdLength);
        dto.ExceptionType = ReadString(execution, ExceptionTypePropertyName, MaxExceptionTypeLength);
    }

    private FailureDescriptor Describe(
        Exception exception,
        Guid executionId,
        string? forcedErrorCode)
    {
        var errorCode = forcedErrorCode ??
                        (exception is BusinessException businessException
                         && businessException.Code != null
                         && AllowedErrorCodes.Contains(businessException.Code)
                            ? businessException.Code
                            : SwapMidDomainErrorCodes.DataIntegration
                                .PipelineExecutionFailed);

        var providedCorrelationId = _correlationIdProvider.Get();
        var correlationId = IsSafeToken(
            providedCorrelationId,
            MaxCorrelationIdLength,
            IsCorrelationIdCharacter)
            ? providedCorrelationId!
            : executionId.ToString("N");

        var exceptionType = exception.GetType().FullName ?? exception.GetType().Name;
        if (!IsSafeToken(exceptionType, MaxExceptionTypeLength, IsExceptionTypeCharacter))
        {
            exceptionType = exception.GetType().Name;
        }
        if (!IsSafeToken(exceptionType, MaxExceptionTypeLength, IsExceptionTypeCharacter))
        {
            exceptionType = nameof(Exception);
        }

        return new FailureDescriptor(
            errorCode,
            Localize(errorCode),
            correlationId,
            exceptionType);
    }

    private string Localize(string errorCode)
        => _localizer[errorCode].Value;

    private static string? ReadString(PipelineExecution execution, string key, int maxLength)
    {
        if (!execution.ExtraProperties.TryGetValue(key, out var value))
        {
            return null;
        }

        var text = value switch
        {
            string stringValue => stringValue,
            JsonElement { ValueKind: JsonValueKind.String } jsonValue => jsonValue.GetString(),
            _ => null,
        };

        Func<char, bool> characterPolicy = key switch
        {
            ErrorCodePropertyName => IsErrorCodeCharacter,
            CorrelationIdPropertyName => IsCorrelationIdCharacter,
            _ => IsExceptionTypeCharacter,
        };
        return IsSafeToken(text, maxLength, characterPolicy) ? text : null;
    }

    private static bool IsSafeToken(
        string? value,
        int maxLength,
        Func<char, bool> characterPolicy)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maxLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!characterPolicy(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCorrelationIdCharacter(char value)
        => char.IsAsciiLetterOrDigit(value) || value is '-' or '_' or '.' or ':';

    private static bool IsErrorCodeCharacter(char value)
        => char.IsAsciiLetterOrDigit(value) || value == ':';

    private static bool IsExceptionTypeCharacter(char value)
        => char.IsAsciiLetterOrDigit(value) || value is '.' or '_' or '+' or '`';

    /// <summary>供安全日志与通知使用的有限失败描述。</summary>
    public sealed record FailureDescriptor(
        string ErrorCode,
        string SafeMessage,
        string CorrelationId,
        string ExceptionType);
}

/// <summary>仅作为安全诊断类型保存，不携带外部错误正文。</summary>
internal sealed class PipelineExecutionReconciledNotAppliedException : Exception
{
}

/// <summary>仅作为受控恢复的安全诊断类型保存，不携带工单、证据或外部状态。</summary>
internal sealed class PipelineExecutionControlledRecoveryException : Exception
{
}
