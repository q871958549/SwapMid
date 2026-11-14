'use client'

import * as React from 'react'
import * as SheetPrimitive from '@radix-ui/react-dialog'
import { X } from 'lucide-react'
import { cn } from '@/lib/utils'

const Sheet = SheetPrimitive.Root
const SheetTrigger = SheetPrimitive.Trigger
const SheetClose = SheetPrimitive.Close
const SheetPortal = SheetPrimitive.Portal

function SheetOverlay({ className, ...props }: React.ComponentProps<typeof SheetPrimitive.Overlay>) {
  return (
    <SheetPrimitive.Overlay
      data-slot="sheet-overlay"
      className={cn('data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 fixed inset-0 z-50 bg-black/50', className)}
      {...props}
    />
  )
}

/**
 * 左右两侧的面板在桌面端（md 起）是一张浮动卡片：四边描边、`rounded-xl`、离视口 8px——
 * 与左侧导航栏、主内容卡片（`(dashboard)/layout.tsx` 的 `md:gap-2 md:p-2`）同一套几何。
 * 窄屏不浮动，铺满高度并贴边，把宽度全留给内容。
 */
const sideClasses = {
  right: 'data-[state=closed]:slide-out-to-right data-[state=open]:slide-in-from-right inset-y-0 right-0 h-full w-full border-l md:inset-y-2 md:right-2 md:h-auto md:w-[calc(100%-1rem)] md:rounded-xl md:border',
  left: 'data-[state=closed]:slide-out-to-left data-[state=open]:slide-in-from-left inset-y-0 left-0 h-full w-full border-r md:inset-y-2 md:left-2 md:h-auto md:w-[calc(100%-1rem)] md:rounded-xl md:border',
  top: 'data-[state=closed]:slide-out-to-top data-[state=open]:slide-in-from-top inset-x-0 top-0 h-auto border-b',
  bottom: 'data-[state=closed]:slide-out-to-bottom data-[state=open]:slide-in-from-bottom inset-x-0 bottom-0 h-auto border-t',
} as const

/** 左右抽屉标准宽度四档（docs/ui-design-principles.md §2.4 设置抽屉推荐 lg=sm:max-w-xl）。 */
export const sheetSizeClasses = {
  sm: 'sm:max-w-sm',
  md: 'sm:max-w-md',
  lg: 'sm:max-w-xl',
  xl: 'sm:max-w-2xl',
} as const

export type SheetSize = keyof typeof sheetSizeClasses

function SheetContent({
  className, children, side = 'right', size = 'sm', showCloseButton = true, ...props
}: React.ComponentProps<typeof SheetPrimitive.Content> & {
  side?: keyof typeof sideClasses
  /** 左右抽屉最大宽度档位，默认 `'sm'`（384px）；设置/双列抽屉推荐 `'lg'`（576px）。 */
  size?: SheetSize
  /** 与 shadcn Dialog 的同名 prop 对齐：内容区自带关闭入口（如导航抽屉的品牌行）时关掉右上角默认 X，避免双关闭钮。 */
  showCloseButton?: boolean
}) {
  const isHorizontal = side === 'right' || side === 'left'
  return (
    <SheetPortal>
      <SheetOverlay />
      <SheetPrimitive.Content
        data-slot="sheet-content"
        data-size={isHorizontal ? size : undefined}
        className={cn(
          // 淡入淡出叠在滑动上：浮动后面板离边 8px，只滑动的话收起到最后会剩一条 8px 的边再突然消失。
          'bg-background data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 fixed z-50 flex flex-col gap-0 overflow-hidden shadow-lg transition ease-in-out data-[state=closed]:duration-200 data-[state=open]:duration-200 motion-reduce:transition-none',
          sideClasses[side],
          isHorizontal && sheetSizeClasses[size],
          className,
        )}
        {...props}
      >
        {children}
        {showCloseButton && (
          <SheetPrimitive.Close className="data-[state=open]:bg-secondary absolute top-4 right-4 rounded-xs opacity-70 transition-opacity hover:opacity-100 focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color) focus-visible:outline-hidden disabled:pointer-events-none [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4">
            <X />
            <span className="sr-only">Close</span>
          </SheetPrimitive.Close>
        )}
      </SheetPrimitive.Content>
    </SheetPortal>
  )
}

/**
 * 页头 / 正文 / 页脚的内边距和分隔线都在这里，调用方不用再手写 `shrink-0 border-b px-6 py-4`。
 * 页头右侧留出关闭钮的位置（`pr-12`）；`showCloseButton={false}` 的面板自己改回 `pr-6`。
 */
function SheetHeader({ className, ...props }: React.ComponentProps<'div'>) {
  return <div data-slot="sheet-header" className={cn('flex shrink-0 flex-col gap-1.5 border-b px-6 py-4 pr-12', className)} {...props} />
}

/** 正文：面板里唯一滚动的一段，页头和页脚始终可见。 */
function SheetBody({ className, ...props }: React.ComponentProps<'div'>) {
  return <div data-slot="sheet-body" className={cn('min-h-0 flex-1 overflow-y-auto px-6 py-4', className)} {...props} />
}

function SheetFooter({ className, ...props }: React.ComponentProps<'div'>) {
  return <div data-slot="sheet-footer" className={cn('flex shrink-0 flex-col-reverse gap-2 border-t px-6 py-4 sm:flex-row sm:justify-end', className)} {...props} />
}

function SheetTitle({ className, ...props }: React.ComponentProps<typeof SheetPrimitive.Title>) {
  return <SheetPrimitive.Title data-slot="sheet-title" className={cn('text-lg leading-none font-semibold', className)} {...props} />
}

function SheetDescription({ className, ...props }: React.ComponentProps<typeof SheetPrimitive.Description>) {
  return <SheetPrimitive.Description data-slot="sheet-description" className={cn('text-muted-foreground text-sm', className)} {...props} />
}

export { Sheet, SheetPortal, SheetOverlay, SheetTrigger, SheetClose, SheetContent, SheetHeader, SheetBody, SheetFooter, SheetTitle, SheetDescription }
