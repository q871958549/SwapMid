export const ETL_WRITE_MODES = [
  'append',
  'truncate',
  'upsert',
  'append-receipt-v1',
  'truncate-receipt-v1',
  'upsert-receipt-v1',
] as const

export type EtlWriteMode = (typeof ETL_WRITE_MODES)[number]
export type EtlWriteModeFormValue = EtlWriteMode | 'unsupported'

export interface EtlColumnMapping {
  sourceColumn: string
  targetColumn: string
}

interface MappableColumnDescriptor {
  name?: string | null
  dataType?: string | null
}

export interface EtlTransformFormValue {
  sourceQuery: string
  targetTable: string
  mode: EtlWriteModeFormValue
  keyColumnsText?: string
  watermarkColumn?: string
  columnMappings?: EtlColumnMapping[]
  transformConfigValid: boolean
}

export interface ReceiptTargetOption {
  type: number
  config?: string | null
}

const TABLE_IDENTIFIER_PATTERN = /^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?$/
const KAFKA_TOPIC_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/
const OBJECT_PREFIX_PATTERN = /^[A-Za-z0-9][A-Za-z0-9/_.-]{0,191}$/
const COLUMN_IDENTIFIER_PATTERN = /^[A-Za-z_][A-Za-z0-9_]*$/
const MAX_UPSERT_KEY_COLUMNS = 16
const MAX_COLUMN_MAPPINGS = 256

function validateColumnMappings(raw: unknown): EtlColumnMapping[] | null {
  if (raw === undefined || raw === null) return []
  if (!Array.isArray(raw) || raw.length > MAX_COLUMN_MAPPINGS) return null

  const seenTargets = new Set<string>()
  const normalized: EtlColumnMapping[] = []

  for (const item of raw) {
    if (!item || typeof item !== 'object' || Array.isArray(item)) return null
    const record = item as Record<string, unknown>
    if (typeof record.sourceColumn !== 'string' || typeof record.targetColumn !== 'string') {
      return null
    }
    const sourceColumn = record.sourceColumn.trim()
    const targetColumn = record.targetColumn.trim()
    if (!COLUMN_IDENTIFIER_PATTERN.test(sourceColumn) || !COLUMN_IDENTIFIER_PATTERN.test(targetColumn)) {
      return null
    }
    const targetKey = targetColumn.toLowerCase()
    if (seenTargets.has(targetKey)) return null
    seenTargets.add(targetKey)
    normalized.push({ sourceColumn, targetColumn })
  }

  return normalized
}

function normalizeColumnKey(name: string): string {
  return name.replace(/[_-\s]/g, '').toLowerCase()
}

export function buildAutoColumnMappings(
  sourceColumns: readonly MappableColumnDescriptor[],
  targetColumns: readonly MappableColumnDescriptor[],
): EtlColumnMapping[] {
  const validSources = sourceColumns
    .map(col => (col.name ?? '').trim())
    .filter(name => COLUMN_IDENTIFIER_PATTERN.test(name))
  const validTargets = targetColumns
    .map(col => (col.name ?? '').trim())
    .filter(name => COLUMN_IDENTIFIER_PATTERN.test(name))

  if (validSources.length === 0 || validTargets.length === 0) return []

  const exactSourceByLower = new Map<string, string>()
  const normalizedSourceByKey = new Map<string, string>()
  for (const src of validSources) {
    const lower = src.toLowerCase()
    if (!exactSourceByLower.has(lower)) exactSourceByLower.set(lower, src)
    const norm = normalizeColumnKey(src)
    if (norm && !normalizedSourceByKey.has(norm)) normalizedSourceByKey.set(norm, src)
  }

  const mappings: EtlColumnMapping[] = []
  const seenTargets = new Set<string>()
  for (const tgt of validTargets) {
    if (mappings.length >= MAX_COLUMN_MAPPINGS) break
    const tgtLower = tgt.toLowerCase()
    if (seenTargets.has(tgtLower)) continue
    seenTargets.add(tgtLower)

    const matchedSource =
      exactSourceByLower.get(tgtLower)
      ?? normalizedSourceByKey.get(normalizeColumnKey(tgt))
    if (matchedSource) {
      mappings.push({ sourceColumn: matchedSource, targetColumn: tgt })
    }
  }

  return mappings
}

function classifyColumnTypeFamily(rawType?: string | null): string {
  const t = (rawType ?? '').trim().toLowerCase()
  if (!t) return 'other'
  if (
    t.includes('int') || t.includes('numeric') || t.includes('decimal')
    || t.includes('float') || t.includes('double') || t.includes('real')
    || t.includes('money') || t.includes('single')
  ) {
    return 'number'
  }
  if (t.includes('bool') || t === 'bit') return 'boolean'
  if (t.includes('uuid') || t.includes('uniqueidentifier') || t.includes('guid')) return 'uuid'
  if (t.includes('date') || t.includes('time')) return 'temporal'
  if (t.includes('char') || t.includes('text') || t.includes('json') || t.includes('xml') || t.includes('string')) {
    return 'text'
  }
  return 'other'
}

