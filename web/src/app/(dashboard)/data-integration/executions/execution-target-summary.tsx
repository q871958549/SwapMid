'use client'

import { useTranslations } from 'next-intl'
import { type PipelineExecutionDto } from '@/lib/api/types'
import { cn } from '@/lib/utils'

function formatWriteModeLabel(
  value: string | null | undefined,
  t: ReturnType<typeof useTranslations>,
): string | null {
  const normalized = value?.trim().toLowerCase()
  if (!normalized) return null
  if (normalized === 'append') return t('writeModeAppend')
  if (normalized === 'truncate') return t('writeModeTruncate')
  return value!.trim()
}

function SummaryField({
  label,
  value,
  mono = false,
}: {
  label: string
  value: string | null | undefined
  mono?: boolean
}) {
  const trimmed = value?.trim()
  if (!trimmed) return null
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className={cn('mt-0.5 break-all text-sm', mono && 'font-mono text-xs')}>
        {trimmed}
      </dd>
    </div>
  )
}

export function hasExecutionTargetSummary(execution: PipelineExecutionDto): boolean {
  return Boolean(
    execution.sourceDataSourceName?.trim()
    || execution.targetDataSourceName?.trim()
    || execution.targetProvider?.trim()
    || execution.targetEndpoint?.trim()
    || execution.targetDatabase?.trim()
    || execution.targetTable?.trim()
    || execution.targetResourceGroup?.trim()
    || execution.writeMode?.trim(),
  )
}

export function ExecutionTargetSummary({ execution }: { execution: PipelineExecutionDto }) {
  const t = useTranslations('DataIntegration.execution')
  if (!hasExecutionTargetSummary(execution)) return null

  const writeModeLabel = formatWriteModeLabel(execution.writeMode, t)

  return (
    <section
      aria-label={t('targetDeliverySummaryTitle')}
      className="space-y-3 rounded-md border bg-muted/30 p-3 text-foreground"
    >
      <h3 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
        {t('targetDeliverySummaryTitle')}
      </h3>
      <dl className="grid grid-cols-2 gap-x-5 gap-y-2.5 sm:grid-cols-3">
        <SummaryField label={t('sourceDataSourceNameLabel')} value={execution.sourceDataSourceName} />
        <SummaryField label={t('targetDataSourceNameLabel')} value={execution.targetDataSourceName} />
        <SummaryField label={t('targetProviderLabel')} value={execution.targetProvider} mono />
        <SummaryField label={t('targetEndpointLabel')} value={execution.targetEndpoint} mono />
        <SummaryField label={t('targetDatabaseLabel')} value={execution.targetDatabase} mono />
        <SummaryField label={t('targetTableLabel')} value={execution.targetTable} mono />
        <SummaryField label={t('targetResourceGroupLabel')} value={execution.targetResourceGroup} mono />
        <SummaryField label={t('writeModeLabel')} value={writeModeLabel} />
      </dl>
    </section>
  )
}
