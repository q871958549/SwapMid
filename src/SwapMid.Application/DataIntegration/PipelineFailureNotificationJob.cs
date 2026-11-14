using System;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Notification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

/// <summary>
/// ETL 失败通知的耐久后台参数。只携带失败策略产生的稳定标识，禁止加入异常正文、
/// SQL、连接字符串、密钥或任意业务配置。
/// </summary>
[BackgroundJobName("Han.DataIntegration.PipelineFailureNotification:v1")]
public sealed class PipelineFailureNotificationJobArgs
{
    public Guid PipelineId { get; set; }

    public Guid ExecutionId { get; set; }

    public string ErrorCode { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public string ExceptionType { get; set; } = string.Empty;

    public bool OutcomeUnknown { get; set; }
}

/// <summary>
/// 独立于 ETL 协调器执行权的失败通知后台任务。通知异常只触发 ABP 后台任务重试，
/// 不可能回流改变已提交的执行终态或继续占用容量/目标 fence。
/// </summary>
public class PipelineFailureNotificationJob
    : AsyncBackgroundJob<PipelineFailureNotificationJobArgs>
{
    private const int MaxErrorCodeLength = 32;
    private const int MaxCorrelationIdLength = 64;
    private const int MaxExceptionTypeLength = 256;

    private readonly INotificationService _notificationService;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICancellationTokenProvider _cancellationTokenProvider;
    private readonly EtlExecutionOptions _options;
    private readonly ILogger<PipelineFailureNotificationJob> _logger;

    public PipelineFailureNotificationJob(
        INotificationService notificationService,
        IUnitOfWorkManager unitOfWorkManager,
        ICancellationTokenProvider cancellationTokenProvider,
        IOptions<EtlExecutionOptions> options,
        ILogger<PipelineFailureNotificationJob> logger)
    {
        _notificationService = notificationService;
        _unitOfWorkManager = unitOfWorkManager;
        _cancellationTokenProvider = cancellationTokenProvider;
        _options = options.Value;
        _logger = logger;
    }

    [UnitOfWork(IsDisabled = true)]
    public override async Task ExecuteAsync(
        PipelineFailureNotificationJobArgs args)
    {
        Validate(args);
        var workerCancellationToken = _cancellationTokenProvider.Token;
        using var timeoutCancellation = new CancellationTokenSource(
            TimeSpan.FromSeconds(
                _options.NotificationDeliveryTimeoutSeconds));
        using var deliveryCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                workerCancellationToken,
                timeoutCancellation.Token);
        var deliveryToken = deliveryCancellation.Token;
        try
        {
            using var notificationUnitOfWork = _unitOfWorkManager.Begin(
                requiresNew: true,
                isTransactional: false);
            var result = await _notificationService.SendAsync(
                CreateRequest(args),
                deliveryToken);
            if (!result.Success && !result.Skipped)
            {
                throw new PipelineFailureNotificationDeliveryException(
                    args.ExecutionId,
                    "delivery_failed");
            }

            await notificationUnitOfWork.CompleteAsync(deliveryToken);
        }
        catch (OperationCanceledException) when (
            workerCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested)
        {
            TryLogDeliveryFailure(args, "delivery_timeout");
            throw new PipelineFailureNotificationDeliveryException(
                args.ExecutionId,
                "delivery_timeout");
        }
        catch (PipelineFailureNotificationDeliveryException)
        {
            throw;
        }
        catch (Exception notificationException)
        {
            var exceptionType = notificationException.GetType().FullName ??
                                notificationException.GetType().Name;
            TryLogDeliveryFailure(args, exceptionType);
            throw new PipelineFailureNotificationDeliveryException(
                args.ExecutionId,
                "infrastructure_exception");
        }
    }

    private static NotificationSendRequest CreateRequest(
        PipelineFailureNotificationJobArgs args)
        => new()
        {
            Title = args.OutcomeUnknown
                ? "ETL 管道结果待人工核对"
                : "ETL 管道执行失败",
            Content =
                $"管道 ID：{args.PipelineId:N}\n" +
                $"执行 ID：{args.ExecutionId:N}\n" +
                $"错误码：{args.ErrorCode}\n" +
                $"关联号：{args.CorrelationId}\n" +
                $"异常类型：{args.ExceptionType}\n" +
                "说明：请在平台执行历史中按错误码查看安全说明。" +
                (args.OutcomeUnknown
                    ? "\n处置：禁止重试，必须先核对目标库最终数据。"
                    : string.Empty),
            Level = args.OutcomeUnknown
                ? NotificationLevel.Critical
                : NotificationLevel.Warning,
            Source = NotificationSources.DataIntegration,
        };

    private static void Validate(PipelineFailureNotificationJobArgs args)
    {
        if (args.PipelineId == Guid.Empty ||
            args.ExecutionId == Guid.Empty ||
            !IsSafeToken(
                args.ErrorCode,
                MaxErrorCodeLength,
                character => char.IsAsciiLetterOrDigit(character) ||
                             character == ':') ||
            !args.ErrorCode.StartsWith("SwapMid:ETL:", StringComparison.Ordinal) ||
            !IsSafeToken(
                args.CorrelationId,
                MaxCorrelationIdLength,
                character => char.IsAsciiLetterOrDigit(character) ||
                             character is '-' or '_' or '.' or ':') ||
            !IsSafeToken(
                args.ExceptionType,
                MaxExceptionTypeLength,
                character => char.IsAsciiLetterOrDigit(character) ||
                             character is '.' or '_' or '+' or '`'))
        {
            throw new PipelineFailureNotificationDeliveryException(
                args.ExecutionId,
                "invalid_arguments");
        }
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

    private void TryLogDeliveryFailure(
        PipelineFailureNotificationJobArgs args,
        string exceptionType)
    {
        try
        {
            _logger.LogWarning(
                "ETL failure notification background job failed pipelineId={PipelineId} executionId={ExecutionId} exceptionType={ExceptionType}",
                args.PipelineId,
                args.ExecutionId,
                exceptionType);
        }
        catch
        {
            // 日志 provider 故障不得改变后台任务的安全重试信号。
        }
    }
}

/// <summary>
/// 交给 ABP 重试器的低敏失败标记；不保留通知渠道异常、返回正文或 inner exception。
/// </summary>
internal sealed class PipelineFailureNotificationDeliveryException : Exception
{
    public PipelineFailureNotificationDeliveryException(
        Guid executionId,
        string failureKind)
        : base(
            $"ETL failure notification {executionId:N} failed ({failureKind}); " +
            "see protected service logs and retry through the background job worker.")
    {
    }
}
