import { useEffect, useMemo, useState } from 'react'
import { dataTableLinkClass } from '@/components/data-table/data-table-link-cell'
import { type Table } from '@tanstack/react-table'
import { useTranslations } from 'next-intl'
import { ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight, Plus, X } from 'lucide-react'
import { useOptionalSessionIdentity } from '@/components/providers/session-identity-provider'
import { Button } from '@/components/ui/button'
import { IconButton } from '@/components/ui/icon-button'
import { Input } from '@/components/ui/input'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from '@/components/ui/select'
import { cn } from '@/lib/utils'
import {
  scheduleCloudTableViewSync,
  TABLE_CLOUD_SYNC_EVENT,
  type TableCloudSyncDetail,
} from './table-preference-cloud'
import {
  BASE_PAGE_SIZE_OPTIONS, INTERACTIVE_LIST_MAX_PAGE_SIZE, MAX_PAGE_SIZE,
  readCustomPageSizes, addCustomPageSize, removeCustomPageSize,
  readStoredPageSize, retainUsableCustomPageSizes, splitCustomPageSizes,
} from './use-table-persistence'

interface DataTablePaginationProps<TData> {
  table: Table<TData>
  /** 「共 X 条」的 X：客户端=过滤后总行数，服务端=rowCount。 */
  totalLabel: number
  /** 「（过滤自 Y 条）」的 Y；null 不显示（无过滤或服务端分页）。 */
  filteredFromLabel: number | null
  pageIndex: number
  pageSize: number
  pageCount: number
  /** 服务端分页模式：每页档位上限默认钳到 INTERACTIVE_LIST_MAX_PAGE_SIZE（200），而非客户端的 MAX_PAGE_SIZE。 */
  manualPagination?: boolean
  /**
   * 覆盖每页档位上限（默认按 manualPagination 推导，即 200 / 10000）。
   * 给「需要比交互默认更宽」的列表用——如宽分页审计视图把上限放到 500（后端同步静默钳制）。
   * 不传时服务端分页走 200，避免自定义档位把单页撑到硬顶 5000。
   */
  maxPageSize?: number
  /** 持久化键：自定义每页条数按表隔离，不再写全局。 */
  tableKey?: string
  /**
   * 总数截断阈值：`totalLabel > countCap` 时显示「{countCap}+」而非精确数字。
   * 配后端的截断计数使用（服务端为省掉千万级 COUNT(*) 只回 cap+1）——**不传就按精确数显示**，
   * 所以给了 cap 的页面必须确认后端真的在截断，否则会把精确数显示成「10 万+」。
   */
  countCap?: number
}

function readUrlRequestedPageSize(): number | null {
  try {
    if (typeof window === 'undefined') return null
    const raw = new URLSearchParams(window.location.search).get('size')
    if (!raw) return null
    const parsed = Number(raw)
    return Number.isInteger(parsed) && parsed > 0 ? parsed : null
  } catch {
    return null
  }
}

function PageSizeChip({
  n, muted = false, removeLabel, tooltip, onRemove,
}: {
  n: number
  muted?: boolean
  removeLabel?: string
  tooltip?: string
  onRemove?: () => void
}) {
  return (
    <span
      title={tooltip}
      className={cn(
        'inline-flex items-center gap-1 rounded-md border px-2 py-0.5 text-xs',
        muted
          ? 'border-dashed bg-muted/20 text-muted-foreground'
          : 'bg-muted/40 text-foreground',
      )}
    >
      {n}
      {onRemove && (
        <button
          type="button"
          aria-label={removeLabel}
          className="text-muted-foreground hover:text-destructive"
          onClick={onRemove}
        >
          <X className="size-3" />
        </button>
      )}
    </span>
  )
}

