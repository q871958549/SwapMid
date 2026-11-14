import { type ReactNode } from 'react'
import { cn } from '@/lib/utils'

export interface EmptyStateProps {
  /** 可选前导图标（建议传入 lucide 图标节点，组件自动施加柔和尺寸与色阶）。 */
  icon?: ReactNode
  /** 一句话说明当前为什么为空（如「暂无符合筛选条件的记录」「当前月份尚未生成报表」）。 */
  title: ReactNode
  /** 可选补充说明（建议 ≤2 行，控制在 `max-w-prose` 阅读行宽内）。 */
  description?: ReactNode
  /** 下一步动作槽位（如「清除筛选」「新建」「调整日期」按钮）。 */
  action?: ReactNode
  /** 紧凑模式（用于矮面板、子表或抽屉内嵌列表，收紧纵向留白确保首屏可见）。 */
  compact?: boolean
  className?: string
}

/**
 * 标准空状态共享件（docs/ui-design-principles.md §5、docs/ui-governance-blueprint.md §3.1）。
 *
 * 空状态是明确的结果而非空白：固定「图标（可选）→ 原因标题 → 简短指引 → 下一步操作按钮」结构，
 * 行宽约束在 `max-w-prose` 内，默认高度保证在 1366×768 列表表体内完整落入首屏。
 */
export function EmptyState({
  icon,
  title,
  description,
  action,
  compact = false,
  className,
}: EmptyStateProps) {
  return (
    <div
      data-slot="empty-state"
      data-compact={compact ? '' : undefined}
      className={cn(
        'mx-auto flex max-w-prose flex-col items-center justify-center text-center',
        compact ? 'gap-2 px-4 py-5' : 'gap-2.5 px-4 py-8',
        className,
      )}
    >
      {icon != null && icon !== false && (
        <div
          aria-hidden
          data-slot="empty-state-icon"
          className="bg-muted text-muted-foreground flex size-10 shrink-0 items-center justify-center rounded-full [&_svg]:size-5"
        >
          {icon}
        </div>
      )}
      <div className="space-y-1">
        <p data-slot="empty-state-title" className="text-foreground text-sm font-medium">
          {title}
        </p>
        {description != null && description !== false && (
          <p data-slot="empty-state-description" className="text-muted-foreground max-w-prose text-xs leading-relaxed">
            {description}
          </p>
        )}
      </div>
      {action != null && action !== false && (
        <div data-slot="empty-state-action" className="mt-1 flex flex-wrap items-center justify-center gap-2">
          {action}
        </div>
      )}
    </div>
  )
}
