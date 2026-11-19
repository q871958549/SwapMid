using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using SwapMid.Notification;
using SwapMid.Permissions;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
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
    private readonly KafkaDebeziumCdcCaptureService? _kafkaCaptureService;
    private readonly CdcObjectStorageCompactionService? _lakehouseCompactor;
    private readonly DeltaLakeHistoryReader? _deltaHistoryReader;
    private readonly ExternalLakehouseCatalogSyncService? _catalogSyncService;
    private readonly CdcLakehouseDataPreviewService? _lakehousePreviewService;
    private readonly CdcDataContractEvaluator? _contractEvaluator;
    private readonly IcebergV2MaintenanceService? _icebergMaintenanceService;

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
        MySqlBinlogCdcCaptureService? mySqlCaptureService = null,
        DeltaLakeHistoryReader? deltaHistoryReader = null,
        ExternalLakehouseCatalogSyncService? catalogSyncService = null,
        CdcLakehouseDataPreviewService? lakehousePreviewService = null,
        CdcDataContractEvaluator? contractEvaluator = null,
        KafkaDebeziumCdcCaptureService? kafkaCaptureService = null,
        IcebergV2MaintenanceService? icebergMaintenanceService = null)
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
        _kafkaCaptureService = kafkaCaptureService;
        _lakehouseCompactor = lakehouseCompactor;
        _deltaHistoryReader = deltaHistoryReader;
        _catalogSyncService = catalogSyncService;
        _lakehousePreviewService = lakehousePreviewService;
        _contractEvaluator = contractEvaluator;
        _icebergMaintenanceService = icebergMaintenanceService;
    }

    public async Task<PagedResultDto<CdcStreamDto>> GetListAsync(GetCdcStreamListInput input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Default);
        var sorting = ListQuerySortingExtensions.ResolveWhitelistedSorting(
            input.Sorting, CdcStreamMappingHelpers.StreamSortingWhitelist, defaultSorting: "creationtime DESC", logger: Logger, idFieldName: null);
        var totalCount = await _streamRepository.GetCountAsync(input.Filter, input.SourceId, input.Status);
        var items = await _streamRepository.GetListAsync(input.Filter, input.SourceId, input.Status, sorting, input.MaxResultCount, input.SkipCount);
        var dtos = await CdcStreamMappingHelpers.MapToDtoListAsync(items, _sourceRepository, _pipelineRepository);
        return new PagedResultDto<CdcStreamDto>(totalCount, dtos);
    }

    public async Task<CdcStreamDto> GetAsync(Guid id)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Default);
        var stream = await _streamRepository.GetAsync(id);
        return await EnrichDtoAsync(stream);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Create)]
    public async Task<CdcStreamDto> CreateAsync(CdcStreamCreateUpdateDto input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Create);
        var normalized = await CdcStreamMappingHelpers.ValidateAndNormalizeInputAsync(
            input, excludeId: null, _streamRepository, _sourceRepository, _pipelineRepository);
        var stream = new CdcStreamDefinition(
            GuidGenerator.Create(), normalized.Name, input.SourceId, normalized.PublicationName, normalized.SlotName,
            input.TargetMode, normalized.TargetTopicName, normalized.TargetPipelineId, normalized.MonitoredTables,
            input.AutoCreatePublicationAndSlot, input.PollIntervalSeconds, input.MaxBatchSize, input.Description,
            normalized.TargetSinkId, normalized.TargetSinkDestination, normalized.TargetSinkKeyColumns,
            input.InitialSnapshotEnabled, input.TargetDeletePolicy, input.SchemaDriftPolicy,
            normalized.TargetFieldMappingsJson, normalized.SoftDeleteColumn, normalized.SoftDeleteTimestampColumn,
            normalized.SourceType, normalized.LakehouseCompactMinFiles, normalized.LakehouseTableFormat,
            normalized.CatalogType, normalized.CatalogSinkId, normalized.CatalogNamespace, normalized.CatalogTableName);
        stream.ConfigureLakehousePartitionStrategy(normalized.LakehousePartitionStrategy);
        stream.ConfigureLakehouseWriteMode(input.LakehouseWriteMode);
        stream.ConfigureLakehouseWapAndMaintenance(
            input.LakehouseWriteBranch,
            DataPipelineCronValidator.ValidateAndNormalize(false, input.LakehouseMaintenanceCron),
            input.LakehouseExpireRetainLast, input.LakehouseExpireOlderThanMinutes, input.LakehouseDeleteOrphanedFiles);
        stream.ConfigureDataContract(input.MaxFreshnessLagSeconds, input.MaxAllowedWalLagBytes, input.RequiredColumns);

        await _streamRepository.InsertAsync(stream, autoSave: true);
        return await EnrichDtoAsync(stream);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Update)]
    public async Task<CdcStreamDto> UpdateAsync(Guid id, CdcStreamCreateUpdateDto input)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Update);
        var stream = await _streamRepository.GetAsync(id);
        var normalized = await CdcStreamMappingHelpers.ValidateAndNormalizeInputAsync(
            input, excludeId: id, _streamRepository, _sourceRepository, _pipelineRepository);

        stream.Update(
            normalized.Name, input.SourceId, normalized.PublicationName, normalized.SlotName,
            input.TargetMode, normalized.TargetTopicName, normalized.TargetPipelineId, normalized.MonitoredTables,
            input.AutoCreatePublicationAndSlot, input.PollIntervalSeconds, input.MaxBatchSize, input.Description,
            normalized.TargetSinkId, normalized.TargetSinkDestination, normalized.TargetSinkKeyColumns,
            input.InitialSnapshotEnabled, input.TargetDeletePolicy, input.SchemaDriftPolicy,
            normalized.TargetFieldMappingsJson, normalized.SoftDeleteColumn, normalized.SoftDeleteTimestampColumn,
            normalized.SourceType, normalized.LakehouseCompactMinFiles, normalized.LakehouseTableFormat,
            normalized.CatalogType, normalized.CatalogSinkId, normalized.CatalogNamespace, normalized.CatalogTableName);
        stream.ConfigureLakehousePartitionStrategy(normalized.LakehousePartitionStrategy);
        stream.ConfigureLakehouseWriteMode(input.LakehouseWriteMode);
        stream.ConfigureLakehouseWapAndMaintenance(
            input.LakehouseWriteBranch,
            DataPipelineCronValidator.ValidateAndNormalize(false, input.LakehouseMaintenanceCron),
            input.LakehouseExpireRetainLast, input.LakehouseExpireOlderThanMinutes, input.LakehouseDeleteOrphanedFiles);
        stream.ConfigureDataContract(input.MaxFreshnessLagSeconds, input.MaxAllowedWalLagBytes, input.RequiredColumns);

        await _streamRepository.UpdateAsync(stream, autoSave: true);
        var effectiveScheduleStatus = stream.SourceType == CdcSourceType.PushWebhook ? CdcStreamStatus.Paused : stream.Status;
        await _scheduleSynchronizer.SyncAsync(stream.Id, effectiveScheduleStatus, stream.PollIntervalSeconds);
        await _scheduleSynchronizer.SyncMaintenanceAsync(stream.Id, stream.Status, stream.LakehouseMaintenanceCron);
        return await EnrichDtoAsync(stream);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Delete);
        await _scheduleSynchronizer.RemoveAsync(id);
        await _streamRepository.DeleteAsync(id);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public async Task<CdcMirrorSchemaInspectionDto> InspectMirrorSchemaAsync(InspectCdcMirrorSchemaInput input, CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Manage);
        var preflight = _mirrorPreflightService
            ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                .WithData("Reason", "关系表镜像结构预检服务未启用。");
        var source = await _sourceRepository.GetAsync(input.SourceId, cancellationToken: cancellationToken);
        var target = await _sourceRepository.GetAsync(input.TargetSinkId, cancellationToken: cancellationToken);
        return await preflight.InspectAsync(source, target, input, cancellationToken);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public async Task<CdcPreflightResultDto> PreflightAsync(Guid id, CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Manage);
        var stream = await _streamRepository.GetAsync(id, cancellationToken: cancellationToken);
        var source = await _sourceRepository.GetAsync(stream.SourceId, cancellationToken: cancellationToken);
        return await CdcStreamMappingHelpers.BuildPreflightWithMirrorCheckAsync(
            _captureService, _sqlServerCaptureService, _mySqlCaptureService, _mirrorPreflightService, _sourceRepository, source, stream, cancellationToken, _kafkaCaptureService);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public async Task ActivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Manage);
        var stream = await _streamRepository.GetAsync(id, cancellationToken: cancellationToken);
        var source = await _sourceRepository.GetAsync(stream.SourceId, cancellationToken: cancellationToken);

        var preflight = await CdcStreamMappingHelpers.BuildPreflightWithMirrorCheckAsync(
            _captureService, _sqlServerCaptureService, _mySqlCaptureService, _mirrorPreflightService, _sourceRepository, source, stream, cancellationToken, _kafkaCaptureService);
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
        var effectiveScheduleStatus = stream.SourceType == CdcSourceType.PushWebhook ? CdcStreamStatus.Paused : stream.Status;
        await _scheduleSynchronizer.SyncAsync(stream.Id, effectiveScheduleStatus, stream.PollIntervalSeconds, cancellationToken);
        await _scheduleSynchronizer.SyncMaintenanceAsync(stream.Id, stream.Status, stream.LakehouseMaintenanceCron, cancellationToken);
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
        await _scheduleSynchronizer.SyncMaintenanceAsync(stream.Id, stream.Status, stream.LakehouseMaintenanceCron);
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
    public async Task<CdcSinkTemplateDto> ProvisionExternalSinkAsync(Guid id, CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Manage);
        var (stream, sink) = await CdcStreamMappingHelpers.LoadExternalSinkContextAsync(
            id, "仅外部接收端（ExternalSink）模式的 CDC 流支持自动初始化下游目标表。", _streamRepository, _sourceRepository, cancellationToken);
        return await CdcExternalSinkTemplateBuilder.ProvisionClickHouseAsync(stream, sink, _sinkProbeService, cancellationToken);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public Task<CdcAcceptSchemaResultDto> AcceptSchemaBaselineAsync(Guid id, CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Manage);
        return CdcStreamMappingHelpers.AcceptSchemaBaselineCoreAsync(
            id, _streamRepository, _sourceRepository, _captureService, _sqlServerCaptureService, _mySqlCaptureService, _notificationService, Logger, cancellationToken);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public async Task<CdcLakehouseCompactionResultDto> CompactLakehouseAsync(Guid id, CompactCdcLakehouseInput input, CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Manage);
        var compactor = _lakehouseCompactor ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid).WithData("Reason", "Lakehouse 分片压缩服务未启用。");
        var (stream, sink) = await CdcStreamMappingHelpers.LoadExternalSinkContextAsync(
            id, "仅外部接收端（ObjectStorage）模式的 CDC 流支持 Lakehouse 分片合并。", _streamRepository, _sourceRepository, cancellationToken);
        var result = await compactor.CompactAsync(stream, sink, input.MinFiles, input.DeleteCompactedFiles, cancellationToken);
        if (result.Compacted && _catalogSyncService != null)
        {
            await _catalogSyncService.TryBestEffortSyncAsync(stream, sink, versionOverride: null, cancellationToken);
        }

        return result;
    }

    public async Task<CdcLakehouseHistoryDto> GetLakehouseHistoryAsync(Guid id, GetCdcLakehouseHistoryInput input, CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Default);
        var reader = _deltaHistoryReader ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid).WithData("Reason", "Delta Lake Time-Travel 历史读取服务未启用。");
        var (stream, sink) = await CdcStreamMappingHelpers.LoadExternalSinkContextAsync(
            id, "仅外部接收端（ObjectStorage）模式的 CDC 流支持查询 Delta Lake Time-Travel 提交历史。", _streamRepository, _sourceRepository, cancellationToken);
        return await reader.ReadAsync(stream, sink, input.Version, input.MaxCommits, cancellationToken);
    }

    public async Task<CdcLakehousePreviewDto> PreviewLakehouseDataAsync(Guid id, GetCdcLakehousePreviewInput input, CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Default);
        var preview = _lakehousePreviewService ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid).WithData("Reason", "Lakehouse 实时数据预览服务未启用。");
        var (stream, sink) = await CdcStreamMappingHelpers.LoadExternalSinkContextAsync(
            id, "仅外部接收端（ObjectStorage）模式的 CDC 流支持 Lakehouse 实时数据预览。", _streamRepository, _sourceRepository, cancellationToken);
        return await preview.PreviewAsync(stream, sink, input.Version, input.MaxRows, cancellationToken);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public Task<CdcCatalogSyncResultDto> SyncCatalogAsync(Guid id, CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Manage);
        var syncService = _catalogSyncService ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid).WithData("Reason", "外部开放目录元数据同步服务未启用。");
        return syncService.SyncAsync(id, cancellationToken);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public Task<CdcContractEvaluationDto> EvaluateContractAsync(Guid id, CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Manage);
        var evaluator = _contractEvaluator ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid).WithData("Reason", "CDC 数据契约 SLA 评估服务未启用。");
        return evaluator.EvaluateAndPersistAsync(id, probeLiveUpstream: true, cancellationToken);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public async Task<CdcLakehouseTagResultDto> CreateLakehouseTagAsync(Guid id, CreateCdcLakehouseTagInput input, CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Manage);
        var maintenance = _icebergMaintenanceService ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid).WithData("Reason", "Apache Iceberg v2 表维护服务未启用。");
        var (stream, sink) = await CdcStreamMappingHelpers.LoadExternalSinkContextAsync(
            id, "仅外部接收端（ObjectStorage）模式的 Apache Iceberg v2 CDC 流支持创建命名标签。", _streamRepository, _sourceRepository, cancellationToken);
        return await maintenance.CreateTagAsync(stream, sink, input, cancellationToken);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public async Task<CdcLakehouseExpireResultDto> ExpireLakehouseSnapshotsAsync(Guid id, ExpireCdcLakehouseSnapshotsInput input, CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Manage);
        var maintenance = _icebergMaintenanceService ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid).WithData("Reason", "Apache Iceberg v2 表维护服务未启用。");
        var (stream, sink) = await CdcStreamMappingHelpers.LoadExternalSinkContextAsync(
            id, "仅外部接收端（ObjectStorage）模式的 Apache Iceberg v2 CDC 流支持快照过期清理。", _streamRepository, _sourceRepository, cancellationToken);
        return await maintenance.ExpireSnapshotsAsync(stream, sink, input, cancellationToken);
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public async Task<CdcLakehouseBranchPromoteResultDto> PromoteLakehouseBranchAsync(Guid id, PromoteCdcLakehouseBranchInput input, CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Manage);
        var maintenance = _icebergMaintenanceService ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid).WithData("Reason", "Apache Iceberg v2 表维护服务未启用。");
        var (stream, sink) = await CdcStreamMappingHelpers.LoadExternalSinkContextAsync(
            id, "仅外部接收端（ObjectStorage）模式的 Apache Iceberg v2 CDC 流支持 WAP 分支快进发布。", _streamRepository, _sourceRepository, cancellationToken);
        var result = await maintenance.PromoteBranchAsync(stream, sink, input, cancellationToken);
        if (!result.DryRun && !result.AlreadyUpToDate && _catalogSyncService != null)
        {
            await _catalogSyncService.TryBestEffortSyncAsync(stream, sink, versionOverride: result.PromotedSnapshotVersion, cancellationToken);
        }

        return result;
    }

    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    public async Task<CdcLakehouseRollbackResultDto> RollbackLakehouseAsync(Guid id, RollbackCdcLakehouseInput input, CancellationToken cancellationToken = default)
    {
        global::SwapMid.DataIntegration.PipelineExecutionAppService.EnsureHostContext(base.CurrentTenant.Id, SwapMidPermissions.DataIntegration.CdcStreams.Manage);
        var maintenance = _icebergMaintenanceService ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid).WithData("Reason", "Apache Iceberg v2 表维护服务未启用。");
        var (stream, sink) = await CdcStreamMappingHelpers.LoadExternalSinkContextAsync(
            id, "仅外部接收端（ObjectStorage）模式的 Apache Iceberg v2 CDC 流支持原子时间旅行回滚。", _streamRepository, _sourceRepository, cancellationToken);
        var result = await maintenance.RollbackAsync(stream, sink, input, cancellationToken);
        if (!result.DryRun && !result.AlreadyAtTarget && _catalogSyncService != null)
        {
            await _catalogSyncService.TryBestEffortSyncAsync(stream, sink, versionOverride: result.TargetSnapshotVersion, cancellationToken);
        }

        return result;
    }

    private Task<CdcStreamDto> EnrichDtoAsync(CdcStreamDefinition stream)
        => CdcStreamMappingHelpers.EnrichDtoAsync(stream, _sourceRepository, _pipelineRepository);
}
