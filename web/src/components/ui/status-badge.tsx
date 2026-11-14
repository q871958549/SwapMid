import * as React from 'react'
import { cva, type VariantProps } from 'class-variance-authority'
import { cn } from '@/lib/utils'

/**
 * 业务生命周期状态胶囊（区别于普通方圆角属性 Badge）：
 * - 全圆角胶囊（`rounded-full`）+ 1px 同色微描边 + 前置 6px 状态锚点（满足规范 §8「颜色不是唯一编码」）。
 * - 统一收口实物与单据的八类标准生命状态：
 *   - `success`：就绪 / 在库 / 空闲可用 / 已签收 / 已确认 / 正常（翠绿柔底 + 实心绿点）
 *   - `info`：在途 / 调拨中 / 进行中 / 评审中 / 审批中（冷蓝柔底 + 实心蓝点，替代误用 primary/10 的水泥灰）
 *   - `warning`：占用 / 领用中 / 待处理 / 临期预警（琥珀柔底 + 实心琥珀点）
 *   - `outbound`：已离仓外发 / 已寄出 / 待外部反馈（清透底冷蓝框 + 双环点，与仓内库存区分）
 *   - `danger`：异常 / 逾期 / 已退回 / 已驳回（柔红浅底 + 实心红点，不与确认按钮实心大红抢视觉）
 *   - `draft`：已登记未入库 / 草稿 / 未开始（虚线细框透明底 + 空心圈，表达尚未激活）
 *   - `neutral`：已关闭 / 已归档（正常完结稳态，柔和灰底）
 *   - `scrapped`：已报废 / 已作废 / 已取消（暗灰退后 + 删除线消亡态，彻底避免与「在库」混淆）
 */
const statusBadgeVariants = cva(
  'inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-xs leading-4 transition-colors select-none',
  {
    variants: {
      tone: {
        success: 'border-success/30 bg-success-soft font-semibold text-success',
        info: 'border-info/30 bg-info-soft font-semibold text-info',
        warning: 'border-warning/35 bg-warning-soft font-semibold text-warning',
        outbound: 'border-info/35 bg-background font-medium text-info',
        danger: 'border-destructive/30 bg-destructive-soft font-semibold text-destructive',
        draft: 'border-dashed border-muted-foreground/45 bg-transparent font-normal text-muted-foreground',
        neutral: 'border-border bg-muted/70 font-normal text-muted-foreground',
        scrapped: 'border-border/80 bg-muted/60 font-normal text-muted-foreground/80 line-through decoration-muted-foreground/60',
      },
    },
    defaultVariants: {
      tone: 'neutral',
    },
  },
)

export type StatusTone = NonNullable<VariantProps<typeof statusBadgeVariants>['tone']>

const DOT_CLASS_BY_TONE: Record<StatusTone, string> = {
  success: 'bg-current',
  info: 'bg-current',
  warning: 'bg-current',
  outbound: 'border border-current bg-info-soft',
  danger: 'bg-current',
  draft: 'border border-current bg-transparent',
  neutral: 'bg-muted-foreground/60',
  scrapped: 'bg-muted-foreground/45',
}

export interface StatusBadgeProps
  extends React.ComponentProps<'span'>,
    VariantProps<typeof statusBadgeVariants> {
  /** 是否显示前置 6px 状态锚点，默认 true。 */
  dot?: boolean
}

function StatusBadge({
  className,
  tone = 'neutral',
  dot = true,
  children,
  ...props
}: StatusBadgeProps) {
  const resolvedTone: StatusTone = tone ?? 'neutral'
  return (
    <span
      data-slot="status-badge"
      data-tone={resolvedTone}
      className={cn(statusBadgeVariants({ tone: resolvedTone }), className)}
      {...props}
    >
      {dot && (
        <span
          aria-hidden="true"
          className={cn('size-1.5 shrink-0 rounded-full', DOT_CLASS_BY_TONE[resolvedTone])}
        />
      )}
      {children}
    </span>
  )
}

export { StatusBadge, statusBadgeVariants }
