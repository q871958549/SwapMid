'use client'

import { useState } from 'react'
import { useTranslations } from 'next-intl'
import { AlertTriangle, CheckCircle2, Loader2, ShieldCheck } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { getApiErrorMessage } from '@/lib/api-error'
import type {
  PipelineSchemaPreflightResultDto,
  PreflightSchemaIssueDto,
} from '@/lib/api/types'
import {
  serializeEtlTransformConfig,
  type EtlTransformFormValue,
} from './transform-config'

export function PipelineSchemaPreflightPanel({
  sourceId,
  targetId,
  form,
}: {
  sourceId: string
  targetId: string
  form: EtlTransformFormValue
}) {
  const t = useTranslations('DataIntegration.pipeline')
  const [loading, setLoading] = useState(false)
  const [result, setResult] = useState<PipelineSchemaPreflightResultDto | null>(null)
  const [requestError, setRequestError] = useState<string | null>(null)

  const canRunPreflight = Boolean(sourceId) && Boolean(form.sourceQuery.trim())

  async function handleRunPreflight() {
    if (!canRunPreflight || loading) return
    const serialized = serializeEtlTransformConfig(form) ?? ''
    setLoading(true)
    setRequestError(null)
    try {
      const res = await fetch('/api/proxy/data-pipelines/preflight-schema', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          sourceId,
          targetId: targetId || null,
          transformConfig: serialized,
        }),
      })
      const text = await res.text()
      if (!res.ok) {
        setResult(null)
        setRequestError(getApiErrorMessage(new Error(text), t('preflightFailedFallback')))
        return
      }
      const parsed = JSON.parse(text) as PipelineSchemaPreflightResultDto
      setResult(parsed)
    } catch (err) {
      setResult(null)
      setRequestError(getApiErrorMessage(err, t('preflightFailedFallback')))
    } finally {
      setLoading(false)
    }
  }

  const issues: PreflightSchemaIssueDto[] = result?.issues ?? []
  const sourceCount = result?.sourceColumns?.length ?? 0
  const targetCount = result?.targetColumns?.length ?? 0

  return (
    <div className="space-y-2 pt-1">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={!canRunPreflight || loading}
          onClick={handleRunPreflight}
        >
          {loading
            ? <Loader2 className="mr-1.5 size-3.5 animate-spin" />
            : <ShieldCheck className="mr-1.5 size-3.5" />}
          {loading ? t('preflightRunning') : t('preflightButton')}
        </Button>
        {!canRunPreflight && (
          <span className="text-muted-foreground text-xs">{t('preflightSelectSourceHint')}</span>
        )}
        {result && (
          <div className="flex items-center gap-1.5">
            <Badge variant={result.compatible ? 'success' : 'destructive-soft'}>
              {result.compatible ? t('preflightCompatibleBadge') : t('preflightIncompatibleBadge')}
            </Badge>
            <span className="text-muted-foreground font-mono text-xs">
              {t('preflightSummaryStats', { sourceCount, targetCount })}
            </span>
          </div>
        )}
      </div>

      {requestError && (
        <p role="alert" className="text-destructive text-xs">{requestError}</p>
      )}

      {result && (
        <div className="bg-background/80 max-h-36 space-y-1.5 overflow-y-auto rounded-md border p-2 text-xs">
          {issues.length === 0 ? (
            <div className="text-success flex items-center gap-1.5">
              <CheckCircle2 className="size-3.5 shrink-0" />
              <span>{t('preflightNoIssuesHint')}</span>
            </div>
          ) : (
            issues.map((issue, idx) => (
              <div
                key={`${issue.code ?? 'issue'}-${idx}`}
                className="flex items-start gap-1.5"
              >
                <AlertTriangle
                  className={
                    issue.severity === 'error'
                      ? 'text-destructive mt-0.5 size-3.5 shrink-0'
                      : 'text-warning mt-0.5 size-3.5 shrink-0'
                  }
                />
                <div className="min-w-0 flex-1">
                  <span className="text-muted-foreground mr-1.5 font-mono text-[11px]">
                    [{issue.code}]
                  </span>
                  <span className={issue.severity === 'error' ? 'text-destructive' : 'text-foreground'}>
                    {issue.message}
                  </span>
                </div>
              </div>
            ))
          )}
        </div>
      )}
    </div>
  )
}
