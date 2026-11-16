using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using SwapMid.Permissions;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

[Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Default)]
public class CdcStreamAppService : SwapMidAppService, ICdcStreamAppService
{
    private readonly ICdcStreamRepository _streamRepository;
    private readonly IRepository<DataSource, Guid> _sourceRepository;
    private readonly IRepository<DataPipeline, Guid> _pipelineRepository;
    private readonly PgOutputCdcCaptureService _captureService;
    private readonly CdcStreamCaptureCoordinator _captureCoordinator;
    private readonly ICdcScheduleSynchronizer _scheduleSynchronizer;

    public CdcStreamAppService(
        ICdcStreamRepository streamRepository,
        IRepository<DataSource, Guid> sourceRepository,
        IRepository<DataPipeline, Guid> pipelineRepository,
        PgOutputCdcCaptureService captureService,
        CdcStreamCaptureCoordinator captureCoordinator,
        ICdcScheduleSynchronizer? scheduleSynchronizer = null)
    {
        _streamRepository = streamRepository;
        _sourceRepository = sourceRepository;
        _pipelineRepository = pipelineRepository;
        _captureService = captureService;
        _captureCoordinator = captureCoordinator;
        _scheduleSynchronizer = scheduleSynchronizer ?? NullCdcScheduleSynchronizer.Instance;
    }

