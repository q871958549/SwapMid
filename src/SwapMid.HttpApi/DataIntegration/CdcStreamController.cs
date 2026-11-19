using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwapMid.DataIntegration;
using SwapMid.Permissions;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Uow;

namespace SwapMid.Controllers.DataIntegration;

[RemoteService]
[Area("app")]
[Route("api/v1/cdc-streams")]
public class CdcStreamController : SwapMidController, ICdcStreamAppService
{
    private readonly ICdcStreamAppService _service;

    public CdcStreamController(ICdcStreamAppService service)
    {
        _service = service;
    }

    /// <summary>分页查询 PostgreSQL CDC 实时流定义列表</summary>
    [HttpGet]
    public Task<PagedResultDto<CdcStreamDto>> GetListAsync(GetCdcStreamListInput input)
        => _service.GetListAsync(input);

    /// <summary>获取单个 PostgreSQL CDC 实时流详情</summary>
    [HttpGet("{id}")]
    public Task<CdcStreamDto> GetAsync(Guid id)
        => _service.GetAsync(id);

    /// <summary>新建 PostgreSQL CDC 实时流定义</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Create)]
    [HttpPost]
    public Task<CdcStreamDto> CreateAsync(CdcStreamCreateUpdateDto input)
        => _service.CreateAsync(input);

    /// <summary>更新 PostgreSQL CDC 实时流配置</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Update)]
    [HttpPut("{id}")]
    public Task<CdcStreamDto> UpdateAsync(Guid id, CdcStreamCreateUpdateDto input)
        => _service.UpdateAsync(id, input);

    /// <summary>删除 PostgreSQL CDC 实时流定义</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Delete)]
    [HttpDelete("{id}")]
    public Task DeleteAsync(Guid id)
        => _service.DeleteAsync(id);

    /// <summary>执行 PostgreSQL 逻辑复制前置预检（wal_level、Publication 与 Replication Slot）</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPost("{id}/preflight")]
    public Task<CdcPreflightResultDto> PreflightAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _service.PreflightAsync(id, cancellationToken);

    /// <summary>激活 CDC 实时流并注册 Hangfire 轮询捕获作业</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPut("{id}/activate")]
    public Task ActivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _service.ActivateAsync(id, cancellationToken);

    /// <summary>暂停 CDC 实时流并移除调度轮询作业</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPut("{id}/pause")]
    public Task PauseAsync(Guid id)
        => _service.PauseAsync(id);

    /// <summary>手动立即捕获一批 WAL 变更并投递到下游目标</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPost("{id}/capture")]
    [UnitOfWork(IsDisabled = true)]
    public Task<CdcCaptureResultDto> CaptureNowAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _service.CaptureNowAsync(id, cancellationToken);

    /// <summary>重置或清空 CDC 流的逻辑复制位点（LSN）</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPut("{id}/checkpoint")]
    public Task<CdcStreamDto> ResetCheckpointAsync(
        Guid id,
        [FromBody] CdcResetCheckpointInput input)
        => _service.ResetCheckpointAsync(id, input);

    /// <summary>安全释放闲置的 PostgreSQL 逻辑复制槽并清空检查点位点</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Delete)]
    [HttpPost("{id}/drop-slot")]
    public Task<CdcDropSlotResultDto> DropSlotAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _service.DropSlotAsync(id, cancellationToken);

    /// <summary>获取外部接收端（ClickHouse / Kafka / ObjectStorage）标准结构与建表 DDL 模板</summary>
    [HttpGet("{id}/sink-template")]
    public Task<CdcSinkTemplateDto> GetSinkTemplateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _service.GetSinkTemplateAsync(id, cancellationToken);

    /// <summary>执行一次 PostgreSQL 一致性全量基线快照（EXPORT_SNAPSHOT）并投递下游</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPost("{id}/snapshot")]
    [UnitOfWork(IsDisabled = true)]
    public Task<CdcSnapshotResultDto> RunSnapshotAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _service.RunSnapshotAsync(id, cancellationToken);

    /// <summary>针对 ClickHouse 外部接收端自动创建标准 CDC 变更日志表</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPost("{id}/provision-sink")]
    public Task<CdcSinkTemplateDto> ProvisionExternalSinkAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _service.ProvisionExternalSinkAsync(id, cancellationToken);

    /// <summary>确认上游最新表结构作为新的 Schema 基线并清除漂移告警</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPost("{id}/accept-schema")]
    public Task<CdcAcceptSchemaResultDto> AcceptSchemaBaselineAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _service.AcceptSchemaBaselineAsync(id, cancellationToken);

    /// <summary>探测关系表行级镜像双端表结构并验证字段映射与软删除列契约</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPost("inspect-mirror-schema")]
    public Task<CdcMirrorSchemaInspectionDto> InspectMirrorSchemaAsync(
        [FromBody] InspectCdcMirrorSchemaInput input,
        CancellationToken cancellationToken = default)
        => _service.InspectMirrorSchemaAsync(input, cancellationToken);

