'use client'

import { useMemo, useRef, useState } from 'react'
import { useTranslations } from 'next-intl'
import { type ColumnDef, type SortingState } from '@tanstack/react-table'
import { ArrowLeft, Clock3, X } from 'lucide-react'
import Link from 'next/link'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { DatePicker } from '@/components/ui/date-picker'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { dataTableLinkClass } from '@/components/data-table/data-table-link-cell'
import { PageHeader } from '@/components/data-table/page-header'
import { type PagedResult } from '@/lib/api-client'
import { useCrudResource } from '@/lib/crud/use-crud-resource'
import { useUrlListState } from '@/lib/crud/use-url-list-state'
import { ExecutionDetailDialog } from '../execution-detail-dialog'
import { durationText, fmt, StatusBadge, triggerText, type JobExecution } from '../execution-shared'
import { appendExecutionFilters } from './execution-query'
import type { ScheduledJob } from '../columns'

/** 纯函数不能直接调 hook，筛选下拉选项由持有 useTranslations('System.scheduledJobs') 的调用方传入。 */
function getStatusOptions(t: ReturnType<typeof useTranslations>) {
  return [
    { value: '0', label: t('statusLabels.running') },
    { value: '1', label: t('statusLabels.succeeded') },
    { value: '2', label: t('statusLabels.failed') },
    { value: '3', label: t('statusLabels.interrupted') },
    { value: '4', label: t('statusLabels.skipped') },
  ]
}
function getTriggerOptions(t: ReturnType<typeof useTranslations>) {
  return [
    { value: '0', label: t('triggerLabels.scheduled') },
    { value: '1', label: t('triggerLabels.manual') },
  ]
}

