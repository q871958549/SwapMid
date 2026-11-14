import { Fragment, type ReactNode } from 'react'
import { useTranslations } from 'next-intl'
import { CircleHelp } from 'lucide-react'
import { IconButton } from '@/components/ui/icon-button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { DropdownButton } from './dropdown-button'
import { cn } from '@/lib/utils'

/** 规范上限（docs/ui-design-principles.md §1.2、§7）。 */
export const PAGE_HEADER_MAX_SECONDARY = 2
export const PAGE_HEADER_DESCRIPTION_LIMIT = 40

interface PageHeaderHelp {
  /** 图标按钮的 tooltip 与可访问名，如「口径说明」。 */
  label: string
  /** 浮层正文。长说明（分组、表格）改用 `ReportGuide`。 */
  content: ReactNode
}

interface PageHeaderProps {
  title: string
  /** 一行，≤40 字；窄屏隐藏，所以关键信息不要只写在这里。 */
  description?: string
  /** 本页唯一主按钮（`Button` 默认变体或 `CreateButton`），固定在操作区最右。 */
  primaryAction?: ReactNode
  /** 次按钮（`outline` / `ghost`），最多 2 个；更多的放进 `moreActions`。 */
  secondaryActions?: ReactNode[]
  /** 低频动作：一组 `DropdownMenuItem`，渲染为「更多」按钮。 */
  moreActions?: ReactNode
  /** 口径 / 使用说明：页头问号图标，点开浮层，不占文档流。 */
  help?: PageHeaderHelp
  /** 逃生舱：自由内容，与上面的结构化插槽可以并用，但同样计入页头按钮额度（`pnpm check:ui`）。 */
  action?: ReactNode
  extra?: ReactNode
  className?: string
}

function MoreActions({ items }: { items: ReactNode }) {
  const t = useTranslations('Common')
  return <DropdownButton label={t('more')} size="sm" items={items} />
}

function warnInDev(title: string, description: string | undefined, secondary: number) {
  if (process.env.NODE_ENV === 'production') return
  if (secondary > PAGE_HEADER_MAX_SECONDARY) {
    console.warn(`[PageHeader]「${title}」有 ${secondary} 个次按钮，超过 ${PAGE_HEADER_MAX_SECONDARY} 个：低频动作放进 moreActions。见 docs/ui-design-principles.md §1.2`)
  }
  if (description && [...description].length > PAGE_HEADER_DESCRIPTION_LIMIT) {
    console.warn(`[PageHeader]「${title}」的描述超过 ${PAGE_HEADER_DESCRIPTION_LIMIT} 字：长说明放进 help。见 docs/ui-design-principles.md §7`)
  }
}

/**
 * 页头。结构化插槽按固定顺序排：说明图标 → 次按钮 → 更多 → 自由内容 → 主按钮（最右）。
 * 新页面用插槽；旧页面的 `action` / `extra` 继续可用。
 */
export function PageHeader({
  title,
  description,
  primaryAction,
  secondaryActions,
  moreActions,
  help,
  action,
  extra,
  className,
}: PageHeaderProps) {
  warnInDev(title, description, secondaryActions?.length ?? 0)
  return (
    <div
      data-slot="page-header"
      className={cn('mb-3 flex shrink-0 flex-col gap-2 md:mb-6 md:flex-row md:items-center md:justify-between', className)}
    >
      <div className="min-w-0">
        <h1 className="text-lg font-semibold tracking-tight md:text-xl">{title}</h1>
        {description && (
          <p className="text-muted-foreground mt-0.5 hidden text-sm md:block">{description}</p>
        )}
      </div>
      {/* 桌面统一 32px：页头里不写 size 的按钮是 36px、写 `sm` 的是 32px，同一行高低不齐（蓝图 §1.1）。
          纯图标按钮（`size-9`）同步收成 32×32。窄屏仍按触控 44px。 */}
      <div
        data-slot="page-header-actions"
        className="flex min-w-0 flex-wrap items-center gap-2 max-md:[&>div]:w-full max-md:[&>div]:flex-wrap max-md:[&_button]:h-11 max-md:[&_a]:h-11 md:shrink-0 md:[&_[data-slot=button]]:h-8 md:[&_[data-slot=button][class*=size-9]]:w-8"
      >
        {help && (
          <Popover>
            <PopoverTrigger asChild>
              <IconButton label={help.label}><CircleHelp /></IconButton>
            </PopoverTrigger>
            <PopoverContent align="end" className="max-h-[60vh] w-80 overflow-y-auto text-sm">{help.content}</PopoverContent>
          </Popover>
        )}
        {extra}
        {secondaryActions?.map((node, index) => <Fragment key={index}>{node}</Fragment>)}
        {moreActions && <MoreActions items={moreActions} />}
        {action}
        {primaryAction}
      </div>
    </div>
  )
}
