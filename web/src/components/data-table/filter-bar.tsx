'use client'

import { type ReactNode } from 'react'
import { useTranslations } from 'next-intl'
import { ListFilter, X } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { cn } from '@/lib/utils'

export interface FilterBarChip {
  key: string
  label: string
  onRemove: () => void
  removeAriaLabel?: string
}

export interface FilterBarProps {
  /** 常驻行内筛选项（建议 ≤4 个高频控件，其余收进 `moreFilters`）。 */
  children?: ReactNode
  /** 低频筛选项面板内容；传入后自动渲染带生效计数的「筛选」Popover。 */
  moreFilters?: ReactNode
  /** 已生效的高级筛选条数（>0 时在触发按钮显示数字徽标，并在浮层底部提供重置入口）。 */
  activeCount?: number
  /** 「更多筛选」触发按钮文案，默认取 `Common.filter`（「筛选」）。 */
  moreLabel?: string
  /** 浮层底部清空按钮文案，默认取 `Common.reset`（「重置」）。 */
  clearLabel?: string
  /** 点击浮层底部清空按钮的回调。 */
  onClear?: () => void
  /** Popover 浮层自定义类名（如宽度覆盖）。 */
  popoverClassName?: string
  /** Popover 对齐方向，默认 `'start'`。 */
  popoverAlign?: 'start' | 'center' | 'end'
  /** 已选条件胶囊列表，点击可单独移除对应条件。 */
  chips?: FilterBarChip[]
  /** 行尾动作槽位（如「查询」按钮或轻量辅助统计信息）。 */
  actions?: ReactNode
  className?: string
}

/**
 * 列表页扁平筛选条（docs/ui-design-principles.md §2.1、docs/ui-governance-blueprint.md §3.1）。
 *
 * 不加外层卡片边框与多余纵向内边距，避免将首屏表格向下挤压；
 * 高频条件行内直摆，低频条件收进带计数徽标的 Popover 浮层并支持已选条件胶囊清除。
 */
export function FilterBar({
  children,
  moreFilters,
  activeCount = 0,
  moreLabel,
  clearLabel,
  onClear,
  popoverClassName,
  popoverAlign = 'start',
  chips,
  actions,
  className,
}: FilterBarProps) {
  const tc = useTranslations('Common')
  const triggerLabel = moreLabel ?? tc('filter')
  const resetText = clearLabel ?? tc('reset')

  return (
    <div
      data-slot="filter-bar"
      className={cn('flex min-w-0 shrink-0 flex-wrap items-center gap-2', className)}
    >
      {children}
      {moreFilters != null && moreFilters !== false && (
        <Popover>
          <PopoverTrigger asChild>
            <Button type="button" variant="outline" size="sm" className="h-8">
              <ListFilter className="size-3.5" />
              {triggerLabel}
              {activeCount > 0 && (
                <Badge variant="secondary" className="ml-1 h-4 min-w-4 rounded-full px-1 text-[10px] tabular-nums">
                  {activeCount}
                </Badge>
              )}
            </Button>
          </PopoverTrigger>
          <PopoverContent align={popoverAlign} className={cn('max-h-[65vh] w-72 space-y-3 overflow-y-auto', popoverClassName)}>
            {moreFilters}
            {activeCount > 0 && onClear && (
              <Button type="button" variant="ghost" size="sm" className="text-muted-foreground h-7 w-full" onClick={onClear}>
                {resetText}
              </Button>
            )}
          </PopoverContent>
        </Popover>
      )}
      {chips?.map((chip) => (
        <Button
          key={chip.key}
          type="button"
          variant="secondary"
          size="sm"
          className="h-7 max-w-64 gap-1 px-2 text-xs font-normal"
          onClick={chip.onRemove}
          aria-label={chip.removeAriaLabel ?? `${tc('clear')} ${chip.label}`}
        >
          <span className="truncate">{chip.label}</span>
          <X className="size-3 shrink-0" />
        </Button>
      ))}
      {actions != null && actions !== false && (
        <div data-slot="filter-bar-actions" className="flex flex-wrap items-center gap-2">
          {actions}
        </div>
      )}
    </div>
  )
}