export function ScheduledJobExecutionsClient({
  initial,
  jobs,
  initialJobKey = '', initialStatus = '', initialTriggerKind = '', initialIsDryRun = '',
  initialStartedFrom = '', initialWindowStartedAt = '', initialWindowEndedAt = '', initialStartedTo = '',
  initialPage = 0, initialPageSize = 20, initialSorting = [],
}: {
  initial: PagedResult<JobExecution>
  jobs: ScheduledJob[]
  initialJobKey?: string; initialStatus?: string; initialTriggerKind?: string; initialIsDryRun?: string
  initialStartedFrom?: string; initialWindowStartedAt?: string; initialWindowEndedAt?: string; initialStartedTo?: string
  initialPage?: number; initialPageSize?: number; initialSorting?: SortingState
}) {
  const t = useTranslations('System.scheduledJobs')
  const tc = useTranslations('Common')
  const statusOptions = useMemo(() => getStatusOptions(t), [t])
  const triggerOptions = useMemo(() => getTriggerOptions(t), [t])
  // 页面级筛选（任务/状态/触发方式/演练/时间范围）经 useUrlListState 镜像到 URL；均为下拉/日期，onChange 即时生效
  // （不含文本输入，不用「查询」按钮，见 AGENTS.md「纯下拉/日期筛选则 onChange 即时」约定）。
  const [filters, setFilters] = useUrlListState({
    jobKey: initialJobKey,
    status: initialStatus,
    triggerKind: initialTriggerKind,
    isDryRun: initialIsDryRun,
    startedFrom: initialStartedFrom,
    windowStartedAt: initialWindowStartedAt,
    windowEndedAt: initialWindowEndedAt,
    startedTo: initialStartedTo,
  })
  const [detail, setDetail] = useState<JobExecution | null>(null)
  const detailTriggerRef = useRef<HTMLButtonElement | null>(null)

  const jobNameByKey = useMemo(() => new Map(jobs.map((j) => [j.key, j.displayName])), [jobs])

  const list = useCrudResource<JobExecution>({
    endpoint: 'scheduling/executions',
    initial,
    errorTitle: t('executions.errorTitle'),
    urlSync: true,
    initialSorting,
    initialPage,
    pageSize: initialPageSize,
    extraDeps: [filters.jobKey, filters.status, filters.triggerKind, filters.isDryRun, filters.startedFrom, filters.windowStartedAt, filters.windowEndedAt, filters.startedTo],
    buildParams: (p) => { appendExecutionFilters(p, filters) },
  })

  const columns: ColumnDef<JobExecution, unknown>[] = [
    {
      accessorKey: 'jobKey',
      size: 180,
      meta: { pin: 'left' },
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('executions.columns.job')} />,
      cell: ({ row }) => {
        const key = row.original.jobKey
        return (
          <span className="text-sm font-medium">
            {key ? (jobNameByKey.get(key) ?? key) : <span className="text-muted-foreground">{t('executions.manualQueued')}</span>}
          </span>
        )
      },
    },
    {
      accessorKey: 'triggerKind',
      size: 110,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('executions.columns.trigger')} />,
      cell: ({ row }) => (
        <span className="text-xs whitespace-nowrap">
          {triggerText(row.original.triggerKind, t)}
          {row.original.isDryRun && <span className="ml-1 text-warning">{t('history.dryRunSuffix')}</span>}
          {row.original.argsJson && <span className="text-muted-foreground ml-1">{t('history.withArgsSuffix')}</span>}
        </span>
      ),
    },
    {
      accessorKey: 'status',
      size: 80,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('executions.columns.status')} />,
      cell: ({ row }) => <StatusBadge status={row.original.status} />,
    },
    {
      accessorKey: 'startedAt',
      size: 150,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('executions.columns.startedAt')} />,
      cell: ({ row }) => <span className="text-muted-foreground font-mono text-xs">{fmt(row.original.startedAt)}</span>,
    },
    {
      accessorKey: 'durationMs',
      size: 80,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('executions.columns.duration')} />,
      cell: ({ row }) => <span className="text-sm">{durationText(row.original.durationMs)}</span>,
    },
    {
      accessorKey: 'summary',
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('executions.columns.summaryOrError')} />,
      cell: ({ row }) => {
        const r = row.original
        return (
          <button
            type="button"
            className={`${dataTableLinkClass} block max-w-96 truncate text-left text-xs`}
            title={t('executions.viewDetailTitle')}
            onClick={(event) => {
              detailTriggerRef.current = event.currentTarget
              setDetail(r)
            }}
          >
            {r.error ? <span className="text-destructive">{r.error}</span> : (r.summary ?? <span className="text-muted-foreground">—</span>)}
          </button>
        )
      },
    },
  ]

  return (
    <>
      <PageHeader
        title={t('executions.pageTitle')}
        description={t('executions.pageDescriptionTemplate', { count: list.total })}
        extra={
          <Button asChild variant="outline" size="sm">
            <Link href="/system/scheduled-jobs"><ArrowLeft className="mr-1.5 size-4" />{t('executions.backToControlPage')}</Link>
          </Button>
        }
      />

      <div className="mb-4 flex flex-wrap items-end gap-3 rounded-lg border bg-card p-3">
        <div className="space-y-1">
          <Label className="text-xs">{t('executions.filters.job')}</Label>
          <Select value={filters.jobKey || 'all'} onValueChange={(v) => { setFilters({ jobKey: v === 'all' ? '' : v }); list.setPage(0) }}>
            <SelectTrigger className="h-8 w-[180px]"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">{tc('all')}</SelectItem>
              {jobs.map((j) => <SelectItem key={j.key} value={j.key}>{j.displayName}</SelectItem>)}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1">
          <Label className="text-xs">{t('executions.filters.status')}</Label>
          <Select value={filters.status || 'all'} onValueChange={(v) => { setFilters({ status: v === 'all' ? '' : v }); list.setPage(0) }}>
            <SelectTrigger className="h-8 w-[100px]"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">{tc('all')}</SelectItem>
              {statusOptions.map((o) => <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>)}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1">
          <Label className="text-xs">{t('executions.filters.trigger')}</Label>
          <Select value={filters.triggerKind || 'all'} onValueChange={(v) => { setFilters({ triggerKind: v === 'all' ? '' : v }); list.setPage(0) }}>
            <SelectTrigger className="h-8 w-[100px]"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">{tc('all')}</SelectItem>
              {triggerOptions.map((o) => <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>)}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1">
          <Label className="text-xs">{t('executions.filters.dryRun')}</Label>
          <Select value={filters.isDryRun || 'all'} onValueChange={(v) => { setFilters({ isDryRun: v === 'all' ? '' : v }); list.setPage(0) }}>
            <SelectTrigger className="h-8 w-[100px]"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">{tc('all')}</SelectItem>
              <SelectItem value="true">{t('executions.filters.dryRunOnly')}</SelectItem>
              <SelectItem value="false">{t('executions.filters.formalOnly')}</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1"><Label className="text-xs">{t('executions.filters.startedFrom')}</Label><DatePicker className="w-[145px]" value={filters.startedFrom} onChange={(v) => { setFilters({ startedFrom: v, windowStartedAt: '', windowEndedAt: '' }); list.setPage(0) }} /></div>
        <div className="space-y-1"><Label className="text-xs">{t('executions.filters.startedTo')}</Label><DatePicker className="w-[145px]" value={filters.startedTo} onChange={(v) => { setFilters({ startedTo: v, windowStartedAt: '', windowEndedAt: '' }); list.setPage(0) }} /></div>
        {filters.windowStartedAt && filters.windowEndedAt && (
          <Button
            type="button"
            variant="secondary"
            size="sm"
            className="h-8 gap-1.5"
            title={t('executions.filters.statsWindowTemplate', {
              start: filters.windowStartedAt.replace('T', ' '),
              end: filters.windowEndedAt.replace('T', ' '),
            })}
            onClick={() => { setFilters({ windowStartedAt: '', windowEndedAt: '' }); list.setPage(0) }}
          >
            <Clock3 className="size-3.5" />
            {t('executions.filters.statsWindow')}
            <X className="size-3.5" />
          </Button>
        )}
      </div>

      <DataTable
        columns={columns}
        data={list.rows}
        {...list.tableProps}
        enableSearch={false}
        exportFileName="scheduled-job-executions.csv"
        tableKey="scheduled-job-executions"
      />

      <ExecutionDetailDialog
        detail={detail}
        onClose={() => setDetail(null)}
        jobName={(detail?.jobKey && jobNameByKey.get(detail.jobKey)) || detail?.jobKey || undefined}
        returnFocusRef={detailTriggerRef}
      />
    </>
  )
}
