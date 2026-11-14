'use client'

import { Children, isValidElement, type ReactNode } from 'react'
import { type Column, type Table } from '@tanstack/react-table'
import { ArrowDown, ArrowUp, ArrowUpDown } from 'lucide-react'
import { cn } from '@/lib/utils'
import { isDescFirstColumn, isNumericColumn, nextSortState, type SortState } from './numeric-column'

const _titleRegistry = new Map<string, string>()

export function registerColumnTitle(colId: string, title: string) {
  _titleRegistry.set(colId, title)
}

export function getRegisteredColumnTitle(colId: string): string | undefined {
  return _titleRegistry.get(colId)
}

/** 把列切到指定排序态；`enableMultiSort: false` 下 toggleSorting 会替换掉其他列的排序。 */
export function applySortState<TData, TValue>(column: Column<TData, TValue>, next: SortState) {
  if (next === false) column.clearSorting()
  else column.toggleSorting(next === 'desc')
}

/** 本列单击排序是否先降序。标题优先取注册表（`DataTableColumnHeader` 渲染时登记）。 */
export function columnSortsDescFirst<TData, TValue>(column: Column<TData, TValue>, title?: string): boolean {
  return isDescFirstColumn(
    column.columnDef.meta,
    title ?? getRegisteredColumnTitle(column.id),
    column.columnDef.sortDescFirst,
  )
}

function containsColumnHeader(node: ReactNode, depth = 0): boolean {
  if (depth > 4 || !isValidElement(node)) return false
  if (node.type === DataTableColumnHeader) return true
  const children = (node.props as { children?: ReactNode }).children
  return Children.toArray(children).some((child) => containsColumnHeader(child, depth + 1))
}

/**
 * 这一列是否由页面作者明确提供了排序：可排序，且表头用了 `DataTableColumnHeader`（允许外面包几层）。
 * 纯文本表头的列只是没写 `enableSorting: false`，后端往往不认这个字段——白名单接口会悄悄退回默认排序，
 * 菜单却显示「升序」已勾选。所以列菜单只对这里返回 true 的列给排序项。
 */
export function columnOffersSorting<TData, TValue>(column: Column<TData, TValue>, table: Table<TData>): boolean {
  if (!column.getCanSort()) return false
  const header = column.columnDef.header
  if (typeof header !== 'function') return false
  return containsColumnHeader(header({ column, header: undefined, table } as never))
}

interface DataTableColumnHeaderProps<TData, TValue> {
  column: Column<TData, TValue>
  title: string
  className?: string
}

/**
 * 列标题。可排序列单击即在「默认 → 先手方向 → 反方向 → 默认」间循环，一步完成；
 * 隐藏列等其余操作在列菜单里（右键表头或悬停出现的「⋮」，见 `data-table-column-menu.tsx`）。
 */
export function DataTableColumnHeader<TData, TValue>({
  column,
  title,
  className,
}: DataTableColumnHeaderProps<TData, TValue>) {
  registerColumnTitle(column.id, title)
  const numeric = isNumericColumn(column.columnDef.meta, title)

  if (!column.getCanSort()) {
    return <div className={cn('text-xs font-medium', numeric && 'ml-auto text-right', className)}>{title}</div>
  }

  const sorted = column.getIsSorted()
  // 图标三态。铁律：隐藏的只是「这列可排序」的提示，**绝不隐藏「已排序」这个事实**——
  // 方向箭头常显，用户任何时刻都能看出当前排序；未排序列的双箭头才做 hover 渐显，
  // 否则十几列就是十几个常驻灰图标，纯视觉噪声。
  const icon = sorted === 'desc' ? (
    <ArrowDown className="size-3.5 shrink-0" />
  ) : sorted === 'asc' ? (
    <ArrowUp className="size-3.5 shrink-0" />
  ) : (
    <ArrowUpDown
      className={cn(
        'size-3.5 shrink-0 opacity-0 transition-opacity duration-150',
        // 列头 hover（group/head 在 data-table-grid 的 TableHead 上）与键盘聚焦都要显示：
        // 无障碍不能受 hover 限制。
        'group-hover/head:opacity-40 group-focus-within/head:opacity-40',
        // 触屏无 hover：恒显低透明度，否则触屏用户永远发现不了排序入口。
        '[@media(pointer:coarse)]:opacity-30',
      )}
    />
  )

  return (
    <button
      type="button"
      onClick={() => applySortState(column, nextSortState(sorted, columnSortsDescFirst(column, title)))}
      className={cn(
        'inline-flex max-w-full cursor-pointer items-center gap-1 text-xs font-medium outline-none',
        // 整格点击面：伪元素铺满表头格（TableHead 是定位容器），点格子里任何位置都排序。
        // 焦点环也画在伪元素上，框住整个格子。
        "after:absolute after:inset-0 after:content-['']",
        'focus-visible:after:ring-(length:--focus-ring-width) focus-visible:after:ring-inset focus-visible:after:ring-(--focus-ring-color)',
        // 已排序列的列名同步加深，与未排序列拉开对比——「当前按哪列排」要一眼可见。
        sorted ? 'text-foreground' : 'text-muted-foreground',
        numeric && 'ml-auto',
        className,
      )}
    >
      {/* 数字列右对齐，箭头放列名左侧，免得贴到右边的下一列。 */}
      {numeric && icon}
      <span className="truncate">{title}</span>
      {!numeric && icon}
    </button>
  )
}
