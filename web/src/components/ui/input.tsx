import * as React from 'react'
import { cn } from '@/lib/utils'

function Input({ className, type, ...props }: React.ComponentProps<'input'>) {
  return (
    <input
      type={type}
      data-slot="input"
      className={cn(
        'flex h-9 w-full min-w-0 rounded-md border border-input bg-transparent px-3 py-1 text-base shadow-xs transition-[color,box-shadow] outline-none md:text-sm',
        'file:border-0 file:bg-transparent file:text-sm file:font-medium file:text-foreground',
        'placeholder:text-muted-foreground',
        // 焦点环画在框内（ring-inset）：输入框常贴着滚动容器边缘，外扩的环会被 overflow 裁掉一侧（规范 §8「焦点」）。
        'focus-visible:border-ring focus-visible:ring-(--focus-ring-color) focus-visible:ring-(length:--focus-ring-width) ring-inset',
        'aria-invalid:ring-(--focus-ring-color-destructive) aria-invalid:border-destructive',
        'disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50',
        className
      )}
      {...props}
    />
  )
}

export { Input }
