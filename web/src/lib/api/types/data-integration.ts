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
export type CdcTargetMode = import('../generated').SwapMidDataIntegrationCdcTargetMode
export type CdcPreflightResultDto = import('../generated').SwapMidDataIntegrationCdcPreflightResultDto
export type CdcCaptureResultDto = import('../generated').SwapMidDataIntegrationCdcCaptureResultDto
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
