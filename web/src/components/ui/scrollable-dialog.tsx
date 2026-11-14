'use client'

import { type ComponentProps, type ReactNode } from 'react'
import {
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { cn } from '@/lib/utils'

interface ScrollableDialogContentProps {
  /** 弹框标题 */
  title: ReactNode
  /** 标题下方的副标题/说明（可选） */
  description?: ReactNode
  /** 滚动区主体内容 */
  children: ReactNode
  /** 固定在底部、不随主体滚动的操作区（可选） */
  footer?: ReactNode
  /** 透传给 DialogContent 的类名（可覆盖宽度/最大高度等） */
  className?: string
  /** 透传给滚动主体的类名（可覆盖内边距等） */
  bodyClassName?: string
  /** Radix 关闭完成后的焦点回落处理（嵌套弹框可精确返回触发元素） */
  onCloseAutoFocus?: ComponentProps<typeof DialogContent>['onCloseAutoFocus']
}

/**
 * 通用「带固定头部 + 内部滚动主体」的弹框内容。
 *
 * 用原生 `overflow-y-auto` 滚动容器（`flex-1 min-h-0`），不依赖 Radix ScrollArea，
 * 避免其在 flex 布局中高度无法收缩导致内部不滚动的问题。
 *
 * @example
 * <Dialog open={open} onOpenChange={setOpen}>
 *   <ScrollableDialogContent title="标题" footer={<Button>关闭</Button>}>
 *     <div className="p-6">...很长的内容...</div>
 *   </ScrollableDialogContent>
 * </Dialog>
 */
export function ScrollableDialogContent({
  title,
  description,
  children,
  footer,
  className,
  bodyClassName,
  onCloseAutoFocus,
}: ScrollableDialogContentProps) {
  return (
    <DialogContent
      className={cn(
        // overflow-y-hidden 覆盖 DialogContent 基类的 overflow-y-auto，避免外层也滚动
        'flex max-h-[88vh] flex-col gap-0 overflow-hidden overflow-y-hidden p-0 sm:max-w-2xl',
        className,
      )}
      onCloseAutoFocus={onCloseAutoFocus}
    >
      <DialogHeader className="shrink-0 space-y-1 border-b px-6 py-4 text-left">
        <DialogTitle>{title}</DialogTitle>
        {description ? <DialogDescription>{description}</DialogDescription> : null}
      </DialogHeader>

      <div className={cn('min-h-0 flex-1 overflow-y-auto overscroll-none [overflow-anchor:none]', bodyClassName)}>
        {children}
      </div>

      {footer ? (
        <div className="shrink-0 border-t px-6 py-3">{footer}</div>
      ) : null}
    </DialogContent>
  )
}
