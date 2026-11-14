import { type ReactNode } from 'react'
import { type Table } from '@tanstack/react-table'
import { useTranslations } from 'next-intl'
import { ChevronDown, Download, Filter, RefreshCw, RotateCcw, Search, Settings2, SlidersHorizontal, Upload } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Popover, PopoverAnchor, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { useAfterMenuClose } from '@/components/ui/use-after-menu-close'
import { cn } from '@/lib/utils'
import { DataTableColumnSettings, type ColumnSettingsState } from './data-table-column-settings'
import { useNarrowScreen } from './use-narrow-screen'
import { countToolbarExtraSlots, partitionToolbarSlots } from './toolbar-overflow'
import { IconButton } from '@/components/ui/icon-button'

interface DataTableToolbarProps<TData> {
  table: Table<TData>
  searchPlaceholder: string
  /** 是否显示全局搜索框（服务端分页且后端无统一 filter 关键字时传 false 隐藏）。 */
  enableSearch: boolean
  globalFilter: string
  onGlobalFilterChange: (value: string) => void
  selectedRows: TData[]
  /** 全部跨页选中的 id（rowSelection keys）；「已选 N」与 bulkActions 第 3 参数用它。 */
  selectedIds: string[]
  selectedCount: number
  bulkActions?: (selectedRows: TData[], clearSelection: () => void, selectedIds: string[]) => React.ReactNode
  clearSelection: () => void
  toolbarExtra?: React.ReactNode
  compactActions?: boolean
  showImport: boolean
  fileInputRef: React.RefObject<HTMLInputElement | null>
  onImportFile: (file: File) => void
  showExport: boolean
  onExport: () => void
  /** 列筛选开关是否显示（服务端分页时隐藏，改用页面级筛选）。 */
  showFilterToggle: boolean
  filtersActive: boolean
  onToggleFilters: () => void
  activeFilters: number
  /** 「重置列表视图」：清搜索/分页/排序/页面级筛选（传入才显示按钮，useCrudResource 页自动带）。 */
  onResetView?: () => void
  /** 视图是否偏离默认：与 `onResetView` 同时满足才渲染重置按钮（默认视图下不显示无效操作）。 */
  viewDirty: boolean
  /** 「刷新数据」：重取当前页 + 失效 Router Cache（传入才显示按钮，useCrudResource 页自动带）。 */
  onReload?: () => void | Promise<void>
  /** 取数中：刷新按钮图标转为 `animate-spin` 并禁用，防连点重复请求。 */
  loading: boolean
  /** 列设置面板（「列」按钮、「更多」里的「列设置…」、列菜单都打开它）。 */
  columnSettings: ColumnSettingsState
}

const buttonClass = 'h-11 gap-1.5 md:h-8'

// 留在外面的优先级：刷新、筛选、导出最高；重置、列、导入先收进「更多」。
const PRIORITY = { refresh: 100, filter: 80, export: 70, reset: 30, columns: 20, import: 10 } as const

