'use client'

import { useState } from 'react'
import { useLocale, useTranslations } from 'next-intl'
import { type ColumnDef, type RowSelectionState, type SortingState } from '@tanstack/react-table'
import { Eye, Loader2, MoreHorizontal, RotateCcw } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { IconButton } from '@/components/ui/icon-button'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Dialog } from '@/components/ui/dialog'
import { ScrollableDialogContent } from '@/components/ui/scrollable-dialog'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { useToast } from '@/components/ui/use-toast'
import { DataTable } from '@/components/data-table/data-table'
import { DataTableColumnHeader } from '@/components/data-table/data-table-column-header'
import { numberColumn } from '@/components/data-table/column-factories'
import { dataTableLinkClass } from '@/components/data-table/data-table-link-cell'
import { PageHeader } from '@/components/data-table/page-header'
import { type PagedResult } from '@/lib/api-client'
import { submitBatchIds } from '@/lib/crud/batch-chunk'
import { useCrudResource } from '@/lib/crud/use-crud-resource'
import { useUrlListState } from '@/lib/crud/use-url-list-state'

export interface MessageRecord {
  id: string
  topicId: string
  topicName: string | null
  subject: string
  payload: string
  status: number
  retryCount: number
  deliveredAt: string | null
  errorMessage: string | null
  creationTime: string
}

// 一条消息对单个订阅的投递明细（P2 缺陷A 修复后 record.Status 退为聚合视图，逐订阅结果看这里）。
export interface MessageDeliveryAttempt {
  id: string
  subscriptionId: string
  subscriptionName: string
  status: number
  retryCount: number
  lastError: string | null
  deliveredAt: string | null
  creationTime: string
}

// 与后端 MessageStatus 枚举一致：Pending=0 Delivered=1 Failed=2 DeadLettered=3。
// 模块级常量含 UI 文案，改为由持有 useTranslations 的组件调用的工厂函数（纯函数不能直接调 hook）。
function getStatusMap(t: ReturnType<typeof useTranslations>): Record<number, { label: string; variant: 'success' | 'destructive' | 'warning' | 'secondary' }> {
  return {
    0: { label: t('statusPending'), variant: 'warning' },
    1: { label: t('statusDelivered'), variant: 'success' },
    2: { label: t('statusFailed'), variant: 'destructive' },
    3: { label: t('statusDeadLettered'), variant: 'destructive' },
  }
}

// 日期展示格式随平台语言（批次 3f 口径），locale 由持有 useLocale 的组件传入。
function formatDateTime(value: string | null, locale: string): string {
  return value ? new Date(value).toLocaleString(locale) : '-'
}