    /// <summary>手动触发 Lakehouse（S3/MinIO）Parquet/JSONL 小分片合并压缩并更新 _swapmid/latest.json</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPost("{id}/compact-lakehouse")]
    public Task<CdcLakehouseCompactionResultDto> CompactLakehouseAsync(
        Guid id,
        [FromBody] CompactCdcLakehouseInput? input = null,
        CancellationToken cancellationToken = default)
        => _service.CompactLakehouseAsync(id, input ?? new CompactCdcLakehouseInput(), cancellationToken);

    /// <summary>查询 Delta Lake _delta_log 提交时间线及任意历史版本活跃分片文件快照（Time-Travel）</summary>
    [HttpGet("{id}/lakehouse-history")]
    public Task<CdcLakehouseHistoryDto> GetLakehouseHistoryAsync(
        Guid id,
        [FromQuery] GetCdcLakehouseHistoryInput? input = null,
        CancellationToken cancellationToken = default)
        => _service.GetLakehouseHistoryAsync(id, input ?? new GetCdcLakehouseHistoryInput(), cancellationToken);

    /// <summary>零计算集群实时预览 Lakehouse（S3/MinIO Parquet 或 JSONL）任意 Time-Travel 版本的样本数据行</summary>
    [HttpGet("{id}/lakehouse-preview")]
    public Task<CdcLakehousePreviewDto> PreviewLakehouseDataAsync(
        Guid id,
        [FromQuery] GetCdcLakehousePreviewInput? input = null,
        CancellationToken cancellationToken = default)
        => _service.PreviewLakehouseDataAsync(id, input ?? new GetCdcLakehousePreviewInput(), cancellationToken);

    /// <summary>手动将当前 Lakehouse 最新表位置与列 Schema 注册或同步到外部 Unity Catalog / Iceberg REST 目录</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPost("{id}/sync-catalog")]
    public Task<CdcCatalogSyncResultDto> SyncCatalogAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _service.SyncCatalogAsync(id, cancellationToken);

    /// <summary>评估 CDC 数据契约与新鲜度 SLA 合规态（新鲜度滞后、必填列、Schema 漂移与 WAL 保留预算）并更新告警状态</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPost("{id}/evaluate-contract")]
    public Task<CdcContractEvaluationDto> EvaluateContractAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _service.EvaluateContractAsync(id, cancellationToken);

    /// <summary>为 Apache Iceberg v2 快照创建或更新命名标签（WAP / 审计锚点，受 expireSnapshots 永久保护）</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPost("{id}/lakehouse-tags")]
    public Task<CdcLakehouseTagResultDto> CreateLakehouseTagAsync(
        Guid id,
        [FromBody] CreateCdcLakehouseTagInput input,
        CancellationToken cancellationToken = default)
        => _service.CreateLakehouseTagAsync(id, input, cancellationToken);

    /// <summary>安全评估或执行 Apache Iceberg v2 快照过期（expireSnapshots）与孤儿对象清理（默认 DryRun=true）</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPost("{id}/expire-lakehouse-snapshots")]
    public Task<CdcLakehouseExpireResultDto> ExpireLakehouseSnapshotsAsync(
        Guid id,
        [FromBody] ExpireCdcLakehouseSnapshotsInput? input = null,
        CancellationToken cancellationToken = default)
        => _service.ExpireLakehouseSnapshotsAsync(id, input ?? new ExpireCdcLakehouseSnapshotsInput(), cancellationToken);

    /// <summary>校验数据契约门禁与祖先链并将 Apache Iceberg v2 WAP 分支（如 staging）原子快进发布至 main</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPost("{id}/promote-lakehouse-branch")]
    public Task<CdcLakehouseBranchPromoteResultDto> PromoteLakehouseBranchAsync(
        Guid id,
        [FromBody] PromoteCdcLakehouseBranchInput? input = null,
        CancellationToken cancellationToken = default)
        => _service.PromoteLakehouseBranchAsync(id, input ?? new PromoteCdcLakehouseBranchInput(), cancellationToken);

    /// <summary>将 Apache Iceberg v2 表 main 分支原子回滚至指定历史快照版本或命名标签（默认 DryRun=true，支持备份原 main 标签）</summary>
    [Authorize(SwapMidPermissions.DataIntegration.CdcStreams.Manage)]
    [HttpPost("{id}/rollback-lakehouse")]
    public Task<CdcLakehouseRollbackResultDto> RollbackLakehouseAsync(
        Guid id,
        [FromBody] RollbackCdcLakehouseInput input,
        CancellationToken cancellationToken = default)
        => _service.RollbackLakehouseAsync(id, input, cancellationToken);
}
