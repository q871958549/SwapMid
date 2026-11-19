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
  sourceType: '0' | '1' | '2' | '3' | '4'
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
  lakehouseCompactMinFiles: string
  lakehouseTableFormat: '0' | '1' | '2'
  lakehousePartitionStrategy: '0' | '1'
  lakehouseWriteMode: '0' | '1'
  catalogType: '0' | '1' | '2'
  catalogSinkId: string
  catalogNamespace: string
  catalogTableName: string
  schemaDriftPolicy: '0' | '1' | '2'
  maxFreshnessLagSeconds: string
  maxAllowedWalLagMb: string
  requiredColumns: string
}

export const DEFAULT_CDC_TOPIC = 'integration.cdc.events'

export const defaultCdcForm: CdcFormState = {
  name: '',
  description: '',
  sourceId: '',
  sourceType: '0',
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
  lakehouseCompactMinFiles: '',
  lakehouseTableFormat: '0',
  lakehousePartitionStrategy: '0',
  lakehouseWriteMode: '0',
  catalogType: '0',
  catalogSinkId: '',
  catalogNamespace: '',
  catalogTableName: '',
  schemaDriftPolicy: '0',
  maxFreshnessLagSeconds: '',
  maxAllowedWalLagMb: '',
  requiredColumns: '',
}

export const CATALOG_IDENTIFIER_PATTERN = /^[A-Za-z0-9_.-]+$/

export function isValidCatalogIdentifier(value: string): boolean {
  const trimmed = value.trim()
  if (!trimmed || trimmed.startsWith('.') || trimmed.endsWith('.') || trimmed.includes('..')) {
    return false
  }
  return CATALOG_IDENTIFIER_PATTERN.test(trimmed)
}

export function normalizeCatalogType(catalogType: number | undefined): '0' | '1' | '2' {
  if (Number(catalogType) === 1) return '1'
  if (Number(catalogType) === 2) return '2'
  return '0'
}

export function normalizeSourceType(sourceType: number | undefined): '0' | '1' | '2' | '3' | '4' {
  if (Number(sourceType) === 1) return '1'
  if (Number(sourceType) === 2) return '2'
  if (Number(sourceType) === 3) return '3'
  if (Number(sourceType) === 4) return '4'
  return '0'
}

export function normalizeLakehouseTableFormat(format: number | undefined): '0' | '1' | '2' {
  if (Number(format) === 1) return '1'
  if (Number(format) === 2) return '2'
  return '0'
}

export function normalizeLakehousePartitionStrategy(strategy: number | undefined): '0' | '1' {
  return Number(strategy) === 1 ? '1' : '0'
}

export function normalizeLakehouseWriteMode(mode: number | undefined): '0' | '1' {
  return Number(mode) === 1 ? '1' : '0'
}

export function detectCdcSourceTypeFromSource(sourceId: string, sources: CdcSourceOption[]): '0' | '1' | '2' | '3' | '4' {
  const matched = sources.find(s => s.id === sourceId)
  if (matched?.type === 1) return '3'
  if (matched?.type === 3) return '4'
  if (!matched?.config) return '0'
  try {
    const parsed = JSON.parse(matched.config) as { provider?: unknown }
    if (typeof parsed.provider === 'string') {
      const lower = parsed.provider.toLowerCase()
      if (lower === 'sqlserver' || lower === 'mssql') return '1'
      if (lower === 'mysql' || lower === 'mariadb') return '2'
    }
  } catch {
    if (/"provider"\s*:\s*"(sqlserver|mssql)"/i.test(matched.config)) return '1'
    if (/"provider"\s*:\s*"(mysql|mariadb)"/i.test(matched.config)) return '2'
  }
  return '0'
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
    sourceType: normalizeSourceType(item.sourceType),
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
    lakehouseCompactMinFiles: item.lakehouseCompactMinFiles ? String(item.lakehouseCompactMinFiles) : '',
    lakehouseTableFormat: normalizeLakehouseTableFormat(item.lakehouseTableFormat),
    lakehousePartitionStrategy: normalizeLakehousePartitionStrategy(item.lakehousePartitionStrategy),
    lakehouseWriteMode: normalizeLakehouseWriteMode(item.lakehouseWriteMode),
    catalogType: normalizeCatalogType(item.catalogType),
    catalogSinkId: item.catalogSinkId ?? '',
    catalogNamespace: item.catalogNamespace ?? '',
    catalogTableName: item.catalogTableName ?? '',
    schemaDriftPolicy: normalizeDriftPolicy(item.schemaDriftPolicy),
    maxFreshnessLagSeconds: item.maxFreshnessLagSeconds ? String(item.maxFreshnessLagSeconds) : '',
    maxAllowedWalLagMb: item.maxAllowedWalLagBytes
      ? String(Math.max(1, Math.round(item.maxAllowedWalLagBytes / (1024 * 1024))))
      : '',
    requiredColumns: item.requiredColumns ?? '',
  }
}

