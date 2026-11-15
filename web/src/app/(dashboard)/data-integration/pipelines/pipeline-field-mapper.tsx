'use client'

import { ArrowRight } from 'lucide-react'
import { useTranslations } from 'next-intl'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import type { PreflightColumnDto } from '@/lib/api/types'
import {
  type EtlColumnMapping,
  buildAutoColumnMappings,
  isMappingTypeIncompatible,
} from './transform-config'

const UNMAPPED_SOURCE_VALUE = '__unmapped__'

function deduplicateColumns(columns: readonly PreflightColumnDto[]): PreflightColumnDto[] {
  const seen = new Set<string>()
  const result: PreflightColumnDto[] = []
  for (const col of columns) {
    const name = (col.name ?? '').trim()
    if (!name) continue
    const lower = name.toLowerCase()
    if (seen.has(lower)) continue
    seen.add(lower)
    result.push({ ...col, name })
  }
  return result
}

export function PipelineFieldMapperPanel({
  sourceColumns,
  targetColumns,
  columnMappings,
  onChange,
}: {
  sourceColumns: readonly PreflightColumnDto[]
  targetColumns: readonly PreflightColumnDto[]
  columnMappings: readonly EtlColumnMapping[]
  onChange: (mappings: EtlColumnMapping[]) => void
}) {
  const t = useTranslations('DataIntegration.pipeline')

  const normalizedSources = deduplicateColumns(sourceColumns)
  const normalizedTargets = deduplicateColumns(targetColumns)

  const sourceMap = new Map<string, PreflightColumnDto>()
  for (const src of normalizedSources) {
    if (src.name) sourceMap.set(src.name.toLowerCase(), src)
  }

  const targetOrderMap = new Map<string, number>()
  normalizedTargets.forEach((tgt, idx) => {
    if (tgt.name) targetOrderMap.set(tgt.name.toLowerCase(), idx)
  })

  const mappingByTargetLower = new Map<string, EtlColumnMapping>()
  for (const mapping of columnMappings) {
    mappingByTargetLower.set(mapping.targetColumn.toLowerCase(), mapping)
  }

  const orphanMappings = normalizedTargets.length > 0
    ? columnMappings.filter(m => !targetOrderMap.has(m.targetColumn.toLowerCase()))
    : [...columnMappings]

  const canAutoMatch = normalizedSources.length > 0 && normalizedTargets.length > 0
  const hasExplicitMappings = columnMappings.length > 0
  const totalCount = normalizedTargets.length > 0 ? normalizedTargets.length : columnMappings.length

  function handleSelectSource(targetName: string, nextValue: string) {
    const targetLower = targetName.toLowerCase()
    const filtered = columnMappings.filter(m => m.targetColumn.toLowerCase() !== targetLower)
    if (nextValue !== UNMAPPED_SOURCE_VALUE) {
      filtered.push({ sourceColumn: nextValue, targetColumn: targetName })
    }
    filtered.sort((a, b) => {
      const aOrder = targetOrderMap.get(a.targetColumn.toLowerCase()) ?? Number.MAX_SAFE_INTEGER
      const bOrder = targetOrderMap.get(b.targetColumn.toLowerCase()) ?? Number.MAX_SAFE_INTEGER
      return aOrder - bOrder
    })
    onChange(filtered)
  }

  function handleAutoMatch() {
    onChange(buildAutoColumnMappings(normalizedSources, normalizedTargets))
  }

  return (
    <div className="space-y-2 rounded-md border bg-muted/20 p-2.5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <span className="text-xs font-medium">{t('mapperTitle')}</span>
          <Badge variant="outline">
            {t('mapperMappedCount', { mapped: columnMappings.length, total: totalCount })}
          </Badge>
        </div>
        <div className="flex items-center gap-1.5">
          {canAutoMatch && (
            <Button
              type="button"
              size="sm"
              variant="outline"
              onClick={handleAutoMatch}
            >
              {t('mapperAutoMatch')}
            </Button>
          )}
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled={!hasExplicitMappings}
            onClick={() => onChange([])}
          >
            {t('mapperClear')}
          </Button>
        </div>
      </div>

      {!hasExplicitMappings && normalizedTargets.length > 0 && (
        <p className="text-xs text-muted-foreground">{t('mapperImplicitHint')}</p>
      )}

      {normalizedTargets.length === 0 && hasExplicitMappings && (
        <p className="text-xs text-muted-foreground">{t('mapperPreflightToEditHint')}</p>
      )}

      {normalizedTargets.length > 0 && (
        <div className="max-h-60 space-y-1.5 overflow-y-auto pr-1">
          {normalizedTargets.map((targetCol) => {
            const targetName = targetCol.name ?? ''
            const mapped = mappingByTargetLower.get(targetName.toLowerCase())
            const mappedSourceName = mapped?.sourceColumn
            const matchedSourceCol = mappedSourceName
              ? sourceMap.get(mappedSourceName.toLowerCase())
              : undefined
            const isMissingSource =
              !!mappedSourceName && normalizedSources.length > 0 && !matchedSourceCol
            const isTypeMismatch =
              !!matchedSourceCol
              && isMappingTypeIncompatible(matchedSourceCol.dataType, targetCol.dataType)
            const isRequiredUnmapped =
              !mappedSourceName
              && hasExplicitMappings
              && !targetCol.isNullable
              && !targetCol.hasDefaultOrIdentity

            const selectValue = mappedSourceName ?? UNMAPPED_SOURCE_VALUE
            const selectableSources = normalizedSources.some(
              s => s.name?.toLowerCase() === mappedSourceName?.toLowerCase(),
            )
              ? normalizedSources
              : mappedSourceName
                ? [...normalizedSources, { name: mappedSourceName, dataType: undefined }]
                : normalizedSources

            return (
              <div
                key={targetName}
                className="flex items-center gap-2 rounded-md border bg-background px-2.5 py-1.5 text-xs"
              >
                <div className="min-w-0 flex-1">
                  <Select
                    value={selectValue}
                    onValueChange={val => handleSelectSource(targetName, val)}
                  >
                    <SelectTrigger
                      size="sm"
                      aria-label={targetName}
                      className="w-full text-xs"
                    >
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={UNMAPPED_SOURCE_VALUE}>
                        {t('mapperUnmappedOption')}
                      </SelectItem>
                      {selectableSources.map((srcCol) => {
                        const srcName = srcCol.name ?? ''
                        return (
                          <SelectItem key={srcName} value={srcName}>
                            {srcCol.dataType ? `${srcName} (${srcCol.dataType})` : srcName}
                          </SelectItem>
                        )
                      })}
                    </SelectContent>
                  </Select>
                </div>

                <ArrowRight className="size-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />

                <div className="min-w-0 flex-1 space-y-0.5">
                  <div className="flex flex-wrap items-center gap-1">
                    <span className="truncate font-mono font-medium">{targetName}</span>
                    {targetCol.dataType && (
                      <span className="truncate text-muted-foreground">{targetCol.dataType}</span>
                    )}
                    {targetCol.isPrimaryKey && (
                      <Badge variant="secondary">{t('mapperPkBadge')}</Badge>
                    )}
                    {!targetCol.isNullable && !targetCol.hasDefaultOrIdentity && (
                      <Badge variant="outline">{t('mapperRequiredBadge')}</Badge>
                    )}
                    {isMissingSource && (
                      <Badge variant="destructive-soft">{t('mapperSourceMissingBadge')}</Badge>
                    )}
                    {isTypeMismatch && (
                      <Badge variant="warning">{t('mapperTypeMismatchBadge')}</Badge>
                    )}
                    {isRequiredUnmapped && (
                      <Badge variant="warning">{t('mapperRequiredUnmappedBadge')}</Badge>
                    )}
                  </div>
                </div>
              </div>
            )
          })}
        </div>
      )}

      {orphanMappings.length > 0 && (
        <div className="max-h-40 space-y-1.5 overflow-y-auto pr-1">
          {orphanMappings.map(mapping => (
            <div
              key={`${mapping.sourceColumn}->${mapping.targetColumn}`}
              className="flex items-center justify-between gap-2 rounded-md border bg-background px-2.5 py-1.5 text-xs"
            >
              <div className="flex min-w-0 items-center gap-2 font-mono">
                <span className="truncate">{mapping.sourceColumn}</span>
                <ArrowRight className="size-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
                <span className="truncate font-medium">{mapping.targetColumn}</span>
              </div>
              {normalizedTargets.length > 0 && (
                <Badge variant="destructive-soft">{t('mapperTargetMissingBadge')}</Badge>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
