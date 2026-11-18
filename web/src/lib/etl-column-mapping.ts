export interface EtlColumnMapping {
  sourceColumn: string
  targetColumn: string
}

export interface MappableColumnDescriptor {
  name?: string | null
  dataType?: string | null
}

export const COLUMN_IDENTIFIER_PATTERN = /^[A-Za-z_][A-Za-z0-9_]*$/
export const MAX_COLUMN_MAPPINGS = 256

export function validateColumnMappings(raw: unknown): EtlColumnMapping[] | null {
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
  excludedTargetNames?: readonly string[],
): EtlColumnMapping[] {
  const excludedSet = new Set(
    (excludedTargetNames ?? [])
      .map(name => name.trim().toLowerCase())
      .filter(Boolean),
  )

  const validSources = sourceColumns
    .map(col => (col.name ?? '').trim())
    .filter(name => COLUMN_IDENTIFIER_PATTERN.test(name))
  const validTargets = targetColumns
    .map(col => (col.name ?? '').trim())
    .filter(name => COLUMN_IDENTIFIER_PATTERN.test(name) && !excludedSet.has(name.toLowerCase()))

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

export function classifyColumnTypeFamily(rawType?: string | null): string {
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
  if (sf === 'other' || tf === 'other' || sf == tf) return false
  return tf !== 'text'
}
