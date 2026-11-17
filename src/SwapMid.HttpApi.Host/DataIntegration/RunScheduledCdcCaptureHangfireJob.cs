using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Extensions.Logging;
using SwapMid.DataIntegration;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace SwapMid.HttpApiHost.DataIntegration;

/// <summary>
/// 定时拉取已激活 CDC 流（PostgreSQL pgoutput）已提交 WAL 变更的 Hangfire 执行器。
/// </summary>
[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
[DisableConcurrentExecution(timeoutInSeconds: 5)]
public class RunScheduledCdcCaptureHangfireJob : ITransientDependency
{
    private readonly CdcStreamCaptureCoordinator _captureCoordinator;
    private readonly ILogger<RunScheduledCdcCaptureHangfireJob> _logger;

    public RunScheduledCdcCaptureHangfireJob(
        CdcStreamCaptureCoordinator captureCoordinator,
        ILogger<RunScheduledCdcCaptureHangfireJob> logger)
    {
        _captureCoordinator = captureCoordinator;
        _logger = logger;
    }

    [UnitOfWork(IsDisabled = true)]
    public virtual async Task ExecuteAsync(
        Guid streamId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _captureCoordinator.CaptureAsync(
                streamId,
                requireActiveStatus: true,
                cancellationToken);
        }
        catch (BusinessException ex) when (
            ex.Code == SwapMidDomainErrorCodes.DataIntegration.CdcStreamNotActive ||
            ex.Code == SwapMidDomainErrorCodes.DataIntegration.CdcStreamCaptureBusy)
        {
            _logger.LogDebug(
                "定时触发 CDC 流捕获被跳过 streamId={StreamId} code={ErrorCode}",
                streamId,
                ex.Code);
        }
        catch (BusinessException ex) when (
            ex.Code == SwapMidDomainErrorCodes.DataIntegration.CdcStreamSchemaDriftDetected)
        {
            _logger.LogWarning(
                "定时触发 CDC 流因上游 Schema 漂移被护栏自动暂停或阻塞 streamId={StreamId} code={ErrorCode}",
                streamId,
                ex.Code);
        }
    }
}
