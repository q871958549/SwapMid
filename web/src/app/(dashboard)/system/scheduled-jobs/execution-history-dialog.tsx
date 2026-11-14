'use client'

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslations } from 'next-intl'
import Link from 'next/link'
import { ArrowRight, History, Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { SegmentedControl } from '@/components/ui/segmented-control'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { loadAuditUsers, type AuditUserInfo } from '@/components/data-table/data-table-audit'
import { dataTableLinkClass } from '@/components/data-table/data-table-link-cell'
import { useOptionalSessionIdentity } from '@/components/providers/session-identity-provider'
import { useSerializedPolling } from '@/lib/use-serialized-polling'
import { ExecutionDetailDialog } from './execution-detail-dialog'
import {
  durationText,
  fmt,
  isDurationAnomaly,
  StatusBadge,
  successfulDurationMedian,
  triggerText,
  type JobExecution,
} from './execution-shared'

export type HistoryQuickFilter = 'all' | 'succeeded' | 'failed' | 'skipped' | 'dryRun'

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === 'AbortError'
}

export function buildExecutionHistoryParams(jobKey: string, filter: HistoryQuickFilter) {
  const params = new URLSearchParams({ jobKey, maxResultCount: '30', sorting: 'StartedAt desc' })
  if (filter === 'succeeded') params.set('Status', '1')
  if (filter === 'failed') params.set('Status', '2')
  if (filter === 'skipped') params.set('Status', '4')
  if (filter === 'dryRun') params.set('IsDryRun', 'true')
  return params
}

export function buildDurationBaselineParams(jobKey: string, isDryRun: boolean) {
  const params = buildExecutionHistoryParams(jobKey, 'succeeded')
  params.set('IsDryRun', String(isDryRun))
  return params
}