export function toCdcBody(form: CdcFormState) {
  const poll = Number.parseInt(form.pollIntervalSeconds, 10)
  const batch = Number.parseInt(form.maxBatchSize, 10)
  const sourceType = Number(normalizeSourceType(Number(form.sourceType)))
  const rawMode = Number(form.targetMode)
  const mode = rawMode === 1 || rawMode === 2 || rawMode === 3 ? rawMode : 0
  const usesSinkTarget = mode === 2 || mode === 3
  const deletePolicy = mode === 3 ? Number(normalizeDeletePolicy(Number(form.targetDeletePolicy))) : 0
  const compactMin = mode === 2 && form.lakehouseCompactMinFiles.trim()
    ? Number.parseInt(form.lakehouseCompactMinFiles.trim(), 10)
    : null
  const tableFormat = mode === 2 ? Number(normalizeLakehouseTableFormat(Number(form.lakehouseTableFormat))) : 0
  const partitionStrategy = mode === 2 && tableFormat === 2
    ? Number(normalizeLakehousePartitionStrategy(Number(form.lakehousePartitionStrategy)))
    : 0
  const writeMode = mode === 2 && tableFormat === 2
    ? Number(normalizeLakehouseWriteMode(Number(form.lakehouseWriteMode)))
    : 0
  const catalogTypeNum = mode === 2 ? Number(normalizeCatalogType(Number(form.catalogType))) : 0
  const useCatalog = mode === 2 && catalogTypeNum > 0
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
    sourceType,
    publicationName: (sourceType === 0 || sourceType === 4) ? form.publicationName.trim() : (form.publicationName.trim() || null),
    slotName: sourceType === 0 ? form.slotName.trim() : (form.slotName.trim() || null),
    monitoredTables: form.monitoredTables.trim() || null,
    autoCreatePublicationAndSlot: sourceType === 0 ? form.autoCreatePublicationAndSlot : false,
    initialSnapshotEnabled: (sourceType === 3 || sourceType === 4) ? false : form.initialSnapshotEnabled,
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
    lakehouseCompactMinFiles: compactMin !== null && Number.isFinite(compactMin) ? compactMin : null,
    lakehouseTableFormat: tableFormat,
    lakehousePartitionStrategy: partitionStrategy,
    lakehouseWriteMode: writeMode,
    catalogType: catalogTypeNum,
    catalogSinkId: useCatalog ? (form.catalogSinkId.trim() || null) : null,
    catalogNamespace: useCatalog ? (form.catalogNamespace.trim() || null) : null,
    catalogTableName: useCatalog ? (form.catalogTableName.trim() || null) : null,
    schemaDriftPolicy: Number(form.schemaDriftPolicy) === 1 ? 1 : (Number(form.schemaDriftPolicy) === 2 ? 2 : 0),
    maxFreshnessLagSeconds: form.maxFreshnessLagSeconds.trim()
      ? Number.parseInt(form.maxFreshnessLagSeconds.trim(), 10)
      : null,
    maxAllowedWalLagBytes: form.maxAllowedWalLagMb.trim()
      ? Math.round(Number.parseFloat(form.maxAllowedWalLagMb.trim()) * 1024 * 1024)
      : null,
    requiredColumns: form.requiredColumns.trim() || null,
  }
}

export function isCdcFormValid(form: CdcFormState): boolean {
  const requiresMonitoredTables = form.sourceType === '1' || form.sourceType === '2'
  if (!form.name.trim() || !form.sourceId) return false
  if (form.sourceType === '0' && (!form.publicationName.trim() || !form.slotName.trim())) return false
  if (form.sourceType === '4' && !form.publicationName.trim()) return false
  if (requiresMonitoredTables && !form.monitoredTables.trim()) return false

  const poll = Number.parseInt(form.pollIntervalSeconds, 10)
  const batch = Number.parseInt(form.maxBatchSize, 10)
  if (!Number.isFinite(poll) || poll < 5 || poll > 3600) return false
  if (!Number.isFinite(batch) || batch < 1 || batch > 5000) return false
  if (form.targetMode === '2' && form.lakehouseCompactMinFiles.trim().length > 0) {
    const minFiles = Number.parseInt(form.lakehouseCompactMinFiles.trim(), 10)
    if (!Number.isFinite(minFiles) || minFiles < 2 || minFiles > 100) return false
  }
  if (form.targetMode === '2' && normalizeCatalogType(Number(form.catalogType)) !== '0') {
    if (!form.catalogSinkId.trim()) return false
    if (!isValidCatalogIdentifier(form.catalogNamespace) || !isValidCatalogIdentifier(form.catalogTableName)) {
      return false
    }
  }
  if (form.maxFreshnessLagSeconds.trim().length > 0) {
    const freshness = Number.parseInt(form.maxFreshnessLagSeconds.trim(), 10)
    if (!Number.isFinite(freshness) || freshness < 10 || freshness > 86400) return false
  }
  if (form.maxAllowedWalLagMb.trim().length > 0) {
    const walMb = Number.parseFloat(form.maxAllowedWalLagMb.trim())
    if (!Number.isFinite(walMb) || walMb < 1 || walMb > 102400) return false
  }
  if (form.requiredColumns.trim().length > 0) {
    const cols = form.requiredColumns.split(',').map(s => s.trim()).filter(Boolean)
    if (cols.length > 64 || cols.some(c => !COLUMN_IDENTIFIER_PATTERN.test(c))) return false
  }
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
  if (form.targetMode === '2' && form.lakehouseTableFormat === '2' && form.lakehouseWriteMode === '1') {
    if (!form.targetSinkKeyColumns.trim()) return false
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
