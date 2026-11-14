'use client'

import {
  Toast,
  ToastClose,
  ToastDescription,
  ToastProvider,
  ToastTitle,
  ToastViewport,
} from '@/components/ui/toast'
import { useToast } from '@/components/ui/use-toast'

// destructive toast 默认停留时长（Q 轮，蓝图 §20.3 Q4）：错误文案通常比成功提示长，5s 默认值读不完就自动
// 消失；调用方显式传 duration 时仍以其为准（下方 ?? 只兜底未传的情况）。其余 variant 维持 Radix 默认 5s。
const DESTRUCTIVE_TOAST_DEFAULT_DURATION = 10000

export function Toaster() {
  const { toasts } = useToast()

  return (
    <ToastProvider>
      {toasts.map(({ id, title, description, action, variant, duration, ...props }) => (
        <Toast
          key={id}
          variant={variant}
          duration={duration ?? (variant === 'destructive' ? DESTRUCTIVE_TOAST_DEFAULT_DURATION : undefined)}
          {...props}
        >
          <div className="grid gap-1">
            {title && <ToastTitle>{title}</ToastTitle>}
            {description && <ToastDescription>{description}</ToastDescription>}
          </div>
          {action}
          <ToastClose />
        </Toast>
      ))}
      <ToastViewport />
    </ToastProvider>
  )
}
