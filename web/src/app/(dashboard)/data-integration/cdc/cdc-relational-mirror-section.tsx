'use client'

import { useState } from 'react'
import { AlertTriangle, ArrowRight, CheckCircle2, Eraser, Loader2, ScanSearch, Sparkles } from 'lucide-react'
import { useTranslations } from 'next-intl'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { StatusBadge } from '@/components/ui/status-badge'
import { readApiErrorMessage } from '@/lib/api-error'
import type {
  CdcDeletePolicy,
  CdcFieldMappingDto,
  CdcMirrorSchemaInspectionDto,
  CdcSourceType,
  InspectCdcMirrorSchemaInput,
  PreflightColumnDto,
} from '@/lib/api/types'
import {
  buildAutoColumnMappings,
  isMappingTypeIncompatible,
} from '@/lib/etl-column-mapping'
import type { CdcFormState } from './cdc-form-state'

const UNMAPPED_SOURCE_VALUE = '__unmapped__'

export function CdcRelationalMirrorSection({
  crud,
}: {
  crud: {
    form: CdcFormState
    set: (patch: Partial<CdcFormState>) => void
  }
}) {
  const t = useTranslations('DataIntegration.cdc')
  const [inspecting, setInspecting] = useState(false)
  const [inspectError, setInspectError] = useState<string | null>(null)
  const [inspection, setInspection] = useState<CdcMirrorSchemaInspectionDto | null>(null)

  const form = crud.form
  const currentMappings = form.targetFieldMappings
  const managedSet = new Set(
    form.targetDeletePolicy === '2'
      ? [form.softDeleteColumn.trim().toLowerCase(), form.softDeleteTimestampColumn.trim().toLowerCase()].filter(Boolean)
      : [],
  )
  const canInspect =
    Boolean(form.sourceId && form.targetSinkId && form.targetSinkDestination.trim())
    && form.monitoredTables.split(/[,;\r\n]/).map(s => s.trim()).filter(Boolean).length === 1

  const sourceColumns: PreflightColumnDto[] = inspection?.sourceColumns ?? []
  const targetColumns: PreflightColumnDto[] = inspection?.targetColumns ?? []
  const sourceMap = new Map<string, PreflightColumnDto>()
  for (const col of sourceColumns) {
    if (col.name) sourceMap.set(col.name.toLowerCase(), col)
  }

  const mappingByTargetLower = new Map<string, CdcFieldMappingDto>()
  for (const m of currentMappings) {
    if (m.targetColumn) mappingByTargetLower.set(m.targetColumn.toLowerCase(), m)
  }

  async function runInspectSchema() {
    if (!canInspect) return
    setInspecting(true)
    setInspectError(null)
    try {
      const payload: InspectCdcMirrorSchemaInput = {
        sourceId: form.sourceId,
        sourceType: Number(form.sourceType) as CdcSourceType,
        monitoredTables: form.monitoredTables.trim() || null,
        targetSinkId: form.targetSinkId,
        targetSinkDestination: form.targetSinkDestination.trim(),
        targetSinkKeyColumns: form.targetSinkKeyColumns.trim() || null,
        targetDeletePolicy: Number(form.targetDeletePolicy) as CdcDeletePolicy,
        softDeleteColumn: form.targetDeletePolicy === '2' ? (form.softDeleteColumn.trim() || null) : null,
        softDeleteTimestampColumn: form.targetDeletePolicy === '2'
          ? (form.softDeleteTimestampColumn.trim() || null)
          : null,
        targetFieldMappings: currentMappings.length > 0 ? currentMappings : null,
      }
      const res = await fetch('/api/proxy/cdc-streams/inspect-mirror-schema', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })
      if (!res.ok) {
        setInspectError(await readApiErrorMessage(res, t('mirrorInspectFailedFallback')))
        return
      }
      const result = (await res.json()) as CdcMirrorSchemaInspectionDto
      setInspection(result)
    } catch {
      setInspectError(t('mirrorInspectFailedFallback'))
    } finally {
      setInspecting(false)
    }
  }

  function handleAutoMatch() {
    const matched = buildAutoColumnMappings(
      sourceColumns,
      targetColumns,
      Array.from(managedSet),
    )
    crud.set({ targetFieldMappings: matched })
  }

  function handleSelectSource(targetName: string, nextValue: string) {
    const targetLower = targetName.toLowerCase()
    const filtered = currentMappings.filter(m => (m.targetColumn ?? '').toLowerCase() !== targetLower)
    if (nextValue === UNMAPPED_SOURCE_VALUE) {
      crud.set({ targetFieldMappings: filtered })
      return
    }
    crud.set({
      targetFieldMappings: [...filtered, { sourceColumn: nextValue, targetColumn: targetName }],
    })
  }

  return (
    <div className="space-y-3">
      {form.targetDeletePolicy === '2' && (
        <div className="grid grid-cols-2 gap-3 rounded-md border bg-muted/30 p-3">
          <div className="space-y-1.5">
            <Label>{t('softDeleteColumnFieldLabel')}</Label>
            <Input
              value={form.softDeleteColumn}
              onChange={e => crud.set({ softDeleteColumn: e.target.value })}
              placeholder="is_deleted"
              className="font-mono"
            />
            <p className="text-xs text-muted-foreground">{t('softDeleteColumnHint')}</p>
          </div>
          <div className="space-y-1.5">
            <Label>{t('softDeleteTimestampColumnFieldLabel')}</Label>
            <Input
              value={form.softDeleteTimestampColumn}
              onChange={e => crud.set({ softDeleteTimestampColumn: e.target.value })}
              placeholder="deleted_at_utc"
              className="font-mono"
            />
            <p className="text-xs text-muted-foreground">{t('softDeleteTimestampColumnHint')}</p>
          </div>
        </div>
      )}

      <div className="space-y-2.5 rounded-md border bg-muted/20 p-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="space-y-0.5">
            <Label>{t('mirrorMapperTitle')}</Label>
            <p className="text-xs text-muted-foreground">{t('mirrorMapperDefaultHint')}</p>
          </div>
          <div className="flex flex-wrap items-center gap-1.5">
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={!canInspect || inspecting}
              onClick={() => void runInspectSchema()}
            >
              {inspecting
                ? <Loader2 className="mr-1 size-3.5 animate-spin" />
                : <ScanSearch className="mr-1 size-3.5" />}
              {t('mirrorInspectBtn')}
            </Button>
            {targetColumns.length > 0 && sourceColumns.length > 0 && (
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={handleAutoMatch}
              >
                <Sparkles className="mr-1 size-3.5" />
                {t('mirrorAutoMatchBtn')}
              </Button>
            )}
            {currentMappings.length > 0 && (
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() => crud.set({ targetFieldMappings: [] })}
              >
                <Eraser className="mr-1 size-3.5" />
                {t('mirrorClearMappingsBtn')}
              </Button>
            )}
          </div>
        </div>

        {inspectError && <p className="text-xs text-destructive">{inspectError}</p>}

        {inspection && (
          <div className="space-y-2 rounded-md border bg-background p-2.5">
            <div className="flex flex-wrap items-center gap-2 text-xs">
              <StatusBadge tone={inspection.compatible ? 'success' : 'danger'}>
                {inspection.compatible ? t('mirrorCompatibleBadge') : t('mirrorIncompatibleBadge')}
              </StatusBadge>
              <span className="font-mono text-muted-foreground">
                {`${inspection.canonicalSourceTable || '-'} -> ${inspection.canonicalTargetTable || '-'}`}
              </span>
              {inspection.targetProvider && (
                <Badge variant="outline" className="font-mono">{inspection.targetProvider}</Badge>
              )}
            </div>

            {inspection.issues && inspection.issues.length > 0 && (
              <ul className="space-y-1 text-xs">
                {inspection.issues.map((issue, idx) => (
                  <li
                    key={`${issue.code ?? 'issue'}-${idx}`}
                    className={issue.severity === 'error'
                      ? 'flex items-start gap-1.5 text-destructive'
                      : 'flex items-start gap-1.5 text-warning'}
                  >
                    {issue.severity === 'error'
                      ? <AlertTriangle className="mt-0.5 size-3.5 shrink-0" />
                      : <CheckCircle2 className="mt-0.5 size-3.5 shrink-0" />}
                    <span>{issue.message}</span>
                  </li>
                ))}
              </ul>
            )}

            {targetColumns.length > 0 && (
              <div className="max-h-56 space-y-1.5 overflow-y-auto pr-1">
                {targetColumns.map((targetCol) => {
                  const targetName = targetCol.name ?? ''
                  const isManaged = managedSet.has(targetName.toLowerCase())
                  const mapped = mappingByTargetLower.get(targetName.toLowerCase())
                  const mappedSource = mapped?.sourceColumn
                  const matchedSource = mappedSource ? sourceMap.get(mappedSource.toLowerCase()) : undefined
                  const typeMismatch = Boolean(
                    matchedSource && isMappingTypeIncompatible(matchedSource.dataType, targetCol.dataType),
                  )

                  return (
                    <div
                      key={targetName}
                      className="flex items-center gap-2 rounded-md border bg-muted/20 px-2.5 py-1.5 text-xs"
                    >
                      <div className="min-w-0 flex-1">
                        {isManaged ? (
                          <StatusBadge tone="info">{t('mirrorSystemManagedBadge')}</StatusBadge>
                        ) : (
                          <Select
                            value={mappedSource ?? UNMAPPED_SOURCE_VALUE}
                            onValueChange={val => handleSelectSource(targetName, val)}
                          >
                            <SelectTrigger size="sm" aria-label={targetName} className="w-full text-xs">
                              <SelectValue />
                            </SelectTrigger>
                            <SelectContent>
                              <SelectItem value={UNMAPPED_SOURCE_VALUE}>{t('mirrorUnmappedOption')}</SelectItem>
                              {sourceColumns.map((srcCol) => {
                                const srcName = srcCol.name ?? ''
                                return (
                                  <SelectItem key={srcName} value={srcName}>
                                    {srcCol.dataType ? `${srcName} (${srcCol.dataType})` : srcName}
                                  </SelectItem>
                                )
                              })}
                            </SelectContent>
                          </Select>
                        )}
                      </div>
                      <ArrowRight className="size-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-1">
                          <span className="truncate font-mono font-medium">{targetName}</span>
                          {targetCol.dataType && (
                            <span className="truncate text-muted-foreground">{targetCol.dataType}</span>
                          )}
                          {targetCol.isPrimaryKey && <Badge variant="secondary">{t('mirrorPkBadge')}</Badge>}
                          {typeMismatch && <Badge variant="warning">{t('mirrorTypeMismatchBadge')}</Badge>}
                        </div>
                      </div>
                    </div>
                  )
                })}
              </div>
            )}
          </div>
        )}

        {!inspection && currentMappings.length > 0 && (
          <div className="flex flex-wrap gap-1.5">
            {currentMappings.map(m => (
              <Badge key={`${m.sourceColumn}->${m.targetColumn}`} variant="outline" className="font-mono text-xs">
                {`${m.sourceColumn} -> ${m.targetColumn}`}
              </Badge>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
