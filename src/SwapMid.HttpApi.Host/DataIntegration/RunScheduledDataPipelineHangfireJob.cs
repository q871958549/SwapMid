using System;
using System.Security.Cryptography;
using System.Text;
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
/// 数据管道 Cron 定时触发的 Hangfire 执行器。
/// 禁用 Hangfire 自动重试，所有准入栅栏、幂等重放与失败通知均由 <see cref="DataPipelineExecutionCoordinator"/> 统一治理。
/// </summary>
[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
[DisableConcurrentExecution(timeoutInSeconds: 5)]
public class RunScheduledDataPipelineHangfireJob : ITransientDependency
{
    private readonly DataPipelineExecutionCoordinator _executionCoordinator;
    private readonly ILogger<RunScheduledDataPipelineHangfireJob> _logger;

    public RunScheduledDataPipelineHangfireJob(
        DataPipelineExecutionCoordinator executionCoordinator,
        ILogger<RunScheduledDataPipelineHangfireJob> logger)
    {
        _executionCoordinator = executionCoordinator;
        _logger = logger;
    }

    [UnitOfWork(IsDisabled = true)]
    public virtual async Task ExecuteAsync(
        Guid pipelineId,
        CancellationToken cancellationToken = default)
    {
        var runId = CreateMinuteDeterministicRunId(pipelineId, DateTime.UtcNow);
        try
        {
            await _executionCoordinator.ExecuteAsync(
                pipelineId,
                runId,
                TriggerType.Scheduled,
                cancellationToken);
        }
        catch (BusinessException ex) when (
            ex.Code == SwapMidDomainErrorCodes.DataIntegration.PipelineNotActive ||
            ex.Code == SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionBusy ||
            ex.Code == SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionTargetBusy ||
            ex.Code == SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionCapacityExhausted ||
            ex.Code == SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionOutcomeUnresolved)
        {
            _logger.LogWarning(
                "定时触发数据管道被准入策略跳过 pipelineId={PipelineId} runId={RunId} code={ErrorCode}",
                pipelineId,
                runId,
                ex.Code);
        }
    }

    internal static Guid CreateMinuteDeterministicRunId(
        Guid pipelineId,
        DateTime utcTimestamp)
    {
        var seed = $"swapmid-scheduled-etl-v1:{pipelineId:D}:{utcTimestamp:yyyyMMddHHmm}";
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(seed), hash);
        Span<byte> guidBytes = stackalloc byte[16];
        hash[..16].CopyTo(guidBytes);
        // Set RFC 4122 version 5 & variant bits so result is never Guid.Empty
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes, bigEndian: true);
    }
}
