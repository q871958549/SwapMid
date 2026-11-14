'use client'

import { type RefObject } from 'react'
import { useTranslations } from 'next-intl'
import { Button } from '@/components/ui/button'
import { Dialog } from '@/components/ui/dialog'
import { ScrollableDialogContent } from '@/components/ui/scrollable-dialog'
import {
  durationText,
  fmt,
  StatusBadge,
  triggerText,
  type JobExecution,
} from './execution-shared'

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="text-muted-foreground text-xs">{label}</dt>
      <dd className="mt-0.5 text-sm">{children}</dd>
    </div>
  )
}
export function ExecutionDetailDialog({
  detail,
  onClose,
  jobName,
  returnFocusRef,
}: {
  detail: JobExecution | null
  onClose: () => void
  jobName?: string
  returnFocusRef?: RefObject<HTMLButtonElement | null>
}) {
  const t = useTranslations('System.scheduledJobs')
  const tc = useTranslations('Common')

  const displayName = jobName || detail?.jobKey || t('executions.manualQueuedPlain')

  return (
    <Dialog open={!!detail} onOpenChange={(open) => { if (!open) onClose() }}>
      {detail && (
        <ScrollableDialogContent
          title={t('executions.detail.titleTemplate', { name: displayName })}
          description={t('executions.detail.idTemplate', { id: detail.id })}
          footer={<div className="flex justify-end"><Button variant="outline" onClick={onClose}>{tc('close')}</Button></div>}
          onCloseAutoFocus={(event) => {
            const target = returnFocusRef?.current
            if (!target?.isConnected) return
            event.preventDefault()
            target.focus()
          }}
        >
          <div className="space-y-4 p-6">
            <dl className="grid grid-cols-2 gap-x-6 gap-y-3 sm:grid-cols-3">
              <Field label={t('executions.detail.status')}><StatusBadge status={detail.status} /></Field>
              <Field label={t('executions.detail.triggerMethod')}>
                {triggerText(detail.triggerKind, t)}{detail.isDryRun && t('executions.detail.dryRunSuffix')}
              </Field>
              <Field label={t('executions.detail.duration')}>{durationText(detail.durationMs)}</Field>
              <Field label={t('executions.detail.startedAt')}>{fmt(detail.startedAt)}</Field>
              <Field label={t('executions.detail.finishedAt')}>{fmt(detail.finishedAt)}</Field>
              <Field label={t('executions.detail.jobType')}><span className="font-mono text-xs">{detail.jobType}</span></Field>
            </dl>
            {detail.argsJson && (
              <div>
                <p className="text-muted-foreground mb-1 text-xs">{t('executions.detail.argsLabel')}</p>
                <pre className="max-h-40 overflow-auto rounded-md border bg-muted/40 p-2 font-mono text-xs">{detail.argsJson}</pre>
              </div>
            )}
            {detail.error ? (
              <div>
                <p className="text-muted-foreground mb-1 text-xs">{t('executions.detail.errorLabel')}</p>
                <pre className="text-destructive border-destructive/30 bg-destructive/5 max-h-60 overflow-auto rounded-md border p-3 font-mono text-xs whitespace-pre-wrap">{detail.error}</pre>
              </div>
            ) : detail.summary && (
              <div>
                <p className="text-muted-foreground mb-1 text-xs">{t('executions.detail.summaryLabel')}</p>
                <p className="bg-muted/40 rounded-md border p-3 text-sm whitespace-pre-wrap">{detail.summary}</p>
              </div>
            )}
          </div>
        </ScrollableDialogContent>
      )}
    </Dialog>
  )
}
