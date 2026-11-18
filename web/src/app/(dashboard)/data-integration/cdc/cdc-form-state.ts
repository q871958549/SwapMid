import type {
  CdcFieldMappingDto,
  CdcStreamDto,
  DataPipelineDto,
  DataSourceDto,
  MessageTopicDto,
} from '@/lib/api/types'
import { COLUMN_IDENTIFIER_PATTERN } from '@/lib/etl-column-mapping'

export type CdcSourceOption = Pick<DataSourceDto, 'id' | 'name' | 'type' | 'config'>
export type CdcPipelineOption = Pick<DataPipelineDto, 'id' | 'name'>
export type CdcTopicOption = Pick<MessageTopicDto, 'id' | 'name'>

export interface CdcFormState {
  name: string
  description: string
  sourceId: string
  publicationName: string
  slotName: string
  monitoredTables: string
  autoCreatePublicationAndSlot: boolean
  initialSnapshotEnabled: boolean
  pollIntervalSeconds: string
  maxBatchSize: string
  targetMode: '0' | '1' | '2' | '3'
  targetTopicName: string
  targetPipelineId: string
  targetSinkId: string
  targetSinkDestination: string
  targetSinkKeyColumns: string
  targetDeletePolicy: '0' | '1' | '2'
  softDeleteColumn: string
  softDeleteTimestampColumn: string
  targetFieldMappings: CdcFieldMappingDto[]
  schemaDriftPolicy: '0' | '1' | '2'
}

export const DEFAULT_CDC_TOPIC = 'integration.cdc.events'

export const defaultCdcForm: CdcFormState = {
  name: '',
  description: '',
  sourceId: '',
  publicationName: 'swapmid_cdc_pub',
  slotName: 'swapmid_cdc_slot',
  monitoredTables: '',
  autoCreatePublicationAndSlot: true,
  initialSnapshotEnabled: false,
  pollIntervalSeconds: '15',
  maxBatchSize: '500',
  targetMode: '0',
  targetTopicName: DEFAULT_CDC_TOPIC,
  targetPipelineId: '',
  targetSinkId: '',
  targetSinkDestination: '',
  targetSinkKeyColumns: '',
  targetDeletePolicy: '0',
  softDeleteColumn: '',
  softDeleteTimestampColumn: '',
  targetFieldMappings: [],
  schemaDriftPolicy: '0',
}

export function normalizeTargetMode(mode: number | undefined): '0' | '1' | '2' | '3' {
  if (Number(mode) === 1) return '1'
  if (Number(mode) === 2) return '2'
  if (Number(mode) === 3) return '3'
  return '0'
}

export function normalizeDeletePolicy(policy: number | undefined): '0' | '1' | '2' {
  if (Number(policy) === 1) return '1'
  if (Number(policy) === 2) return '2'
  return '0'
}

export function normalizeDriftPolicy(policy: number | undefined): '0' | '1' | '2' {
  if (Number(policy) === 1) return '1'
  if (Number(policy) === 2) return '2'
  return '0'
}

export function toCdcForm(item: CdcStreamDto): CdcFormState {
  const mappings = (item.targetFieldMappings ?? [])
    .filter((m): m is { sourceColumn: string; targetColumn: string } =>
      Boolean(m.sourceColumn?.trim() && m.targetColumn?.trim()))
    .map(m => ({
      sourceColumn: m.sourceColumn.trim(),
      targetColumn: m.targetColumn.trim(),
    }))

  return {
    name: item.name,
    description: item.description ?? '',
    sourceId: item.sourceId,
    publicationName: item.publicationName,
    slotName: item.slotName,
    monitoredTables: item.monitoredTables ?? '',
    autoCreatePublicationAndSlot: item.autoCreatePublicationAndSlot,
    initialSnapshotEnabled: item.initialSnapshotEnabled ?? false,
    pollIntervalSeconds: String(item.pollIntervalSeconds || 15),
    maxBatchSize: String(item.maxBatchSize || 500),
    targetMode: normalizeTargetMode(item.targetMode),
    targetTopicName: item.targetTopicName ?? DEFAULT_CDC_TOPIC,
    targetPipelineId: item.targetPipelineId ?? '',
    targetSinkId: item.targetSinkId ?? '',
    targetSinkDestination: item.targetSinkDestination ?? '',
    targetSinkKeyColumns: item.targetSinkKeyColumns ?? '',
    targetDeletePolicy: normalizeDeletePolicy(item.targetDeletePolicy),
    softDeleteColumn: item.softDeleteColumn ?? '',
    softDeleteTimestampColumn: item.softDeleteTimestampColumn ?? '',
    targetFieldMappings: mappings,
    schemaDriftPolicy: normalizeDriftPolicy(item.schemaDriftPolicy),
  }
}

