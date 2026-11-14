'use client'

import { useCallback, useMemo, useState } from 'react'
import { useLocale, useTranslations } from 'next-intl'
import { type ColumnDef, type SortingState } from '@tanstack/react-table'
import { Loader2, Search } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { DatePicker } from '@/components/ui/date-picker'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { Dialog } from '@/components/ui/dialog'
import { ScrollableDialogContent } from '@/components/ui/scrollable-dialog'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { dataTableLinkClass } from '@/components/data-table/data-table-link-cell'
import { PageHeader } from '@/components/data-table/page-header'
import { type PagedResult } from '@/lib/api-client'
import { useCrudResource } from '@/lib/crud/use-crud-resource'
import { useUrlListState } from '@/lib/crud/use-url-list-state'
import type { AuditLogDto } from '@/lib/api/types'

export type AuditLog = AuditLogDto

const HTTP_METHODS = ['GET', 'POST', 'PUT', 'DELETE', 'PATCH']

/** 「导出全部」的行数硬上限（理由见下方 useCrudResource 的 exportMaxRows 注释）。 */
const AUDIT_LOG_EXPORT_MAX_ROWS = 20_000

function methodVariant(method: string | null | undefined): 'default' | 'secondary' | 'destructive' | 'outline' {
  switch (method) {
    case 'GET': return 'secondary'
    case 'DELETE': return 'destructive'
    default: return 'outline'
  }
}

function statusVariant(code: number | null | undefined): 'success' | 'warning' | 'destructive' | 'secondary' {
  if (code == null) return 'secondary'
  if (code < 300) return 'success'
  if (code < 500) return 'warning'
  return 'destructive'
}

function formatDateTime(value: string | null | undefined, locale: string): string {
  return value ? new Date(value).toLocaleString(locale) : '-'
}

function prettyJson(value: string | null | undefined): string {
  if (!value) return ''
  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    return value
  }
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 text-sm">{children}</dd>
    </div>
  )
}

