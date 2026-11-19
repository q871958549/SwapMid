'use client'

import { useState } from 'react'
import { Columns3, Loader2, Pencil, Plus, Trash2 } from 'lucide-react'
import { useTranslations } from 'next-intl'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { StatusBadge } from '@/components/ui/status-badge'
import { toast } from '@/components/ui/use-toast'
import { readApiErrorMessage } from '@/lib/api-error'
import type {
  CdcLakehouseSchemaEvolutionResultDto,
  CdcLakehouseSchemaFieldDto,
  CdcStreamDto,
} from '@/lib/api/types'

const ICEBERG_PRIMITIVE_TYPES = ['string', 'long', 'double', 'boolean', 'timestamptz'] as const

export function CdcIcebergSchemaSection({
  stream,
  currentSchemaId,
  lastColumnId,
  fields,
  canManage,
  onMutated,
}: {
  stream: CdcStreamDto
  currentSchemaId: number
  lastColumnId: number
  fields: CdcLakehouseSchemaFieldDto[]
  canManage: boolean
  onMutated: () => void
}) {
  const t = useTranslations('DataIntegration.cdc')
  const [newColName, setNewColName] = useState('')
  const [newColType, setNewColType] = useState<string>('double')
  const [selectedCol, setSelectedCol] = useState('')
  const [renameTo, setRenameTo] = useState('')
  const [busy, setBusy] = useState<'dry' | 'add' | 'rename' | 'drop' | null>(null)
  const [lastResult, setLastResult] = useState<CdcLakehouseSchemaEvolutionResultDto | null>(null)

  const coreCount = fields.filter(f => f.isCoreEnvelope).length
  const promotedFields = fields.filter(f => !f.isCoreEnvelope)

  const submitEvolution = async (
    mode: 'dry' | 'add' | 'rename' | 'drop',
    body: {
      addColumns?: Array<{ name: string; type: string }>
      renameColumns?: Array<{ fromName: string; toName: string }>
      dropColumns?: string[]
      dryRun: boolean
    },
  ) => {
    setBusy(mode)
    try {
      const res = await fetch(`/api/proxy/cdc-streams/${stream.id}/evolve-lakehouse-schema`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })
      if (!res.ok) {
        const msg = await readApiErrorMessage(res, t('icebergSchemaFailedTitle'))
        toast({ variant: 'destructive', title: t('icebergSchemaFailedTitle'), description: msg })
        return
      }
      const data = await res.json() as CdcLakehouseSchemaEvolutionResultDto
      setLastResult(data)
      if (!body.dryRun) {
        toast({ variant: 'success', title: t('icebergSchemaSuccessTitle'), description: data.message ?? undefined })
        if (mode === 'add') setNewColName('')
        if (mode === 'rename') {
          setRenameTo('')
          setSelectedCol('')
        }
        if (mode === 'drop') setSelectedCol('')
        onMutated()
      }
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="space-y-3 rounded-md border bg-muted/20 p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-1.5 text-xs font-medium text-muted-foreground">
          <Columns3 className="size-3.5" />
          <span>{t('icebergSchemaSectionTitle')}</span>
        </div>
        <div className="flex flex-wrap items-center gap-1.5">
          <StatusBadge tone="info">{`schema-id=${currentSchemaId}`}</StatusBadge>
          <StatusBadge tone="neutral">{`last-column-id=${lastColumnId || 12}`}</StatusBadge>
          {coreCount > 0 && (
            <StatusBadge tone="neutral">{t('icebergSchemaCoreEnvelopeBadge', { count: coreCount })}</StatusBadge>
          )}
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-1.5">
        {promotedFields.length > 0 ? promotedFields.map(f => (
          <StatusBadge key={f.fieldId} tone="success">
            {`#${f.fieldId} ${f.name}: ${f.type}`}
          </StatusBadge>
        )) : (
          <span className="text-xs text-muted-foreground">{t('icebergSchemaNoPromotedHint')}</span>
        )}
      </div>

      {canManage && (
        <div className="space-y-2 border-t pt-2.5">
          <div className="flex flex-wrap items-center gap-2">
            <Input
              value={newColName}
              onChange={e => setNewColName(e.target.value)}
              placeholder={t('icebergSchemaAddPlaceholder')}
              className="h-8 max-w-[200px] font-mono text-xs"
            />
            <Select value={newColType} onValueChange={setNewColType}>
              <SelectTrigger className="h-8 w-[124px] font-mono text-xs">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {ICEBERG_PRIMITIVE_TYPES.map(tp => (
                  <SelectItem key={tp} value={tp} className="font-mono text-xs">{tp}</SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Button
              size="sm"
              variant="outline"
              disabled={busy !== null || !newColName.trim()}
              onClick={() => void submitEvolution('dry', {
                addColumns: [{ name: newColName.trim(), type: newColType }],
                dryRun: true,
              })}
            >
              {busy === 'dry' && <Loader2 className="mr-1.5 size-3.5 animate-spin" />}
              {t('icebergSchemaDryRunButton')}
            </Button>
            <Button
              size="sm"
              variant="outline"
              disabled={busy !== null || !newColName.trim()}
              onClick={() => void submitEvolution('add', {
                addColumns: [{ name: newColName.trim(), type: newColType }],
                dryRun: false,
              })}
            >
              {busy === 'add'
                ? <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                : <Plus className="mr-1.5 size-3.5" />}
              {t('icebergSchemaAddButton')}
            </Button>
          </div>

          {promotedFields.length > 0 && (
            <div className="flex flex-wrap items-center gap-2">
              <Select value={selectedCol} onValueChange={setSelectedCol}>
                <SelectTrigger className="h-8 w-[170px] font-mono text-xs">
                  <SelectValue placeholder={t('icebergSchemaSelectTargetPlaceholder')} />
                </SelectTrigger>
                <SelectContent>
                  {promotedFields.map(f => (
                    <SelectItem key={f.fieldId} value={f.name ?? ''} className="font-mono text-xs">
                      {`#${f.fieldId} ${f.name}`}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <Input
                value={renameTo}
                onChange={e => setRenameTo(e.target.value)}
                placeholder={t('icebergSchemaRenamePlaceholder')}
                className="h-8 max-w-[175px] font-mono text-xs"
              />
              <Button
                size="sm"
                variant="outline"
                disabled={busy !== null || !selectedCol || !renameTo.trim()}
                onClick={() => void submitEvolution('rename', {
                  renameColumns: [{ fromName: selectedCol, toName: renameTo.trim() }],
                  dryRun: false,
                })}
              >
                {busy === 'rename'
                  ? <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                  : <Pencil className="mr-1.5 size-3.5" />}
                {t('icebergSchemaRenameButton')}
              </Button>
              <Button
                size="sm"
                variant="destructive-outline"
                disabled={busy !== null || !selectedCol}
                onClick={() => void submitEvolution('drop', {
                  dropColumns: [selectedCol],
                  dryRun: false,
                })}
              >
                {busy === 'drop'
                  ? <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                  : <Trash2 className="mr-1.5 size-3.5" />}
                {t('icebergSchemaDropButton')}
              </Button>
            </div>
          )}

          {lastResult && (
            <div className="rounded border bg-background px-3 py-2 text-xs text-muted-foreground">
              {lastResult.message}
            </div>
          )}
        </div>
      )}
    </div>
  )
}
