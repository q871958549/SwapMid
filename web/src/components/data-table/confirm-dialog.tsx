'use client'

import { type ComponentProps, type ReactNode } from 'react'
import { useTranslations } from 'next-intl'
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { cn } from '@/lib/utils'

/**
 * 确认对话框壳（删除 / 批量删除 / 危险操作通用）。
 * 收掉每个 CRUD 页逐字重写的那串 AlertDialog 模板；`busy` 时禁用按钮并显示进行中文案，
 * 确认按钮统一 `preventDefault` 以便在 onConfirm 里 await（避免对话框抢先关闭）。
 *
 * @example
 * <ConfirmDialog
 *   open={!!crud.deleteTarget}
 *   onOpenChange={(o) => !o && crud.closeDelete()}
 *   title="确认删除岗位"
 *   description={<>将永久删除岗位「{crud.deleteTarget?.name}」，此操作无法撤销。</>}
 *   confirmText="删除"
 *   busyText="删除中..."
 *   destructive
 *   busy={crud.deleting}
 *   onConfirm={crud.confirmDelete}
 * />
 */
interface ConfirmDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: ReactNode
  onConfirm: () => void | Promise<void>
  /** 确认按钮文案，默认「确认」（i18n Common.confirm）。 */
  confirmText?: string
  /** 取消按钮文案，默认「取消」（i18n Common.cancel）。 */
  cancelText?: string
  /** 进行中文案，默认「处理中...」（i18n Common.processing）。 */
  busyText?: string
  /** 进行中：禁用两个按钮、确认按钮显示 busyText。 */
  busy?: boolean
  /** 危险操作：确认按钮用 destructive 配色。 */
  destructive?: boolean
  /** 条件未齐时禁用确认（例如驳回原因还没填）。取消仍可点。 */
  confirmDisabled?: boolean
  /**
   * 嵌在 Dialog 内时必须 false，避免双 modal 抢焦点 / 误关外层。
   * 独立确认框保持默认 true。
   */
  modal?: boolean
}

export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  onConfirm,
  confirmText,
  cancelText,
  busyText,
  busy = false,
  destructive = false,
  confirmDisabled = false,
  modal = true,
}: ConfirmDialogProps) {
  const t = useTranslations('Common')
  const resolvedConfirmText = confirmText ?? t('confirm')
  const resolvedCancelText = cancelText ?? t('cancel')
  const resolvedBusyText = busyText ?? t('processing')
  return (
    <AlertDialog
      {...({
        open,
        modal,
        onOpenChange: (o: boolean) => { if (!busy) onOpenChange(o) },
      } as ComponentProps<typeof AlertDialog>)}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription asChild>
            <div className="text-muted-foreground text-sm">{description}</div>
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={busy}>{resolvedCancelText}</AlertDialogCancel>
          <AlertDialogAction
            onClick={(e) => { e.preventDefault(); if (!confirmDisabled) void onConfirm() }}
            disabled={busy || confirmDisabled}
            className={cn(destructive && 'bg-destructive text-destructive-foreground hover:bg-destructive/90')}
          >
            {busy ? resolvedBusyText : resolvedConfirmText}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
