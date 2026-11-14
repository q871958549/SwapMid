'use client'

import * as React from 'react'
import * as DialogPrimitive from '@radix-ui/react-dialog'
import { X } from 'lucide-react'
import { cn } from '@/lib/utils'

const Dialog = DialogPrimitive.Root
const DialogTrigger = DialogPrimitive.Trigger
const DialogPortal = DialogPrimitive.Portal
const DialogClose = DialogPrimitive.Close

function DialogOverlay({ className, ...props }: React.ComponentProps<typeof DialogPrimitive.Overlay>) {
  return (
    <DialogPrimitive.Overlay
      data-slot="dialog-overlay"
      className={cn('data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 fixed inset-0 z-50 bg-black/50', className)}
      {...props}
    />
  )
}

/**
 * 面板自己整体滚动时，页脚要钉在面板底部，否则长表单的保存按钮会被滚出视口
 * （2026-08 与 2026-10 曾逐页修过 5 次）。调用方改了内边距、或自己用 overflow-hidden
 * 分出头 / 体 / 脚（ScrollableDialog 等）时不钉，避免负边距错位。
 */
export function pinsDialogFooter(className?: string) {
  const c = className ?? ''
  return !/(?:^|\s)(?:[\w-]+:)*p[xytrbl]?-/.test(c) && !/(?:^|\s)(?:[\w-]+:)*overflow-(?:hidden|visible|y-hidden)\b/.test(c)
}

/** 对话框标准宽度六档（docs/ui-governance-blueprint.md §3.1），统一带 sm: 前缀，避免裸 max-w-* 被基类覆盖。 */
export const dialogSizeClasses = {
  sm: 'sm:max-w-md',
  md: 'sm:max-w-lg',
  lg: 'sm:max-w-2xl',
  xl: 'sm:max-w-4xl',
  '2xl': 'sm:max-w-6xl',
  full: 'sm:max-w-[92vw]',
} as const

export type DialogSize = keyof typeof dialogSizeClasses

function DialogContent({
  className,
  children,
  size = 'md',
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Content> & {
  /** 面板桌面端最大宽度档位，默认 `'md'`（512px）。 */
  size?: DialogSize
}) {
  return (
    <DialogPortal>
      <DialogOverlay />
      <DialogPrimitive.Content
        data-slot="dialog-content"
        data-size={size}
        data-pin-footer={pinsDialogFooter(className) ? '' : undefined}
        className={cn(
          'group/dialog bg-background data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 fixed top-[50%] left-[50%] z-50 flex w-full max-w-[calc(100%-2rem)] translate-x-[-50%] translate-y-[-50%] flex-col gap-4 rounded-xl border p-6 shadow-lg duration-200 max-h-[90vh] overflow-y-auto scrollbar-fade',
          dialogSizeClasses[size],
          className
        )}
        {...props}
      >
        {children}
        <DialogPrimitive.Close className="data-[state=open]:bg-accent data-[state=open]:text-muted-foreground absolute top-4 right-4 rounded-xs opacity-70 transition-opacity hover:opacity-100 focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color) focus-visible:outline-hidden disabled:pointer-events-none [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4">
          <X />
          <span className="sr-only">Close</span>
        </DialogPrimitive.Close>
      </DialogPrimitive.Content>
    </DialogPortal>
  )
}

function DialogHeader({ className, ...props }: React.ComponentProps<'div'>) {
  return <div data-slot="dialog-header" className={cn('flex flex-col gap-2 text-center sm:text-left', className)} {...props} />
}

// 钉底：负边距铺满面板内边距，再用等量内边距把按钮放回原位；不滚动时与原布局逐像素一致，
// 滚动时正文从页脚背后经过。上沿多铺 12px 背景，避免正文贴着按钮露出来。
// sticky 的边界会被滚动容器的内边距内缩，所以用 -bottom-6 抵消面板的 p-6：写 bottom-0 时
// 静止状态按钮会上移 24px，滚动时页脚下方还会露出一条正文（浏览器实测）。
const pinnedFooterClass =
  'group-data-[pin-footer]/dialog:sticky group-data-[pin-footer]/dialog:-bottom-6 group-data-[pin-footer]/dialog:z-10 group-data-[pin-footer]/dialog:-mx-6 group-data-[pin-footer]/dialog:-mt-3 group-data-[pin-footer]/dialog:-mb-6 group-data-[pin-footer]/dialog:bg-background group-data-[pin-footer]/dialog:px-6 group-data-[pin-footer]/dialog:pt-3 group-data-[pin-footer]/dialog:pb-6'

function DialogFooter({ className, ...props }: React.ComponentProps<'div'>) {
  return <div data-slot="dialog-footer" className={cn('flex flex-col-reverse gap-2 sm:flex-row sm:justify-end', pinnedFooterClass, className)} {...props} />
}

function DialogTitle({ className, ...props }: React.ComponentProps<typeof DialogPrimitive.Title>) {
  return <DialogPrimitive.Title data-slot="dialog-title" className={cn('text-lg leading-none font-semibold', className)} {...props} />
}

function DialogDescription({ className, ...props }: React.ComponentProps<typeof DialogPrimitive.Description>) {
  return <DialogPrimitive.Description data-slot="dialog-description" className={cn('text-muted-foreground text-sm', className)} {...props} />
}

/** 窄屏铺满视口，桌面仍是居中卡片。给通知里会在手机上打开的办理框用。 */
export const narrowScreenDialogClass =
  'max-sm:top-0 max-sm:left-0 max-sm:h-dvh max-sm:max-h-dvh max-sm:w-full max-sm:max-w-none max-sm:translate-x-0 max-sm:translate-y-0 max-sm:rounded-none'

export { Dialog, DialogPortal, DialogOverlay, DialogTrigger, DialogClose, DialogContent, DialogHeader, DialogFooter, DialogTitle, DialogDescription }
