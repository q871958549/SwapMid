export const ETL_WRITE_MODES = [
  'append',
  'truncate',
  'append-receipt-v1',
  'truncate-receipt-v1',
] as const

export type EtlWriteMode = (typeof ETL_WRITE_MODES)[number]
export type EtlWriteModeFormValue = EtlWriteMode | 'unsupported'

export interface EtlTransformFormValue {
  sourceQuery: string
  targetTable: string
  mode: EtlWriteModeFormValue
  transformConfigValid: boolean
}

export interface ReceiptTargetOption {
  type: number
  config?: string | null
}

export function parseEtlWriteMode(value: unknown): EtlWriteMode | null {
  if (value === undefined) return 'append'
  return typeof value === 'string' && ETL_WRITE_MODES.includes(value as EtlWriteMode)
    ? value as EtlWriteMode
    : null
}

export function parseEtlTransformConfig(config: string | null): EtlTransformFormValue {
  if (config === null) {
    return { sourceQuery: '', targetTable: '', mode: 'append', transformConfigValid: true }
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
    return {
      sourceQuery: typeof value.sourceQuery === 'string' ? value.sourceQuery : '',
      targetTable: typeof value.targetTable === 'string' ? value.targetTable : '',
      mode: mode ?? 'unsupported',
      transformConfigValid: mode !== null,
    }
  } catch {
    return { sourceQuery: '', targetTable: '', mode: 'unsupported', transformConfigValid: false }
  }
}

export function serializeEtlTransformConfig(value: EtlTransformFormValue): string | null {
  const mode = parseEtlWriteMode(value.mode)
  if (!value.transformConfigValid || !mode) throw new Error('Unsupported ETL transform config')
  const sourceQuery = value.sourceQuery.trim()
  const targetTable = value.targetTable.trim()
  if (mode === 'append' && !sourceQuery && !targetTable) return null
  return JSON.stringify({ sourceQuery, targetTable, mode })
}

export function isReceiptMode(mode: EtlWriteModeFormValue): boolean {
  return mode === 'append-receipt-v1' || mode === 'truncate-receipt-v1'
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
