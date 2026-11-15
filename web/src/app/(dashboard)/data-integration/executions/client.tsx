'use client'

import { useCallback, useMemo, useRef, useState } from 'react'
import { useLocale, useTranslations } from 'next-intl'
import { useRouter } from 'next/navigation'
import { type ColumnDef, type SortingState } from '@tanstack/react-table'
import { AlertTriangle, ExternalLink, Eye, MoreHorizontal, ShieldCheck } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { StatusBadge, type StatusTone } from '@/components/ui/status-badge'
import { Button } from '@/components/ui/button'
import { IconButton } from '@/components/ui/icon-button'
import { Label } from '@/components/ui/label'
import { DatePicker } from '@/components/ui/date-picker'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Dialog } from '@/components/ui/dialog'
import { ScrollableDialogContent } from '@/components/ui/scrollable-dialog'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { toast } from '@/components/ui/use-toast'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { primaryColumn } from '@/components/data-table/column-factories'
import { PageHeader } from '@/components/data-table/page-header'
import { type PagedResult } from '@/lib/api-client'
import { getApiErrorMessage } from '@/lib/api-error'
import { type PipelineExecutionDto } from '@/lib/api/types'
import { useCrudResource } from '@/lib/crud/use-crud-resource'
import { useUrlListState } from '@/lib/crud/use-url-list-state'
import { classifyEtlExecutionPayload } from '../pipelines/execution-recovery'
import { useEtlExecutionRecovery } from '../pipelines/use-etl-execution-recovery'
import { ExecutionEvidenceCard } from './execution-evidence-card'
import { ExecutionTargetSummary } from './execution-target-summary'
import {
  OutcomeResolutionDialog,
  type OutcomeResolutionSubmission,
} from './outcome-resolution-dialog'
import {
  clearSettledEtlExecutionRecovery,
  isExpectedOutcomeResolutionPayload,
} from './outcome-resolution'

const OUTCOME_UNKNOWN_STATUS = 5
const RECEIPT_RECONCILIATION_STRATEGY = 'target-receipt-v1'
const MANUAL_RECONCILIATION_STRATEGY = 'manual-v0'

export type PipelineExecution = PipelineExecutionDto

// 与后端 ExecutionStatus 枚举一致：Pending=0 Running=1 Succeeded=2 Failed=3
// Cancelled=4 OutcomeUnknown=5。
// 模块级常量含 UI 文案，改为由持有 useTranslations 的组件调用的工厂函数（纯函数不能直接调 hook）。
function getStatusMap(t: ReturnType<typeof useTranslations>): Record<number, { label: string; tone: StatusTone }> {
  return {
    0: { label: t('statusPending'), tone: 'draft' },
    1: { label: t('statusRunning'), tone: 'info' },
    2: { label: t('statusSucceeded'), tone: 'success' },
    3: { label: t('statusFailed'), tone: 'danger' },
    4: { label: t('statusCancelled'), tone: 'neutral' },
    5: { label: t('statusOutcomeUnknown'), tone: 'warning' },
  }
}

function getTriggerMap(t: ReturnType<typeof useTranslations>): Record<number, string> {
  return { 0: t('triggerManual'), 1: t('triggerScheduled') }
}

// 日期展示格式随平台语言（批次 3f 口径），locale 由持有 useLocale 的组件传入。
function formatDateTime(value: string | null, locale: string): string {
  return value ? new Date(value).toLocaleString(locale) : '-'
}

function formatDuration(seconds: number | null): string {
  if (seconds == null) return '-'
  if (seconds < 60) return `${seconds.toFixed(1)}s`
  return `${Math.floor(seconds / 60)}m${Math.round(seconds % 60)}s`
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 text-sm">{children}</dd>
    </div>
  )
}

function isOutcomeUnknown(execution: PipelineExecution): boolean {
  return Number(execution.status) === OUTCOME_UNKNOWN_STATUS
}