// 工具栏：搜索框 + 右侧工具区（最多 4 个文字按钮，含「更多」）；有勾选行时右侧换成选中条。
// 内置按钮都是视图工具（`variant="tool"`）：只改看到什么，不改数据；改数据的命令放页头。
export function DataTableToolbar<TData>({
  table,
  searchPlaceholder,
  enableSearch,
  globalFilter,
  onGlobalFilterChange,
  selectedRows,
  selectedIds,
  selectedCount,
  bulkActions,
  clearSelection,
  toolbarExtra,
  compactActions,
  showImport,
  fileInputRef,
  onImportFile,
  showExport,
  onExport,
  showFilterToggle,
  filtersActive,
  onToggleFilters,
  activeFilters,
  onResetView,
  viewDirty,
  onReload,
  loading,
  columnSettings,
}: DataTableToolbarProps<TData>) {
  const t = useTranslations('Common')
  const narrow = useNarrowScreen()
  const showReset = !!onResetView && viewDirty
  // 窄屏本来就用 CSS 藏起列筛选开关，不占可见名额，也不塞进「更多」。
  const showFilter = showFilterToggle && !narrow
  const slots: { id: string; priority: number }[] = []
  if (showImport) slots.push({ id: 'import', priority: PRIORITY.import })
  if (showExport) slots.push({ id: 'export', priority: PRIORITY.export })
  if (showReset) slots.push({ id: 'reset', priority: PRIORITY.reset })
  if (onReload) slots.push({ id: 'refresh', priority: PRIORITY.refresh })
  if (showFilter) slots.push({ id: 'filter', priority: PRIORITY.filter })
  slots.push({ id: 'columns', priority: PRIORITY.columns })
  const { visible, overflow } = partitionToolbarSlots(slots, countToolbarExtraSlots(toolbarExtra))
  const visibleIds = new Set(visible.map((slot) => slot.id))
  const overflowIds = new Set(overflow.map((slot) => slot.id))
  const moreSignals = overflowIds.has('reset') || (overflowIds.has('filter') && (filtersActive || activeFilters > 0))
  const selecting = selectedCount > 0 && !!bulkActions

  // 从「更多」菜单打开列设置：必须等菜单关好再开，见 useAfterMenuClose。
  const afterMenuClose = useAfterMenuClose()

  return (
    <Popover open={columnSettings.open} onOpenChange={columnSettings.onOpenChange}>
    <div className="flex shrink-0 flex-col gap-2 md:flex-row md:items-center">
      {enableSearch && (
        <div className="relative w-full md:max-w-sm md:flex-1">
          <Search className="absolute left-2.5 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" />
          <Input
            placeholder={searchPlaceholder}
            value={globalFilter}
            onChange={(e) => onGlobalFilterChange(e.target.value)}
            className="h-11 pl-8 text-sm md:h-8"
          />
        </div>
      )}

      {showImport && (
          <input
            ref={fileInputRef}
            type="file"
            accept=".xlsx,.xls,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/vnd.ms-excel"
            className="hidden"
            onChange={(event) => {
              const file = event.target.files?.[0]
              event.target.value = ''
              if (file) onImportFile(file)
            }}
          />
      )}

      {selecting ? (
        // 选中条：有勾选行时替换右侧工具区（搜索框保留，方便继续找、继续勾）。
        // 中性浅灰底 + 加粗计数，不用黑底——黑底只给页头主按钮（蓝图 §3.5、§7 D6）。
        <div className="flex min-h-11 w-full flex-wrap items-center gap-2 rounded-md border bg-muted px-2 py-1 md:ml-auto md:min-h-8 md:flex-1">
          <span className="text-sm font-medium" aria-live="polite">{t('selectedRowsCount', { count: selectedCount })}</span>
          <Button variant="link" size="sm" className="h-auto px-0 text-muted-foreground" onClick={clearSelection}>
            {t('clearSelection')}
          </Button>
          <div className="flex flex-wrap items-center gap-2 md:ml-auto">
            {bulkActions?.(selectedRows, clearSelection, selectedIds)}
          </div>
        </div>
      ) : (
      <div className="flex flex-wrap items-center gap-2 md:ml-auto">
        {toolbarExtra}

        {visibleIds.has('import') && (
          <ToolbarButton onClick={() => fileInputRef.current?.click()} icon={<Upload className="size-3.5" />}>
            {t('import')}
          </ToolbarButton>
        )}

        {visibleIds.has('export') && (compactActions ? (
          <IconButton label={t('export')} variant="tool" className="size-11 md:size-8" onClick={onExport}>
            <Download className="size-3.5" />
          </IconButton>
        ) : (
          <ToolbarButton onClick={onExport} icon={<Download className="size-3.5" />}>
            {t('export')}
          </ToolbarButton>
        ))}

        {/* 重置只在视图偏离默认时出现。名额够时留在外面，作为「当前不是默认视图」的指示；
            名额不够时收进「更多」，按钮改用 secondary 保住这个信号。 */}
        {visibleIds.has('reset') && (
          <ToolbarButton onClick={onResetView} title={t('resetViewTooltip')} icon={<RotateCcw className="size-3.5" />}>
            {t('reset')}
          </ToolbarButton>
        )}

        {visibleIds.has('refresh') && (compactActions ? (
          <IconButton label={t('refreshData')} variant="tool" className="size-11 md:size-8" disabled={loading} onClick={() => void onReload?.()}>
            <RefreshCw className={cn('size-3.5', loading && 'animate-spin')} />
          </IconButton>
        ) : (
          <ToolbarButton
            onClick={() => void onReload?.()}
            disabled={loading}
            icon={<RefreshCw className={cn('size-3.5', loading && 'animate-spin')} />}
          >
            {t('refreshData')}
          </ToolbarButton>
        ))}

        {visibleIds.has('filter') && (
          <FilterButton
            filtersActive={filtersActive}
            activeFilters={activeFilters}
            onClick={onToggleFilters}
            label={t('filter')}
          />
        )}

        {visibleIds.has('columns') && (
          <PopoverTrigger asChild>
            {compactActions ? (
              <IconButton label={t('columnSettings')} variant="tool" className="size-11 md:size-8">
                <SlidersHorizontal className="size-3.5" />
              </IconButton>
            ) : (
              <Button variant="tool" size="sm" className={buttonClass}>
                <SlidersHorizontal className="size-3.5" />
                {t('columns')}
              </Button>
            )}
          </PopoverTrigger>
        )}

        {overflow.length > 0 && (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              {/* 有收起来的「重置」或生效中的筛选时浅灰底提示，不必点开才知道。 */}
              <Button variant="tool" size="sm" className={cn(buttonClass, moreSignals && 'bg-secondary text-foreground')}>
                {t('more')}
                <ChevronDown className="size-3.5" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-52" onCloseAutoFocus={afterMenuClose.onCloseAutoFocus}>
              {overflowIds.has('import') && (
                <DropdownMenuItem onClick={() => fileInputRef.current?.click()}>
                  <Upload className="size-3.5" />{t('import')}
                </DropdownMenuItem>
              )}
              {overflowIds.has('export') && (
                <DropdownMenuItem onClick={onExport}>
                  <Download className="size-3.5" />{t('export')}
                </DropdownMenuItem>
              )}
              {overflowIds.has('reset') && (
                <DropdownMenuItem onClick={onResetView} title={t('resetViewTooltip')}>
                  <RotateCcw className="size-3.5" />{t('reset')}
                </DropdownMenuItem>
              )}
              {overflowIds.has('refresh') && (
                <DropdownMenuItem disabled={loading} onClick={() => void onReload?.()}>
                  <RefreshCw className={cn('size-3.5', loading && 'animate-spin')} />{t('refreshData')}
                </DropdownMenuItem>
              )}
              {overflowIds.has('filter') && (
                <DropdownMenuItem onClick={onToggleFilters}>
                  <Filter className="size-3.5" />
                  {t('filter')}
                  {activeFilters > 0 ? ` (${activeFilters})` : ''}
                </DropdownMenuItem>
              )}
              {overflowIds.has('columns') && (
                <>
                  {(overflow.length > 1) && <DropdownMenuSeparator />}
                  <DropdownMenuItem onSelect={() => afterMenuClose.schedule(() => columnSettings.onOpenChange(true))}>
                    <Settings2 className="size-3.5" />{t('columnSettingsEllipsis')}
                  </DropdownMenuItem>
                </>
              )}
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </div>
      )}
      {/* 面板固定挂在工具栏最右端：选中条替换右侧工具区、或从列菜单打开时，仍有地方落。 */}
      <PopoverAnchor asChild>
        <span aria-hidden className="h-0 w-0 self-end md:self-stretch" />
      </PopoverAnchor>
    </div>
    <PopoverContent align="end" className="w-80 p-2">
      <DataTableColumnSettings table={table} settings={columnSettings} />
    </PopoverContent>
    </Popover>
  )
}

function ToolbarButton({
  children,
  icon,
  onClick,
  disabled,
  title,
}: {
  children: ReactNode
  icon: ReactNode
  onClick?: () => void
  disabled?: boolean
  title?: string
}) {
  return (
    <Button variant="tool" size="sm" className={buttonClass} onClick={onClick} disabled={disabled} title={title}>
      {icon}
      {children}
    </Button>
  )
}

function FilterButton({
  filtersActive,
  activeFilters,
  onClick,
  label,
}: {
  filtersActive: boolean
  activeFilters: number
  onClick: () => void
  label: string
}) {
  return (
    <Button
      variant="tool"
      size="sm"
      aria-pressed={filtersActive}
      className={cn('hidden md:inline-flex', buttonClass)}
      onClick={onClick}
    >
      <Filter className="size-3.5" />
      {label}
      {/* 生效条数跟在文字后，不再用黑底角标（黑底只给主按钮）。 */}
      {activeFilters > 0 && (
        <span className="rounded-sm bg-background px-1 text-xs tabular-nums text-foreground">{activeFilters}</span>
      )}
    </Button>
  )
}
