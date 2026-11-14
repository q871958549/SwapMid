'use client'

import * as React from 'react'
import { Button } from '@/components/ui/button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

type IconButtonProps = React.ComponentProps<typeof Button> & {
  /** 图标按钮的语义说明：同时作为 aria-label（读屏）与悬浮 tooltip 文案。**必填**——纯图标按钮必须有说明。 */
  label: string
  /** 是否显示悬浮 tooltip（默认 true）。置 false 仅保留 aria-label（如按钮已置于带可见文字的容器内）。 */
  showTooltip?: boolean
  /** tooltip 弹出方向，默认 'top'。 */
  tooltipSide?: React.ComponentProps<typeof TooltipContent>['side']
}

/**
 * 纯图标操作按钮的统一封装：一处 `label` 同时产出「可见 tooltip」+「无障碍 aria-label」。
 *
 * 与底线护栏 `swapmid/icon-button-label`（强制裸 `<Button size="icon">` 带 aria-label）互补：
 * 本组件是「推荐升级」——不仅给读屏无障碍名，还给鼠标用户可见的 hover 说明。
 *
 * - 默认 `variant="ghost"`、`size="icon"`，可透传覆盖（variant/size/className/onClick/disabled…）。
 * - `label` 必填：从类型层面杜绝「裸图标」——没有说明的图标按钮编译不过。
 * - 悬浮延迟走 `tooltip.tsx` 的全局默认（150ms），无需在此配置。
 *
 * @example
 * <IconButton label="删除" className="text-destructive" onClick={onDelete}><Trash2 /></IconButton>
 */
export function IconButton({
  label,
  showTooltip = true,
  tooltipSide = 'top',
  variant = 'ghost',
  size = 'icon',
  children,
  ...props
}: IconButtonProps) {
  // aria-label 放在 {...props} 之后，确保始终等于 label、不被调用方误覆盖。
  const button = (
    <Button variant={variant} size={size} {...props} aria-label={label}>
      {children}
    </Button>
  )
  if (!showTooltip) return button
  return (
    <Tooltip>
      <TooltipTrigger asChild>{button}</TooltipTrigger>
      <TooltipContent side={tooltipSide}>{label}</TooltipContent>
    </Tooltip>
  )
}
