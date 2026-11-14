import { type ReactNode } from 'react'
import { cn } from '@/lib/utils'

export interface ListPageShellProps {
  /** 页头区域（通常传入 `<PageHeader />`）。 */
  header?: ReactNode
  /** 筛选条插槽（通常传入 `<FilterBar />`，固定高度不伸缩）。 */
  filters?: ReactNode
  /** 单行状态或口径轻提示插槽（最多一条，长说明应进 `PageHeader.help` 或弹层）。 */
  notice?: ReactNode
  /** 表格上方的汇总面板插槽（内置 `max-h-[28vh]` 与内部滚动，防止挤占首屏表格高度）。 */
  summary?: ReactNode
  /** 主列表区域（可传 `table` 插槽或直接作为 `children` 传入）。 */
  table?: ReactNode
  children?: ReactNode
  className?: string
}

/**
 * 列表 / 报表页统一高度链骨架（docs/ui-design-principles.md §2.1、docs/ui-governance-blueprint.md §3.1）。
 *
 * 固定结构顺序：页头 → 筛选条 → 单行轻提示 → 有界汇总区（≤28vh 内滚）→ 自适应主表格（flex-1 min-h-0）。
 * 保持从 dashboard layout 到 DataTable 的完整 flex 高度链，确保 1366×768 首屏看见表头与数据行。
 */
export function ListPageShell({
  header,
  filters,
  notice,
  summary,
  table,
  children,
  className,
}: ListPageShellProps) {
  return (
    <div data-slot="list-page-shell" className={cn('flex h-full min-h-0 flex-col gap-3', className)}>
      {header != null && header !== false && (
        <div data-slot="list-page-header" className="shrink-0 empty:hidden [&>*]:mb-0">{header}</div>
      )}
      {filters != null && filters !== false && (
        <div data-slot="list-page-filters" className="shrink-0 empty:hidden">{filters}</div>
      )}
      {notice != null && notice !== false && (
        <div data-slot="list-page-notice" className="shrink-0 empty:hidden [&>*]:mb-0">{notice}</div>
      )}
      {summary != null && summary !== false && (
        <div data-slot="list-page-summary" className="scrollbar-fade max-h-[28vh] shrink-0 overflow-y-auto empty:hidden [&>*]:mb-0">
          {summary}
        </div>
      )}
      <div data-slot="list-page-body" className="flex min-h-0 flex-1 flex-col">
        {table}
        {children}
      </div>
    </div>
  )
}