function canVerifyReceipt(execution: PipelineExecution, allowed: boolean): boolean {
  return allowed
    && isOutcomeUnknown(execution)
    && execution.reconciliationStrategy === RECEIPT_RECONCILIATION_STRATEGY
    && execution.receiptVerificationEligible === true
}

function canResolveManually(execution: PipelineExecution, allowed: boolean): boolean {
  if (!allowed || !isOutcomeUnknown(execution)) return false
  if (execution.reconciliationStrategy === MANUAL_RECONCILIATION_STRATEGY) return true
  return execution.reconciliationStrategy === RECEIPT_RECONCILIATION_STRATEGY
    && execution.receiptVerificationEligible === true
}

export function PipelineExecutionsClient({
  initial,
  initialStatus = '', initialStartedFrom = '', initialStartedTo = '',
  initialPage = 0, initialPageSize = 15, initialSorting = [],
  canResolveOutcome = false,
  canVerifyTargetReceipt = false,
}: {
  initial: PagedResult<PipelineExecution>
  initialStatus?: string; initialStartedFrom?: string; initialStartedTo?: string
  initialPage?: number; initialPageSize?: number; initialSorting?: SortingState
  canResolveOutcome?: boolean
  canVerifyTargetReceipt?: boolean
}) {
  const t = useTranslations('DataIntegration.execution')
  const rc = useTranslations('DataIntegration.common')
  const tc = useTranslations('Common')
  const locale = useLocale()
  const executionRecovery = useEtlExecutionRecovery()
  const {
    ready: recoveryReady,
    retainRunId,
    clearRunId,
    verifyRun,
  } = executionRecovery
  const recoveryStatus = executionRecovery.status ?? (recoveryReady ? 'ready' : 'loading')
  const STATUS_MAP = useMemo(() => getStatusMap(t), [t])
  const TRIGGER_MAP = useMemo(() => getTriggerMap(t), [t])
  const router = useRouter()
  // 页面级筛选（状态 / 开始时间范围）经 useUrlListState 镜像到 URL，切走再切回/刷新可恢复；
  // page/pageSize 由 useCrudResource(urlSync) 同样镜像。初值均来自 page.tsx 的 searchParams。
  const [filters, setFilters] = useUrlListState({
    status: initialStatus,
    startedFrom: initialStartedFrom,
    startedTo: initialStartedTo,
  })
  const [detail, setDetail] = useState<PipelineExecution | null>(null)
  const [reconcileTarget, setReconcileTarget] = useState<PipelineExecution | null>(null)
  const [resolving, setResolving] = useState(false)
  const resolvingRef = useRef(false)
  const [verifyingExecutionId, setVerifyingExecutionId] = useState<string | null>(null)
  const verifyingExecutionRef = useRef<string | null>(null)

  // 服务端分页：翻页 + 状态/时间范围筛选下推后端；该接口无统一 filter 关键字，故关全局搜索框只留翻页。
  const list = useCrudResource<PipelineExecution>({
    endpoint: 'pipeline-executions',
    initial,
    errorTitle: t('loadFailedTitle'),
    urlSync: true,
    initialSorting,
    initialPage,
    pageSize: initialPageSize,
    extraDeps: [filters.status, filters.startedFrom, filters.startedTo],
    buildParams: (p) => {
      if (filters.status) p.set('Status', filters.status)
      if (filters.startedFrom) p.set('StartedFrom', filters.startedFrom)
      if (filters.startedTo) p.set('StartedTo', `${filters.startedTo} 23:59:59`)
    },
  })
  const { reloadAndRefresh } = list

  const openReconciliation = useCallback((execution: PipelineExecution) => {
    setDetail(null)
    setReconcileTarget(execution)
  }, [])

  async function handleResolveOutcome(submission: OutcomeResolutionSubmission) {
    if (
      !reconcileTarget
      || resolvingRef.current
      || recoveryStatus !== 'ready'
    ) return
    const target = reconcileTarget
    const submittedResolution = submission.resolution
    let responseReceived = false
    let successfulHttpResponseReceived = false
    let requestStarted = false
    resolvingRef.current = true
    setResolving(true)
    try {
      // 对账响应也可能在服务端已收敛后丢失；先在 principal 锁下保留原执行 ID，
      // 失败时只读 GET 核验，绝不重放 execute。无法安全持久时不提交裁决。
      if (!await retainRunId(
        target.pipelineId,
        target.id,
        'outcome-unknown',
      )) {
        toast({ variant: 'destructive', title: rc('recoveryStorageUnavailableTitle'), description: rc('recoveryStorageUnavailableDesc') })
        return
      }

      requestStarted = true
      const res = await fetch(`/api/proxy/pipeline-executions/${target.id}/resolve-outcome`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          resolution: Number(submittedResolution),
          processedRows: submission.processedRows,
          verificationNote: submission.evidenceReference,
        }),
      })
      responseReceived = true
      if (!res.ok) throw new Error(await res.text())
      successfulHttpResponseReceived = true

      const resolvedBody: unknown = await res.json()
      if (!isExpectedOutcomeResolutionPayload(
        target.pipelineId,
        target.id,
        submittedResolution,
        resolvedBody,
      )) {
        throw new Error(t('resolveResponseUnknownDesc'))
      }

      const cleanupResult = await clearSettledEtlExecutionRecovery(target.pipelineId, target.id, clearRunId, verifyRun)
      setReconcileTarget(null)
      setDetail(null)
      toast(cleanupResult === 'unavailable'
        ? { variant: 'warning', title: rc('recoveryCleanupUnavailableTitle'), description: rc('recoveryCleanupUnavailableDesc') }
        : { variant: 'success', title: submittedResolution === '0' ? t('resolveAppliedSuccessTitle') : t('resolveNotAppliedSuccessTitle') })
    } catch (error) {
      void verifyRun(target.pipelineId, target.id)
      console.error('[pipeline-execution/resolve-outcome]', error)
      toast({
        variant: 'destructive',
        title: t('resolveFailedTitle'),
        description: responseReceived && !successfulHttpResponseReceived
          ? getApiErrorMessage(error, t('resolveResponseUnknownDesc'))
          : t('resolveResponseUnknownDesc'),
      })
    } finally {
      resolvingRef.current = false
      setResolving(false)
      if (requestStarted) void reloadAndRefresh()
    }
  }

  const verifyTargetReceipt = useCallback(async (target: PipelineExecution) => {
    if (
      verifyingExecutionRef.current !== null
      || !canVerifyReceipt(target, canVerifyTargetReceipt)
    ) return
    if (recoveryStatus === 'unavailable') {
      toast({ variant: 'destructive', title: rc('recoveryStorageUnavailableTitle'), description: rc('recoveryStorageUnavailableDesc') })
      return
    }
    if (!recoveryReady || recoveryStatus !== 'ready') return

    verifyingExecutionRef.current = target.id
    setVerifyingExecutionId(target.id)
    let responseReceived = false
    try {
      if (!await retainRunId(
        target.pipelineId,
        target.id,
        'outcome-unknown',
      )) {
        toast({ variant: 'destructive', title: rc('recoveryStorageUnavailableTitle'), description: rc('recoveryStorageUnavailableDesc') })
        return
      }

      const response = await fetch(
        `/api/proxy/pipeline-executions/${target.id}/verify-target-receipt`,
        { method: 'POST' },
      )
      responseReceived = true
      if (!response.ok) throw new Error(await response.text())

      const body: unknown = await response.json()
      const succeeded = classifyEtlExecutionPayload(target.pipelineId, target.id, body) === 'terminal'
        && typeof body === 'object'
        && body !== null
        && 'status' in body
        && body.status === 2
      if (!succeeded) throw new Error(t('verifyTargetReceiptResponseUnknownDesc'))

      const cleanupResult = await clearSettledEtlExecutionRecovery(target.pipelineId, target.id, clearRunId, verifyRun)
      setDetail(null)
      toast(cleanupResult === 'unavailable'
        ? { variant: 'warning', title: rc('recoveryCleanupUnavailableTitle'), description: rc('recoveryCleanupUnavailableDesc') }
        : { variant: 'success', title: t('verifyTargetReceiptSuccessTitle') })
      await reloadAndRefresh()
    } catch (error) {
      void verifyRun(target.pipelineId, target.id)
      console.error('[pipeline-execution/verify-target-receipt]', error)
      toast({
        variant: 'destructive',
        title: t('verifyTargetReceiptFailedTitle'),
        description: responseReceived
          ? getApiErrorMessage(error, t('verifyTargetReceiptResponseUnknownDesc'))
          : t('verifyTargetReceiptResponseUnknownDesc'),
      })
    } finally {
      verifyingExecutionRef.current = null
      setVerifyingExecutionId(null)
    }
  }, [
    canVerifyTargetReceipt,
    clearRunId,
    reloadAndRefresh,
    rc,
    recoveryReady,
    recoveryStatus,
    retainRunId,
    t,
    verifyRun,
  ])

  const columns = useMemo<ColumnDef<PipelineExecution, unknown>[]>(() => [
    // 主字段列：点管道名开执行详情（此前是本页手写的孤例，已收敛到工厂统一样式与选中态处理）。
    primaryColumn<PipelineExecution>('pipelineName', t('columnPipelineName'), {
      onOpen: setDetail,
      format: (row) => row.pipelineName ?? '-',
    }),
    {
      accessorKey: 'triggerType',
      size: 80,
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('triggerType')} />,
      meta: { filterVariant: 'select', filterOptions: [{ value: 0, label: t('triggerManual') }, { value: 1, label: t('triggerScheduled') }] },
      cell: ({ row }) => <Badge variant="outline">{TRIGGER_MAP[row.original.triggerType] ?? rc('unknown')}</Badge>,
    },
    {
      accessorKey: 'startedAt',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('startedAt')} />,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{formatDateTime(row.original.startedAt, locale)}</span>,
    },
    {
      accessorKey: 'durationSeconds',
      size: 80,
      enableSorting: false,
      meta: { numeric: true },
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columnDuration')} />,
      cell: ({ row }) => <span className="text-sm tabular-nums">{formatDuration(row.original.durationSeconds)}</span>,
    },
    {
      accessorKey: 'processedRows',
      size: 100,
      meta: { numeric: true },
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('processedRows')} />,
      cell: ({ row }) => <span className="text-sm tabular-nums">{row.original.processedRows?.toLocaleString(locale) ?? '-'}</span>,
    },
    {
      accessorKey: 'status',
      size: 90,
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('status')} />,
      meta: { filterVariant: 'select', filterOptions: Object.entries(STATUS_MAP).map(([value, { label }]) => ({ value: Number(value), label })) },
      cell: ({ row }) => {
        const s = STATUS_MAP[Number(row.original.status)] ?? { label: rc('unknown'), tone: 'draft' as const }
        return <StatusBadge tone={s.tone}>{s.label}</StatusBadge>
      },
    },
    {
      accessorKey: 'errorMessage',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('errorMessage')} />,
      cell: ({ row }) => (
        <span className="block max-w-56 truncate text-sm text-destructive" title={row.original.errorMessage ?? undefined}>
          {row.original.errorMessage ?? '-'}
        </span>
      ),
    },
    {
      id: 'actions',
      size: 50,
      enableHiding: false,
      cell: ({ row }) => (
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <IconButton label={tc('actions')} className="size-7"><MoreHorizontal className="size-4" /></IconButton>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem onClick={() => setDetail(row.original)}><Eye className="mr-2 size-4" />{t('viewDetailMenuItem')}</DropdownMenuItem>
            {canVerifyReceipt(row.original, canVerifyTargetReceipt) && (
              <DropdownMenuItem
                disabled={verifyingExecutionId !== null || recoveryStatus === 'loading'}
                onClick={() => void verifyTargetReceipt(row.original)}
              >
                <ShieldCheck className="mr-2 size-4" />{t('verifyTargetReceiptMenuItem')}
              </DropdownMenuItem>
            )}
            {canResolveManually(row.original, canResolveOutcome) && (
              <DropdownMenuItem onClick={() => openReconciliation(row.original)}>
                <ShieldCheck className="mr-2 size-4" />{t('resolveOutcomeMenuItem')}
              </DropdownMenuItem>
            )}
            <DropdownMenuItem onClick={() => router.push('/data-integration/pipelines')}><ExternalLink className="mr-2 size-4" />{t('gotoPipelinesMenuItem')}</DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      ),
    },
  ], [
    STATUS_MAP,
    TRIGGER_MAP,
    canResolveOutcome,
    canVerifyTargetReceipt,
    locale,
    openReconciliation,
    rc,
    recoveryStatus,
    router,
    t,
    tc,
    verifyTargetReceipt,
    verifyingExecutionId,
  ])

  const detailStatus = detail ? (STATUS_MAP[Number(detail.status)] ?? { label: rc('unknown'), tone: 'draft' as const }) : null

  return (
    <>
      <PageHeader title={t('pageTitle')} description={t('countDescription', { count: list.total })} />

      <div className="mb-4 flex flex-wrap items-end gap-3 rounded-lg border bg-card p-3">
        <div className="space-y-1">
          <Label className="text-xs">{t('statusFilterLabel')}</Label>
          <Select value={filters.status || 'all'} onValueChange={(v) => { setFilters({ status: v === 'all' ? '' : v }); list.setPage(0) }}>
            <SelectTrigger className="h-8 w-[120px]"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">{t('statusAll')}</SelectItem>
              {Object.entries(STATUS_MAP).map(([value, { label }]) => <SelectItem key={value} value={value}>{label}</SelectItem>)}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1"><Label className="text-xs">{t('startedFromLabel')}</Label><DatePicker className="w-[150px]" value={filters.startedFrom} onChange={(v) => { setFilters({ startedFrom: v }); list.setPage(0) }} /></div>
        <div className="space-y-1"><Label className="text-xs">{t('startedToLabel')}</Label><DatePicker className="w-[150px]" value={filters.startedTo} onChange={(v) => { setFilters({ startedTo: v }); list.setPage(0) }} /></div>
      </div>

      <DataTable
        columns={columns}
        data={list.rows}
        {...list.tableProps}
        enableSearch={false}
        exportFileName="pipeline-executions.csv"
        tableKey="pipeline-executions"
      />

      <Dialog open={!!detail} onOpenChange={(open) => { if (!open) setDetail(null) }}>
        {detail && (
          <ScrollableDialogContent
            title={t('detailTitle', { name: detail.pipelineName ?? '-' })}
            description={t('detailIdDesc', { id: detail.id })}
            footer={(
              <div className="flex items-center justify-between gap-3">
                <div className="flex flex-wrap gap-2">
                  {canVerifyReceipt(detail, canVerifyTargetReceipt) && (
                    <Button
                      disabled={verifyingExecutionId !== null || recoveryStatus === 'loading'}
                      onClick={() => void verifyTargetReceipt(detail)}
                    >
                      <ShieldCheck className="mr-2 size-4" />
                      {verifyingExecutionId === detail.id
                        ? t('verifyTargetReceiptProcessingButton')
                        : t('verifyTargetReceiptButton')}
                    </Button>
                  )}
                  {canResolveManually(detail, canResolveOutcome) && (
                    <Button variant="destructive-outline" onClick={() => openReconciliation(detail)}>
                      <ShieldCheck className="mr-2 size-4" />{t('resolveOutcomeButton')}
                    </Button>
                  )}
                </div>
                <Button variant="outline" onClick={() => setDetail(null)}>{tc('close')}</Button>
              </div>
            )}
          >
            <div className="space-y-4 p-6">
              {Number(detail.status) === OUTCOME_UNKNOWN_STATUS && (
                <div className="space-y-3">
                  <div role="alert" className="flex gap-3 rounded-md border border-destructive/40 bg-destructive/10 p-3 text-destructive">
                    <AlertTriangle className="mt-0.5 size-5 shrink-0" />
                    <div className="space-y-1">
                      <p className="text-sm font-semibold">{t('outcomeUnknownAlertTitle')}</p>
                      <p className="text-xs leading-5">{t('outcomeUnknownAlertDesc')}</p>
                    </div>
                  </div>
                  <ExecutionEvidenceCard execution={detail} />
                  {isOutcomeUnknown(detail)
                    && detail.reconciliationStrategy === RECEIPT_RECONCILIATION_STRATEGY
                    && detail.receiptVerificationEligible !== true && (
                    <div role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 p-3 text-xs leading-5 text-destructive">
                      {t('receiptVerificationIneligibleDesc')}
                    </div>
                  )}
                </div>
              )}
              <dl className="grid grid-cols-2 gap-x-6 gap-y-3 sm:grid-cols-3">
                <Field label={tc('status')}>{detailStatus && <StatusBadge tone={detailStatus.tone}>{detailStatus.label}</StatusBadge>}</Field>
                <Field label={tc('triggerType')}>{TRIGGER_MAP[detail.triggerType] ?? rc('unknown')}</Field>
                <Field label={tc('processedRows')}>{detail.processedRows?.toLocaleString(locale) ?? '-'}</Field>
                <Field label={tc('startedAt')}>{formatDateTime(detail.startedAt, locale)}</Field>
                <Field label={tc('finishedAt')}>{formatDateTime(detail.finishedAt, locale)}</Field>
                <Field label={t('columnDuration')}>{formatDuration(detail.durationSeconds)}</Field>
                {(Number(detail.status) === 3 || Number(detail.status) === OUTCOME_UNKNOWN_STATUS) && (
                  <>
                    <Field label={t('errorCodeLabel')}>{detail.errorCode ?? '-'}</Field>
                    <Field label={t('correlationIdLabel')}>
                      <span className="break-all font-mono text-xs">{detail.correlationId ?? '-'}</span>
                    </Field>
                    <Field label={t('exceptionTypeLabel')}>
                      <span className="break-all font-mono text-xs">{detail.exceptionType ?? '-'}</span>
                    </Field>
                  </>
                )}
              </dl>
              {Number(detail.status) !== OUTCOME_UNKNOWN_STATUS && (
                <ExecutionTargetSummary execution={detail} />
              )}
              {detail.errorMessage && (
                <div>
                  <p className="mb-1 text-xs text-muted-foreground">{t('errorMessageLabel')}</p>
                  <pre className="max-h-60 overflow-auto whitespace-pre-wrap rounded-md border border-destructive/30 bg-destructive/5 p-3 font-mono text-xs text-destructive">{detail.errorMessage}</pre>
                </div>
              )}
            </div>
          </ScrollableDialogContent>
        )}
      </Dialog>

      {reconcileTarget && (
        <OutcomeResolutionDialog
          key={reconcileTarget.id}
          target={reconcileTarget}
          resolving={resolving}
          recoveryReady={recoveryReady}
          recoveryStatus={recoveryStatus}
          allowConfirmedApplied={reconcileTarget.reconciliationStrategy === MANUAL_RECONCILIATION_STRATEGY}
          onCancel={() => setReconcileTarget(null)}
          onSubmit={submission => void handleResolveOutcome(submission)}
        />
      )}
    </>
  )
}
