using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Extensions.Logging;
using SwapMid.DataIntegration;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace SwapMid.HttpApiHost.DataIntegration;

/// <summary>
/// Apache Iceberg v2 定时表维护（小文件压缩 + 快照过期与孤儿回收）Hangfire 后台执行器。
/// </summary>
[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
[DisableConcurrentExecution(timeoutInSeconds: 30)]
public class RunScheduledLakehouseMaintenanceHangfireJob : ITransientDependency
{
    private readonly ICdcStreamRepository _streamRepository;
    private readonly IRepository<DataSource, Guid> _sourceRepository;
    private readonly CdcObjectStorageCompactionService _compactionService;
    private readonly IcebergV2MaintenanceService _maintenanceService;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<RunScheduledLakehouseMaintenanceHangfireJob> _logger;

    public RunScheduledLakehouseMaintenanceHangfireJob(
        ICdcStreamRepository streamRepository,
        IRepository<DataSource, Guid> sourceRepository,
        CdcObjectStorageCompactionService compactionService,
        IcebergV2MaintenanceService maintenanceService,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<RunScheduledLakehouseMaintenanceHangfireJob> logger)
    {
        _streamRepository = streamRepository;
        _sourceRepository = sourceRepository;
        _compactionService = compactionService;
        _maintenanceService = maintenanceService;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    [UnitOfWork(IsDisabled = true)]
    public virtual async Task ExecuteAsync(
        Guid streamId,
        CancellationToken cancellationToken = default)
    {
        CdcStreamDefinition? stream;
        DataSource? sink;
        using (var readUow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            stream = await _streamRepository.FindAsync(streamId, includeDetails: false, cancellationToken: cancellationToken);
            if (stream == null ||
                stream.Status != CdcStreamStatus.Active ||
                stream.TargetMode != CdcTargetMode.ExternalSink ||
                stream.LakehouseTableFormat != LakehouseTableFormat.ApacheIceberg ||
                !stream.TargetSinkId.HasValue ||
                string.IsNullOrWhiteSpace(stream.LakehouseMaintenanceCron))
            {
                return;
            }

            sink = await _sourceRepository.FindAsync(stream.TargetSinkId.Value, includeDetails: false, cancellationToken: cancellationToken);
            await readUow.CompleteAsync(cancellationToken);
        }

        if (sink is not { Type: DataSourceType.ObjectStorage })
        {
            return;
        }

        var parts = new List<string>(2);
        try
        {
            if (stream.LakehouseCompactMinFiles is >= CdcStreamConsts.MinLakehouseCompactMinFiles)
            {
                var compact = await _compactionService.CompactAsync(
                    stream,
                    sink,
                    stream.LakehouseCompactMinFiles.Value,
                    deleteCompactedFiles: false,
                    cancellationToken);
                if (compact.Compacted)
                {
                    parts.Add($"压缩合并 {compact.CompactedFileCount} 分片");
                }
            }

            var expire = await _maintenanceService.ExpireSnapshotsAsync(
                stream,
                sink,
                new ExpireCdcLakehouseSnapshotsInput
                {
                    RetainLast = stream.LakehouseExpireRetainLast ?? CdcStreamConsts.DefaultLakehouseExpireRetainLast,
                    OlderThanMinutes = stream.LakehouseExpireOlderThanMinutes ?? CdcStreamConsts.DefaultLakehouseExpireOlderThanMinutes,
                    DeleteOrphanedFiles = stream.LakehouseDeleteOrphanedFiles,
                    DryRun = false,
                },
                cancellationToken);
            parts.Add(expire.Summary);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Apache Iceberg v2 定时维护执行失败 streamId={StreamId} exceptionType={Type}",
                streamId,
                ex.GetType().Name);
            parts.Add($"定时维护失败：{ex.Message}");
        }

        var summary = string.Join("；", parts);
        using var writeUow = _unitOfWorkManager.Begin(requiresNew: true);
        var fresh = await _streamRepository.FindAsync(streamId, includeDetails: false, cancellationToken: cancellationToken);
        if (fresh != null)
        {
            fresh.RecordLakehouseMaintenance(DateTime.UtcNow, summary);
            await _streamRepository.UpdateAsync(fresh, autoSave: true, cancellationToken: cancellationToken);
        }

        await writeUow.CompleteAsync(cancellationToken);
    }
}
