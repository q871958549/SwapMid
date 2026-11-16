'use client'

import { useState } from 'react'
import { AlertTriangle, Check, CheckCircle2, Copy, XCircle } from 'lucide-react'
import { useTranslations } from 'next-intl'
import { Button } from '@/components/ui/button'
import { Dialog } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ScrollableDialogContent } from '@/components/ui/scrollable-dialog'
import { StatusBadge, type StatusTone } from '@/components/ui/status-badge'
import type { CdcPreflightResultDto, CdcSinkTemplateDto, CdcStreamDto } from '@/lib/api/types'

export interface CdcPreflightState {
  stream: CdcStreamDto
  result: CdcPreflightResultDto
}

export interface CdcSinkTemplateState {
  stream: CdcStreamDto
  template: CdcSinkTemplateDto
}

function formatWalBytes(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined) return '-'
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`
}

function getReplicaIdentityTone(mode: string | null | undefined): StatusTone {
  switch (mode) {
    case 'full':
      return 'success'
    case 'index':
      return 'info'
    case 'nothing':
      return 'danger'
    default:
      return 'warning'
  }
}

function MetricField({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-md border bg-muted/30 px-3 py-2">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className={mono ? 'mt-0.5 font-mono text-sm font-medium' : 'mt-0.5 text-sm font-medium'}>{value}</div>
    </div>
  )
}

export function CdcPreflightDialog({
  state,
  onClose,
}: {
  state: CdcPreflightState | null
  onClose: () => void
}) {
  const t = useTranslations('DataIntegration.cdc')
  const tc = useTranslations('Common')

  return (
    <Dialog open={!!state} onOpenChange={(open) => { if (!open) onClose() }}>
      {state && (
        <ScrollableDialogContent
          title={t('preflightDialogTitle', { name: state.stream.name })}
          description={state.result.summary ?? undefined}
          className="sm:max-w-lg"
          footer={<Button variant="outline" onClick={onClose}>{tc('close')}</Button>}
        >
          <div className="space-y-4">
            <div className="flex items-center gap-2">
              {state.result.isReady
                ? <CheckCircle2 className="size-4 text-success" />
                : <XCircle className="size-4 text-destructive" />}
              <StatusBadge tone={state.result.isReady ? 'success' : 'danger'}>
                {state.result.isReady ? t('preflightReadyBadge') : t('preflightNotReadyBadge')}
              </StatusBadge>
            </div>

            <div className="grid grid-cols-2 gap-2.5">
              <MetricField label={t('preflightWalLevelLabel')} value={state.result.walLevel || '-'} mono />
              <MetricField label={t('preflightPgVersionLabel')} value={state.result.postgreSqlVersion || '-'} />
              <MetricField
                label={t('preflightPublicationLabel')}
                value={state.result.publicationExists ? t('existsLabel') : t('missingLabel')}
              />
              <MetricField
                label={t('preflightSlotLabel')}
                value={state.result.slotExists
                  ? `${state.result.slotPlugin || 'pgoutput'} · ${state.result.slotActive ? t('slotActiveLabel') : t('slotIdleLabel')}`
                  : t('missingLabel')}
              />
              <MetricField
                label={t('preflightConfirmedLsnLabel')}
                value={state.result.confirmedFlushLsn || '-'}
                mono
              />
              <MetricField
                label={t('preflightCurrentLsnLabel')}
                value={state.result.currentWalLsn || '-'}
                mono
              />
              <MetricField
                label={t('preflightWalLagLabel')}
                value={formatWalBytes(state.result.walLagBytes)}
                mono
              />
            </div>

            {state.result.tableReplicaIdentities && state.result.tableReplicaIdentities.length > 0 && (
              <div className="space-y-1.5 rounded-md border bg-muted/30 px-3 py-2.5">
                <div className="text-xs text-muted-foreground">{t('preflightReplicaIdentityLabel')}</div>
                <div className="flex flex-wrap gap-1.5">
                  {state.result.tableReplicaIdentities.map(item => (
                    <StatusBadge
                      key={`${item.schema}.${item.table}`}
                      tone={getReplicaIdentityTone(item.replicaIdentity)}
                    >
                      {`${item.schema}.${item.table} · ${item.replicaIdentity}`}
                    </StatusBadge>
                  ))}
                </div>
              </div>
            )}

            {state.result.diagnostics && state.result.diagnostics.length > 0 && (
              <div className="space-y-1.5 rounded-md border border-warning/35 bg-warning-soft p-3 text-xs text-warning">
                <div className="flex items-center gap-1.5 font-medium">
                  <AlertTriangle className="size-3.5 shrink-0" />
                  <span>{t('preflightDiagnosticsTitle')}</span>
                </div>
                <ul className="list-disc space-y-1 pl-4">
                  {state.result.diagnostics.map((item, idx) => (
                    <li key={idx}>{item}</li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        </ScrollableDialogContent>
      )}
    </Dialog>
  )
}

export function CdcCheckpointDialog({
  target,
  value,
  submitting,
  error,
  onValueChange,
  onClose,
  onSubmit,
}: {
  target: CdcStreamDto | null
  value: string
  submitting: boolean
  error: string | null
  onValueChange: (next: string) => void
  onClose: () => void
  onSubmit: () => void
}) {
  const t = useTranslations('DataIntegration.cdc')
  const tc = useTranslations('Common')

  return (
    <Dialog open={!!target} onOpenChange={(open) => { if (!open) onClose() }}>
      {target && (
        <ScrollableDialogContent
          title={t('checkpointDialogTitle', { name: target.name })}
          description={t('checkpointDialogDesc')}
          className="sm:max-w-md"
          footer={(
            <div className="flex justify-end gap-2">
              <Button variant="outline" disabled={submitting} onClick={onClose}>{tc('cancel')}</Button>
              <Button disabled={submitting} onClick={onSubmit}>
                {submitting ? tc('processing') : tc('confirm')}
              </Button>
            </div>
          )}
        >
          <div className="space-y-3">
            <div className="space-y-1.5">
              <Label>{t('checkpointInputLabel')}</Label>
              <Input
                value={value}
                onChange={e => onValueChange(e.target.value)}
                placeholder="0/16B6C50"
                className="font-mono"
              />
              <p className="text-xs text-muted-foreground">{t('checkpointInputHint')}</p>
            </div>
            {error && <p className="text-xs text-destructive">{error}</p>}
          </div>
        </ScrollableDialogContent>
      )}
    </Dialog>
  )
}

export function CdcDropSlotDialog({
  target,
  submitting,
  error,
  onClose,
  onSubmit,
}: {
  target: CdcStreamDto | null
  submitting: boolean
  error: string | null
  onClose: () => void
  onSubmit: () => void
}) {
  const t = useTranslations('DataIntegration.cdc')
  const tc = useTranslations('Common')

  return (
    <Dialog open={!!target} onOpenChange={(open) => { if (!open) onClose() }}>
      {target && (
        <ScrollableDialogContent
          title={t('dropSlotDialogTitle', { slot: target.slotName })}
          description={t('dropSlotDialogDesc', { name: target.name, slot: target.slotName })}
          className="sm:max-w-md"
          footer={(
            <div className="flex justify-end gap-2">
              <Button variant="outline" disabled={submitting} onClick={onClose}>{tc('cancel')}</Button>
              <Button variant="destructive" disabled={submitting} onClick={onSubmit}>
                {submitting ? tc('processing') : t('dropSlotConfirmButton')}
              </Button>
            </div>
          )}
        >
          {error && <p className="text-xs text-destructive">{error}</p>}
        </ScrollableDialogContent>
      )}
    </Dialog>
  )
}

export function CdcSinkTemplateDialog({
  state,
  onClose,
}: {
  state: CdcSinkTemplateState | null
  onClose: () => void
}) {
  const t = useTranslations('DataIntegration.cdc')
  const tc = useTranslations('Common')
  const [copiedDdl, setCopiedDdl] = useState(false)

  async function copyDdl(ddl: string) {
    try {
      await navigator.clipboard?.writeText(ddl)
      setCopiedDdl(true)
      setTimeout(() => setCopiedDdl(false), 1800)
    } catch {
      setCopiedDdl(false)
    }
  }

  return (
    <Dialog open={!!state} onOpenChange={(open) => { if (!open) onClose() }}>
      {state && (
        <ScrollableDialogContent
          title={t('sinkTemplateDialogTitle', { name: state.stream.name })}
          description={state.template.summary ?? undefined}
          className="sm:max-w-2xl"
          footer={<Button variant="outline" onClick={onClose}>{tc('close')}</Button>}
        >
          <div className="space-y-4">
            <div className="flex flex-wrap items-center gap-2">
              <StatusBadge tone="neutral">{state.template.destination}</StatusBadge>
              {state.template.targetExists === true && (
                <StatusBadge tone="success">{t('sinkTargetExistsLabel')}</StatusBadge>
              )}
              {state.template.targetExists === false && (
                <StatusBadge tone="warning">{t('sinkTargetMissingLabel')}</StatusBadge>
              )}
              {state.template.partitionKeyExpression && (
                <StatusBadge tone="info">{`Key: ${state.template.partitionKeyExpression}`}</StatusBadge>
              )}
            </div>

            {state.template.clickHouseDdl && (
              <div className="space-y-1.5">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-medium text-muted-foreground">{t('sinkTemplateDdlTitle')}</span>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => void copyDdl(state.template.clickHouseDdl!)}
                  >
                    {copiedDdl ? <Check className="mr-1.5 size-3.5" /> : <Copy className="mr-1.5 size-3.5" />}
                    {copiedDdl ? t('copiedDdlButton') : t('copyDdlButton')}
                  </Button>
                </div>
                <pre className="max-h-52 overflow-auto rounded-md border bg-muted/40 p-3 font-mono text-xs">
                  {state.template.clickHouseDdl}
                </pre>
              </div>
            )}

            {state.template.columns && state.template.columns.length > 0 && (
              <div className="space-y-1.5">
                <span className="text-xs font-medium text-muted-foreground">{t('sinkTemplateColumnsTitle')}</span>
                <div className="divide-y rounded-md border text-xs">
                  {state.template.columns.map(col => (
                    <div key={col.name} className="grid grid-cols-12 items-center gap-2 px-3 py-1.5">
                      <span className="col-span-4 font-mono font-medium">{col.name}</span>
                      <span className="col-span-4 font-mono text-muted-foreground">{col.clickHouseType || col.logicalType}</span>
                      <span className="col-span-4 text-muted-foreground">{col.description}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}

            <div className="space-y-1.5">
              <span className="text-xs font-medium text-muted-foreground">{t('sinkTemplateSampleTitle')}</span>
              <pre className="max-h-56 overflow-auto rounded-md border bg-muted/40 p-3 font-mono text-xs">
                {state.template.samplePayloadJson}
              </pre>
            </div>
          </div>
        </ScrollableDialogContent>
      )}
    </Dialog>
  )
}