    private static readonly IReadOnlyDictionary<string, string> SortingWhitelist =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "name",
            ["status"] = "status",
            ["lastcapturedat"] = "lastcapturedat",
            ["totalcapturedcount"] = "totalcapturedcount",
            ["creationtime"] = "creationtime",
        };

    public async Task<PagedResultDto<CdcStreamDto>> GetListAsync(GetCdcStreamListInput input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Default);

        var sorting = ListQuerySortingExtensions.ResolveWhitelistedSorting(
            input.Sorting,
            SortingWhitelist,
            defaultSorting: "creationtime DESC",
            logger: Logger,
            idFieldName: null);
        var totalCount = await _streamRepository.GetCountAsync(input.Filter, input.SourceId, input.Status);
        var items = await _streamRepository.GetListAsync(
            input.Filter,
            input.SourceId,
            input.Status,
            sorting,
            input.MaxResultCount,
            input.SkipCount);

        var sourceIds = items.SelectMany(x =>
            x.TargetSinkId.HasValue ? new[] { x.SourceId, x.TargetSinkId.Value } : new[] { x.SourceId })
            .Distinct()
            .ToList();
        var sources = sourceIds.Count > 0
            ? (await _sourceRepository.GetListAsync(x => sourceIds.Contains(x.Id)))
                .ToDictionary(x => x.Id)
            : new Dictionary<Guid, DataSource>();

        var pipelineIds = items
            .Where(x => x.TargetPipelineId.HasValue)
            .Select(x => x.TargetPipelineId!.Value)
            .Distinct()
            .ToList();
        var pipelines = pipelineIds.Count > 0
            ? (await _pipelineRepository.GetListAsync(x => pipelineIds.Contains(x.Id)))
                .ToDictionary(x => x.Id, x => x.Name)
            : new Dictionary<Guid, string>();

        var dtos = items.Select(x =>
        {
            var src = sources.GetValueOrDefault(x.SourceId);
            var sink = x.TargetSinkId.HasValue ? sources.GetValueOrDefault(x.TargetSinkId.Value) : null;
            return MapToDto(
                x,
                src?.Name,
                x.TargetPipelineId.HasValue ? pipelines.GetValueOrDefault(x.TargetPipelineId.Value) : null,
                sink);
        }).ToList();

        return new PagedResultDto<CdcStreamDto>(totalCount, dtos);
    }

    public async Task<CdcStreamDto> GetAsync(Guid id)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Default);

        var stream = await _streamRepository.GetAsync(id);
        return await EnrichDtoAsync(stream);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Create)]
    public async Task<CdcStreamDto> CreateAsync(CdcStreamCreateUpdateDto input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Create);

        var normalized = await ValidateAndNormalizeInputAsync(input, excludeId: null);
        var stream = new CdcStreamDefinition(
            GuidGenerator.Create(),
            normalized.Name,
            input.SourceId,
            normalized.PublicationName,
            normalized.SlotName,
            input.TargetMode,
            normalized.TargetTopicName,
            normalized.TargetPipelineId,
            normalized.MonitoredTables,
            input.AutoCreatePublicationAndSlot,
            input.PollIntervalSeconds,
            input.MaxBatchSize,
            input.Description,
            normalized.TargetSinkId,
            normalized.TargetSinkDestination);

        await _streamRepository.InsertAsync(stream, autoSave: true);
        return await EnrichDtoAsync(stream);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Update)]
    public async Task<CdcStreamDto> UpdateAsync(Guid id, CdcStreamCreateUpdateDto input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Update);

        var stream = await _streamRepository.GetAsync(id);
        var normalized = await ValidateAndNormalizeInputAsync(input, excludeId: id);

        stream.Update(
            normalized.Name,
            input.SourceId,
            normalized.PublicationName,
            normalized.SlotName,
            input.TargetMode,
            normalized.TargetTopicName,
            normalized.TargetPipelineId,
            normalized.MonitoredTables,
            input.AutoCreatePublicationAndSlot,
            input.PollIntervalSeconds,
            input.MaxBatchSize,
            input.Description,
            normalized.TargetSinkId,
            normalized.TargetSinkDestination);

        await _streamRepository.UpdateAsync(stream, autoSave: true);
        await _scheduleSynchronizer.SyncAsync(stream.Id, stream.Status, stream.PollIntervalSeconds);
        return await EnrichDtoAsync(stream);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Delete);

        await _scheduleSynchronizer.RemoveAsync(id);
        await _streamRepository.DeleteAsync(id);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public async Task<CdcPreflightResultDto> PreflightAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Manage);

        var stream = await _streamRepository.GetAsync(id, cancellationToken: cancellationToken);
        var source = await _sourceRepository.GetAsync(stream.SourceId, cancellationToken: cancellationToken);
        return await _captureService.PreflightAsync(source, stream, cancellationToken);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public async Task ActivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Manage);

        var stream = await _streamRepository.GetAsync(id, cancellationToken: cancellationToken);
        var source = await _sourceRepository.GetAsync(stream.SourceId, cancellationToken: cancellationToken);

        var preflight = await _captureService.PreflightAsync(source, stream, cancellationToken);
        if (!preflight.IsReady)
        {
            var reason = string.Join("；", preflight.Diagnostics);
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                .WithData("Reason", reason);
        }

        stream.Activate();
        await _streamRepository.UpdateAsync(stream, autoSave: true, cancellationToken: cancellationToken);
        await _scheduleSynchronizer.SyncAsync(stream.Id, stream.Status, stream.PollIntervalSeconds, cancellationToken);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public async Task PauseAsync(Guid id)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Manage);

        var stream = await _streamRepository.GetAsync(id);
        stream.Pause();
        await _streamRepository.UpdateAsync(stream, autoSave: true);
        await _scheduleSynchronizer.SyncAsync(stream.Id, stream.Status, stream.PollIntervalSeconds);
    }

    [UnitOfWork(IsDisabled = true)]
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public Task<CdcCaptureResultDto> CaptureNowAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Manage);

        return _captureCoordinator.CaptureAsync(id, requireActiveStatus: false, cancellationToken);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public async Task<CdcStreamDto> ResetCheckpointAsync(Guid id, CdcResetCheckpointInput input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Manage);

        var stream = await _streamRepository.GetAsync(id);
        if (stream.Status == CdcStreamStatus.Active)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "重置 WAL LSN 位点前请先暂停 CDC 流。");
        }

        string? normalizedLsn = null;
        if (!string.IsNullOrWhiteSpace(input.CheckpointLsn))
        {
            normalizedLsn = CdcProtocolHelpers.ValidateLsn(input.CheckpointLsn).ToString();
        }

        stream.ResetCheckpoint(normalizedLsn);
        await _streamRepository.UpdateAsync(stream, autoSave: true);
        return await EnrichDtoAsync(stream);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Delete)]
    public async Task<CdcDropSlotResultDto> DropSlotAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Delete);

        var stream = await _streamRepository.GetAsync(id, cancellationToken: cancellationToken);
        if (stream.Status == CdcStreamStatus.Active)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "释放复制槽前请先暂停 CDC 流。");
        }

        var source = await _sourceRepository.GetAsync(stream.SourceId, cancellationToken: cancellationToken);
        var result = await _captureService.DropSlotAsync(source, stream, cancellationToken);
        if (result.Dropped)
        {
            stream.ResetCheckpoint(null);
            await _streamRepository.UpdateAsync(stream, autoSave: true, cancellationToken: cancellationToken);
        }

        return result;
    }

    private sealed record NormalizedCdcInput(
        string Name,
        string PublicationName,
        string SlotName,
        string? MonitoredTables,
        string? TargetTopicName,
        Guid? TargetPipelineId,
        Guid? TargetSinkId,
        string? TargetSinkDestination);

    private async Task<NormalizedCdcInput> ValidateAndNormalizeInputAsync(
        CdcStreamCreateUpdateDto input,
        Guid? excludeId)
    {
        var name = input.Name.Trim();
        if (await _streamRepository.NameExistsAsync(name, excludeId))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamNameAlreadyExists);
        }

        var source = await _sourceRepository.FindAsync(input.SourceId);
        if (source == null)
        {
            throw new EntityNotFoundException(typeof(DataSource), input.SourceId);
        }

        PgOutputCdcCaptureService.EnsurePostgreSqlSource(source);

        var pub = CdcProtocolHelpers.ValidateReplicationIdentifier(input.PublicationName, "PublicationName");
        var slot = CdcProtocolHelpers.ValidateReplicationIdentifier(input.SlotName, "SlotName");
        var parsedTables = CdcProtocolHelpers.ParseMonitoredTables(input.MonitoredTables);
        var normalizedTables = parsedTables.Count > 0
            ? string.Join(", ", parsedTables.Select(t => t.FullName))
            : null;

        string? topicName = null;
        Guid? pipelineId = null;
        Guid? sinkId = null;
        string? sinkDestination = null;

        if (input.TargetMode == CdcTargetMode.MessageBusTopic)
        {
            topicName = string.IsNullOrWhiteSpace(input.TargetTopicName)
                ? IntegrationTopicNames.CdcEvents
                : input.TargetTopicName.Trim();
        }
        else if (input.TargetMode == CdcTargetMode.DataPipeline)
        {
            if (!input.TargetPipelineId.HasValue)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "联动管道模式下必须选择关联的目标数据管道。");
            }

            var pipeline = await _pipelineRepository.FindAsync(input.TargetPipelineId.Value);
            if (pipeline == null)
            {
                throw new EntityNotFoundException(typeof(DataPipeline), input.TargetPipelineId.Value);
            }

            pipelineId = pipeline.Id;
        }
        else if (input.TargetMode == CdcTargetMode.ExternalSink)
        {
            if (!input.TargetSinkId.HasValue)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "外部接收端模式下必须选择目标接收端数据源。");
            }

            var sink = await _sourceRepository.FindAsync(input.TargetSinkId.Value);
            if (sink == null)
            {
                throw new EntityNotFoundException(typeof(DataSource), input.TargetSinkId.Value);
            }

            if (sink.Type is not (DataSourceType.Kafka or DataSourceType.ClickHouse or DataSourceType.ObjectStorage))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"目标数据源仅支持 Kafka / ClickHouse / ObjectStorage，当前为 {sink.Type}。");
            }

            var dest = input.TargetSinkDestination?.Trim();
            if (string.IsNullOrWhiteSpace(dest) || !EtlTransformConfig.IsValidDestinationFor(sink.Type, dest))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "外部接收端目标标识（Topic / 库表 / 对象前缀）缺失或格式不合法。");
            }

            sinkId = sink.Id;
            sinkDestination = dest;
        }
        else
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "不支持的 CDC 下游投递模式。");
        }

        return new NormalizedCdcInput(
            name,
            pub,
            slot,
            normalizedTables,
            topicName,
            pipelineId,
            sinkId,
            sinkDestination);
    }

    private async Task<CdcStreamDto> EnrichDtoAsync(CdcStreamDefinition stream)
    {
        var source = await _sourceRepository.FindAsync(stream.SourceId);
        var pipeline = stream.TargetPipelineId.HasValue
            ? await _pipelineRepository.FindAsync(stream.TargetPipelineId.Value)
            : null;
        var sink = stream.TargetSinkId.HasValue
            ? await _sourceRepository.FindAsync(stream.TargetSinkId.Value)
            : null;
        return MapToDto(stream, source?.Name, pipeline?.Name, sink);
    }

    private static CdcStreamDto MapToDto(
        CdcStreamDefinition stream,
        string? sourceName,
        string? targetPipelineName,
        DataSource? targetSink) => new()
    {
        Id = stream.Id,
        CreationTime = stream.CreationTime,
        CreatorId = stream.CreatorId,
        LastModificationTime = stream.LastModificationTime,
        LastModifierId = stream.LastModifierId,
        IsDeleted = stream.IsDeleted,
        DeleterId = stream.DeleterId,
        DeletionTime = stream.DeletionTime,
        Name = stream.Name,
        Description = stream.Description,
        SourceId = stream.SourceId,
        SourceName = sourceName,
        PublicationName = stream.PublicationName,
        SlotName = stream.SlotName,
        MonitoredTables = stream.MonitoredTables,
        AutoCreatePublicationAndSlot = stream.AutoCreatePublicationAndSlot,
        PollIntervalSeconds = stream.PollIntervalSeconds,
        MaxBatchSize = stream.MaxBatchSize,
        TargetMode = stream.TargetMode,
        TargetTopicName = stream.TargetTopicName,
        TargetPipelineId = stream.TargetPipelineId,
        TargetPipelineName = targetPipelineName,
        TargetSinkId = stream.TargetSinkId,
        TargetSinkName = targetSink?.Name,
        TargetSinkType = targetSink?.Type,
        TargetSinkDestination = stream.TargetSinkDestination,
        Status = stream.Status,
        CheckpointLsn = stream.CheckpointLsn,
        LastCapturedAt = stream.LastCapturedAt,
        LastCapturedCount = stream.LastCapturedCount,
        TotalCapturedCount = stream.TotalCapturedCount,
        LastErrorCode = stream.LastErrorCode,
        LastErrorMessage = stream.LastErrorMessage,
    };
}
