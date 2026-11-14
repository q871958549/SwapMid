'use client'

import type { ComponentProps } from 'react'
import { Check } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

type FilterChipProps = Omit<ComponentProps<typeof Button>, 'onClick' | 'variant' | 'aria-pressed'> & {
  pressed: boolean
  onPressedChange: (pressed: boolean) => void
  /** 命中条数，如「超期 3」。 */
  count?: number
}

/**
 * 可单独开关的快捷筛选（只看超期、只看待补档）。几个可以同时打开；互斥的用 `SegmentedControl`。
 *
 * 描边按钮，按下后浅灰底 + 对勾——状态不只靠颜色。不要用 `variant={开 ? 'default' : 'outline'}`。
 */
export function FilterChip({ pressed, onPressedChange, count, children, className, size = 'sm', ...props }: FilterChipProps) {
  return (
    <Button
      type="button"
      variant="outline"
      size={size}
      aria-pressed={pressed}
      onClick={() => onPressedChange(!pressed)}
      className={cn('aria-pressed:border-foreground/20 aria-pressed:bg-secondary aria-pressed:text-foreground', className)}
      {...props}
    >
      {pressed && <Check className="size-3.5" />}
      {children}
      {count !== undefined && <span className="text-xs tabular-nums text-muted-foreground">{count}</span>}
    </Button>
  )
}
