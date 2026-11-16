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
    private readonly DataIntegrationSinkProbeService? _sinkProbeService;

    public CdcStreamAppService(
        ICdcStreamRepository streamRepository,
        IRepository<DataSource, Guid> sourceRepository,
        IRepository<DataPipeline, Guid> pipelineRepository,
        PgOutputCdcCaptureService captureService,
        CdcStreamCaptureCoordinator captureCoordinator,
        ICdcScheduleSynchronizer? scheduleSynchronizer = null,
        DataIntegrationSinkProbeService? sinkProbeService = null)
    {
        _streamRepository = streamRepository;
        _sourceRepository = sourceRepository;
        _pipelineRepository = pipelineRepository;
        _captureService = captureService;
        _captureCoordinator = captureCoordinator;
        _scheduleSynchronizer = scheduleSynchronizer ?? NullCdcScheduleSynchronizer.Instance;
        _sinkProbeService = sinkProbeService;
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
            return CdcStreamMappingHelpers.MapToDto(
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

        var normalized = await CdcStreamMappingHelpers.ValidateAndNormalizeInputAsync(
            input,
            excludeId: null,
            _streamRepository,
            _sourceRepository,
            _pipelineRepository);
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
            normalized.TargetSinkDestination,
            normalized.TargetSinkKeyColumns,
            input.InitialSnapshotEnabled);

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
        var normalized = await CdcStreamMappingHelpers.ValidateAndNormalizeInputAsync(
            input,
            excludeId: id,
            _streamRepository,
            _sourceRepository,
            _pipelineRepository);

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
            normalized.TargetSinkDestination,
            normalized.TargetSinkKeyColumns,
            input.InitialSnapshotEnabled);

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
            stream.ResetSnapshot();
            await _streamRepository.UpdateAsync(stream, autoSave: true, cancellationToken: cancellationToken);
        }

        return result;
    }

    public async Task<CdcSinkTemplateDto> GetSinkTemplateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Default);

        var stream = await _streamRepository.GetAsync(id, cancellationToken: cancellationToken);
        if (stream.TargetMode != CdcTargetMode.ExternalSink || !stream.TargetSinkId.HasValue)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "仅外部接收端（ExternalSink）模式的 CDC 流支持生成接收端结构与 DDL 模板。");
        }

        var sink = await _sourceRepository.GetAsync(stream.TargetSinkId.Value, cancellationToken: cancellationToken);
        return await CdcExternalSinkTemplateBuilder.BuildAsync(stream, sink, _sinkProbeService, cancellationToken);
    }

    [UnitOfWork(IsDisabled = true)]
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public Task<CdcSnapshotResultDto> RunSnapshotAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Manage);

        return _captureCoordinator.SnapshotAsync(id, cancellationToken);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public async Task<CdcSinkTemplateDto> ProvisionExternalSinkAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Manage);

        var stream = await _streamRepository.GetAsync(id, cancellationToken: cancellationToken);
        if (stream.TargetMode != CdcTargetMode.ExternalSink || !stream.TargetSinkId.HasValue)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "仅外部接收端（ExternalSink）模式的 CDC 流支持自动初始化下游目标表。");
        }

        var sink = await _sourceRepository.GetAsync(stream.TargetSinkId.Value, cancellationToken: cancellationToken);
        return await CdcExternalSinkTemplateBuilder.ProvisionClickHouseAsync(stream, sink, _sinkProbeService, cancellationToken);
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
        return CdcStreamMappingHelpers.MapToDto(stream, source?.Name, pipeline?.Name, sink);
    }
}
