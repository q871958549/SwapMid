'use client'

import { AlertTriangle } from 'lucide-react'
import { useTranslations } from 'next-intl'
import { type PipelineExecutionDto } from '@/lib/api/types'
import { cn } from '@/lib/utils'

const EXECUTION_EVIDENCE_FIELDS = [
  'targetResourceGroup',
  'targetDataSourceId',
  'targetDataSourceName',
  'targetProvider',
  'targetEndpoint',
  'targetDatabase',
  'targetTable',
  'targetConfigVersion',
  'sourceDataSourceId',
  'sourceDataSourceName',
  'sourceConfigVersion',
  'writeMode',
  'sourceQuerySha256',
] as const satisfies readonly (keyof PipelineExecutionDto)[]

export type ExecutionEvidenceCompleteness = 'complete' | 'incomplete' | 'missing'

function hasEvidenceValue(value: unknown): boolean {
  return typeof value === 'string' && value.trim().length > 0
}

export function getExecutionEvidenceCompleteness(
  execution: PipelineExecutionDto,
): ExecutionEvidenceCompleteness {
  const presentCount = EXECUTION_EVIDENCE_FIELDS.reduce(
    (count, field) => count + (hasEvidenceValue(execution[field]) ? 1 : 0),
    0,
  )
  if (presentCount === 0) return 'missing'
  if (presentCount < EXECUTION_EVIDENCE_FIELDS.length) return 'incomplete'
  return 'complete'
}

function EvidenceField({
  label,
  value,
  missingValue,
  mono = false,
}: {
  label: string
  value: string | null | undefined
  missingValue: string
  mono?: boolean
}) {
  const normalized = value?.trim()
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className={cn(
        'mt-0.5 break-all text-sm',
        mono && normalized && 'font-mono text-xs',
        !normalized && 'font-medium text-destructive',
      )}
      >
        {normalized || missingValue}
      </dd>
    </div>
  )
}

export function ExecutionEvidenceCard({ execution }: { execution: PipelineExecutionDto }) {
  const t = useTranslations('DataIntegration.execution')
  const completeness = getExecutionEvidenceCompleteness(execution)
  const missingValue = t('targetSnapshotFieldMissing')
  const normalizedWriteMode = execution.writeMode?.trim().toLowerCase()
  const writeMode = normalizedWriteMode === 'append'
    ? t('writeModeAppend')
    : normalizedWriteMode === 'truncate'
      ? t('writeModeTruncate')
      : execution.writeMode

  return (
    <section
      aria-label={t('targetSnapshotTitle')}
      className="space-y-3 rounded-md border border-destructive/25 bg-background/80 p-3 text-foreground"
    >
      <div>
        <h3 className="text-sm font-semibold">{t('targetSnapshotTitle')}</h3>
        <p className="mt-1 text-xs leading-5 text-muted-foreground">{t('targetSnapshotDesc')}</p>
      </div>

      {completeness === 'missing' ? (
        <div role="alert" className="flex gap-2 rounded-md border border-destructive/30 bg-destructive/10 p-2.5 text-destructive">
          <AlertTriangle className="mt-0.5 size-4 shrink-0" />
          <p className="text-xs leading-5">{t('targetSnapshotMissing')}</p>
        </div>
      ) : (
        <>
          {completeness === 'incomplete' && (
            <div role="alert" className="flex gap-2 rounded-md border border-destructive/30 bg-destructive/10 p-2.5 text-destructive">
              <AlertTriangle className="mt-0.5 size-4 shrink-0" />
              <p className="text-xs leading-5">{t('targetSnapshotIncomplete')}</p>
            </div>
          )}

          <div className="space-y-2">
            <h4 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
              {t('targetIdentityGroupTitle')}
            </h4>
            <dl className="grid grid-cols-2 gap-x-5 gap-y-2 sm:grid-cols-3">
              <EvidenceField label={t('targetResourceGroupLabel')} value={execution.targetResourceGroup} missingValue={missingValue} mono />
              <EvidenceField label={t('targetDataSourceIdLabel')} value={execution.targetDataSourceId} missingValue={missingValue} mono />
              <EvidenceField label={t('targetDataSourceNameLabel')} value={execution.targetDataSourceName} missingValue={missingValue} />
              <EvidenceField label={t('targetProviderLabel')} value={execution.targetProvider} missingValue={missingValue} mono />
              <EvidenceField label={t('targetEndpointLabel')} value={execution.targetEndpoint} missingValue={missingValue} mono />
              <EvidenceField label={t('targetDatabaseLabel')} value={execution.targetDatabase} missingValue={missingValue} mono />
              <EvidenceField label={t('targetTableLabel')} value={execution.targetTable} missingValue={missingValue} mono />
              <EvidenceField label={t('targetConfigVersionLabel')} value={execution.targetConfigVersion} missingValue={missingValue} mono />
            </dl>
          </div>

          <div className="space-y-2 border-t pt-3">
            <h4 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
              {t('sourceEvidenceGroupTitle')}
            </h4>
            <dl className="grid grid-cols-2 gap-x-5 gap-y-2 sm:grid-cols-3">
              <EvidenceField label={t('sourceDataSourceIdLabel')} value={execution.sourceDataSourceId} missingValue={missingValue} mono />
              <EvidenceField label={t('sourceDataSourceNameLabel')} value={execution.sourceDataSourceName} missingValue={missingValue} />
              <EvidenceField label={t('sourceConfigVersionLabel')} value={execution.sourceConfigVersion} missingValue={missingValue} mono />
            </dl>
          </div>

          <div className="space-y-2 border-t pt-3">
            <h4 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
              {t('writeEvidenceGroupTitle')}
            </h4>
            <dl className="grid grid-cols-1 gap-x-5 gap-y-2 sm:grid-cols-2">
              <EvidenceField label={t('writeModeLabel')} value={writeMode} missingValue={missingValue} />
              <EvidenceField label={t('sourceQuerySha256Label')} value={execution.sourceQuerySha256} missingValue={missingValue} mono />
            </dl>
          </div>
        </>
      )}
    </section>
  )
}