export function AuditLogsClient({
  initial,
  initialStartTime = '', initialEndTime = '', initialUrl = '',
  initialUserName = '', initialMethod = '', initialOnlyException = false,
  initialPage = 0, initialPageSize = 15, initialSorting = [],
}: {
  initial: PagedResult<AuditLog>
  initialStartTime?: string; initialEndTime?: string; initialUrl?: string
  initialUserName?: string; initialMethod?: string; initialOnlyException?: boolean
  initialPage?: number; initialPageSize?: number; initialSorting?: SortingState
}) {
  const t = useTranslations('System.auditLogs')
  const tc = useTranslations('Common')
  const locale = useLocale()
  // 多条件筛选（含文本框）经 useUrlListState 镜像到 URL，切走再切回/刷新可恢复；取数仍靠点「查询」（queryVersion）。
  // onlyException 是布尔，useUrlListState 只存字符串（''/'true'），UI 用时转布尔。初值均来自 page.tsx 的 searchParams。
  const [filters, setFilters] = useUrlListState({
    startTime: initialStartTime,
    endTime: initialEndTime,
    url: initialUrl,
    userName: initialUserName,
    method: initialMethod,
    onlyException: initialOnlyException ? 'true' : '',
  })
  const onlyException = filters.onlyException === 'true'
  // queryVersion 只驱动重取，不进 URL（输入实时进 URL、切回可恢复，但取数仍靠点「查询」）。
  const [queryVersion, setQueryVersion] = useState(0)
  const [detail, setDetail] = useState<AuditLog | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)

  // 服务端分页：翻页下推后端。多条件含文本框，故保留「查询」按钮显式触发（queryVersion 递增 → 重取），
  // 不随每次输入即时查；该接口无统一 filter 关键字，故关全局搜索框。
  const list = useCrudResource<AuditLog>({
    endpoint: 'audit-logs',
    initial,
    errorTitle: t('errorTitle'),
    urlSync: true,
    initialSorting,
    initialPage,
    pageSize: initialPageSize,
    // 「导出全部」行数硬上限。AbpAuditLogs **每个请求一行**，按默认 90 天保留（AuditLogCleanup:RetainDays）
    // 稳态可达百万级；而 fetchAll 是 OFFSET 递增分块拉取、后端累计扫描约 O(N²/2)，不设上限时一次点击
    // 就能把库拖垮。审计场景的真实诉求是「导某段时间/某用户的操作记录」，先用页面上的时间与条件筛选
    // 缩小范围即可；达上限会自动提示。见 docs/data-scale-lessons.md 的同类隐患台账。
    exportMaxRows: AUDIT_LOG_EXPORT_MAX_ROWS,
    extraDeps: [queryVersion],
    buildParams: (p) => {
      if (filters.startTime) p.set('StartTime', filters.startTime)
      if (filters.endTime) p.set('EndTime', `${filters.endTime} 23:59:59`)
      if (filters.url) p.set('Url', filters.url)
      if (filters.userName) p.set('UserName', filters.userName)
      if (filters.method) p.set('HttpMethod', filters.method)
      if (onlyException) p.set('HasException', 'true')
    },
  })

  function runQuery() {
    setQueryVersion((v) => v + 1)
    list.setPage(0)
  }

  // 列表是轻量行（无异常全文/动作明细），打开详情时按 id 取完整记录
  const openDetail = useCallback(async (row: AuditLog) => {
    setDetail(row)
    setDetailLoading(true)
    try {
      const res = await fetch(`/api/proxy/audit-logs/${row.id}`)
      if (res.ok) setDetail(await res.json())
    } catch (e) {
      console.error(e)
    } finally {
      setDetailLoading(false)
    }
  }, [])

  const columns = useMemo<ColumnDef<AuditLog, unknown>[]>(() => [
    {
      accessorKey: 'executionTime',
      size: 150,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.time')} />,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{formatDateTime(row.original.executionTime, locale)}</span>,
    },
    {
      accessorKey: 'httpMethod',
      size: 80,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.method')} />,
      meta: { filterVariant: 'select', filterOptions: HTTP_METHODS.map((m) => ({ value: m, label: m })) },
      cell: ({ row }) => <Badge variant={methodVariant(row.original.httpMethod)}>{row.original.httpMethod ?? '-'}</Badge>,
    },
    {
      accessorKey: 'url',
      header: ({ column }) => <DataTableColumnHeader column={column} title="URL" />,
      cell: ({ row }) => (
        <button
          className={`${dataTableLinkClass} block max-w-96 truncate text-left font-mono text-xs`}
          title={row.original.url ?? undefined}
          onClick={() => void openDetail(row.original)}
        >
          {row.original.url ?? '-'}
        </button>
      ),
    },
    {
      accessorKey: 'httpStatusCode',
      size: 80,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.statusCode')} />,
      cell: ({ row }) => <Badge variant={statusVariant(row.original.httpStatusCode)}>{row.original.httpStatusCode ?? '-'}</Badge>,
    },
    {
      accessorKey: 'userName',
      size: 100,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.user')} />,
      cell: ({ row }) => <span className="text-sm">{row.original.userName ?? <span className="text-muted-foreground">{t('anonymous')}</span>}</span>,
    },
    {
      accessorKey: 'executionDuration',
      size: 90,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.duration')} />,
      cell: ({ row }) => {
        const ms = row.original.executionDuration
        return <span className={`text-sm ${ms >= 3000 ? 'font-medium text-destructive' : ms >= 1000 ? 'text-warning' : ''}`}>{ms}ms</span>
      },
    },
    {
      accessorKey: 'clientIpAddress',
      size: 120,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.clientIp')} />,
      cell: ({ row }) => <span className="font-mono text-xs text-muted-foreground">{row.original.clientIpAddress ?? '-'}</span>,
    },
    {
      accessorKey: 'hasException',
      size: 70,
      // DTO 派生列（实体侧只有 Exceptions 文本），后端排序白名单无对应键 → 隐藏排序 UI，
      // 避免「列头能点、后端静默回退」的界面撒谎（见 AuditLogAppService.SortWhitelist）。
      enableSorting: false,
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columns.exception')} />,
      meta: { filterVariant: 'select', filterOptions: [{ value: true, label: t('exceptionYes') }, { value: false, label: t('exceptionNo') }] },
      cell: ({ row }) => (row.original.hasException ? <Badge variant="destructive">{t('exceptionBadge')}</Badge> : <span className="text-xs text-muted-foreground">—</span>),
    },
  ], [t, locale, openDetail])

  return (
    <>
      <PageHeader title={t('pageTitle')} description={t('pageDescriptionTemplate', { count: list.total })} />

      <div className="mb-4 flex flex-wrap items-end gap-3 rounded-lg border bg-card p-3">
        <div className="space-y-1"><Label className="text-xs">{t('filters.startTime')}</Label><DatePicker className="w-[145px]" value={filters.startTime} onChange={(v) => setFilters({ startTime: v })} /></div>
        <div className="space-y-1"><Label className="text-xs">{t('filters.endTime')}</Label><DatePicker className="w-[145px]" value={filters.endTime} onChange={(v) => setFilters({ endTime: v })} /></div>
        <div className="space-y-1">
          <Label className="text-xs">{t('filters.method')}</Label>
          <Select value={filters.method || 'all'} onValueChange={(v) => setFilters({ method: v === 'all' ? '' : v })}>
            <SelectTrigger className="h-8 w-[100px]"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">{tc('all')}</SelectItem>
              {HTTP_METHODS.map((m) => <SelectItem key={m} value={m}>{m}</SelectItem>)}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1"><Label className="text-xs">{t('filters.urlContains')}</Label><Input className="h-8 w-[200px]" placeholder="/api/v1/..." value={filters.url} onChange={(e) => setFilters({ url: e.target.value })} onKeyDown={(e) => { if (e.key === 'Enter') runQuery() }} /></div>
        <div className="space-y-1"><Label className="text-xs">{t('filters.userName')}</Label><Input className="h-8 w-[120px]" placeholder="admin" value={filters.userName} onChange={(e) => setFilters({ userName: e.target.value })} onKeyDown={(e) => { if (e.key === 'Enter') runQuery() }} /></div>
        <div className="flex h-8 items-center gap-2">
          <Switch id="only-exception" checked={onlyException} onCheckedChange={(v) => setFilters({ onlyException: v ? 'true' : '' })} />
          <Label htmlFor="only-exception" className="cursor-pointer text-xs">{t('filters.onlyException')}</Label>
        </div>
        <Button className="h-8" onClick={runQuery} disabled={list.loading}>
          {list.loading ? <Loader2 className="mr-1.5 size-3.5 animate-spin" /> : <Search className="mr-1.5 size-3.5" />}{t('queryButton')}
        </Button>
      </div>

      <DataTable
        columns={columns}
        data={list.rows}
        {...list.tableProps}
        enableSearch={false}
        exportFileName="audit-logs.csv"
        tableKey="audit-logs"
      />

      <Dialog open={!!detail} onOpenChange={(open) => { if (!open) setDetail(null) }}>
        {detail && (
          <ScrollableDialogContent
            title={t('detail.titleTemplate', { method: detail.httpMethod ?? '', url: detail.url ?? '' })}
            description={t('detail.correlationIdTemplate', { id: detail.correlationId ?? '-' })}
            className="sm:max-w-3xl"
            footer={<div className="flex justify-end"><Button variant="outline" onClick={() => setDetail(null)}>{tc('close')}</Button></div>}
          >
            <div className="space-y-4 p-6">
              <dl className="grid grid-cols-2 gap-x-6 gap-y-3 sm:grid-cols-3">
                <Field label={t('columns.statusCode')}><Badge variant={statusVariant(detail.httpStatusCode)}>{detail.httpStatusCode ?? '-'}</Badge></Field>
                <Field label={t('columns.user')}>{detail.userName ?? t('anonymous')}</Field>
                <Field label={t('columns.duration')}>{detail.executionDuration}ms</Field>
                <Field label={t('columns.time')}>{formatDateTime(detail.executionTime, locale)}</Field>
                <Field label={t('columns.clientIp')}>{detail.clientIpAddress ?? '-'}</Field>
                <Field label={t('detail.applicationLabel')}>{detail.applicationName ?? '-'}</Field>
              </dl>
              {detail.browserInfo && (
                <div>
                  <p className="mb-1 text-xs text-muted-foreground">User-Agent</p>
                  <p className="break-all rounded-md border bg-muted/40 p-2 font-mono text-xs">{detail.browserInfo}</p>
                </div>
              )}

              {detailLoading ? (
                <div className="flex items-center gap-2 py-3 text-sm text-muted-foreground"><Loader2 className="size-4 animate-spin" />{t('detail.loadingDetail')}</div>
              ) : (
                <>
                  {detail.exceptions && (
                    <div>
                      <p className="mb-1 text-xs text-muted-foreground">{t('exceptionBadge')}</p>
                      <pre className="max-h-64 overflow-auto whitespace-pre-wrap rounded-md border border-destructive/30 bg-destructive/5 p-3 font-mono text-xs text-destructive">{detail.exceptions}</pre>
                    </div>
                  )}
                  {detail.actions && detail.actions.length > 0 && (
                    <div>
                      <p className="mb-2 text-xs font-medium text-muted-foreground">{t('detail.actionsTemplate', { count: detail.actions.length })}</p>
                      <ol className="space-y-3">
                        {detail.actions.map((a, i) => (
                          <li key={i} className="rounded-lg border p-3">
                            <div className="flex flex-wrap items-center gap-2">
                              <span className="font-mono text-xs font-medium">{a.serviceName?.split('.').pop()}.{a.methodName}</span>
                              <span className="ml-auto text-xs text-muted-foreground">{a.executionDuration}ms</span>
                            </div>
                            {a.parameters && a.parameters !== '{}' && (
                              <pre className="mt-2 max-h-44 overflow-auto rounded-md border bg-muted/40 p-2 font-mono text-xs">{prettyJson(a.parameters)}</pre>
                            )}
                          </li>
                        ))}
                      </ol>
                    </div>
                  )}
                </>
              )}
            </div>
          </ScrollableDialogContent>
        )}
      </Dialog>
    </>
  )
}
