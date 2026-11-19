import type { Require } from './_require'

// ===== 数据集成 =====
export type DataSourceDto = Require<
  import('../generated').SwapMidDataIntegrationDataSourceDto,
  'id' | 'name' | 'type' | 'config' | 'hasSecret' | 'isEnabled' | 'creationTime'
>
export type DataPipelineDto = Require<import('../generated').SwapMidDataIntegrationDataPipelineDto, 'id' | 'name' | 'sourceId' | 'scheduleEnabled' | 'creationTime'>
export type PipelineExecutionDto = Omit<
  Require<
    import('../generated').SwapMidDataIntegrationPipelineExecutionDto,
    'id' | 'pipelineId' | 'pipelineName' | 'status' | 'triggerType' | 'startedAt'
    | 'reconciliationStrategy' | 'receiptVerificationEligible' | 'creationTime'
  >,
  'finishedAt' | 'durationSeconds' | 'processedRows' | 'errorMessage' | 'errorCode' | 'correlationId' | 'exceptionType'
> & {
  finishedAt: string | null
  durationSeconds: number | null
  processedRows: number | null
  errorMessage: string | null
  errorCode: string | null
  correlationId: string | null
  exceptionType: string | null
}
export type PipelineSchemaPreflightResultDto = import('../generated').SwapMidDataIntegrationPipelineSchemaPreflightResultDto
export type PreflightSchemaIssueDto = import('../generated').SwapMidDataIntegrationPreflightSchemaIssueDto
export type PreflightColumnDto = import('../generated').SwapMidDataIntegrationPreflightColumnDto
export type CdcStreamStatus = import('../generated').SwapMidDataIntegrationCdcStreamStatus
export type CdcSnapshotStatus = import('../generated').SwapMidDataIntegrationCdcSnapshotStatus
export type CdcDeletePolicy = import('../generated').SwapMidDataIntegrationCdcDeletePolicy
export type CdcSchemaDriftPolicy = import('../generated').SwapMidDataIntegrationCdcSchemaDriftPolicy
export type CdcSourceType = import('../generated').SwapMidDataIntegrationCdcSourceType
export type CdcTargetMode = import('../generated').SwapMidDataIntegrationCdcTargetMode
export type CdcTableReplicaIdentityDto = import('../generated').SwapMidDataIntegrationCdcTableReplicaIdentityDto
export type CdcFieldMappingDto = import('../generated').SwapMidDataIntegrationCdcFieldMappingDto
export type CdcMirrorCompatibilityDto = import('../generated').SwapMidDataIntegrationCdcMirrorCompatibilityDto
export type CdcMirrorSchemaInspectionDto = import('../generated').SwapMidDataIntegrationCdcMirrorSchemaInspectionDto
export type InspectCdcMirrorSchemaInput = import('../generated').SwapMidDataIntegrationInspectCdcMirrorSchemaInput
export type CompactCdcLakehouseInput = import('../generated').SwapMidDataIntegrationCompactCdcLakehouseInput
export type CdcLakehouseCompactionResultDto = import('../generated').SwapMidDataIntegrationCdcLakehouseCompactionResultDto
export type CdcPreflightResultDto = import('../generated').SwapMidDataIntegrationCdcPreflightResultDto
export type CdcCaptureResultDto = import('../generated').SwapMidDataIntegrationCdcCaptureResultDto
export type CdcSnapshotResultDto = import('../generated').SwapMidDataIntegrationCdcSnapshotResultDto
export type CdcAcceptSchemaResultDto = import('../generated').SwapMidDataIntegrationCdcAcceptSchemaResultDto
export type CdcDropSlotResultDto = import('../generated').SwapMidDataIntegrationCdcDropSlotResultDto
export type CdcSinkColumnTemplateDto = import('../generated').SwapMidDataIntegrationCdcSinkColumnTemplateDto
export type CdcSinkTemplateDto = import('../generated').SwapMidDataIntegrationCdcSinkTemplateDto
export type CdcLakehouseHistoryDto = import('../generated').SwapMidDataIntegrationCdcLakehouseHistoryDto
export type CdcLakehouseRefDto = import('../generated').SwapMidDataIntegrationCdcLakehouseRefDto
export type CdcLakehouseTagResultDto = import('../generated').SwapMidDataIntegrationCdcLakehouseTagResultDto
export type CdcLakehouseExpireResultDto = import('../generated').SwapMidDataIntegrationCdcLakehouseExpireResultDto
export type CdcLakehouseBranchPromoteResultDto = import('../generated').SwapMidDataIntegrationCdcLakehouseBranchPromoteResultDto
export type CdcLakehouseRollbackResultDto = import('../generated').SwapMidDataIntegrationCdcLakehouseRollbackResultDto
export type CdcLakehousePreviewDto = import('../generated').SwapMidDataIntegrationCdcLakehousePreviewDto
export type CdcCatalogSyncResultDto = import('../generated').SwapMidDataIntegrationCdcCatalogSyncResultDto
export type CdcContractStatus = import('../generated').SwapMidDataIntegrationCdcContractStatus
export type CdcContractViolationItemDto = import('../generated').SwapMidDataIntegrationCdcContractViolationItemDto
export type CdcContractEvaluationDto = import('../generated').SwapMidDataIntegrationCdcContractEvaluationDto
export type CdcWebhookDeliveryListDto = import('../generated').SwapMidDataIntegrationCdcWebhookDeliveryListDto
export type CdcWebhookDeliveryRecordDto = import('../generated').SwapMidDataIntegrationCdcWebhookDeliveryRecordDto
export type CdcWebhookIngestResultDto = import('../generated').SwapMidDataIntegrationCdcWebhookIngestResultDto
export type IntegrationBundleExportResultDto = import('../generated').SwapMidControllersGitOpsIntegrationBundleExportResultDto
export type IntegrationBundleDiffInput = import('../generated').SwapMidControllersGitOpsIntegrationBundleDiffInput
export type IntegrationBundleDiffItemDto = import('../generated').SwapMidControllersGitOpsIntegrationBundleDiffItemDto
export type IntegrationBundleDiffResultDto = import('../generated').SwapMidControllersGitOpsIntegrationBundleDiffResultDto
export type IntegrationBundleApplyInput = import('../generated').SwapMidControllersGitOpsIntegrationBundleApplyInput
export type IntegrationBundleApplyResultDto = import('../generated').SwapMidControllersGitOpsIntegrationBundleApplyResultDto
export type IntegrationLineageNodeDto = import('../generated').SwapMidControllersLineageIntegrationLineageNodeDto
export type IntegrationColumnLineageDto = import('../generated').SwapMidControllersLineageIntegrationColumnLineageDto
export type IntegrationLineageEdgeDto = import('../generated').SwapMidControllersLineageIntegrationLineageEdgeDto
export type IntegrationLineageGraphDto = import('../generated').SwapMidControllersLineageIntegrationLineageGraphDto
export type IntegrationLineageImpactItemDto = import('../generated').SwapMidControllersLineageIntegrationLineageImpactItemDto
export type IntegrationLineageImpactDto = import('../generated').SwapMidControllersLineageIntegrationLineageImpactDto
export type OpenLineageExportDto = import('../generated').SwapMidControllersLineageOpenLineageExportDto
export type CdcStreamDto = Omit<
  Require<
    import('../generated').SwapMidDataIntegrationCdcStreamDto,
    'id' | 'sourceId' | 'autoCreatePublicationAndSlot' | 'pollIntervalSeconds'
    | 'maxBatchSize' | 'targetMode' | 'status' | 'lastCapturedCount'
    | 'totalCapturedCount' | 'creationTime'
  >,
  'name' | 'publicationName' | 'slotName'
> & {
  name: string
  publicationName: string
  slotName: string
}
