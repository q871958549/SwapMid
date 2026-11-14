import { useRef, useState, type ReactNode } from 'react'
import { type ColumnDef, type Row, type Table, flexRender } from '@tanstack/react-table'
import { useVirtualizer } from '@tanstack/react-virtual'
import { useTranslations } from 'next-intl'
import { Filter, GripVertical, Loader2 } from 'lucide-react'
import { cn } from '@/lib/utils'
import {
  Table as UITable, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from '@/components/ui/table'
import { ContextMenu, ContextMenuTrigger } from '@/components/ui/context-menu'
import { IconButton } from '@/components/ui/icon-button'
import {
  LEFT_STRUCTURAL_COL_IDS, RIGHT_STRUCTURAL_COL_IDS, STRUCTURAL_COL_IDS, leftStickyLayout, reconcileColumnOrder,
  type TableDensity,
} from './use-table-persistence'
import { ColumnFilterCell } from './data-table-filter-cell'
import { TruncatedCell } from './truncated-cell'
import { resolveColumnTitle } from './data-table-columns'
import { isNumericColumn } from './numeric-column'
import { DataTableColumnContextMenu, DataTableColumnMenuButton, hasColumnMenu } from './data-table-column-menu'
import { columnOffersSorting } from './data-table-column-header'

// 数据列最小宽度（px）。列宽交给原生 table 自动排版；总宽超出时由 Table 容器横向滚动。
const DATA_COL_MIN_WIDTH = 88
// 单元格左右内边距之和（TableCell 的 px-4），拖过宽度的列按「列宽 − 内边距」截断内容。
const CELL_PADDING_X = 32

// 行虚拟化阈值：当页行数超过它才启用虚拟滚动。小数据走原全量渲染，对存量列表零影响、
// 也避开 jsdom 无布局时虚拟器拿不到尺寸的问题。门控哲学同 manualPagination。
const VIRTUALIZE_THRESHOLD = 60
// 估算行高（px）：仅作首行实测完成前的首帧兜底；真实行高由首行一次性实测取得（见下）。
const ESTIMATED_ROW_HEIGHT: Record<TableDensity, number> = { standard: 48, compact: 36 }

interface DataTableGridProps<TData> {
  table: Table<TData>
  tableColumns: ColumnDef<TData, unknown>[]
  /** 拖拽重排后回调（计算好的新顺序）；由父组件落 state + 持久化。 */
  onColumnReorder: (nextOrder: string[]) => void
  showFilters: boolean
  loading: boolean
  /** 收缩高度（数据少时按内容自然高，否则吃满剩余）。 */
  compact: boolean
  /** 是否处于过滤态（决定空表文案：没有匹配 / 暂无数据）。 */
  isFiltered: boolean
  /** 列菜单「恢复默认列」：列显隐回到页面默认。 */
  onRestoreColumns: () => void
  /** 列显隐是否已偏离默认（决定「恢复默认列」是否可点）。 */
  columnsDirty: boolean
  /** 用户固定到左侧的列（按固定先后）。 */
  pinned: string[]
  onTogglePinned: (columnId: string) => void
  /** 打开工具栏的列设置面板。 */
  onOpenColumnSettings: () => void
  /** 行密度：紧凑行把单元格上下内边距减半、表头降到 32px。 */
  density: TableDensity
  /** 打开列筛选行；不支持列筛选的表（服务端分页）不传，列菜单也就不给「筛选此列」。 */
  onShowFilters?: () => void
  /** 未过滤的空表正文。不传时仍是「暂无数据」。 */
  emptyState?: ReactNode
}

// 表格主体：表头（可拖拽重排）+ 列筛选行 + 表体 + 取数遮罩。样式/sticky/拖拽逻辑内聚于此。
export function DataTableGrid<TData>({
  table,
  tableColumns,
  onColumnReorder,
  showFilters,
  loading,
  compact,
  isFiltered,
  onRestoreColumns,
  columnsDirty,
  pinned,
  onTogglePinned,
  onOpenColumnSettings,
  density,
  onShowFilters,
  emptyState,
}: DataTableGridProps<TData>) {
  const t = useTranslations('Common')
  const [dragColId, setDragColId] = useState<string | null>(null)
  const [dragOverColId, setDragOverColId] = useState<string | null>(null)
  // 按下列宽把手到松开之间为 true：期间屏蔽表头的原生拖拽重排，两种拖拽不打架。
  const resizingRef = useRef(false)
  const columnSizing = table.getState().columnSizing
  const pageRows = table.getRowModel().rows

  // 行虚拟化（仅当页行数 > 阈值时启用）：滚动容器 = Table 外层 div，只 mount 视口内行。
  // 小数据 count=0 → 不产生虚拟行，渲染回退到下方的全量 map，行为与改造前一致。
  //
  // 行高策略 = 固定行高 + 首行一次性实测：数据单元格强制单行 truncate、操作列按钮尺寸固定，
  // 行高在设计上恒定均一。若用 measureElement 逐行动态校正，估值偏差会让拖动原生滚动条时
  // 滚过的行不断被实测、scrollHeight 持续增长，滑块从鼠标下滑走（拖到底又「长出」新高度，
  // 需反复拖拽多次才能真正到底）；固定行高让 totalSize 恒定，滚动条 1:1 跟手。
  const scrollContainerRef = useRef<HTMLDivElement>(null)
  // 实测行高按密度记：切换密度后行高变了，要重新测一次。
  const measuredRowHeightRef = useRef<{ density: TableDensity; height: number } | null>(null)
  const compactRows = density === 'compact'
  const enableVirtual = pageRows.length > VIRTUALIZE_THRESHOLD
  // TanStack Virtual returns helpers React Compiler cannot memoize (same as useReactTable).
  // eslint-disable-next-line react-hooks/incompatible-library
  const rowVirtualizer = useVirtualizer({
    count: enableVirtual ? pageRows.length : 0,
    getScrollElement: () => scrollContainerRef.current,
    estimateSize: () => measuredRowHeightRef.current?.density === density
      ? measuredRowHeightRef.current.height
      : ESTIMATED_ROW_HEIGHT[density],
    overscan: 12,
  })

  // 首行实测回调：每种密度只测一次；取得真实行高后让虚拟器按新值整体重算。
  // jsdom 无布局时 height 为 0，保持兜底估值不动（与 VIRTUALIZE_THRESHOLD 的门控哲学一致）。
  function measureFirstRow(el: HTMLTableRowElement | null) {
    if (!el || measuredRowHeightRef.current?.density === density) return
    const height = el.getBoundingClientRect().height
    if (height > 0) {
      measuredRowHeightRef.current = { density, height }
      rowVirtualizer.measure()
    }
  }

  /** 聚焦某列在筛选行里的输入框（筛选行此时应已显示）；focus 顺带把它横向滚进视野。 */
  function focusColumnFilter(colId: string) {
    const cells = scrollContainerRef.current?.querySelectorAll<HTMLElement>('[data-filter-col]') ?? []
    const cell = Array.from(cells).find((el) => el.dataset.filterCol === colId)
    cell?.querySelector<HTMLElement>('input, button')?.focus()
  }

  function handleColumnDragStart(colId: string) { setDragColId(colId) }

  function isOrderLocked(colId: string) {
    return STRUCTURAL_COL_IDS.has(colId) || table.getColumn(colId)?.columnDef.meta?.pin === 'left' || pinned.includes(colId)
  }

  function handleColumnDrop(targetColId: string) {
    if (!dragColId || dragColId === targetColId) return
    if (isOrderLocked(dragColId) || isOrderLocked(targetColId)) return
    const order = [...table.getState().columnOrder]
    const from = order.indexOf(dragColId)
    const to = order.indexOf(targetColId)
    if (from === -1 || to === -1) return
    order.splice(from, 1)
    order.splice(to, 0, dragColId)
    onColumnReorder(reconcileColumnOrder(order, tableColumns, pinned))
    setDragColId(null)
    setDragOverColId(null)
  }

  function getColumnStyle(colId: string, size: number): React.CSSProperties {
    if (LEFT_STRUCTURAL_COL_IDS.has(colId)) {
      return { width: 44, minWidth: 44, maxWidth: 44 }
    }
    if (RIGHT_STRUCTURAL_COL_IDS.has(colId)) {
      return { width: '1%', whiteSpace: 'nowrap' }
    }

    // 用户拖过的列：宽度锁定为拖出来的值（单元格内容按此截断，见 TruncatedCell 的 maxWidth）。
    if (columnSizing[colId] !== undefined) return { width: size, minWidth: size, maxWidth: size }

    const isCustomSize = Boolean(size && size !== 150)
    const minWidth = isCustomSize ? size : DATA_COL_MIN_WIDTH
    return {
      width: isCustomSize ? size : undefined,
      minWidth,
    }
  }

  const visibleLeafColumns = table.getVisibleLeafColumns()
  const leftLayout = leftStickyLayout(visibleLeafColumns.map((column) => ({
    id: column.id,
    sticky: LEFT_STRUCTURAL_COL_IDS.has(column.id) || column.columnDef.meta?.pin === 'left' || pinned.includes(column.id),
    width: LEFT_STRUCTURAL_COL_IDS.has(column.id)
      ? 44
      : (column.getSize() && column.getSize() !== 150 ? column.getSize() : DATA_COL_MIN_WIDTH),
  })))

  function columnStyle(colId: string, size: number): React.CSSProperties {
    const base = getColumnStyle(colId, size)
    const sticky = leftLayout.get(colId)
    return sticky ? { ...base, left: sticky.offset } : base
  }

  function getStickyClass(colId: string, isHeader = false): string | false {
    if (RIGHT_STRUCTURAL_COL_IDS.has(colId)) {
      return cn(
        'sticky right-0 border-l text-center whitespace-nowrap shadow-[-10px_0_14px_-14px_rgba(15,23,42,0.65)]',
        isHeader ? 'z-30 bg-muted/80' : 'z-20 bg-card/95 [&_button]:opacity-100'
      )
    }

    const sticky = leftLayout.get(colId)
    if (!sticky) return false
    const structural = LEFT_STRUCTURAL_COL_IDS.has(colId)
    return cn(
      'sticky border-r',
      structural && 'whitespace-nowrap',
      sticky.edge && 'shadow-[10px_0_14px_-14px_rgba(15,23,42,0.65)]',
      isHeader
        ? (structural ? 'z-30 bg-muted/80' : 'z-30 bg-muted')
        : (structural
          ? 'z-20 bg-card/95'
          : 'z-20 bg-card group-hover/row:bg-muted/50 group-data-[state=selected]/row:bg-muted'),
    )
  }

  // 单行渲染：虚拟化与全量渲染共用。虚拟行挂 data-index（调试/测试定位用）与首行实测回调。
  function renderRow(row: Row<TData>, virtualIndex?: number) {
    return (
      <TableRow
        key={row.id}
        data-index={virtualIndex}
        ref={virtualIndex === undefined ? undefined : measureFirstRow}
        className="group/row"
        data-state={row.getIsSelected() && 'selected'}
      >
        {row.getVisibleCells().map((cell) => (
          <TableCell
            key={cell.id}
            style={columnStyle(cell.column.id, cell.column.getSize())}
            className={cn(
              compactRows && 'py-1.5',
              getStickyClass(cell.column.id),
              // 数字列连同截断容器一起靠右：容器最宽 400px，宽列里不靠右数字就停在半路；
              // 单元格内容是 flex 时 text-right 不生效，同样推到右边，免得表头靠右、内容靠左。
              isNumericColumn(cell.column.columnDef.meta, resolveColumnTitle(cell.column, table, t))
                && 'text-right tabular-nums [&>div]:ml-auto [&>div>.flex]:justify-end',
            )}
          >
            {STRUCTURAL_COL_IDS.has(cell.column.id) ? (
              flexRender(cell.column.columnDef.cell, cell.getContext())
            ) : (
              // 仍是单行截断（固定行高策略见上方注释），但悬停时惰性检测溢出并补 title，
              // 让被截断的长文本有查看途径——此前全站无任何方式看到全文。
              <TruncatedCell maxWidth={columnSizing[cell.column.id] !== undefined ? cell.column.getSize() - CELL_PADDING_X : undefined}>
                {flexRender(cell.column.columnDef.cell, cell.getContext())}
              </TruncatedCell>
            )}
          </TableCell>
        ))}
      </TableRow>
    )
  }

  return (
    <div
      data-density={density}
      className={cn(
        'relative min-h-0 overflow-hidden rounded-xl border bg-card',
        compact ? 'shrink-0' : 'flex-1'
      )}
    >
      {loading && (
        <div className="absolute inset-0 z-50 flex items-center justify-center bg-card/60 backdrop-blur-[1px]">
          <Loader2 className="size-5 animate-spin text-muted-foreground" />
        </div>
      )}
      <UITable
        containerRef={scrollContainerRef}
        containerClassName={cn(
          // scrollbar-fade：滚动条空闲时虚化、指针进入容器才淡入（见 globals.css）。
          'scrollbar-fade overflow-auto overscroll-none [overflow-anchor:none]',
          compact ? 'max-h-[calc(100vh-18rem)]' : 'h-full'
        )}
      >
        <TableHeader className="sticky top-0 z-40 bg-card shadow-[0_1px_0_0_var(--border)]">
          {/* Main header row */}
          {table.getHeaderGroups().map((headerGroup) => {
            // 拖拽源在当前视觉顺序中的下标：用来判断落位在目标列的左边还是右边（见下方指示线注释）。
            const headerIds = headerGroup.headers.map((h) => h.column.id)
            const dragFromIdx = dragColId ? headerIds.indexOf(dragColId) : -1
            return (
            <TableRow key={headerGroup.id} className="bg-muted/40 hover:bg-muted/40">
              {headerGroup.headers.map((header, index) => {
                const isDraggable = !isOrderLocked(header.column.id)
                // 列间分隔只画在两个数据列之间：最后一列、以及紧挨固定「操作」列（自带左边框）的那列不画。
                const nextHeader = headerGroup.headers[index + 1]
                const divider = !!nextHeader && !RIGHT_STRUCTURAL_COL_IDS.has(nextHeader.column.id)
                const isOver = dragOverColId === header.column.id && dragColId !== header.column.id
                // 落位指示线必须画在真实落点那一侧：handleColumnDrop 先 splice 掉源列再按原下标插入，
                // 所以「源列在目标列左边」时（向右拖）实际落在目标列**右侧**，反之落左侧。
                // 此前恒画左边线，向右拖时线在左、列却落到右边——正是「拖拽反馈说不清楚」的实际成分。
                const dropsAfter = isOver && dragFromIdx !== -1 && dragFromIdx < headerIds.indexOf(header.column.id)
                const numeric = isNumericColumn(header.column.columnDef.meta, resolveColumnTitle(header.column, table, t))
                const withMenu = hasColumnMenu(header.column.id)
                const sorted = header.column.getIsSorted()
                const sortable = columnOffersSorting(header.column, table)
                const filtered = withMenu && header.column.getIsFiltered()
                const menuProps = {
                  column: header.column, table, onRestoreColumns, columnsDirty,
                  // 页面在列定义里钉的列不能由用户取消固定。
                  pin: header.column.columnDef.meta?.pin === 'left' ? 'page' as const : pinned.includes(header.column.id) ? 'user' as const : 'none' as const,
                  onTogglePinned: () => onTogglePinned(header.column.id),
                  onOpenColumnSettings,
                  filter: onShowFilters && header.column.getCanFilter() ? {
                    active: filtered,
                    onOpen: onShowFilters,
                    onFocus: () => focusColumnFilter(header.column.id),
                    onClear: () => header.column.setFilterValue(undefined),
                  } : undefined,
                }
                const content = header.isPlaceholder ? null : flexRender(header.column.columnDef.header, header.getContext())
                const head = (
                  <TableHead
                    key={header.id}
                    style={columnStyle(header.column.id, header.getSize())}
                    draggable={isDraggable}
                    aria-sort={sorted === 'asc' ? 'ascending' : sorted === 'desc' ? 'descending' : undefined}
                    className={cn(
                      // 表头统一 12px：纯文本表头原先继承表格的 14px，和排序表头的 12px 混在一行。
                      // 列间分隔不用通高边框，由右缘的列宽把手画一段居中短线（见 ColumnResizeHandle）；表体不加竖线。
                      'group/head relative select-none text-xs',
                      compactRows && 'h-8',
                      // 可排序列整格是点击面（伪元素见 DataTableColumnHeader），悬停整格加深。
                      sortable ? 'cursor-pointer hover:bg-muted' : isDraggable && 'cursor-grab',
                      dragColId === header.column.id && 'opacity-40',
                      isOver && 'bg-primary/5',
                      isOver && (dropsAfter ? 'border-r-2 border-r-primary' : 'border-l-2 border-l-primary'),
                      getStickyClass(header.column.id, true),
                      numeric && 'text-right',
                    )}
                    onDragStart={(e) => {
                      if (resizingRef.current) { e.preventDefault(); return }
                      if (isDraggable) handleColumnDragStart(header.column.id)
                    }}
                    onDragEnd={() => { setDragColId(null); setDragOverColId(null) }}
                    onDragOver={(e) => {
                      if (!isDraggable) return
                      e.preventDefault()
                      setDragOverColId(header.column.id)
                    }}
                    onDragLeave={() => setDragOverColId(null)}
                    onDrop={(e) => {
                      e.preventDefault()
                      if (isDraggable) handleColumnDrop(header.column.id)
                    }}
                  >
                    {/* 拖拽手柄：hover 才渐显，绝对定位在列头左内边距区——不占流布局，
                        故不改变任何既有列宽/对齐，只是把「这列可以拖」这件事说出来。 */}
                    {isDraggable && (
                      <GripVertical
                        aria-hidden
                        className="pointer-events-none absolute left-0 top-1/2 size-3 -translate-y-1/2 opacity-0 transition-opacity duration-150 group-hover/head:opacity-40"
                      />
                    )}
                    {withMenu ? (
                      // 列名、生效筛选的漏斗、列菜单排成一行，菜单占位而不是浮在列名上：此前它绝对定位在格子右端，
                      // 数字列靠右的列名正好在它底下，悬停时被盖掉半截（「金额」只剩「金」）。
                      // 数字列整行镜像——列名靠右与数字对齐，漏斗和菜单都在左边，远离下一列。
                      <div className={cn('flex items-center gap-0.5', numeric && 'flex-row-reverse')}>
                        <div className="min-w-0">{content}</div>
                        {filtered && (
                          // 生效中的列筛选：列名旁一个前景色小漏斗，点它跳到筛选行里这一列的输入框。
                          <IconButton
                            label={t('columnFiltered')}
                            className="relative z-10 size-5 shrink-0 text-foreground"
                            draggable={false}
                            onDragStart={(event) => event.preventDefault()}
                            onClick={() => focusColumnFilter(header.column.id)}
                          >
                            <Filter className="size-3" />
                          </IconButton>
                        )}
                        {/* 悬停 / 聚焦时淡入，平时透明但占位；压在整格点击面之上。表格按内容定列宽，占位会把列撑宽：
                            按钮收到 20px、伸进 8px 格子内边距（外缘仍离格边 8px，让开列宽把手），每列只多占约 14px。 */}
                        <DataTableColumnMenuButton
                          {...menuProps}
                          className={cn('relative z-10 size-5 shrink-0', numeric ? 'mr-auto -ml-2' : 'ml-auto -mr-2')}
                        />
                      </div>
                    ) : content}
                    {withMenu && header.column.getCanResize() && (
                      <ColumnResizeHandle
                        hint={t('resizeColumn')}
                        divider={divider}
                        resizing={header.column.getIsResizing()}
                        onStart={(e) => {
                          resizingRef.current = true
                          const release = () => { resizingRef.current = false; document.removeEventListener('mouseup', release); document.removeEventListener('touchend', release) }
                          document.addEventListener('mouseup', release)
                          document.addEventListener('touchend', release)
                          header.getResizeHandler()(e)
                        }}
                        onReset={() => header.column.resetSize()}
                      />
                    )}
                  </TableHead>
                )
                if (!withMenu) return head
                // 右键任意数据列（含不可排序列、纯文本表头）弹出同一份列菜单。
                return (
                  <ContextMenu key={header.id}>
                    <ContextMenuTrigger asChild>{head}</ContextMenuTrigger>
                    <DataTableColumnContextMenu {...menuProps} />
                  </ContextMenu>
                )
              })}
            </TableRow>
            )
          })}

          {/* Column filter row — shown when toggled */}
          {showFilters && table.getHeaderGroups()[0] && (
            <TableRow className="bg-muted/10 hover:bg-muted/10 border-t border-dashed">
              {table.getHeaderGroups()[0].headers.map((header) => (
                <TableHead
                  key={`f-${header.id}`}
                  data-filter-col={header.column.id}
                  style={columnStyle(header.column.id, header.getSize())}
                  className={cn('py-1 h-auto', getStickyClass(header.column.id, true))}
                >
                  {!STRUCTURAL_COL_IDS.has(header.column.id) && (
                    <ColumnFilterCell header={header} table={table} />
                  )}
                </TableHead>
              ))}
            </TableRow>
          )}
        </TableHeader>

        <TableBody>
          {pageRows.length === 0 ? (
            <TableRow>
              <TableCell colSpan={tableColumns.length} className={emptyState && !isFiltered ? 'px-4 py-6 text-left' : 'h-32 text-center text-muted-foreground'}>
                {isFiltered ? t('noMatch') : emptyState ?? t('noData')}
              </TableCell>
            </TableRow>
          ) : enableVirtual ? (
            (() => {
              // 虚拟化：只渲染视口内行，前后各垫一个 spacer 行撑出滚动总高（保留原生 table
              // 列宽 auto layout 与 sticky 表头/固定列；不改 display:grid）。
              const virtualItems = rowVirtualizer.getVirtualItems()
              const totalSize = rowVirtualizer.getTotalSize()
              const paddingTop = virtualItems.length ? virtualItems[0].start : 0
              const paddingBottom = virtualItems.length ? totalSize - virtualItems[virtualItems.length - 1].end : 0
              const spacerColSpan = table.getVisibleLeafColumns().length || 1
              return (
                <>
                  {paddingTop > 0 && (
                    <tr aria-hidden="true">
                      <td colSpan={spacerColSpan} style={{ height: paddingTop, padding: 0, border: 0 }} />
                    </tr>
                  )}
                  {virtualItems.map((vi) => renderRow(pageRows[vi.index], vi.index))}
                  {paddingBottom > 0 && (
                    <tr aria-hidden="true">
                      <td colSpan={spacerColSpan} style={{ height: paddingBottom, padding: 0, border: 0 }} />
                    </tr>
                  )}
                </>
              )
            })()
          ) : (
            pageRows.map((row) => renderRow(row))
          )}
        </TableBody>
      </UITable>
    </div>
  )
}

/**
 * 表头右缘的列宽把手，同时是列间分隔：拖动调整，双击恢复按内容自动排版。
 *
 * 分隔线克制：平时只在格子正中画一段 16px 的发丝线（`bg-border`），不贯穿表头、不进表体；
 * 悬停把手时拉长加深，提示「这里能拖」；拖动中拉满整格。通高的 1px 竖线会把表头切成一格一格，观感生硬。
 *
 * 对辅助技术隐藏、不进 Tab 顺序：否则列头的无障碍名会变成「单号 拖动调整列宽…」（与列菜单「⋮」同理）。
 * 键盘用户的退路是列菜单「恢复默认列」。提示文案放原生 title，悬停可见。
 */
function ColumnResizeHandle({ hint, divider, resizing, onStart, onReset }: {
  hint: string
  /** 平时是否显示分隔短线（最后一列、紧挨固定操作列的那列不显示）。 */
  divider: boolean
  resizing: boolean
  onStart: (event: React.MouseEvent | React.TouchEvent) => void
  onReset: () => void
}) {
  return (
    <div
      aria-hidden
      data-slot="column-resize-handle"
      title={hint}
      draggable={false}
      onMouseDown={(e) => { e.stopPropagation(); onStart(e) }}
      onTouchStart={(e) => { e.stopPropagation(); onStart(e) }}
      onClick={(e) => e.stopPropagation()}
      onDoubleClick={(e) => { e.stopPropagation(); onReset() }}
      className={cn(
        'absolute top-0 right-0 z-20 h-full w-2 cursor-col-resize touch-none select-none',
        "after:absolute after:inset-y-3 after:right-0 after:w-px after:rounded-full after:content-['']",
        'after:transition-[top,bottom,background-color] after:duration-150',
        divider ? 'after:bg-border' : 'after:bg-transparent',
        'hover:after:inset-y-1.5 hover:after:bg-foreground/25',
        resizing && 'after:inset-y-0 after:bg-foreground/50',
      )}
    />
  )
}