export function isMappingTypeIncompatible(
  sourceType?: string | null,
  targetType?: string | null,
): boolean {
  const sf = classifyColumnTypeFamily(sourceType)
  const tf = classifyColumnTypeFamily(targetType)
  if (sf === 'other' || tf === 'other' || sf === tf) return false
  return tf !== 'text'
}

export function parseEtlWriteMode(value: unknown): EtlWriteMode | null {
  if (value === undefined) return 'append'
  return typeof value === 'string' && ETL_WRITE_MODES.includes(value as EtlWriteMode)
    ? value as EtlWriteMode
    : null
}

export function isReceiptMode(mode: EtlWriteModeFormValue): boolean {
  return (
    mode === 'append-receipt-v1'
    || mode === 'truncate-receipt-v1'
    || mode === 'upsert-receipt-v1'
  )
}

export function isUpsertMode(mode: EtlWriteModeFormValue): boolean {
  return mode === 'upsert' || mode === 'upsert-receipt-v1'
}

export function parseKeyColumnsText(raw?: string | null): string[] | null {
  if (typeof raw !== 'string') return null
  const columns = raw
    .split(',')
    .map(item => item.trim())
    .filter(item => item.length > 0)
  if (columns.length === 0 || columns.length > MAX_UPSERT_KEY_COLUMNS) return null

  const seen = new Set<string>()
  for (const column of columns) {
    if (!COLUMN_IDENTIFIER_PATTERN.test(column)) return null
    const lower = column.toLowerCase()
    if (seen.has(lower)) return null
    seen.add(lower)
  }
  return columns
}

export function isUpsertConfigValid(
  value: Pick<EtlTransformFormValue, 'mode' | 'keyColumnsText' | 'watermarkColumn'>,
): boolean {
  if (!isUpsertMode(value.mode)) return true
  const keyColumns = parseKeyColumnsText(value.keyColumnsText)
  if (!keyColumns) return false

  const watermark = (value.watermarkColumn ?? '').trim()
  if (!watermark) return true
  if (!COLUMN_IDENTIFIER_PATTERN.test(watermark)) return false
  return !keyColumns.some(col => col.toLowerCase() === watermark.toLowerCase())
}

export function parseEtlTransformConfig(config: string | null): EtlTransformFormValue {
  if (config === null) {
    return {
      sourceQuery: '',
      targetTable: '',
      mode: 'append',
      keyColumnsText: '',
      watermarkColumn: '',
      columnMappings: [],
      transformConfigValid: true,
    }
  }

  try {
    const parsed: unknown = JSON.parse(config)
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('invalid')
    const value = parsed as Record<string, unknown>
    if ((Object.hasOwn(value, 'sourceQuery') && typeof value.sourceQuery !== 'string') ||
        (Object.hasOwn(value, 'targetTable') && typeof value.targetTable !== 'string')) {
      throw new Error('invalid')
    }
    const mode = parseEtlWriteMode(value.mode)
    if (!mode) throw new Error('invalid mode')

    const upsert = isUpsertMode(mode)
    const keyColumns: string[] = []
    if (Object.hasOwn(value, 'keyColumns') && value.keyColumns !== null && value.keyColumns !== undefined) {
      if (!Array.isArray(value.keyColumns)) throw new Error('invalid keyColumns')
      const seen = new Set<string>()
      for (const item of value.keyColumns) {
        if (typeof item !== 'string') throw new Error('invalid key item')
        const trimmed = item.trim()
        if (!COLUMN_IDENTIFIER_PATTERN.test(trimmed)) throw new Error('invalid key identifier')
        const lower = trimmed.toLowerCase()
        if (seen.has(lower)) throw new Error('duplicate key')
        seen.add(lower)
        keyColumns.push(trimmed)
      }
    }

    if (upsert) {
      if (keyColumns.length === 0 || keyColumns.length > MAX_UPSERT_KEY_COLUMNS) {
        throw new Error('missing upsert keys')
      }
    } else if (keyColumns.length > 0) {
      throw new Error('keyColumns forbidden outside upsert')
    }

    let watermarkColumn = ''
    if (Object.hasOwn(value, 'watermarkColumn') && value.watermarkColumn !== null && value.watermarkColumn !== undefined) {
      if (typeof value.watermarkColumn !== 'string') throw new Error('invalid watermarkColumn')
      watermarkColumn = value.watermarkColumn.trim()
      if (watermarkColumn.length > 0) {
        if (!upsert || !COLUMN_IDENTIFIER_PATTERN.test(watermarkColumn)) {
          throw new Error('invalid watermark usage')
        }
        if (keyColumns.some(col => col.toLowerCase() === watermarkColumn.toLowerCase())) {
          throw new Error('watermark overlaps keyColumns')
        }
      }
    }

    const columnMappings = validateColumnMappings(value.columnMappings)
    if (!columnMappings) {
      return {
        sourceQuery: '',
        targetTable: '',
        mode: 'unsupported',
        keyColumnsText: '',
        watermarkColumn: '',
        columnMappings: [],
        transformConfigValid: false,
      }
    }

    return {
      sourceQuery: typeof value.sourceQuery === 'string' ? value.sourceQuery : '',
      targetTable: typeof value.targetTable === 'string' ? value.targetTable : '',
      mode,
      keyColumnsText: keyColumns.join(', '),
      watermarkColumn,
      columnMappings,
      transformConfigValid: true,
    }
  } catch {
    return {
      sourceQuery: '',
      targetTable: '',
      mode: 'unsupported',
      keyColumnsText: '',
      watermarkColumn: '',
      columnMappings: [],
      transformConfigValid: false,
    }
  }
}

