'use client'

import { useState } from 'react'
import { AlertTriangle } from 'lucide-react'
import { useTranslations } from 'next-intl'
import { Button } from '@/components/ui/button'
import { Dialog } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ScrollableDialogContent } from '@/components/ui/scrollable-dialog'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { PipelineExecutionDto } from '@/lib/api/types'
import { ExecutionEvidenceCard } from './execution-evidence-card'
import {
  EMPTY_OUTCOME_RESOLUTION,
  MAX_EVIDENCE_REFERENCE_LENGTH,
  canSubmitOutcomeResolution,
  isStructuredEvidenceReference,
  type OutcomeResolution,
} from './outcome-resolution'

export interface OutcomeResolutionSubmission {
  resolution: Exclude<OutcomeResolution, ''>
  processedRows: number | null
  evidenceReference: string
}

export function OutcomeResolutionDialog({
  target,
  resolving,
  recoveryReady,
  recoveryStatus,
  allowConfirmedApplied,
  onCancel,
  onSubmit,
}: {
  target: PipelineExecutionDto
  resolving: boolean
  recoveryReady: boolean
  recoveryStatus?: 'loading' | 'ready' | 'unavailable'
  allowConfirmedApplied: boolean
  onCancel: () => void
  onSubmit: (submission: OutcomeResolutionSubmission) => void
}) {
  const t = useTranslations('DataIntegration.execution')
  const rc = useTranslations('DataIntegration.common')
  const tc = useTranslations('Common')
  const [resolution, setResolution] = useState<OutcomeResolution>(EMPTY_OUTCOME_RESOLUTION)
  const [evidenceReference, setEvidenceReference] = useState('')
  const [processedRows, setProcessedRows] = useState('')
  const resolvedRecoveryStatus = recoveryStatus ?? (recoveryReady ? 'ready' : 'loading')

  const normalizedEvidenceReference = evidenceReference.trim()
  const parsedProcessedRows = processedRows === '' ? null : Number(processedRows)
  const processedRowsValid = parsedProcessedRows === null
    || (Number.isSafeInteger(parsedProcessedRows) && parsedProcessedRows >= 0)
  const canSubmit = canSubmitOutcomeResolution(
    resolution,
    normalizedEvidenceReference,
    processedRowsValid,
  ) && (allowConfirmedApplied || resolution === '1')

  return (
    <Dialog open onOpenChange={(open) => { if (!open && !resolving) onCancel() }}>
      <ScrollableDialogContent
        title={t('resolveDialogTitle', { name: target.pipelineName ?? '-' })}
        description={t('resolveDialogDesc', { id: target.id })}
        className="sm:max-w-xl"
        footer={(
          <div className="flex justify-end gap-2">
            <Button variant="outline" disabled={resolving} onClick={onCancel}>
              {tc('cancel')}
            </Button>
            <Button
              variant="destructive"
              disabled={!canSubmit || resolving || resolvedRecoveryStatus !== 'ready'}
              onClick={() => {
                if (!canSubmit || resolution === '' || resolvedRecoveryStatus !== 'ready') return
                onSubmit({
                  resolution,
                  processedRows: resolution === '0' ? parsedProcessedRows : null,
                  evidenceReference: normalizedEvidenceReference,
                })
              }}
            >
              {resolving
                ? tc('processing')
                : resolvedRecoveryStatus === 'unavailable'
                  ? rc('recoveryStorageUnavailableTitle')
                  : resolvedRecoveryStatus === 'ready'
                  ? t('submitResolutionButton')
                  : t('executionRecoveryLoadingButton')}
            </Button>
          </div>
        )}
      >
        <div className="space-y-4 p-6">
          {resolvedRecoveryStatus === 'unavailable' && (
            <div role="alert" className="flex gap-3 rounded-md border border-destructive/40 bg-destructive/10 p-3 text-destructive">
              <AlertTriangle className="mt-0.5 size-5 shrink-0" />
              <div className="space-y-1">
                <p className="text-sm font-semibold">{rc('recoveryStorageUnavailableTitle')}</p>
                <p className="text-xs leading-5">{rc('recoveryStorageUnavailableDesc')}</p>
              </div>
            </div>
          )}
          <div role="alert" className="flex gap-3 rounded-md border border-destructive/40 bg-destructive/10 p-3 text-destructive">
            <AlertTriangle className="mt-0.5 size-5 shrink-0" />
            <p className="text-xs leading-5">
              {allowConfirmedApplied ? t('resolveWarning') : t('receiptManualOverrideWarning')}
            </p>
          </div>

          <ExecutionEvidenceCard execution={target} />

          <div className="space-y-1.5">
            <Label htmlFor="outcome-resolution">{t('resolutionFieldLabel')}</Label>
            <Select value={resolution} onValueChange={value => setResolution(value as OutcomeResolution)} disabled={resolving}>
              <SelectTrigger id="outcome-resolution"><SelectValue placeholder={t('resolutionPlaceholder')} /></SelectTrigger>
              <SelectContent>
                {allowConfirmedApplied && (
                  <SelectItem value="0">{t('resolutionConfirmedApplied')}</SelectItem>
                )}
                <SelectItem value="1">{t('resolutionConfirmedNotApplied')}</SelectItem>
              </SelectContent>
            </Select>
          </div>

          {resolution === '0' && (
            <div className="space-y-1.5">
              <Label htmlFor="outcome-processed-rows">{t('processedRowsFieldLabel')}</Label>
              <Input
                id="outcome-processed-rows"
                type="number"
                min={0}
                step={1}
                value={processedRows}
                disabled={resolving}
                aria-invalid={!processedRowsValid}
                onChange={event => setProcessedRows(event.target.value)}
                placeholder={t('processedRowsPlaceholder')}
              />
              {!processedRowsValid && <p className="text-xs text-destructive">{t('processedRowsInvalid')}</p>}
            </div>
          )}

          <div className="space-y-1.5">
            <Label htmlFor="outcome-evidence-reference">{t('verificationNoteFieldLabel')}</Label>
            <Input
              id="outcome-evidence-reference"
              value={evidenceReference}
              maxLength={MAX_EVIDENCE_REFERENCE_LENGTH}
              disabled={resolving}
              aria-invalid={evidenceReference.length > 0
                && !isStructuredEvidenceReference(normalizedEvidenceReference)}
              onChange={event => setEvidenceReference(event.target.value)}
              placeholder={t('verificationNotePlaceholder')}
              autoComplete="off"
            />
            <p className="text-xs text-muted-foreground">
              {t('verificationNoteHint', { count: normalizedEvidenceReference.length })}
            </p>
          </div>
        </div>
      </ScrollableDialogContent>
    </Dialog>
  )
}