function prettyJson(value: string | null): string {
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

export function MessageRecordsClient({
  initial,
  initialStatus = '', initialPage = 0, initialPageSize = 15, initialSorting = [], initialSearch = '',
}: {
  initial: PagedResult<MessageRecord>
  initialStatus?: string; initialPage?: number; initialPageSize?: number; initialSorting?: SortingState; initialSearch?: string
}) {
  const t = useTranslations('MessageBus.record')
  const rc = useTranslations('MessageBus.common')
  const tc = useTranslations('Common')
  const locale = useLocale()
  const STATUS_MAP = getStatusMap(t)
  const { toast } = useToast()
  // 状态筛选经 useUrlListState 镜像到 URL；分页/搜索由 useCrudResource(urlSync) 镜像。初值均来自 page.tsx 的 searchParams。
  const [filters, setFilters] = useUrlListState({ status: initialStatus })
  const [detail, setDetail] = useState<MessageRecord | null>(null)
  const [attempts, setAttempts] = useState<MessageDeliveryAttempt[]>([])
  const [attemptsLoading, setAttemptsLoading] = useState(false)
  const [replayingId, setReplayingId] = useState<string | null>(null)
  const [selectedRowIds, setSelectedRowIds] = useState<RowSelectionState>({})
  const [batchReplaying, setBatchReplaying] = useState(false)

  // 打开详情时按需拉取各订阅投递明细（不用 mount effect 取数，避开 React19 坑）。
  async function openDetail(record: MessageRecord) {
    setDetail(record)
    setAttempts([])
    setAttemptsLoading(true)
    try {
      const res = await fetch(`/api/proxy/message-records/${record.id}/attempts`)
      setAttempts(res.ok ? await res.json() : [])
    } catch {
      setAttempts([])
    } finally {
      setAttemptsLoading(false)
    }
  }

  // 服务端分页：翻页 + 状态筛选下推后端；全局搜索框走后端 Filter（消息主题/内容）。
  const list = useCrudResource<MessageRecord>({
    endpoint: 'message-records',
    initial,
    errorTitle: t('loadFailedTitle'),
    urlSync: true,
    initialSorting,
    initialPage,
    pageSize: initialPageSize,
    initialSearch,
    extraDeps: [filters.status],
    buildParams: (p) => {
      if (filters.status) p.set('Status', filters.status)
    },
  })

  // 批量重推：经后端集合化 /batch-replay 分片一次性重投（保留每片 <=2000 上限与幂等跳过语义）。
  async function batchReplay(ids: string[], clearSelection: () => void) {
    setBatchReplaying(true)
    try {
      const result = await submitBatchIds('message-records/batch-replay', ids)
      clearSelection()
      toast({
        variant: result.skipped > 0 && result.succeeded === 0 ? 'destructive' : 'success',
        title: t('batchReplayDoneTitle'),
        description: t('batchReplaySucceededDesc', { ok: result.succeeded })
          + (result.skipped > 0 ? t('batchReplayFailedSuffix', { fail: result.skipped }) : ''),
      })
      await list.reloadAndRefresh()
    } catch (e) {
      console.error(e)
      toast({
        variant: 'destructive',
        title: t('replayFailedTitle'),
        description: t('replayFailedDesc'),
      })
    } finally {
      setBatchReplaying(false)
    }
  }

  async function replay(record: MessageRecord) {
    setReplayingId(record.id)
    try {
      const res = await fetch(`/api/proxy/message-records/${record.id}/replay`, { method: 'PUT' })
      if (!res.ok) throw new Error(await res.text())
      toast({ variant: 'success', title: t('replayedTitle'), description: t('replayedDesc', { subject: record.subject }) })
      setDetail(null)
      await list.reloadAndRefresh()
    } catch (e) {
      console.error(e)
      toast({ variant: 'destructive', title: t('replayFailedTitle'), description: t('replayFailedDesc') })
    } finally {
      setReplayingId(null)
    }
  }

  const columns: ColumnDef<MessageRecord, unknown>[] = [
    {
      accessorKey: 'topicName',
      size: 140,
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('topic')} />,
      cell: ({ row }) => <span className="font-medium">{row.original.topicName ?? '-'}</span>,
    },
    {
      accessorKey: 'subject',
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columnSubject')} />,
      cell: ({ row }) => (
        <button type="button" className={`${dataTableLinkClass} text-sm`} onClick={() => void openDetail(row.original)}>
          {row.original.subject}
        </button>
      ),
    },
    {
      accessorKey: 'payload',
      enableSorting: false, // 消息内容为大段 JSON 文本快照，仅截断预览展示，不适合做排序键
      header: ({ column }) => <DataTableColumnHeader column={column} title={t('columnPayload')} />,
      cell: ({ row }) => <span className="block max-w-64 truncate font-mono text-xs text-muted-foreground">{row.original.payload}</span>,
    },
    numberColumn<MessageRecord>('retryCount', tc('retryCount'), { size: 80 }),
    {
      accessorKey: 'status',
      size: 90,
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('status')} />,
      meta: { filterVariant: 'select', filterOptions: Object.entries(STATUS_MAP).map(([value, { label }]) => ({ value: Number(value), label })) },
      cell: ({ row }) => {
        const s = STATUS_MAP[row.original.status] ?? { label: rc('unknown'), variant: 'secondary' as const }
        return <Badge variant={s.variant}>{s.label}</Badge>
      },
    },
    {
      accessorKey: 'deliveredAt',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('deliveredAt')} />,
      cell: ({ row }) => <span className="text-sm text-muted-foreground">{formatDateTime(row.original.deliveredAt, locale)}</span>,
    },
    {
      accessorKey: 'errorMessage',
      header: ({ column }) => <DataTableColumnHeader column={column} title={tc('errorMessage')} />,
      cell: ({ row }) => (
        <span className="block max-w-48 truncate text-sm text-destructive" title={row.original.errorMessage ?? undefined}>
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
            <DropdownMenuItem onClick={() => void openDetail(row.original)}><Eye className="mr-2 size-4" />{t('viewDetailMenuItem')}</DropdownMenuItem>
            <DropdownMenuItem disabled={replayingId === row.original.id} onClick={() => void replay(row.original)}>
              <RotateCcw className="mr-2 size-4" />{t('replayMenuItem')}
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      ),
    },
  ]

  const detailStatus = detail ? (STATUS_MAP[detail.status] ?? { label: rc('unknown'), variant: 'secondary' as const }) : null

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
      </div>

      <DataTable
        columns={columns}
        data={list.rows}
        {...list.tableProps}
        searchPlaceholder={t('searchPlaceholder')}
        exportFileName="message-records.csv"
        tableKey="message-records"
        enableRowSelection
        getRowId={row => row.id}
        selectedRowIds={selectedRowIds}
        onSelectedRowIdsChange={setSelectedRowIds}
        bulkActions={(_rows, clearSelection, selectedIds) => (
          <Button size="sm" variant="outline" disabled={batchReplaying} onClick={() => void batchReplay(selectedIds, clearSelection)}>
            {batchReplaying ? <Loader2 className="mr-1 size-3.5 animate-spin" /> : <RotateCcw className="mr-1 size-3.5" />}
            {t('batchReplayButtonLabel', { count: selectedIds.length })}
          </Button>
        )}
      />

      <Dialog open={!!detail} onOpenChange={(open) => { if (!open) setDetail(null) }}>
        {detail && (
          <ScrollableDialogContent
            title={t('detailTitle', { subject: detail.subject })}
            description={t('detailIdDesc', { id: detail.id })}
            footer={
              <div className="flex justify-end gap-2">
                <Button variant="outline" onClick={() => setDetail(null)}>{tc('close')}</Button>
                <Button disabled={replayingId === detail.id} onClick={() => void replay(detail)}>
                  {replayingId === detail.id ? <Loader2 className="mr-1.5 size-4 animate-spin" /> : <RotateCcw className="mr-1.5 size-4" />}{t('replayMenuItem')}
                </Button>
              </div>
            }
          >
            <div className="space-y-4 p-6">
              <dl className="grid grid-cols-2 gap-x-6 gap-y-3 sm:grid-cols-3">
                <Field label={tc('topic')}>{detail.topicName ?? '-'}</Field>
                <Field label={tc('status')}>{detailStatus && <Badge variant={detailStatus.variant}>{detailStatus.label}</Badge>}</Field>
                <Field label={tc('retryCount')}>{detail.retryCount}</Field>
                <Field label={tc('creationTime')}>{formatDateTime(detail.creationTime, locale)}</Field>
                <Field label={tc('deliveredAt')}>{formatDateTime(detail.deliveredAt, locale)}</Field>
              </dl>
              <div>
                <p className="mb-1 text-xs text-muted-foreground">{t('payloadLabel')}</p>
                <pre className="max-h-72 overflow-auto rounded-md border bg-muted/40 p-3 font-mono text-xs">{prettyJson(detail.payload) || t('payloadEmpty')}</pre>
              </div>
              {detail.errorMessage && (
                <div>
                  <p className="mb-1 text-xs text-muted-foreground">{t('errorMessageAggregatedLabel')}</p>
                  <pre className="max-h-60 overflow-auto whitespace-pre-wrap rounded-md border border-destructive/30 bg-destructive/5 p-3 font-mono text-xs text-destructive">{detail.errorMessage}</pre>
                </div>
              )}
              <div>
                <p className="mb-1 text-xs text-muted-foreground">{t('attemptsLabel')}</p>
                {attemptsLoading ? (
                  <p className="text-xs text-muted-foreground">{t('attemptsLoading')}</p>
                ) : attempts.length === 0 ? (
                  <p className="text-xs text-muted-foreground">{t('attemptsEmpty')}</p>
                ) : (
                  <div className="divide-y rounded-md border">
                    {attempts.map((a) => {
                      const s = STATUS_MAP[a.status] ?? { label: rc('unknown'), variant: 'secondary' as const }
                      return (
                        <div key={a.id} className="flex items-center gap-3 p-2 text-sm">
                          <span className="min-w-0 flex-1 truncate font-medium">{a.subscriptionName}</span>
                          <span className="shrink-0 text-xs text-muted-foreground">{t('attemptRetryPrefix', { count: a.retryCount })}</span>
                          {a.lastError && (
                            <span className="max-w-40 shrink-0 truncate text-xs text-destructive" title={a.lastError}>{a.lastError}</span>
                          )}
                          <Badge variant={s.variant} className="shrink-0">{s.label}</Badge>
                        </div>
                      )
                    })}
                  </div>
                )}
              </div>
            </div>
          </ScrollableDialogContent>
        )}
      </Dialog>
    </>
  )
}