export function toCdcBody(form: CdcFormState) {
  const poll = Number.parseInt(form.pollIntervalSeconds, 10)
  const batch = Number.parseInt(form.maxBatchSize, 10)
  const rawMode = Number(form.targetMode)
  const mode = rawMode === 1 || rawMode === 2 || rawMode === 3 ? rawMode : 0
  const usesSinkTarget = mode === 2 || mode === 3
  const deletePolicy = mode === 3 ? Number(normalizeDeletePolicy(Number(form.targetDeletePolicy))) : 0
  const cleanMappings = mode === 3
    ? form.targetFieldMappings
        .map(m => ({
          sourceColumn: m.sourceColumn.trim(),
          targetColumn: m.targetColumn.trim(),
        }))
        .filter(m => m.sourceColumn.length > 0 && m.targetColumn.length > 0)
    : []

  return {
    name: form.name.trim(),
    description: form.description.trim() || null,
    sourceId: form.sourceId,
    publicationName: form.publicationName.trim(),
    slotName: form.slotName.trim(),
    monitoredTables: form.monitoredTables.trim() || null,
    autoCreatePublicationAndSlot: form.autoCreatePublicationAndSlot,
    initialSnapshotEnabled: form.initialSnapshotEnabled,
    pollIntervalSeconds: Number.isFinite(poll) ? poll : 15,
    maxBatchSize: Number.isFinite(batch) ? batch : 500,
    targetMode: mode,
    targetTopicName: mode === 0 ? (form.targetTopicName.trim() || null) : null,
    targetPipelineId: mode === 1 ? (form.targetPipelineId || null) : null,
    targetSinkId: usesSinkTarget ? (form.targetSinkId || null) : null,
    targetSinkDestination: usesSinkTarget ? (form.targetSinkDestination.trim() || null) : null,
    targetSinkKeyColumns: usesSinkTarget ? (form.targetSinkKeyColumns.trim() || null) : null,
    targetDeletePolicy: deletePolicy,
    targetFieldMappings: cleanMappings.length > 0 ? cleanMappings : null,
    softDeleteColumn: mode === 3 && deletePolicy === 2 ? (form.softDeleteColumn.trim() || null) : null,
    softDeleteTimestampColumn: mode === 3 && deletePolicy === 2
      ? (form.softDeleteTimestampColumn.trim() || null)
      : null,
    schemaDriftPolicy: Number(form.schemaDriftPolicy) === 1 ? 1 : (Number(form.schemaDriftPolicy) === 2 ? 2 : 0),
  }
}

export function isCdcFormValid(form: CdcFormState): boolean {
  if (!form.name.trim() || !form.sourceId || !form.publicationName.trim() || !form.slotName.trim()) {
    return false
  }
  const poll = Number.parseInt(form.pollIntervalSeconds, 10)
  const batch = Number.parseInt(form.maxBatchSize, 10)
  if (!Number.isFinite(poll) || poll < 5 || poll > 3600) return false
  if (!Number.isFinite(batch) || batch < 1 || batch > 5000) return false
  if (form.targetMode === '0') {
    return form.targetTopicName.trim().length > 0
  }
  if (form.targetMode === '1') {
    return form.targetPipelineId.trim().length > 0
  }
  if (form.targetMode === '3') {
    const tables = form.monitoredTables.split(/[,;\r\n]/).map(s => s.trim()).filter(Boolean)
    if (tables.length !== 1 || !form.targetSinkId.trim() || !form.targetSinkDestination.trim() || !form.targetSinkKeyColumns.trim()) {
      return false
    }
    if (form.targetDeletePolicy === '2') {
      const flagCol = form.softDeleteColumn.trim()
      if (!COLUMN_IDENTIFIER_PATTERN.test(flagCol)) return false
      const tsCol = form.softDeleteTimestampColumn.trim()
      if (tsCol.length > 0) {
        if (!COLUMN_IDENTIFIER_PATTERN.test(tsCol)) return false
        if (tsCol.toLowerCase() === flagCol.toLowerCase()) return false
      }
    }
    return true
  }
  return form.targetSinkId.trim().length > 0 && form.targetSinkDestination.trim().length > 0
}

export function getSinkTypeTag(type: number): string {
  switch (type) {
    case 3:
      return 'Kafka'
    case 4:
      return 'ClickHouse'
    case 5:
      return 'S3/MinIO'
    default:
      return 'Sink'
  }
}
