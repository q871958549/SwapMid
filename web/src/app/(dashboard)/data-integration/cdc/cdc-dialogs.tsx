'use client'

import { AlertTriangle, CheckCircle2, XCircle } from 'lucide-react'
import { useTranslations } from 'next-intl'
import { Button } from '@/components/ui/button'
import { Dialog } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ScrollableDialogContent } from '@/components/ui/scrollable-dialog'
import { StatusBadge } from '@/components/ui/status-badge'
import type { CdcPreflightResultDto, CdcStreamDto } from '@/lib/api/types'

export interface CdcPreflightState {
  stream: CdcStreamDto
  result: CdcPreflightResultDto
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
            </div>

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
