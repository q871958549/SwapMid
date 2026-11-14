'use client'

import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Slider } from '@/components/ui/slider'
import { cn } from '@/lib/utils'

/** step 的小数位数（0.25 → 2），用于把滑块回传的浮点值收敛到 step 精度。 */
function stepDecimals(step: number): number {
  return (String(step).split('.')[1] ?? '').length
}

/** 把滑块值格式化成表单字符串：按 step 精度四舍五入并去掉多余尾零（40.300000000004 → "40.3"）。 */
export function formatSliderValue(value: number, step: number): string {
  return String(Number(value.toFixed(stepDecimals(step))))
}

/** 把字符串承载的表单值转成滑块可用的数值：非法/空输入退回 min，超界钳到 [min, max]（只影响滑块显示，不改表单值）。 */
export function toSliderValue(value: string, min: number, max: number): number {
  const n = Number(value)
  if (!Number.isFinite(n)) return min
  return Math.min(max, Math.max(min, n))
}

interface SliderInputProps {
  id: string
  /** 可见标签，htmlFor 指向数字输入框（保持「按 label 找数字框」的既有测试语义）。 */
  label: string
  /** 滑块拇指的无障碍名。与 label 区分开，避免同名时 getByLabelText 命中两个控件。 */
  thumbLabel?: string
  min: number
  max: number
  step: number
  /** 表单值以字符串承载（沿用设计器既有约定：输入中间态不至于变 NaN）。 */
  value: string
  onChange: (value: string) => void
  disabled?: boolean
  /** 紧凑内联布局（标签在左、控件收窄），用于段落行内。默认标签在上。 */
  dense?: boolean
  className?: string
}

/**
 * 滑块 + 数字输入的复合控件：拖滑块改数字、改数字动滑块，双向同步。
 *
 * - 数字输入是权威输入通道（值原样上抛，非法中间态交由页面校验兜底）；
 * - 滑块只在 [min, max] 内表达，超界表单值在滑块上钳到边界显示。
 */
export function SliderInput({
  id,
  label,
  thumbLabel,
  min,
  max,
  step,
  value,
  onChange,
  disabled,
  dense,
  className,
}: SliderInputProps) {
  const slider = (
    <Slider
      value={[toSliderValue(value, min, max)]}
      min={min}
      max={max}
      step={step}
      disabled={disabled}
      thumbLabel={thumbLabel}
      onValueChange={([v]) => onChange(formatSliderValue(v, step))}
      className="min-w-20 flex-1"
    />
  )
  const input = (
    <Input
      id={id}
      type="number"
      min={min}
      max={max}
      step={step}
      value={value}
      disabled={disabled}
      onChange={e => onChange(e.target.value)}
      className={cn('h-8 shrink-0 tabular-nums', dense ? 'w-16' : 'w-20')}
    />
  )

  if (dense) {
    return (
      <div className={cn('flex items-center gap-2', className)}>
        <Label htmlFor={id} className="text-muted-foreground shrink-0 text-xs">
          {label}
        </Label>
        {slider}
        {input}
      </div>
    )
  }
  return (
    <div className={cn('space-y-1.5', className)}>
      <Label htmlFor={id}>{label}</Label>
      <div className="flex items-center gap-3">
        {slider}
        {input}
      </div>
    </div>
  )
}