export function serializeEtlTransformConfig(value: EtlTransformFormValue): string | null {
  const mode = parseEtlWriteMode(value.mode)
  if (!value.transformConfigValid || !mode) throw new Error('Unsupported ETL transform config')
  const sourceQuery = value.sourceQuery.trim()
  const targetTable = value.targetTable.trim()
  const columnMappings = validateColumnMappings(value.columnMappings)
  if (!columnMappings) {
    throw new Error('Invalid ETL columnMappings')
  }
  const mappingsPatch = columnMappings.length > 0 ? { columnMappings } : {}

  if (isUpsertMode(mode)) {
    const keyColumns = parseKeyColumnsText(value.keyColumnsText)
    if (!keyColumns || !isUpsertConfigValid(value)) {
      throw new Error('Invalid ETL upsert keyColumns or watermarkColumn')
    }
    const watermarkColumn = (value.watermarkColumn ?? '').trim()
    return JSON.stringify(
      watermarkColumn
        ? { sourceQuery, targetTable, mode, keyColumns, watermarkColumn, ...mappingsPatch }
        : { sourceQuery, targetTable, mode, keyColumns, ...mappingsPatch },
    )
  }

  if (mode === 'append' && !sourceQuery && !targetTable && columnMappings.length === 0) return null
  return JSON.stringify({ sourceQuery, targetTable, mode, ...mappingsPatch })
}

export function supportsTargetReceipt(target: ReceiptTargetOption | undefined): boolean {
  if (!target || target.type !== 0 || !target.config) return false
  try {
    const parsed: unknown = JSON.parse(target.config)
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return false
    const config = parsed as Record<string, unknown>
    if (Object.hasOwn(config, 'provider') && typeof config.provider !== 'string') return false
    const provider = typeof config.provider === 'string'
      ? config.provider.trim().toLowerCase()
      : 'sqlserver'
    const isReceiptCapableProvider =
      provider === 'sqlserver'
      || provider === 'mssql'
      || provider === 'postgresql'
      || provider === 'postgres'
      || provider === 'npgsql'
    return isReceiptCapableProvider &&
      config.encrypt === true && config.trustServerCertificate === false
  } catch {
    return false
  }
}

const CLICKHOUSE_WRITE_MODES: readonly EtlWriteMode[] = ['append', 'truncate']
const STREAM_SINK_WRITE_MODES: readonly EtlWriteMode[] = ['append']

export function getAllowedWriteModesForTarget(targetType?: number): readonly EtlWriteMode[] {
  if (targetType === undefined || targetType === 0) return ETL_WRITE_MODES
  if (targetType === 4) return CLICKHOUSE_WRITE_MODES
  if (targetType === 3 || targetType === 5) return STREAM_SINK_WRITE_MODES
  return []
}

export function isWriteModeAllowedForTarget(
  mode: EtlWriteModeFormValue,
  targetType?: number,
): boolean {
  if (mode === 'unsupported') return false
  return getAllowedWriteModesForTarget(targetType).includes(mode as EtlWriteMode)
}

export function getDestinationKindForTarget(targetType?: number): 'table' | 'topic' | 'prefix' {
  if (targetType === 3) return 'topic'
  if (targetType === 5) return 'prefix'
  return 'table'
}

export function isValidDestinationForTarget(
  destination: string,
  targetType?: number,
): boolean {
  const trimmed = destination.trim()
  if (!trimmed) return false
  if (targetType === 3) {
    return trimmed !== '.' && trimmed !== '..' && KAFKA_TOPIC_PATTERN.test(trimmed)
  }
  if (targetType === 5) {
    return (
      !trimmed.startsWith('/')
      && !trimmed.endsWith('/')
      && !trimmed.includes('..')
      && !trimmed.includes('//')
      && OBJECT_PREFIX_PATTERN.test(trimmed)
    )
  }
  if (targetType === undefined || targetType === 0 || targetType === 4) {
    return TABLE_IDENTIFIER_PATTERN.test(trimmed)
  }
  return false
}