export function ExecutionHistoryButton({
  jobKey,
  jobName,
  disabled,
}: {
  jobKey: string
  jobName: string
  disabled?: boolean
}) {
  const [open, setOpen] = useState(false)
  const [loading, setLoading] = useState(false)
  const [rows, setRows] = useState<JobExecution[]>([])
  const [error, setError] = useState<string | null>(null)
  const [durationBaselineRows, setDurationBaselineRows] = useState<JobExecution[]>([])
  const [quickFilter, setQuickFilter] = useState<HistoryQuickFilter>('all')
  const [detail, setDetail] = useState<JobExecution | null>(null)
  const sessionIdentity = useOptionalSessionIdentity()
  const auditIdentityVersion = sessionIdentity?.identityVersion ?? null
  const quickFilterRef = useRef<HistoryQuickFilter>('all')
  const requestSequence = useRef(0)
  const baselineRequestSequence = useRef(0)
  const foregroundRequestRef = useRef<AbortController | null>(null)
  const baselineRequestRef = useRef<AbortController | null>(null)
  const historyTriggerRef = useRef<HTMLButtonElement | null>(null)
  const detailTriggerRef = useRef<HTMLButtonElement | null>(null)
  // 操作人（CreatorId）解析：手写表格非 DataTable，不享受其内置审计列机制，手动复用同一批量解析函数。
  const [auditUserState, setAuditUserState] = useState<{
    identityVersion: string | null
    users: Record<string, AuditUserInfo>
  }>({ identityVersion: null, users: {} })
  const auditUsers = useMemo<Record<string, AuditUserInfo>>(
    () => open && auditUserState.identityVersion === auditIdentityVersion ? auditUserState.users : {},
    [auditIdentityVersion, auditUserState, open],
  )
  const t = useTranslations('System.scheduledJobs')
  const tc = useTranslations('Common')
  const durationMedians = useMemo(() => ({
    formal: successfulDurationMedian(durationBaselineRows, false),
    dryRun: successfulDurationMedian(durationBaselineRows, true),
  }), [durationBaselineRows])

  const load = useCallback(async (silent: boolean, filter: HistoryQuickFilter, signal?: AbortSignal) => {
    const requestId = ++requestSequence.current
    if (!silent) setLoading(true)
    setError(null)
    try {
      const params = buildExecutionHistoryParams(jobKey, filter)
      const res = await fetch(`/api/proxy/scheduling/executions?${params.toString()}`, { signal })
      if (!res.ok) throw new Error(await res.text())
      const data = (await res.json()) as { items: JobExecution[] }
      const nextRows = data.items ?? []
      if (requestId === requestSequence.current && !signal?.aborted) setRows(nextRows)
      return nextRows
    } catch (e) {
      if (signal?.aborted || isAbortError(e)) {
        if (silent) throw e
        return null
      }
      console.error(e)
      if (requestId === requestSequence.current && !silent) setError(t('history.loadFailed'))
      if (silent) throw e
      return null
    } finally {
      if (requestId === requestSequence.current) setLoading(false)
    }
  }, [jobKey, t])

  const loadDurationBaseline = useCallback(async (signal?: AbortSignal) => {
    const requestId = ++baselineRequestSequence.current
    const loadMode = async (isDryRun: boolean) => {
      try {
        const params = buildDurationBaselineParams(jobKey, isDryRun)
        const res = await fetch(`/api/proxy/scheduling/executions?${params.toString()}`, { signal })
        if (!res.ok) throw new Error(await res.text())
        const data = (await res.json()) as { items: JobExecution[] }
        return data.items ?? []
      } catch (e) {
        if (!signal?.aborted && !isAbortError(e)) console.error(e)
        return []
      }
    }

    const [formalRows, dryRunRows] = await Promise.all([loadMode(false), loadMode(true)])
    if (requestId === baselineRequestSequence.current && !signal?.aborted) {
      setDurationBaselineRows([...formalRows, ...dryRunRows])
    }
  }, [jobKey])

  const startForegroundLoad = useCallback((filter: HistoryQuickFilter) => {
    foregroundRequestRef.current?.abort(new DOMException('Execution history request superseded', 'AbortError'))
    const controller = new AbortController()
    foregroundRequestRef.current = controller
    void load(false, filter, controller.signal).finally(() => {
      if (foregroundRequestRef.current === controller) foregroundRequestRef.current = null
    })
  }, [load])

  const startDurationBaselineLoad = useCallback(() => {
    baselineRequestRef.current?.abort(new DOMException('Execution baseline request superseded', 'AbortError'))
    const controller = new AbortController()
    baselineRequestRef.current = controller
    void loadDurationBaseline(controller.signal).finally(() => {
      if (baselineRequestRef.current === controller) baselineRequestRef.current = null
    })
  }, [loadDurationBaseline])

  function onOpenChange(v: boolean) {
    setOpen(v)
    if (v) {
      startForegroundLoad(quickFilterRef.current)
      startDurationBaselineLoad()
    } else {
      foregroundRequestRef.current?.abort(new DOMException('Execution history closed', 'AbortError'))
      foregroundRequestRef.current = null
      baselineRequestRef.current?.abort(new DOMException('Execution history closed', 'AbortError'))
      baselineRequestRef.current = null
      requestSequence.current += 1
      baselineRequestSequence.current += 1
      setLoading(false)
      setDetail(null)
      setDurationBaselineRows([])
    }
  }

  // 仅人工触发记录展示操作人；随当前 30 条历史增量解析，不再在开窗时全量预热组织账号。
  const auditUserIdsKey = useMemo(() => [...new Set(rows
    .filter((row) => row.triggerKind === 1 && row.creatorId)
    .map((row) => row.creatorId as string))]
    .sort()
    .join(','), [rows])

  useEffect(() => {
    let active = true
    const ids = auditUserIdsKey ? auditUserIdsKey.split(',') : []
    if (!open || !auditIdentityVersion || ids.length === 0) {
      return undefined
    }

    void loadAuditUsers(ids, auditIdentityVersion).then((users) => {
      if (active) setAuditUserState({ identityVersion: auditIdentityVersion, users })
    })
    return () => { active = false }
  }, [auditIdentityVersion, auditUserIdsKey, open])

  useEffect(() => () => {
    foregroundRequestRef.current?.abort(new DOMException('Execution history unmounted', 'AbortError'))
    baselineRequestRef.current?.abort(new DOMException('Execution history unmounted', 'AbortError'))
    requestSequence.current += 1
    baselineRequestSequence.current += 1
  }, [])

  // 执行中反馈（U7）：请求完成 5s 后再轮询，保证单飞；终态、后台、离线或关闭时立即停止。
  const hasRunning = rows.some(r => r.status === 0)
  useSerializedPolling({
    enabled: open && hasRunning,
    key: `${jobKey}:${quickFilter}`,
    runImmediately: false,
    intervalMs: 5000,
    attemptTimeoutMs: 15000,
    retry: { initialMs: 10000, maxMs: 60000, multiplier: 2, jitterRatio: 0.1 },
    pauseWhenHidden: true,
    pauseWhenOffline: true,
    task: async ({ signal }) => {
      const nextRows = await load(true, quickFilterRef.current, signal)
      return nextRows?.some(row => row.status === 0) ? 'continue' : 'stop'
    },
  })

  return (
    <>
      <Button
        ref={historyTriggerRef}
        size="sm"
        variant="outline"
        className="h-7"
        disabled={disabled}
        onClick={() => onOpenChange(true)}
      >
        <History className="mr-1 size-3.5" />{t('history.button')}
      </Button>
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent
          className="sm:max-w-4xl"
          aria-describedby={undefined}
          onCloseAutoFocus={(event) => {
            const target = historyTriggerRef.current
            if (!target?.isConnected) return
            event.preventDefault()
            target.focus()
          }}
        >
          <DialogHeader>
            <DialogTitle>{t('history.dialogTitleTemplate', { name: jobName })}</DialogTitle>
          </DialogHeader>
          <SegmentedControl
            className="w-fit"
            aria-label={t('history.quickFiltersLabel')}
            value={quickFilter}
            onValueChange={(value) => {
              quickFilterRef.current = value
              setQuickFilter(value)
              startForegroundLoad(value)
            }}
            options={[
              { value: 'all', label: tc('all') },
              { value: 'succeeded', label: t('statusLabels.succeeded') },
              { value: 'failed', label: t('statusLabels.failed') },
              { value: 'skipped', label: t('statusLabels.skipped') },
              { value: 'dryRun', label: t('executions.filters.dryRunOnly') },
            ]}
          />
          <div className="max-h-[60vh] min-h-[8rem] overflow-auto rounded-lg border">
            {loading ? (
              <div className="text-muted-foreground flex items-center justify-center py-12">
                <Loader2 className="mr-2 size-4 animate-spin" />{t('history.loading')}
              </div>
            ) : error ? (
              <div className="text-destructive py-12 text-center text-sm">{error}</div>
            ) : rows.length === 0 ? (
              <div className="text-muted-foreground py-12 text-center text-sm">{t('history.empty')}</div>
            ) : (
              <table className="w-full table-fixed text-sm">
                <thead className="bg-muted text-muted-foreground sticky top-0 z-10 text-left text-xs">
                  <tr className="border-b">
                    <th className="w-[150px] px-3 py-2 font-medium whitespace-nowrap">{t('history.columns.startedAt')}</th>
                    <th className="w-[200px] px-3 py-2 font-medium whitespace-nowrap">{t('history.columns.trigger')}</th>
                    <th className="w-[80px] px-3 py-2 font-medium whitespace-nowrap">{t('history.columns.status')}</th>
                    <th className="w-[80px] px-3 py-2 text-right font-medium whitespace-nowrap">{t('history.columns.duration')}</th>
                    <th className="px-3 py-2 font-medium whitespace-nowrap">{t('history.columns.summaryOrError')}</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => {
                    const preview = r.error ?? r.summary
                    const durationAnomaly = isDurationAnomaly(
                      r.durationMs,
                      r.isDryRun ? durationMedians.dryRun : durationMedians.formal,
                    )
                    const operatorName = r.creatorId
                      ? (auditUsers[r.creatorId]?.name ?? tc('unknownUser'))
                      : tc('unknownUser')
                    const triggerLabel = triggerText(r.triggerKind, t)
                    return (
                      <tr key={r.id} className="hover:bg-muted/20 border-b last:border-0">
                        <td className="px-3 py-2 font-mono text-xs whitespace-nowrap">{fmt(r.startedAt)}</td>
                        <td className="overflow-hidden px-3 py-2 text-xs">
                          <div className="min-w-0 space-y-0.5">
                            <div className="flex min-w-0 items-center gap-1 whitespace-nowrap">
                              <span className="shrink-0">{triggerLabel}</span>
                              {r.triggerKind === 1 && (
                                <span className="text-muted-foreground min-w-0 flex-1 truncate" title={operatorName}>
                                  · {operatorName}
                                </span>
                              )}
                            </div>
                            {(r.isDryRun || r.argsJson) && (
                              <div className="flex flex-wrap gap-x-1 whitespace-nowrap">
                                {r.isDryRun && <span className="text-warning">{t('history.dryRunSuffix')}</span>}
                                {r.argsJson && <span className="text-muted-foreground">{t('history.withArgsSuffix')}</span>}
                              </div>
                            )}
                          </div>
                        </td>
                        <td className="px-3 py-2 whitespace-nowrap"><StatusBadge status={r.status} /></td>
                        <td
                          className={`px-3 py-2 text-right text-xs whitespace-nowrap tabular-nums ${durationAnomaly ? 'font-medium text-warning ' : 'text-muted-foreground'}`}
                          title={durationAnomaly ? t('history.durationAnomalyTitle') : undefined}
                        >
                          {durationText(r.durationMs)}
                        </td>
                        <td className="max-w-0 px-3 py-2 text-xs">
                          {preview ? (
                            <button
                              type="button"
                              className={`block w-full truncate text-left ${r.error ? 'cursor-pointer text-destructive underline-offset-4 hover:underline' : dataTableLinkClass}`}
                              title={t('executions.viewDetailTitle')}
                              onClick={(event) => {
                                detailTriggerRef.current = event.currentTarget
                                setDetail(r)
                              }}
                            >
                              {preview}
                            </button>
                          ) : <span className="text-muted-foreground">—</span>}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            )}
          </div>
          <div className="flex justify-end">
            <Button asChild variant="link" size="sm" className="h-auto px-0">
              <Link href={`/system/scheduled-jobs/executions?jobKey=${encodeURIComponent(jobKey)}`}>
                {t('history.viewFullHistory')}<ArrowRight className="size-3.5" />
              </Link>
            </Button>
          </div>
          <ExecutionDetailDialog
            detail={detail}
            onClose={() => setDetail(null)}
            jobName={jobName}
            returnFocusRef={detailTriggerRef}
          />
        </DialogContent>
      </Dialog>
    </>
  )
}
