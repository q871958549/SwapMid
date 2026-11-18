using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using SwapMid.Monitoring;
using SwapMid.Notification;
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
    private readonly INotificationService? _notificationService;
    private readonly CdcRelationalMirrorPreflightService? _mirrorPreflightService;
    private readonly SqlServerCdcCaptureService? _sqlServerCaptureService;
    private readonly MySqlBinlogCdcCaptureService? _mySqlCaptureService;
    private readonly CdcObjectStorageCompactionService? _lakehouseCompactor;

    public CdcStreamAppService(
        ICdcStreamRepository streamRepository,
        IRepository<DataSource, Guid> sourceRepository,
        IRepository<DataPipeline, Guid> pipelineRepository,
        PgOutputCdcCaptureService captureService,
        CdcStreamCaptureCoordinator captureCoordinator,
        ICdcScheduleSynchronizer? scheduleSynchronizer = null,
        DataIntegrationSinkProbeService? sinkProbeService = null,
        INotificationService? notificationService = null,
        CdcRelationalMirrorPreflightService? mirrorPreflightService = null,
        SqlServerCdcCaptureService? sqlServerCaptureService = null,
        CdcObjectStorageCompactionService? lakehouseCompactor = null,
        MySqlBinlogCdcCaptureService? mySqlCaptureService = null)
    {
        _streamRepository = streamRepository;
        _sourceRepository = sourceRepository;
        _pipelineRepository = pipelineRepository;
        _captureService = captureService;
        _captureCoordinator = captureCoordinator;
        _scheduleSynchronizer = scheduleSynchronizer ?? NullCdcScheduleSynchronizer.Instance;
        _sinkProbeService = sinkProbeService;
        _notificationService = notificationService;
        _mirrorPreflightService = mirrorPreflightService;
        _sqlServerCaptureService = sqlServerCaptureService;
        _mySqlCaptureService = mySqlCaptureService;
        _lakehouseCompactor = lakehouseCompactor;
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
        var dtos = await CdcStreamMappingHelpers.MapToDtoListAsync(items, _sourceRepository, _pipelineRepository);
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
            input.InitialSnapshotEnabled,
            input.TargetDeletePolicy,
            input.SchemaDriftPolicy,
            normalized.TargetFieldMappingsJson,
            normalized.SoftDeleteColumn,
            normalized.SoftDeleteTimestampColumn,
            normalized.SourceType,
            normalized.LakehouseCompactMinFiles,
            normalized.LakehouseTableFormat);

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
            input.InitialSnapshotEnabled,
            input.TargetDeletePolicy,
            input.SchemaDriftPolicy,
            normalized.TargetFieldMappingsJson,
            normalized.SoftDeleteColumn,
            normalized.SoftDeleteTimestampColumn,
            normalized.SourceType,
            normalized.LakehouseCompactMinFiles,
            normalized.LakehouseTableFormat);

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
    public async Task<CdcMirrorSchemaInspectionDto> InspectMirrorSchemaAsync(
        InspectCdcMirrorSchemaInput input,
        CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Manage);

        var preflight = _mirrorPreflightService
            ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                .WithData("Reason", "关系表镜像结构预检服务未启用。");
        var source = await _sourceRepository.GetAsync(input.SourceId, cancellationToken: cancellationToken);
        var target = await _sourceRepository.GetAsync(input.TargetSinkId, cancellationToken: cancellationToken);
        return await preflight.InspectAsync(source, target, input, cancellationToken);
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
        return await CdcStreamMappingHelpers.BuildPreflightWithMirrorCheckAsync(
            _captureService,
            _sqlServerCaptureService,
            _mySqlCaptureService,
            _mirrorPreflightService,
            _sourceRepository,
            source,
            stream,
            cancellationToken);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public async Task ActivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Manage);

        var stream = await _streamRepository.GetAsync(id, cancellationToken: cancellationToken);
        var source = await _sourceRepository.GetAsync(stream.SourceId, cancellationToken: cancellationToken);

        var preflight = await CdcStreamMappingHelpers.BuildPreflightWithMirrorCheckAsync(
            _captureService,
            _sqlServerCaptureService,
            _mySqlCaptureService,
            _mirrorPreflightService,
            _sourceRepository,
            source,
            stream,
            cancellationToken);
        if (!preflight.IsReady)
        {
            var reason = string.Join("；", preflight.Diagnostics);
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                .WithData("Reason", reason);
        }

        if (preflight.SchemaDriftDetected && stream.SchemaDriftPolicy == CdcSchemaDriftPolicy.PauseAndAlert)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamSchemaDriftDetected)
                .WithData("Reason", preflight.SchemaDriftSummary ?? "检测到上游表结构漂移，请先确认新 Schema 基线后再激活。");
        }

        if (string.IsNullOrWhiteSpace(stream.SchemaBaselineJson))
        {
            var eval = await CdcStreamMappingHelpers.EvaluateSchemaForStreamAsync(
                _captureService, _sqlServerCaptureService, _mySqlCaptureService, source, stream, cancellationToken);
            if (eval.CurrentJson != "[]") stream.UpdateSchemaBaseline(eval.CurrentHash, eval.CurrentJson);
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

        var normalizedLsn = CdcStreamMappingHelpers.NormalizeCheckpointResetToken(stream.SourceType, input.CheckpointLsn);
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
        if (stream.SourceType != CdcSourceType.PostgreSqlLogicalReplication)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "仅 PostgreSQL 逻辑复制流支持释放上游 Replication Slot。");
        }

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

        var (stream, sink) = await CdcStreamMappingHelpers.LoadExternalSinkContextAsync(
            id,
            "仅外部接收端（ExternalSink）模式的 CDC 流支持生成接收端结构与 DDL 模板。",
            _streamRepository,
            _sourceRepository,
            cancellationToken);
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

        var (stream, sink) = await CdcStreamMappingHelpers.LoadExternalSinkContextAsync(
            id,
            "仅外部接收端（ExternalSink）模式的 CDC 流支持自动初始化下游目标表。",
            _streamRepository,
            _sourceRepository,
            cancellationToken);
        return await CdcExternalSinkTemplateBuilder.ProvisionClickHouseAsync(stream, sink, _sinkProbeService, cancellationToken);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public async Task<CdcLakehouseCompactionResultDto> CompactLakehouseAsync(
        Guid id,
        CompactCdcLakehouseInput input,
        CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Manage);

        var compactor = _lakehouseCompactor ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
            .WithData("Reason", "Lakehouse 分片压缩服务未启用。");
        var (stream, sink) = await CdcStreamMappingHelpers.LoadExternalSinkContextAsync(
            id, "仅外部接收端（ObjectStorage）模式的 CDC 流支持 Lakehouse 分片合并。", _streamRepository, _sourceRepository, cancellationToken);
        return await compactor.CompactAsync(stream, sink, input.MinFiles, input.DeleteCompactedFiles, cancellationToken);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public async Task<CdcAcceptSchemaResultDto> AcceptSchemaBaselineAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(
            base.CurrentTenant.Id,
            SwapMidPermissions.DataIntegration.CdcStreams.Manage);

        var stream = await _streamRepository.GetAsync(id, cancellationToken: cancellationToken);
        var source = await _sourceRepository.GetAsync(stream.SourceId, cancellationToken: cancellationToken);
        var eval = await CdcStreamMappingHelpers.EvaluateSchemaForStreamAsync(
            _captureService, _sqlServerCaptureService, _mySqlCaptureService, source, stream, cancellationToken);
        if (eval.CurrentJson == "[]")
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "未能读取到上游监控表或发布表的列定义，无法建立 Schema 基线。");
        }

        var hadDrift = stream.SchemaDriftDetected;
        stream.UpdateSchemaBaseline(eval.CurrentHash, eval.CurrentJson);
        await _streamRepository.UpdateAsync(stream, autoSave: true, cancellationToken: cancellationToken);
        PlatformMetrics.CdcSchemaDriftEventsTotal.WithLabels("accepted").Inc();

        if (hadDrift)
        {
            await CdcStreamMappingHelpers.NotifySchemaAcceptedAsync(
                stream,
                eval.CurrentHash,
                _notificationService,
                Logger,
                cancellationToken);
        }

        return new CdcAcceptSchemaResultDto
        {
            StreamId = stream.Id,
            SchemaBaselineHash = eval.CurrentHash,
            AcceptedAtUtc = DateTime.UtcNow,
            Message = $"已更新上游 Schema 基线（{eval.CurrentHash[..12]}），可正常激活或继续捕获。",
        };
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
