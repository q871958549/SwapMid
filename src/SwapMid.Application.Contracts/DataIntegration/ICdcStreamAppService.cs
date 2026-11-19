using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace SwapMid.DataIntegration;

public interface ICdcStreamAppService : IApplicationService
{
    Task<PagedResultDto<CdcStreamDto>> GetListAsync(GetCdcStreamListInput input);
    Task<CdcStreamDto> GetAsync(Guid id);
    Task<CdcStreamDto> CreateAsync(CdcStreamCreateUpdateDto input);
    Task<CdcStreamDto> UpdateAsync(Guid id, CdcStreamCreateUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<CdcPreflightResultDto> PreflightAsync(Guid id, CancellationToken cancellationToken = default);
    Task ActivateAsync(Guid id, CancellationToken cancellationToken = default);
    Task PauseAsync(Guid id);
    Task<CdcCaptureResultDto> CaptureNowAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CdcStreamDto> ResetCheckpointAsync(Guid id, CdcResetCheckpointInput input);
    Task<CdcDropSlotResultDto> DropSlotAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CdcSinkTemplateDto> GetSinkTemplateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CdcSnapshotResultDto> RunSnapshotAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CdcSinkTemplateDto> ProvisionExternalSinkAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CdcAcceptSchemaResultDto> AcceptSchemaBaselineAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CdcMirrorSchemaInspectionDto> InspectMirrorSchemaAsync(InspectCdcMirrorSchemaInput input, CancellationToken cancellationToken = default);
    Task<CdcLakehouseCompactionResultDto> CompactLakehouseAsync(Guid id, CompactCdcLakehouseInput input, CancellationToken cancellationToken = default);
    Task<CdcLakehouseHistoryDto> GetLakehouseHistoryAsync(Guid id, GetCdcLakehouseHistoryInput input, CancellationToken cancellationToken = default);
    Task<CdcLakehousePreviewDto> PreviewLakehouseDataAsync(Guid id, GetCdcLakehousePreviewInput input, CancellationToken cancellationToken = default);
    Task<CdcCatalogSyncResultDto> SyncCatalogAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CdcContractEvaluationDto> EvaluateContractAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CdcLakehouseTagResultDto> CreateLakehouseTagAsync(Guid id, CreateCdcLakehouseTagInput input, CancellationToken cancellationToken = default);
    Task<CdcLakehouseExpireResultDto> ExpireLakehouseSnapshotsAsync(Guid id, ExpireCdcLakehouseSnapshotsInput input, CancellationToken cancellationToken = default);
    Task<CdcLakehouseBranchPromoteResultDto> PromoteLakehouseBranchAsync(Guid id, PromoteCdcLakehouseBranchInput input, CancellationToken cancellationToken = default);
    Task<CdcLakehouseRollbackResultDto> RollbackLakehouseAsync(Guid id, RollbackCdcLakehouseInput input, CancellationToken cancellationToken = default);
}
