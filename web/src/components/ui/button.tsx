'use client'

import * as React from 'react'
import { Slot } from '@radix-ui/react-slot'
import { cva, type VariantProps } from 'class-variance-authority'
import { Loader2 } from 'lucide-react'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'

const buttonVariants = cva(
  'inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-md text-sm font-medium transition-all disabled:cursor-not-allowed disabled:opacity-50 disabled:pointer-events-none [&_svg]:pointer-events-none [&_svg:not([class*="size-"])]:size-4 shrink-0 [&_svg]:shrink-0 outline-none focus-visible:border-ring focus-visible:ring-(--focus-ring-color) focus-visible:ring-(length:--focus-ring-width) aria-invalid:ring-(--focus-ring-color-destructive) aria-invalid:border-destructive cursor-pointer select-none',
  {
    variants: {
      variant: {
        default: 'bg-primary text-primary-foreground shadow-xs hover:bg-primary/90 active:bg-primary/80',
        destructive: 'bg-destructive text-white shadow-xs hover:bg-destructive/90 active:bg-destructive/80 focus-visible:ring-(--focus-ring-color-destructive)',
        outline: 'border bg-background shadow-xs hover:bg-accent hover:text-accent-foreground active:bg-accent/80 dark:bg-input/30 dark:border-input dark:hover:bg-input/50',
        secondary: 'bg-secondary text-secondary-foreground shadow-xs hover:bg-secondary/80 active:bg-secondary/70',
        ghost: 'hover:bg-accent hover:text-accent-foreground active:bg-accent/80 dark:hover:bg-accent/50',
        link: 'text-primary underline-offset-4 hover:underline active:underline',
        // 视图工具：只改「看到什么」、不改数据的按钮（工具栏的筛选 / 列 / 导出 / 刷新）。
        // 无边框灰字，和页头改数据的描边按钮拉开层级；按下或菜单展开时浅灰底、文字变黑。
        tool: 'text-muted-foreground hover:bg-accent hover:text-foreground active:bg-accent/80 aria-pressed:bg-secondary aria-pressed:text-foreground data-[state=open]:bg-accent data-[state=open]:text-foreground',
        // 批量危险动作：红字描边。实心红只留给确认框里的最终确认。
        'destructive-outline': 'border border-destructive/40 bg-background text-destructive shadow-xs hover:bg-destructive/10 active:bg-destructive/15 focus-visible:border-destructive focus-visible:ring-(--focus-ring-color-destructive) dark:bg-input/30',
      },
      size: {
        default: 'h-9 px-4 py-2 has-[>svg]:px-3',
        sm: 'h-8 rounded-md gap-1.5 px-3 has-[>svg]:px-2.5',
        lg: 'h-10 rounded-md px-6 has-[>svg]:px-4',
        icon: 'size-9',
      },
    },
    defaultVariants: { variant: 'default', size: 'default' },
  }
)

function Button({
  className,
  variant,
  size,
  asChild = false,
  loading = false,
  disabledReason,
  disabled,
  children,
  ...props
}: React.ComponentProps<'button'> & VariantProps<typeof buttonVariants> & {
  asChild?: boolean
  /** 为 true 时禁用点击、显示转圈，并挡住重复提交。 */
  loading?: boolean
  /** 因业务原因禁用时的说明。包在可悬浮的外层上，禁用按钮本身收不到指针事件。 */
  disabledReason?: string
}) {
  const Comp = asChild ? Slot : 'button'
  const isDisabled = Boolean(disabled || loading)
  const button = asChild ? (
    <Comp
      data-slot="button"
      data-loading={loading ? '' : undefined}
      aria-busy={loading || undefined}
      disabled={isDisabled}
      className={cn(buttonVariants({ variant, size, className }))}
      {...props}
    >
      {children}
    </Comp>
  ) : (
    <Comp
      data-slot="button"
      data-loading={loading ? '' : undefined}
      aria-busy={loading || undefined}
      disabled={isDisabled}
      className={cn(buttonVariants({ variant, size, className }))}
      {...props}
    >
      {loading ? <Loader2 className="size-4 animate-spin" aria-hidden /> : null}
      {children}
    </Comp>
  )
  if (disabledReason && isDisabled && !asChild) {
    return (
      <Tooltip>
        <TooltipTrigger asChild>
          <span className="inline-flex cursor-not-allowed" tabIndex={0}>
            {button}
          </span>
        </TooltipTrigger>
        <TooltipContent>{disabledReason}</TooltipContent>
      </Tooltip>
    )
  }
  return button
}

export { Button, buttonVariants }
