'use client'

import { useState } from 'react'
import { PanelLeftClose, PanelLeftOpen, RotateCcw, Users } from 'lucide-react'
import { useTranslations } from 'next-intl'
import type { NavT } from '@/lib/nav-utils'
import { ConfirmDialog } from '@/components/data-table/confirm-dialog'
import { IconButton } from '@/components/ui/icon-button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

/**
 * 侧栏底栏：日期靠左（非按钮），恢复默认排序靠右。
 * 恢复不再弹确认框，而是立即生效 + toast 里给「撤销」。
 */
export function SidebarFooter({
  collapsed,
  isCustomized,
  release,
  showCollapseToggle = true,
  onToggleCollapsed,
  resetLabel,
  onResetOrder,
  onPublishDefault,
}: {
  collapsed: boolean
  isCustomized: boolean
  /** 恢复按钮的说明：单域视图只恢复本域，工作台全景恢复全部。 */
  resetLabel: string
  release: { text: string; shortText: string; title: string }
  /** 兼容单测既有调用签名；组件内部自调 `useTranslations('Sidebar')`。 */
  ts?: NavT
  /** 抽屉形态没有「收起成图标列」这回事，置 false 隐去折叠钮、只留日期与重置。 */
  showCollapseToggle?: boolean
  onToggleCollapsed: () => void
  onResetOrder: () => void
  /** 仅 admin 传入：把自己当前的顺序显式发布为全员默认。 */
  onPublishDefault?: () => void
}) {
  const t = useTranslations('Sidebar')
  const [confirmPublish, setConfirmPublish] = useState(false)
  const iconClass = 'size-8 text-muted-foreground/60 hover:bg-sidebar-accent/40 hover:text-foreground'

  if (collapsed) {
    return (
      <div className="flex items-center justify-center border-t px-0 py-2">
        <IconButton
          label={t('expandNav')}
          tooltipSide="top"
          onClick={onToggleCollapsed}
          className={iconClass}
        >
          <PanelLeftOpen className="size-4" />
        </IconButton>
      </div>
    )
  }

  return (
    <>
      <div className="flex items-center justify-between gap-2 border-t px-2.5 py-2">
        <div className="flex min-w-0 items-center gap-1.5">
          {showCollapseToggle && (
            <IconButton
              label={t('collapseNav')}
              tooltipSide="top"
              onClick={onToggleCollapsed}
              className={iconClass}
            >
              <PanelLeftClose className="size-4" />
            </IconButton>
          )}
          <Tooltip>
            <TooltipTrigger asChild>
              <span
                tabIndex={0}
                aria-label={release.text}
                className="min-w-0 truncate font-mono text-[11px] leading-none tabular-nums text-muted-foreground/70 select-none"
              >
                {release.shortText}
              </span>
            </TooltipTrigger>
            <TooltipContent side="top">{release.text}</TooltipContent>
          </Tooltip>
        </div>
        <div className="flex shrink-0 items-center">
          {onPublishDefault && (
            <IconButton
              label={t('publishDefault')}
              tooltipSide="top"
              onClick={() => setConfirmPublish(true)}
              className={iconClass}
            >
              <Users className="size-4" />
            </IconButton>
          )}
          {isCustomized && (
            <IconButton
              label={resetLabel}
              tooltipSide="top"
              onClick={onResetOrder}
              className={iconClass}
            >
              <RotateCcw className="size-4" />
            </IconButton>
          )}
        </div>
      </div>
      {onPublishDefault && (
        <ConfirmDialog
          open={confirmPublish}
          onOpenChange={setConfirmPublish}
          title={t('publishDefault')}
          description={t('publishDefaultDescription')}
          confirmText={t('publishDefaultAction')}
          onConfirm={() => {
            onPublishDefault()
            setConfirmPublish(false)
          }}
        />
      )}
    </>
  )
}
