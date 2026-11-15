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

export interface EtlTransformFormValue {
  sourceQuery: string
  targetTable: string
  mode: EtlWriteModeFormValue
  keyColumnsText?: string
  watermarkColumn?: string
  transformConfigValid: boolean
}

export interface ReceiptTargetOption {
  type: number
  config?: string | null
}

const COLUMN_IDENTIFIER_PATTERN = /^[A-Za-z_][A-Za-z0-9_]*$/
const MAX_UPSERT_KEY_COLUMNS = 16

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

    return {
      sourceQuery: typeof value.sourceQuery === 'string' ? value.sourceQuery : '',
      targetTable: typeof value.targetTable === 'string' ? value.targetTable : '',
      mode,
      keyColumnsText: keyColumns.join(', '),
      watermarkColumn,
      transformConfigValid: true,
    }
  } catch {
    return {
      sourceQuery: '',
      targetTable: '',
      mode: 'unsupported',
      keyColumnsText: '',
      watermarkColumn: '',
      transformConfigValid: false,
    }
  }
}

export function serializeEtlTransformConfig(value: EtlTransformFormValue): string | null {
  const mode = parseEtlWriteMode(value.mode)
  if (!value.transformConfigValid || !mode) throw new Error('Unsupported ETL transform config')
  const sourceQuery = value.sourceQuery.trim()
  const targetTable = value.targetTable.trim()

  if (isUpsertMode(mode)) {
    const keyColumns = parseKeyColumnsText(value.keyColumnsText)
    if (!keyColumns || !isUpsertConfigValid(value)) {
      throw new Error('Invalid ETL upsert keyColumns or watermarkColumn')
    }
    const watermarkColumn = (value.watermarkColumn ?? '').trim()
    return JSON.stringify(
      watermarkColumn
        ? { sourceQuery, targetTable, mode, keyColumns, watermarkColumn }
        : { sourceQuery, targetTable, mode, keyColumns },
    )
  }

  if (mode === 'append' && !sourceQuery && !targetTable) return null
  return JSON.stringify({ sourceQuery, targetTable, mode })
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