// 分页控件：共 X 条 / 过滤提示 / 每页条数 / 第 a/b 页 / 首末上下页。翻页与每页改动均经 table 实例。
export function DataTablePagination<TData>({
  table,
  totalLabel,
  filteredFromLabel,
  pageIndex,
  pageSize,
  pageCount,
  manualPagination = false,
  maxPageSize: maxPageSizeOverride,
  tableKey,
  countCap,
}: DataTablePaginationProps<TData>) {
  const t = useTranslations('Common')
  const principalId = useOptionalSessionIdentity()?.storagePrincipalId ?? null
  // 用户自定义每页条数（按 tableKey 隔离，无本表记录时回退全局旧键）。本控件仅在父级
  // hydrated 后挂载（永不 SSR），故可在 useState 懒初始化里直接读 localStorage。
  const [customSizes, setCustomSizes] = useState<number[]>(() => readCustomPageSizes(tableKey))
  const [addOpen, setAddOpen] = useState(false)
  const [draft, setDraft] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [urlRequestedSize] = useState(readUrlRequestedPageSize)
  const [initialPageSize] = useState(pageSize)
  const [clampDismissed, setClampDismissed] = useState(false)

  useEffect(() => {
    if (!tableKey) return undefined
    function handleCloudSync(event: Event) {
      const detail = (event as CustomEvent<TableCloudSyncDetail>).detail
      if (detail?.tables?.includes(tableKey!)) {
        setCustomSizes(readCustomPageSizes(tableKey))
      }
    }
    window.addEventListener(TABLE_CLOUD_SYNC_EVENT, handleCloudSync)
    return () => window.removeEventListener(TABLE_CLOUD_SYNC_EVENT, handleCloudSync)
  }, [tableKey])

  // 服务端分页（manual）默认走交互上限 200；客户端分页一次性加载全部、前端切片，沿用 MAX_PAGE_SIZE。
  // 调用方可用 maxPageSize 放宽（如宽分页视图 500）或收得更严。硬顶仍是后端 MaxMaxResultCount=5000。
  const maxPageSize = maxPageSizeOverride ?? (manualPagination ? INTERACTIVE_LIST_MAX_PAGE_SIZE : MAX_PAGE_SIZE)
  const { usable: usableSizes, unavailable: unavailableSizes } = useMemo(
    () => splitCustomPageSizes(customSizes, maxPageSize),
    [customSizes, maxPageSize],
  )
  // 下拉选项 = 系统默认 + 本页可用自定义 + 当前值（防止当前值被移除后下拉空白），去重升序。
  // 超限档位不进下拉；当前 pageSize 始终保留兜底（持久化值可能尚未被上层钳制）。
  const options = useMemo(
    () => Array.from(new Set([...BASE_PAGE_SIZE_OPTIONS, ...usableSizes, pageSize]))
      .filter((n) => n <= maxPageSize || n === pageSize)
      .sort((a, b) => a - b),
    [usableSizes, pageSize, maxPageSize],
  )
  const usableSet = useMemo(() => new Set(usableSizes), [usableSizes])
  const storedRequestedSize = tableKey ? readStoredPageSize(tableKey, 0) : 0
  const requestedOverMax = (urlRequestedSize && urlRequestedSize > maxPageSize ? urlRequestedSize : null)
    ?? (storedRequestedSize > maxPageSize ? storedRequestedSize : null)
  const showClampNotice = !clampDismissed
    && requestedOverMax != null
    && requestedOverMax > pageSize
    && (pageSize === initialPageSize || pageSize === maxPageSize)

  function handleAdd() {
    const value = Number(draft.trim())
    if (!draft.trim() || !Number.isInteger(value) || value <= 0) {
      setError(t('positiveIntegerRequired'))
      return
    }
    if (value > maxPageSize) {
      setError(t('maxPageSizeExceeded', { max: maxPageSize }))
      return
    }
    if (BASE_PAGE_SIZE_OPTIONS.includes(value) || usableSet.has(value)) {
      setError(t('duplicatePageSize'))
      return
    }
    const next = addCustomPageSize(value, tableKey, maxPageSize)
    if (next) {
      setCustomSizes(next)
      scheduleCloudTableViewSync(principalId, tableKey)
    }
    setDraft('')
    setError(null)
    setClampDismissed(true)
    table.setPageSize(value)
    setAddOpen(false)
  }

  function handleRemove(value: number) {
    setCustomSizes(removeCustomPageSize(value, tableKey, maxPageSize))
    scheduleCloudTableViewSync(principalId, tableKey)
  }

  function handleDismissUnavailable() {
    setCustomSizes(retainUsableCustomPageSizes(tableKey, maxPageSize))
    scheduleCloudTableViewSync(principalId, tableKey)
  }

  return (
    <div className="flex shrink-0 flex-wrap items-center justify-between gap-2 text-sm text-muted-foreground select-none">
      <span>
        {countCap !== undefined && totalLabel > countCap
          ? t('totalCountCapped', { count: countCap })
          : t('totalCount', { count: totalLabel })}
        {filteredFromLabel !== null ? t('filteredFrom', { count: filteredFromLabel }) : ''}
      </span>
      <div className="flex flex-wrap items-center justify-end gap-2 md:gap-4">
        <div className="hidden items-center gap-1.5 md:flex">
          <span className="text-xs">{t('perPage')}</span>
          <Select
            value={String(pageSize)}
            onValueChange={(v) => { setClampDismissed(true); table.setPageSize(Number(v)) }}
          >
            <SelectTrigger className="h-7 w-20 text-xs">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {options.map((n) => (
                <SelectItem key={n} value={String(n)} className="text-xs">{n}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          {/* 自定义每页条数：添加 / 移除已添加项；超限遗留在此说明原因，不进下拉。 */}
          <Popover
            open={addOpen}
            onOpenChange={(o) => { setAddOpen(o); if (!o) { setDraft(''); setError(null) } }}
          >
            <PopoverTrigger asChild>
              <IconButton
                label={unavailableSizes.length > 0 ? t('customPageSizeHasUnavailable') : t('customPageSize')}
                variant="outline"
                className="relative size-7"
              >
                <Plus className="size-3.5" />
                {unavailableSizes.length > 0 && (
                  <span className="bg-primary absolute top-0.5 right-0.5 size-1.5 rounded-full" aria-hidden />
                )}
              </IconButton>
            </PopoverTrigger>
            <PopoverContent align="end" className="w-80 space-y-3">
              <div className="space-y-1.5">
                <p className="text-sm font-medium text-foreground">{t('customPageSize')}</p>
                <p className="text-xs text-muted-foreground">{t('customPageSizeHint', { max: maxPageSize })}</p>
              </div>
              <div className="flex items-center gap-2">
                <Input
                  type="number"
                  min={1}
                  max={maxPageSize}
                  value={draft}
                  onChange={(e) => { setDraft(e.target.value); setError(null) }}
                  onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); handleAdd() } }}
                  placeholder={t('pageSizePlaceholderExample', { max: maxPageSize })}
                  className="h-8 text-xs"
                />
                <Button size="sm" className="h-8" onClick={handleAdd}>{t('add')}</Button>
              </div>
              {error && <p className="text-xs text-destructive">{error}</p>}
              {usableSizes.length > 0 && (
                <div className="space-y-1.5">
                  <p className="text-xs text-muted-foreground">{t('addedPageSizes')}</p>
                  <div className="flex flex-wrap gap-1.5">
                    {usableSizes.map((n) => (
                      <PageSizeChip
                        key={n}
                        n={n}
                        removeLabel={t('removePageSize', { n })}
                        onRemove={() => handleRemove(n)}
                      />
                    ))}
                  </div>
                </div>
              )}
              {unavailableSizes.length > 0 && (
                <div className="space-y-1.5 rounded-md border border-dashed bg-muted/30 p-2">
                  <p className="text-xs text-muted-foreground">{t('unavailablePageSizes', { max: maxPageSize })}</p>
                  <div className="flex flex-wrap gap-1.5">
                    {unavailableSizes.map((n) => (
                      <PageSizeChip
                        key={n}
                        n={n}
                        muted
                        tooltip={t('unavailablePageSizeTooltip')}
                      />
                    ))}
                  </div>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    className="h-7 px-0 text-xs"
                    onClick={handleDismissUnavailable}
                  >
                    {t('removeUnavailablePageSizes')}
                  </Button>
                  <p className="text-[11px] text-muted-foreground">{t('removeUnavailablePageSizesHint')}</p>
                </div>
              )}
            </PopoverContent>
          </Popover>
          {showClampNotice && requestedOverMax != null && (
            <span className="flex items-center gap-1 text-xs">
              {t('pageSizeClampedNotice', { requested: requestedOverMax, max: maxPageSize })}
              <button
                type="button"
                className={`${dataTableLinkClass} text-xs`}
                onClick={() => setClampDismissed(true)}
              >
                {t('dismissPageSizeClampedNotice')}
              </button>
            </span>
          )}
        </div>
        <span className="text-xs">{t('pageIndicator', { current: pageCount === 0 ? 0 : pageIndex + 1, total: pageCount })}</span>
        <div className="flex items-center gap-1">
          <IconButton variant="outline" className="hidden size-7 md:inline-flex" label={t('firstPage')} onClick={() => table.setPageIndex(0)} disabled={!table.getCanPreviousPage()}>
            <ChevronsLeft className="size-3.5" />
          </IconButton>
          <IconButton variant="outline" className="size-7 max-md:size-11" label={t('prevPage')} onClick={() => table.previousPage()} disabled={!table.getCanPreviousPage()}>
            <ChevronLeft className="size-3.5" />
          </IconButton>
          <IconButton variant="outline" className="size-7 max-md:size-11" label={t('nextPage')} onClick={() => table.nextPage()} disabled={!table.getCanNextPage()}>
            <ChevronRight className="size-3.5" />
          </IconButton>
          <IconButton variant="outline" className="hidden size-7 md:inline-flex" label={t('lastPage')} onClick={() => table.setPageIndex(pageCount - 1)} disabled={!table.getCanNextPage()}>
            <ChevronsRight className="size-3.5" />
          </IconButton>
        </div>
      </div>
    </div>
  )
}
